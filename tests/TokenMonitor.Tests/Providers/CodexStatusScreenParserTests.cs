using TokenMonitor.Core;
using TokenMonitor.Providers.Cli;

namespace TokenMonitor.Tests.Providers;

public class CodexStatusScreenParserTests
{
    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cli", fileName));

    [Fact]
    public void Parse_ExtractsFiveHourAndWeeklyFromFixture()
    {
        var observedAt = new DateTimeOffset(2026, 9, 16, 20, 0, 0, TimeSpan.Zero);

        var result = CodexStatusScreenParser.Parse(ReadFixture("codex-status.screen.txt"), observedAt, TimeZoneInfo.Utc);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);

        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(0, fiveHour.UsedPercent);
        Assert.Equal(Tool.Codex, fiveHour.Tool);
        Assert.Equal(UsageOrigin.Cli, fiveHour.Origin);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 21, 44, 0, TimeSpan.Zero), fiveHour.ResetsAt);

        var weekly = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.Weekly);
        Assert.Equal(80, weekly.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 21, 2, 0, TimeSpan.Zero), weekly.ResetsAt);
    }

    [Theory]
    [InlineData("codex-status-v0.157.1-120.screen.txt", 68)]
    [InlineData("codex-status-v0.157.1-80.screen.txt", 69)]
    public void Parse_ExtractsCurrentStatusCaptures(string fileName, double expectedFiveHourUsedPercent)
    {
        var observedAt = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);

        var result = CodexStatusScreenParser.Parse(ReadFixture(fileName), observedAt, TimeZoneInfo.Utc);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(expectedFiveHourUsedPercent, fiveHour.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 18, 16, 0, TimeSpan.Zero), fiveHour.ResetsAt);
        var weekly = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.Weekly);
        Assert.Equal(11, weekly.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 13, 16, 0, TimeSpan.Zero), weekly.ResetsAt);
    }

    [Theory]
    [InlineData("12:05 AM", 0, 5)]
    [InlineData("12:30 PM", 12, 30)]
    public void Parse_ConvertsTwelveHourBoundaries(string resetTime, int expectedHour, int expectedMinute)
    {
        var screen = $"5h limit:  40% left (resets {resetTime})";
        var observedAt = new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);

        var result = CodexStatusScreenParser.Parse(screen, observedAt, TimeZoneInfo.Utc);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, expectedHour, expectedMinute, 0, TimeSpan.Zero), snapshot.ResetsAt);
    }

    [Fact]
    public void Parse_SkipsInvalidTwelveHourLine()
    {
        var screen = "5h limit:  40% left (resets 13:00 PM)\nWeekly limit:  60% left (resets 1:16 PM on 4 Oct)";
        var observedAt = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);

        var result = CodexStatusScreenParser.Parse(screen, observedAt, TimeZoneInfo.Utc);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(UsageWindow.Weekly, snapshot.Window);
    }

    [Fact]
    public void Parse_TimeOnlyReset_RollsOverToNextDay_WhenAlreadyPast()
    {
        var screen = "5h limit:  [████] 70% left (resets 05:00)";
        var observedAt = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

        var result = CodexStatusScreenParser.Parse(screen, observedAt, TimeZoneInfo.Utc);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(30, snapshot.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 5, 0, 0, TimeSpan.Zero), snapshot.ResetsAt);
    }

    [Fact]
    public void Parse_DatedReset_RollsOverToNextYear_WhenDateAlreadyPast()
    {
        var screen = "Weekly limit:  [████] 60% left (resets 00:00 on 1 Jan)";
        var observedAt = new DateTimeOffset(2026, 9, 16, 5, 0, 0, TimeSpan.Zero);

        var result = CodexStatusScreenParser.Parse(screen, observedAt, TimeZoneInfo.Utc);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), snapshot.ResetsAt);
    }

    [Fact]
    public void Parse_DuplicateFiveHourLine_KeepsLastValueOnly()
    {
        var screen = "5h limit:  [████] 70% left (resets 05:00)\n5h limit:  [██] 40% left (resets 05:00)";
        var observedAt = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);

        var result = CodexStatusScreenParser.Parse(screen, observedAt, TimeZoneInfo.Utc);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(60, snapshot.UsedPercent);
    }

    [Fact]
    public void Parse_ReturnsInvalidData_ForGarbageInput()
    {
        var result = CodexStatusScreenParser.Parse("nothing useful here at all", DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.InvalidData, result.FailureKind);
    }

    [Fact]
    public void Parse_ReturnsInvalidData_ForEmptyInput()
    {
        var result = CodexStatusScreenParser.Parse(string.Empty, DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.InvalidData, result.FailureKind);
    }
}
