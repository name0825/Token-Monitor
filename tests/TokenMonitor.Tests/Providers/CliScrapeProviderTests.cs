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
        Assert.StartsWith("claude 폴더 신뢰 필요", result.Message);
        Assert.Contains(tempDirectory.Path, result.Message);
        Assert.Empty(session.Writes);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task GetUsageAsync_ReportsUpdateRequired_WithoutSendingKeys_WhenUpdatePromptDetected()
    {
        using var tempDirectory = new TempDirectory();
        var session = new FakeCliSession(ReadCodexFixture(), string.Empty);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Codex, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unavailable, result.FailureKind);
        Assert.StartsWith("codex 업데이트 필요", result.Message);
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
    public async Task GetUsageAsync_RetriesAfterTwoMinutes_FollowingAFailure_ButNotBefore()
    {
        using var tempDirectory = new TempDirectory();
        var callCount = 0;
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Claude, tempDirectory.Path, (_, _) =>
        {
            callCount++;
            return new FakeCliSession("Do you trust the files in this folder?\n1. Yes\n2. No", string.Empty);
        }, timeProvider);

        var first = await provider.GetUsageAsync(CancellationToken.None);
        timeProvider.Now += TimeSpan.FromMinutes(1);
        var second = await provider.GetUsageAsync(CancellationToken.None);

        Assert.Equal(1, callCount);
        Assert.False(first.IsSuccess);
        Assert.Same(first, second);

        timeProvider.Now += TimeSpan.FromMinutes(2);
        var third = await provider.GetUsageAsync(CancellationToken.None);

        Assert.Equal(2, callCount);
        Assert.False(third.IsSuccess);
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

    private static string ReadClaudeFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cli", "claude-usage.screen.txt"));

    [Fact]
    public async Task GetUsageAsync_RetriesParse_UntilScreenFinishesLoading()
    {
        using var tempDirectory = new TempDirectory();
        var session = new SequenceCliSession("Loading usage...", "Loading usage...", ReadClaudeFixture());
        var timeProvider = new AutoAdvanceTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Claude, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);
        Assert.Equal(2, timeProvider.DelayCount);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsOriginalFailure_AfterRetryWindow_WhenScreenNeverParses()
    {
        using var tempDirectory = new TempDirectory();
        var session = new SequenceCliSession("Some unrelated screen");
        var timeProvider = new AutoAdvanceTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Claude, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.InvalidData, result.FailureKind);
        Assert.Equal("No usage sections found in CLI screen", result.Message);
        Assert.Equal(10, timeProvider.DelayCount);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task GetUsageAsync_AppendsScreenHint_WhenFailureScreenHasErrorLine()
    {
        using var tempDirectory = new TempDirectory();
        var session = new SequenceCliSession("  Error: failed to load usage (rate limited)  \nEsc to cancel");
        var timeProvider = new AutoAdvanceTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Claude, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.InvalidData, result.FailureKind);
        Assert.Equal("No usage sections found in CLI screen — 화면: \"Error: failed to load usage (rate limited)\"", result.Message);
    }

    [Fact]
    public async Task GetUsageAsync_TruncatesScreenHint_To80Characters()
    {
        using var tempDirectory = new TempDirectory();
        var session = new SequenceCliSession("Error: " + new string('x', 100));
        var timeProvider = new AutoAdvanceTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Claude, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        var expectedLine = ("Error: " + new string('x', 100))[..80] + "…";
        Assert.Equal($"No usage sections found in CLI screen — 화면: \"{expectedLine}\"", result.Message);
    }

    [Fact]
    public async Task GetUsageAsync_DoesNotAppendScreenHint_WhenNoKeywordMatches()
    {
        using var tempDirectory = new TempDirectory();
        var session = new SequenceCliSession("What's contributing to your limits usage?");
        var timeProvider = new AutoAdvanceTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Claude, tempDirectory.Path, (_, _) => session, timeProvider);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("No usage sections found in CLI screen", result.Message);
    }

    [Fact]
    public async Task GetUsageAsync_PropagatesCancellation_WhenCallerCancelsDuringParseRetry()
    {
        using var tempDirectory = new TempDirectory();
        using var cts = new CancellationTokenSource();
        var session = new SequenceCliSession("Loading usage...") { OnRender = renderCount => { if (renderCount == 2) { cts.Cancel(); } } };
        var timeProvider = new AutoAdvanceTimeProvider(DateTimeOffset.UtcNow);
        var provider = new CliScrapeProvider(Tool.Claude, tempDirectory.Path, (_, _) => session, timeProvider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetUsageAsync(cts.Token));
        Assert.True(session.Disposed);
    }

    private sealed class SequenceCliSession : ICliSession
    {
        private readonly string[] _screens;
        private int _renderCalls;

        public SequenceCliSession(params string[] screens)
        {
            _screens = screens;
        }

        public bool Disposed { get; private set; }

        public Action<int>? OnRender { get; set; }

        public Task WriteAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WaitForIdleAsync(TimeSpan quietPeriod, TimeSpan hardTimeout, CancellationToken cancellationToken) => Task.CompletedTask;

        public string RenderScreen()
        {
            var call = _renderCalls++;
            OnRender?.Invoke(call);
            return call == 0 ? "claude startup" : _screens[Math.Min(call - 1, _screens.Length - 1)];
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AutoAdvanceTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public AutoAdvanceTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public int DelayCount { get; private set; }

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new ImmediateTimer(this, callback, state, dueTime);

        private sealed class ImmediateTimer : ITimer
        {
            private readonly AutoAdvanceTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;

            public ImmediateTimer(AutoAdvanceTimeProvider owner, TimerCallback callback, object? state, TimeSpan dueTime)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                Change(dueTime, Timeout.InfiniteTimeSpan);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    _owner.DelayCount++;
                    _owner._now += dueTime;
                    ThreadPool.QueueUserWorkItem(_ => _callback(_state));
                }

                return true;
            }

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
