using TokenMonitor.Core;

namespace TokenMonitor.Tests.Core;

public class PollingScheduleTests
{
    [Fact]
    public void EffectiveInterval_ClampsToMinimumInterval()
    {
        var options = new PollingOptions { Interval = TimeSpan.FromSeconds(10), MinimumInterval = TimeSpan.FromSeconds(60) };
        var schedule = new PollingSchedule(options);

        Assert.Equal(TimeSpan.FromSeconds(60), schedule.EffectiveInterval);
    }

    [Fact]
    public void Next_DoublesAndClampsBackoff_OnRepeatedRateLimited()
    {
        var options = new PollingOptions
        {
            Interval = TimeSpan.FromSeconds(1),
            MinimumInterval = TimeSpan.FromSeconds(1),
            InitialBackoff = TimeSpan.FromSeconds(10),
            MaximumBackoff = TimeSpan.FromSeconds(30),
        };
        var schedule = new PollingSchedule(options);
        var rateLimited = UsageResult.Failure(UsageFailureKind.RateLimited, "429");

        var first = schedule.Next(rateLimited);
        var second = schedule.Next(rateLimited);
        var third = schedule.Next(rateLimited);

        Assert.Equal(TimeSpan.FromSeconds(10), first);
        Assert.Equal(TimeSpan.FromSeconds(20), second);
        Assert.Equal(TimeSpan.FromSeconds(30), third);
    }

    [Fact]
    public void Next_DoublesFromEffectiveInterval_OnRepeatedRateLimited_WithDefaultOptions()
    {
        var schedule = new PollingSchedule(new PollingOptions());
        var rateLimited = UsageResult.Failure(UsageFailureKind.RateLimited, "429");

        var first = schedule.Next(rateLimited);
        var second = schedule.Next(rateLimited);
        var third = schedule.Next(rateLimited);
        var fourth = schedule.Next(rateLimited);
        var fifth = schedule.Next(rateLimited);

        Assert.Equal(TimeSpan.FromMinutes(3), first);
        Assert.Equal(TimeSpan.FromMinutes(6), second);
        Assert.Equal(TimeSpan.FromMinutes(12), third);
        Assert.Equal(TimeSpan.FromMinutes(24), fourth);
        Assert.Equal(TimeSpan.FromMinutes(30), fifth);
    }

    [Fact]
    public void Next_ResetsBackoff_OnSuccessAfterRateLimited()
    {
        var options = new PollingOptions
        {
            Interval = TimeSpan.FromSeconds(5),
            MinimumInterval = TimeSpan.FromSeconds(1),
            InitialBackoff = TimeSpan.FromSeconds(10),
            MaximumBackoff = TimeSpan.FromSeconds(30),
        };
        var schedule = new PollingSchedule(options);

        schedule.Next(UsageResult.Failure(UsageFailureKind.RateLimited, "429"));
        var afterSuccess = schedule.Next(UsageResult.Success([]));
        var afterAnotherRateLimit = schedule.Next(UsageResult.Failure(UsageFailureKind.RateLimited, "429"));

        Assert.Equal(TimeSpan.FromSeconds(5), afterSuccess);
        Assert.Equal(TimeSpan.FromSeconds(10), afterAnotherRateLimit);
    }

    [Fact]
    public void Next_ReturnsEffectiveInterval_ForNonRateLimitedFailure()
    {
        var options = new PollingOptions { Interval = TimeSpan.FromMinutes(3), MinimumInterval = TimeSpan.FromSeconds(60) };
        var schedule = new PollingSchedule(options);

        var delay = schedule.Next(UsageResult.Failure(UsageFailureKind.Unavailable, "boom"));

        Assert.Equal(schedule.EffectiveInterval, delay);
    }

    [Fact]
    public void EffectiveInterval_DoesNotClamp_WhenIntervalAtOrAboveMinimum()
    {
        var options = new PollingOptions { Interval = TimeSpan.FromSeconds(3), MinimumInterval = TimeSpan.FromSeconds(3) };
        var schedule = new PollingSchedule(options);

        Assert.Equal(TimeSpan.FromSeconds(3), schedule.EffectiveInterval);
    }

    [Fact]
    public void UpdateInterval_ChangesEffectiveInterval_ForSubsequentDelay()
    {
        var options = new PollingOptions { Interval = TimeSpan.FromMinutes(3), MinimumInterval = TimeSpan.FromSeconds(60) };
        var schedule = new PollingSchedule(options);

        schedule.UpdateInterval(TimeSpan.FromMinutes(10));
        var delay = schedule.Next(UsageResult.Success([]));

        Assert.Equal(TimeSpan.FromMinutes(10), delay);
    }

    [Fact]
    public void UpdateInterval_DoesNotResetActiveBackoff()
    {
        var options = new PollingOptions
        {
            Interval = TimeSpan.FromSeconds(1),
            MinimumInterval = TimeSpan.FromSeconds(1),
            InitialBackoff = TimeSpan.FromSeconds(10),
            MaximumBackoff = TimeSpan.FromSeconds(30),
        };
        var schedule = new PollingSchedule(options);
        var rateLimited = UsageResult.Failure(UsageFailureKind.RateLimited, "429");

        schedule.Next(rateLimited);
        schedule.UpdateInterval(TimeSpan.FromSeconds(5));
        var second = schedule.Next(rateLimited);

        Assert.Equal(TimeSpan.FromSeconds(20), second);
    }

    [Fact]
    public void UpdateInterval_Throws_WhenNotPositive()
    {
        var schedule = new PollingSchedule(new PollingOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => schedule.UpdateInterval(TimeSpan.Zero));
    }

    [Fact]
    public void Constructor_Throws_WhenMaximumBackoffLessThanInitialBackoff()
    {
        var options = new PollingOptions { InitialBackoff = TimeSpan.FromMinutes(5), MaximumBackoff = TimeSpan.FromMinutes(1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new PollingSchedule(options));
    }
}
