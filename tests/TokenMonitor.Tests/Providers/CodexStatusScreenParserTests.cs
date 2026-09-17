using TokenMonitor.Core;
using TokenMonitor.Providers.Cli;

namespace TokenMonitor.Tests.Providers;

public class CodexStatusScreenParserTests
{
    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cli", "codex-status.screen.txt"));

    [Fact]
    public void Parse_ExtractsFiveHourAndWeeklyFromFixture()
    {
        var observedAt = new DateTimeOffset(2026, 9, 16, 20, 0, 0, TimeSpan.Zero);

        var result = CodexStatusScreenParser.Parse(ReadFixture(), observedAt, TimeZoneInfo.Utc);

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
