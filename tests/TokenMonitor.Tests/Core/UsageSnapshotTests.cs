using TokenMonitor.Core;

namespace TokenMonitor.Tests.Core;

public class UsageSnapshotTests
{
    [Fact]
    public void AsOf_ResetsUsedPercentAndClearsResetsAt_WhenResetsAtIsInPast()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new UsageSnapshot(Tool.Claude, UsageWindow.FiveHour, 50, now.AddMinutes(-1), now.AddHours(-1), UsageOrigin.Api);

        var result = snapshot.AsOf(now);

        Assert.Equal(0, result.UsedPercent);
        Assert.Null(result.ResetsAt);
    }

    [Fact]
    public void AsOf_ReturnsUnchanged_WhenResetsAtIsInFuture()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new UsageSnapshot(Tool.Claude, UsageWindow.FiveHour, 50, now.AddHours(1), now, UsageOrigin.Api);

        var result = snapshot.AsOf(now);

        Assert.Equal(snapshot, result);
    }

    [Fact]
    public void AsOf_ReturnsUnchanged_WhenResetsAtIsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new UsageSnapshot(Tool.Claude, UsageWindow.FiveHour, 50, null, now, UsageOrigin.Api);

        var result = snapshot.AsOf(now);

        Assert.Equal(snapshot, result);
    }
}
