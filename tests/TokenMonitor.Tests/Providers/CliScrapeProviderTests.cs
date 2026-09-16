using TokenMonitor.Core;
using TokenMonitor.Providers.Cli;
using TokenMonitor.Tests.TestSupport;

namespace TokenMonitor.Tests.Providers;

public class CliScrapeProviderTests
{
    private const string CodexNormalStartup = "OpenAI Codex (v0.153.4)\n\n› Ask Codex to do anything";

    private static string ReadCodexFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cli", "codex-status.screen.txt"));

    [Fact]
    public async Task GetUsageAsync_ParsesScreen_AndSendsUsageCommandThenEnter()
    {
        using var tempDirectory = new TempDirectory();
        var session = new FakeCliSession(CodexNormalStartup, ReadCodexFixture());
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Codex, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);
        Assert.Equal(["/status", "\r"], session.Writes);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task GetUsageAsync_AbortsWithoutSendingKeys_WhenTrustPromptDetected()
    {
        using var tempDirectory = new TempDirectory();
        var session = new FakeCliSession("Do you trust the files in this folder?\n1. Yes\n2. No", string.Empty);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Claude, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unavailable, result.FailureKind);
        Assert.Contains("CLI 폴백 설정 필요", result.Message);
        Assert.Empty(session.Writes);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsCachedResult_WithinMinimumInterval_WithoutStartingNewSession()
    {
        using var tempDirectory = new TempDirectory();
        var callCount = 0;
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Codex, tempDirectory.Path, (_, _) =>
        {
            callCount++;
            return new FakeCliSession(CodexNormalStartup, ReadCodexFixture());
        }, timeProvider);

        var first = await provider.GetUsageAsync(CancellationToken.None);
        timeProvider.Now += TimeSpan.FromMinutes(5);
        var second = await provider.GetUsageAsync(CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.True(first.IsSuccess);
        Assert.Same(first, second);

        timeProvider.Now += TimeSpan.FromMinutes(6);
        var third = await provider.GetUsageAsync(CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.True(third.IsSuccess);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsUnavailableFailure_WhenSessionTimesOut()
    {
        using var tempDirectory = new TempDirectory();
        var session = new FakeCliSession(CodexNormalStartup, ReadCodexFixture())
        {
            OnWaitForIdle = _ => throw new OperationCanceledException(),
        };
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Codex, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unavailable, result.FailureKind);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsUnavailableFailure_WhenSessionThrowsOnWrite()
    {
        using var tempDirectory = new TempDirectory();
        var session = new FakeCliSession(CodexNormalStartup, ReadCodexFixture())
        {
            WriteException = new IOException("pipe broken"),
        };
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Codex, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unavailable, result.FailureKind);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task GetUsageAsync_PropagatesCancellation_WhenCallerCancels()
    {
        using var tempDirectory = new TempDirectory();
        var session = new FakeCliSession(CodexNormalStartup, ReadCodexFixture());
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Codex, tempDirectory.Path, (_, _) => session, timeProvider);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetUsageAsync(cts.Token));
    }
}
