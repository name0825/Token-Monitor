using TokenMonitor.Core;
using TokenMonitor.Providers.Cli;

namespace TokenMonitor.Tests.Providers;

public class ClaudeUsageScreenParserTests
{
    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cli", "claude-usage.screen.txt"));

    [Fact]
    public void Parse_ExtractsFiveHourAndWeeklyFromFixture()
    {
        var observedAt = new DateTimeOffset(2026, 9, 16, 5, 0, 0, TimeSpan.Zero); // 2026-09-16 14:00 Asia/Seoul

        var result = ClaudeUsageScreenParser.Parse(ReadFixture(), observedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);

        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(83, fiveHour.UsedPercent);
        Assert.Equal(UsageOrigin.Cli, fiveHour.Origin);
        Assert.Equal(Tool.Claude, fiveHour.Tool);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 20, 10, 0, TimeSpan.FromHours(9)), fiveHour.ResetsAt);

        var weekly = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.Weekly);
        Assert.Equal(48, weekly.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 22, 0, 0, TimeSpan.FromHours(9)), weekly.ResetsAt);
    }

    [Fact]
    public void Parse_TimeOnlyReset_RollsOverToNextDay_WhenAlreadyPast()
    {
        var screen = "Current session\n50% used\nResets 1am (Asia/Seoul)";
        var observedAt = new DateTimeOffset(2026, 9, 16, 1, 0, 0, TimeSpan.Zero); // 2026-09-16 10:00 Asia/Seoul

        var result = ClaudeUsageScreenParser.Parse(screen, observedAt);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 1, 0, 0, TimeSpan.FromHours(9)), snapshot.ResetsAt);
    }

    [Fact]
    public void Parse_DatedReset_RollsOverToNextYear_WhenDateAlreadyPast()
    {
        var screen = "Current week (all models)\n30% used\nResets Jan 1, 12am (Asia/Seoul)";
        var observedAt = new DateTimeOffset(2026, 9, 16, 5, 0, 0, TimeSpan.Zero); // 2026-09-16 Asia/Seoul

        var result = ClaudeUsageScreenParser.Parse(screen, observedAt);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)), snapshot.ResetsAt);
    }

    [Fact]
    public void Parse_UnknownZone_FallsBackToInjectedLocalZone()
    {
        var localZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
        var screen = "Current session\n50% used\nResets 1am (Nonexistent/Zone)";
        var observedAt = new DateTimeOffset(2026, 9, 16, 1, 0, 0, TimeSpan.Zero); // 2026-09-16 10:00 Asia/Seoul

        var result = ClaudeUsageScreenParser.Parse(screen, observedAt, localZone);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 1, 0, 0, TimeSpan.FromHours(9)), snapshot.ResetsAt);
    }

    [Fact]
    public void Parse_ResetInDstSpringForwardGap_DoesNotThrow_AndYieldsSensibleInstant()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var screen = "Current session\n50% used\nResets 2:30am (America/New_York)";
        var observedAt = new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero); // 2026-03-08 00:00 America/New_York (EST), spring-forward at 2am

        var result = ClaudeUsageScreenParser.Parse(screen, observedAt, zone);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.NotNull(snapshot.ResetsAt);
        Assert.True(snapshot.ResetsAt!.Value.Offset is { Hours: -5 } or { Hours: -4 });
    }

    [Fact]
    public void Parse_DoesNotMatchSubagentPercentTable()
    {
        var screen = "Current session\n40% used\nResets 8pm (Asia/Seoul)\n\nSubagents               % of usage\ncode-editor-sonnet-high        15%";

        var result = ClaudeUsageScreenParser.Parse(screen, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(40, snapshot.UsedPercent);
    }

    [Fact]
    public void Parse_ReturnsInvalidData_ForGarbageInput()
    {
        var result = ClaudeUsageScreenParser.Parse("nothing useful here at all", DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.InvalidData, result.FailureKind);
    }

    [Fact]
    public void Parse_ReturnsInvalidData_ForEmptyInput()
    {
        var result = ClaudeUsageScreenParser.Parse(string.Empty, DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.InvalidData, result.FailureKind);
    }
}
