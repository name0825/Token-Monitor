using TokenMonitor.Providers.Cli;

namespace TokenMonitor.Tests.Cli;

public class CliResetTimeResolverTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void ExplicitMonthDay_EqualToTodayButTimeAlreadyPassed_RollsToNextYear()
    {
        var observedAt = new DateTimeOffset(2026, 3, 15, 18, 0, 0, TimeSpan.Zero);

        var result = CliResetTimeResolver.ResolveNextOccurrence(observedAt, Utc, hour: 9, minute: 0, month: 3, day: 15);

        Assert.Equal(new DateTimeOffset(2027, 3, 15, 9, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ExplicitMonthDay_Today_TimeInFuture_ReturnsToday()
    {
        var observedAt = new DateTimeOffset(2026, 3, 15, 6, 0, 0, TimeSpan.Zero);

        var result = CliResetTimeResolver.ResolveNextOccurrence(observedAt, Utc, hour: 9, minute: 0, month: 3, day: 15);

        Assert.Equal(new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero), result);
    }
}
