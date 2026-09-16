using TokenMonitor.Core;
using TokenMonitor.Tests.TestSupport;

namespace TokenMonitor.Tests.Core;

public class UsagePollerTests
{
    private static UsageResult SuccessResult() =>
        UsageResult.Success([new UsageSnapshot(Tool.Claude, UsageWindow.FiveHour, 10, null, DateTimeOffset.UtcNow, UsageOrigin.Api)]);

    private static PollingOptions LongInterval() => new()
    {
        Interval = TimeSpan.FromMinutes(30),
        MinimumInterval = TimeSpan.FromMinutes(30),
    };

    [Fact]
    public async Task Start_PopulatesLatestAndRaisesUpdated_OnFirstPoll()
    {
        var provider = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(SuccessResult()));
        await using var poller = new UsagePoller(provider, LongInterval());

        var tcs = new TaskCompletionSource<UsageResult>();
        poller.Updated += (_, result) => tcs.TrySetResult(result);

        poller.Start();
        var raised = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(raised.IsSuccess);
        Assert.NotNull(poller.Latest);
        Assert.True(poller.Latest!.IsSuccess);
        Assert.NotNull(poller.LatestAt);
    }

    [Fact]
    public void Start_IsIdempotent_WhenCalledTwice()
    {
        var provider = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(SuccessResult()));
        var poller = new UsagePoller(provider, LongInterval());

        poller.Start();
        var exception = Record.Exception(() => poller.Start());

        Assert.Null(exception);
    }

    [Fact]
    public async Task RefreshAsync_ReturnsResultAndUpdatesLatest()
    {
        var provider = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(SuccessResult()));
        await using var poller = new UsagePoller(provider, LongInterval());

        var result = await poller.RefreshAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(result, poller.Latest);
        Assert.NotNull(poller.LatestAt);
    }

    [Fact]
    public async Task DisposeAsync_IsSafeAndIdempotent()
    {
        var provider = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(SuccessResult()));
        var poller = new UsagePoller(provider, LongInterval());
        poller.Start();

        await poller.DisposeAsync();
        var exception = await Record.ExceptionAsync(() => poller.DisposeAsync().AsTask());

        Assert.Null(exception);
    }

    [Fact]
    public async Task GetUsageAsync_ExceptionFromProvider_BecomesUnavailableFailure()
    {
        var provider = new FakeUsageProvider(Tool.Claude, ct => throw new InvalidOperationException("boom"));
        await using var poller = new UsagePoller(provider, LongInterval());

        var result = await poller.RefreshAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unavailable, result.FailureKind);
    }

    private static PollingOptions ShortInterval() => new()
    {
        Interval = TimeSpan.FromMilliseconds(50),
        MinimumInterval = TimeSpan.FromMilliseconds(50),
    };

    [Fact]
    public async Task Stop_PreventsFurtherPolls_AfterTimeAdvances()
    {
        var callCount = 0;
        var provider = new FakeUsageProvider(Tool.Claude, ct =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(SuccessResult());
        });
        await using var poller = new UsagePoller(provider, ShortInterval());

        var firstPoll = new TaskCompletionSource<UsageResult>();
        poller.Updated += (_, result) => firstPoll.TrySetResult(result);

        poller.Start();
        await firstPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));
        poller.Stop();

        var countAfterStop = Volatile.Read(ref callCount);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.Equal(countAfterStop, Volatile.Read(ref callCount));
    }

    [Fact]
    public async Task Start_AfterStop_PollsAgain()
    {
        var callCount = 0;
        var provider = new FakeUsageProvider(Tool.Claude, ct =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(SuccessResult());
        });
        await using var poller = new UsagePoller(provider, LongInterval());

        var firstPoll = new TaskCompletionSource<UsageResult>();
        poller.Updated += (_, result) => firstPoll.TrySetResult(result);

        poller.Start();
        await firstPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));
        poller.Stop();

        var secondPoll = new TaskCompletionSource<UsageResult>();
        poller.Updated += (_, result) => secondPoll.TrySetResult(result);

        poller.Start();
        await secondPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, Volatile.Read(ref callCount));
    }

    [Fact]
    public async Task UpdateInterval_WakesPendingDelay_AndAppliesNewInterval()
    {
        var callCount = 0;
        var provider = new FakeUsageProvider(Tool.Claude, ct =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(SuccessResult());
        });
        await using var poller = new UsagePoller(provider, LongInterval());

        var firstPoll = new TaskCompletionSource<UsageResult>();
        poller.Updated += (_, result) => firstPoll.TrySetResult(result);

        poller.Start();
        await firstPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondPoll = new TaskCompletionSource<UsageResult>();
        poller.Updated += (_, result) => secondPoll.TrySetResult(result);

        poller.UpdateInterval(TimeSpan.FromMilliseconds(50));
        await secondPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, Volatile.Read(ref callCount));
    }

    [Fact]
    public void Stop_IsIdempotent()
    {
        var provider = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(SuccessResult()));
        var poller = new UsagePoller(provider, LongInterval());

        var exception = Record.Exception(() =>
        {
            poller.Stop();
            poller.Start();
            poller.Stop();
            poller.Stop();
        });

        Assert.Null(exception);
    }
}
