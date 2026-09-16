using TokenMonitor.Core;

namespace TokenMonitor.Providers.Cli;

public delegate ICliSession CliSessionFactory(string executable, string workingDirectory);

public sealed class CliScrapeProvider : IUsageProvider
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(750);

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
            if (_lastRunAt is { } lastRunAt && now - lastRunAt < MinimumInterval)
            {
                // Self-imposed throttle, mirrors how the real APIs would rate-limit repeated requests.
                return _lastResult ?? UsageResult.Failure(UsageFailureKind.RateLimited, "CLI 폴백은 최소 10분 간격으로만 실행됩니다");
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
                return UsageResult.Failure(UsageFailureKind.Unavailable, $"CLI 폴백 설정 필요: {promptIssue}");
            }

            await session.WriteAsync(usageCommand, timeoutCts.Token).ConfigureAwait(false);
            await session.WriteAsync("\r", timeoutCts.Token).ConfigureAwait(false);
            await session.WaitForIdleAsync(QuietPeriod, HardTimeout, timeoutCts.Token).ConfigureAwait(false);

            var screen = session.RenderScreen();
            var observedAt = _timeProvider.GetUtcNow();

            return _tool == Tool.Claude
                ? ClaudeUsageScreenParser.Parse(screen, observedAt)
                : CodexStatusScreenParser.Parse(screen, observedAt);
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

    private static ICliSession CreateDefaultSession(string executable, string workingDirectory) =>
        ConPtyCliSession.Start($"cmd.exe /c {executable}", workingDirectory);

    public static string ResolveDefaultWorkingDirectory()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TokenMonitor", "cli-workdir");
    }
}
