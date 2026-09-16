using TokenMonitor.Core;

namespace TokenMonitor.Tests.Core;

public class UsageFormattingTests
{
    [Theory]
    [InlineData(42.4, "42%")]
    [InlineData(42.5, "43%")]
    [InlineData(0, "0%")]
    [InlineData(100, "100%")]
    [InlineData(-5, "0%")]
    [InlineData(150, "100%")]
    public void FormatPercent_ClampsAndRounds(double usedPercent, string expected)
    {
        Assert.Equal(expected, UsageFormatting.FormatPercent(usedPercent));
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void FormatPercent_ReturnsDash_WhenNotFinite(double usedPercent)
    {
        Assert.Equal("—", UsageFormatting.FormatPercent(usedPercent));
    }

    public static IEnumerable<object[]> NonFiniteValues()
    {
        yield return new object[] { double.NaN };
        yield return new object[] { double.PositiveInfinity };
        yield return new object[] { double.NegativeInfinity };
    }

    [Fact]
    public void FormatRemaining_ReturnsDash_WhenResetsAtIsNull()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("—", UsageFormatting.FormatRemaining(null, now));
    }

    [Fact]
    public void FormatRemaining_ReturnsResetLabel_WhenResetsAtEqualsNow()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("리셋됨", UsageFormatting.FormatRemaining(now, now));
    }

    [Fact]
    public void FormatRemaining_ReturnsResetLabel_WhenResetsAtIsInPast()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("리셋됨", UsageFormatting.FormatRemaining(now.AddMinutes(-1), now));
    }

    [Fact]
    public void FormatRemaining_ReturnsDaysAndHours_WhenAtLeastOneDay()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("2d3h", UsageFormatting.FormatRemaining(now.AddDays(2).AddHours(3), now));
    }

    [Fact]
    public void FormatRemaining_ReturnsDaysAndHours_AtExactlyOneDayBoundary()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("1d0h", UsageFormatting.FormatRemaining(now.AddDays(1), now));
    }

    [Fact]
    public void FormatRemaining_ReturnsHoursAndMinutes_WhenUnderOneDay()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("2h05m", UsageFormatting.FormatRemaining(now.AddHours(2).AddMinutes(5), now));
    }

    [Fact]
    public void FormatRemaining_ReturnsHoursAndMinutes_AtExactlyOneHourBoundary()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("1h00m", UsageFormatting.FormatRemaining(now.AddHours(1), now));
    }

    [Fact]
    public void FormatRemaining_ReturnsMinutes_WhenUnderOneHour()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("47m", UsageFormatting.FormatRemaining(now.AddMinutes(47), now));
    }

    [Fact]
    public void FormatRemaining_ReturnsMinutes_AtExactlyOneMinuteBoundary()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("1m", UsageFormatting.FormatRemaining(now.AddMinutes(1), now));
    }

    [Fact]
    public void FormatRemaining_ReturnsUnderOneMinute_WhenLessThanOneMinute()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("<1m", UsageFormatting.FormatRemaining(now.AddSeconds(30), now));
    }

    [Fact]
    public void FormatFreshness_ReturnsJustNow_WhenAgeIsNegative()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("방금", UsageFormatting.FormatFreshness(now.AddSeconds(5), now));
    }

    [Fact]
    public void FormatFreshness_ReturnsJustNow_WhenUnderOneMinute()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("방금", UsageFormatting.FormatFreshness(now.AddSeconds(-59), now));
    }

    [Fact]
    public void FormatFreshness_ReturnsMinutes_AtExactlySixtySeconds()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("1분 전", UsageFormatting.FormatFreshness(now.AddSeconds(-60), now));
    }

    [Fact]
    public void FormatFreshness_ReturnsMinutes_UnderOneHour()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("30분 전", UsageFormatting.FormatFreshness(now.AddMinutes(-30), now));
    }

    [Fact]
    public void FormatFreshness_ReturnsHours_AtExactlyOneHourBoundary()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("1시간 전", UsageFormatting.FormatFreshness(now.AddHours(-1), now));
    }

    [Fact]
    public void FormatFreshness_ReturnsHours_UnderOneDay()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("5시간 전", UsageFormatting.FormatFreshness(now.AddHours(-5), now));
    }

    [Fact]
    public void FormatFreshness_ReturnsDays_AtExactlyOneDayBoundary()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("1일 전", UsageFormatting.FormatFreshness(now.AddHours(-24), now));
    }

    [Fact]
    public void FormatFreshness_ReturnsDays_WhenSeveralDaysOld()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("3일 전", UsageFormatting.FormatFreshness(now.AddDays(-3), now));
    }

    [Theory]
    [InlineData(UsageOrigin.Api, "API")]
    [InlineData(UsageOrigin.Log, "로그")]
    [InlineData(UsageOrigin.Cli, "CLI")]
    public void FormatOrigin_ReturnsExpectedLabel(UsageOrigin origin, string expected)
    {
        Assert.Equal(expected, UsageFormatting.FormatOrigin(origin));
    }
}
