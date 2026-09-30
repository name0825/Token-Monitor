using System.Text.RegularExpressions;
using TokenMonitor.Core;

namespace TokenMonitor.Providers.Cli;

public delegate ICliSession CliSessionFactory(string executable, string workingDirectory);

public sealed class CliScrapeProvider : IUsageProvider
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryAfterFailureInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ParseRetryInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ParseRetryWindow = TimeSpan.FromSeconds(5);
    private const int HintMaxLength = 80;

    private static readonly Regex HintPattern = new(
        @"error|failed|failure|unable|couldn't|could not|rate.?limit|loading|timed out|try again",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly Tool _tool;
    private readonly string _workingDirectory;
    private readonly CliSessionFactory _sessionFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private DateTimeOffset? _lastRunAt;
    private UsageResult? _lastResult;

    public CliScrapeProvider(Tool tool, string workingDirectory, CliSessionFactory sessionFactory, TimeProvider timeProvider)
    {
        _tool = tool;
        _workingDirectory = workingDirectory;
        _sessionFactory = sessionFactory;
        _timeProvider = timeProvider;
    }

    public CliScrapeProvider(Tool tool)
        : this(tool, ResolveDefaultWorkingDirectory(), CreateDefaultSession, TimeProvider.System)
    {
    }

    public Tool Tool => _tool;

    public async Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_lastRunAt is { } lastRunAt && _lastResult is { } lastResult)
            {
                // Self-imposed throttle, mirrors how the real APIs would rate-limit repeated requests.
                // A failed attempt gets a shorter cooldown so transient issues (e.g. expired token, update prompt) can recover sooner.
                var throttleInterval = lastResult.IsSuccess ? MinimumInterval : RetryAfterFailureInterval;
                if (now - lastRunAt < throttleInterval)
                {
                    return lastResult;
                }
            }

            UsageResult result;
            try
            {
                result = await RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = UsageResult.Failure(UsageFailureKind.Unavailable, "CLI 폴백이 20초 안에 응답하지 않았습니다");
            }

            _lastRunAt = _timeProvider.GetUtcNow();
            _lastResult = result;
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private string DescribePromptIssue(string promptIssue, string executable) => promptIssue switch
    {
        "update prompt" => $"{executable} 업데이트 필요 — 터미널에서 {executable}를 실행해 업데이트하세요",
        "login prompt" => $"{executable} 로그인 필요 — 터미널에서 {executable}를 실행해 로그인하세요",
        "trust prompt" => $"{executable} 폴더 신뢰 필요 — {_workingDirectory}에서 {executable}를 실행해 승인하세요",
        _ => $"CLI 폴백 설정 필요: {promptIssue}",
    };

    private async Task<UsageResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_workingDirectory);
        }
        catch (IOException ex)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, $"작업 디렉터리를 만들 수 없습니다: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, $"작업 디렉터리를 만들 수 없습니다: {ex.Message}");
        }

        var (executable, usageCommand) = _tool == Tool.Claude ? ("claude", "/usage") : ("codex", "/status");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(HardTimeout);

        ICliSession session;
        try
        {
            session = _sessionFactory(executable, _workingDirectory);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, $"{executable} 실행에 실패했습니다: {ex.Message}");
        }

        try
        {
            await session.WaitForIdleAsync(QuietPeriod, HardTimeout, timeoutCts.Token).ConfigureAwait(false);

            var startupScreen = session.RenderScreen();
            var promptIssue = CliPromptDetector.Detect(startupScreen);
            if (promptIssue is not null)
            {
                return UsageResult.Failure(UsageFailureKind.Unavailable, DescribePromptIssue(promptIssue, executable));
            }

            await session.WriteAsync(usageCommand, timeoutCts.Token).ConfigureAwait(false);
            await session.WaitForIdleAsync(QuietPeriod, HardTimeout, timeoutCts.Token).ConfigureAwait(false);
            await session.WriteAsync("\r", timeoutCts.Token).ConfigureAwait(false);
            await session.WaitForIdleAsync(QuietPeriod, HardTimeout, timeoutCts.Token).ConfigureAwait(false);

            var screen = session.RenderScreen();
            var result = ParseScreen(screen);
            if (result.IsSuccess)
            {
                return result;
            }

            var retryDeadline = _timeProvider.GetUtcNow() + ParseRetryWindow;
            try
            {
                while (!result.IsSuccess && _timeProvider.GetUtcNow() < retryDeadline)
                {
                    await Task.Delay(ParseRetryInterval, _timeProvider, timeoutCts.Token).ConfigureAwait(false);
                    screen = session.RenderScreen();
                    result = ParseScreen(screen);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            return result.IsSuccess ? result : AppendScreenHint(result, screen);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, $"CLI 세션과 통신 중 오류가 발생했습니다: {ex.Message}");
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private UsageResult ParseScreen(string screen)
    {
        var observedAt = _timeProvider.GetUtcNow();
        return _tool == Tool.Claude
            ? ClaudeUsageScreenParser.Parse(screen, observedAt)
            : CodexStatusScreenParser.Parse(screen, observedAt);
    }

    private static UsageResult AppendScreenHint(UsageResult failure, string screen)
    {
        foreach (var rawLine in screen.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || !HintPattern.IsMatch(line))
            {
                continue;
            }

            if (line.Length > HintMaxLength)
            {
                line = line[..HintMaxLength] + "…";
            }

            return UsageResult.Failure(failure.FailureKind!.Value, $"{failure.Message} — 화면: \"{line}\"", failure.FallbackMessage);
        }

        return failure;
    }

    private static ICliSession CreateDefaultSession(string executable, string workingDirectory) =>
        ConPtyCliSession.Start($"cmd.exe /c {executable}", workingDirectory);

    public static string ResolveDefaultWorkingDirectory()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TokenMonitor", "cli-workdir");
    }
}
