using TokenMonitor.Core;
using TokenMonitor.Tests.TestSupport;

namespace TokenMonitor.Tests.Core;

public class FallbackUsageProviderStalenessTests
{
    private static UsageSnapshot Snapshot(Tool tool, UsageOrigin origin, DateTimeOffset observedAt) =>
        new(tool, UsageWindow.FiveHour, 10, null, observedAt, origin);

    [Fact]
    public async Task GetUsageAsync_ReturnsPrimary_WhenPrimaryFresh()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var primary = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Success([Snapshot(Tool.Codex, UsageOrigin.Log, now - TimeSpan.FromMinutes(1))])));
        var fallback = new FakeUsageProvider(Tool.Codex, ct => throw new InvalidOperationException("fallback should not be called"));
        var sut = new FallbackUsageProvider(primary, fallback, TimeSpan.FromMinutes(10), timeProvider);

        var result = await sut.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UsageOrigin.Log, result.Snapshots![0].Origin);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsFallback_WhenPrimaryStaleAndFallbackNewer()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var primary = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Success([Snapshot(Tool.Codex, UsageOrigin.Log, now - TimeSpan.FromMinutes(20))])));
        var fallback = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Success([Snapshot(Tool.Codex, UsageOrigin.Cli, now - TimeSpan.FromMinutes(1))])));
        var sut = new FallbackUsageProvider(primary, fallback, TimeSpan.FromMinutes(10), timeProvider);

        var result = await sut.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UsageOrigin.Cli, result.Snapshots![0].Origin);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsPrimary_WhenPrimaryStaleAndFallbackFails()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var primary = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Success([Snapshot(Tool.Codex, UsageOrigin.Log, now - TimeSpan.FromMinutes(20))])));
        var fallback = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Failure(UsageFailureKind.RateLimited, "throttled")));
        var sut = new FallbackUsageProvider(primary, fallback, TimeSpan.FromMinutes(10), timeProvider);

        var result = await sut.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UsageOrigin.Log, result.Snapshots![0].Origin);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsPrimary_WhenPrimaryStaleAndFallbackOlder()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var primary = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Success([Snapshot(Tool.Codex, UsageOrigin.Log, now - TimeSpan.FromMinutes(20))])));
        var fallback = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Success([Snapshot(Tool.Codex, UsageOrigin.Cli, now - TimeSpan.FromMinutes(25))])));
        var sut = new FallbackUsageProvider(primary, fallback, TimeSpan.FromMinutes(10), timeProvider);

        var result = await sut.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UsageOrigin.Log, result.Snapshots![0].Origin);
    }

    [Fact]
    public void Constructor_Throws_WhenMaxPrimaryAgeNotPositive()
    {
        var primary = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Success([])));
        var fallback = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Success([])));

        Assert.Throws<ArgumentOutOfRangeException>(() => new FallbackUsageProvider(primary, fallback, TimeSpan.Zero, new FakeTimeProvider(DateTimeOffset.UtcNow)));
    }
}

public class FallbackUsageProviderTests
{
    private static UsageSnapshot Snapshot(Tool tool, UsageOrigin origin) =>
        new(tool, UsageWindow.FiveHour, 10, null, DateTimeOffset.UtcNow, origin);

    [Fact]
    public async Task GetUsageAsync_ReturnsPrimaryResult_WhenPrimarySucceeds()
    {
        var primary = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(UsageResult.Success([Snapshot(Tool.Claude, UsageOrigin.Api)])));
        var fallback = new FakeUsageProvider(Tool.Claude, ct => throw new InvalidOperationException("fallback should not be called"));
        var sut = new FallbackUsageProvider(primary, fallback);

        var result = await sut.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UsageOrigin.Api, result.Snapshots![0].Origin);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsFallbackResult_WhenPrimaryFailsAndFallbackSucceeds()
    {
        var primary = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(UsageResult.Failure(UsageFailureKind.Unauthorized, "expired")));
        var fallback = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(UsageResult.Success([Snapshot(Tool.Claude, UsageOrigin.Cli)])));
        var sut = new FallbackUsageProvider(primary, fallback);

        var result = await sut.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UsageOrigin.Cli, result.Snapshots![0].Origin);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsPrimaryFailureKind_WhenBothFail()
    {
        var primary = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(UsageResult.Failure(UsageFailureKind.Unauthorized, "expired")));
        var fallback = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(UsageResult.Failure(UsageFailureKind.Unavailable, "no cli")));
        var sut = new FallbackUsageProvider(primary, fallback);

        var result = await sut.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unauthorized, result.FailureKind);
    }

    [Fact]
    public async Task GetUsageAsync_Throws_WhenCancelled()
    {
        var primary = new FakeUsageProvider(Tool.Claude, ct => throw new OperationCanceledException(ct));
        var fallback = new FakeUsageProvider(Tool.Claude, ct => throw new InvalidOperationException("fallback should not be called"));
        var sut = new FallbackUsageProvider(primary, fallback);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetUsageAsync(cts.Token));
    }

    [Fact]
    public void Constructor_Throws_WhenToolsMismatch()
    {
        var primary = new FakeUsageProvider(Tool.Claude, ct => Task.FromResult(UsageResult.Success([])));
        var fallback = new FakeUsageProvider(Tool.Codex, ct => Task.FromResult(UsageResult.Success([])));

        Assert.Throws<ArgumentException>(() => new FallbackUsageProvider(primary, fallback));
    }

    [Fact]
    public async Task GetUsageAsync_ConvertsProviderException_ToUnavailableFailure()
    {
        var primary = new FakeUsageProvider(Tool.Claude, ct => throw new InvalidOperationException("boom"));
        var fallback = new FakeUsageProvider(Tool.Claude, ct => throw new InvalidOperationException("boom2"));
        var sut = new FallbackUsageProvider(primary, fallback);

        var result = await sut.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unavailable, result.FailureKind);
    }
}
