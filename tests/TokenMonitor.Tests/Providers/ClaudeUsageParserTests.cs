using TokenMonitor.Core;
using TokenMonitor.Providers.Claude;

namespace TokenMonitor.Tests.Providers;

public class ClaudeUsageParserTests
{
    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude", "usage-response.json"));

    [Fact]
    public void Parse_UsesLimitsArray_WhenPresent()
    {
        var result = ClaudeUsageParser.Parse(ReadFixture(), DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);

        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(17, fiveHour.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-15T20:00:00.029705+00:00"), fiveHour.ResetsAt);
        Assert.Equal(Tool.Claude, fiveHour.Tool);
        Assert.Equal(UsageOrigin.Api, fiveHour.Origin);

        var weekly = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.Weekly);
        Assert.Equal(28, weekly.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-18T13:00:00.029724+00:00"), weekly.ResetsAt);
    }

    [Fact]
    public void Parse_FallsBackToFiveHourAndSevenDay_WhenLimitsMissing()
    {
        var json = """
        {
          "five_hour": { "utilization": 10.0, "resets_at": "2026-09-16T00:00:00+00:00" },
          "seven_day": { "utilization": 20.0, "resets_at": "2026-09-20T00:00:00+00:00" }
        }
        """;

        var result = ClaudeUsageParser.Parse(json, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);

        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(10.0, fiveHour.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-16T00:00:00+00:00"), fiveHour.ResetsAt);

        var weekly = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.Weekly);
        Assert.Equal(20.0, weekly.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-20T00:00:00+00:00"), weekly.ResetsAt);
    }

    [Fact]
    public void Parse_FallsBack_WhenLimitsContainsOnlyUnknownKinds()
    {
        var json = """
        {
          "limits": [ { "kind": "monthly", "percent": 50, "resets_at": null } ],
          "five_hour": { "utilization": 5.0, "resets_at": null },
          "seven_day": { "utilization": 6.0, "resets_at": null }
        }
        """;

        var result = ClaudeUsageParser.Parse(json, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);
        Assert.All(result.Snapshots!, s => Assert.Null(s.ResetsAt));
    }

    [Fact]
    public void Parse_ReturnsInvalidData_ForMalformedJson()
    {
        var result = ClaudeUsageParser.Parse("{ not valid json", DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.InvalidData, result.FailureKind);
    }

    [Fact]
    public void Parse_ReturnsInvalidData_WhenNothingUsable()
    {
        var result = ClaudeUsageParser.Parse("{}", DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.InvalidData, result.FailureKind);
    }

    [Fact]
    public void Parse_SkipsLimit_WhenPercentIsString()
    {
        var json = """
        {
          "limits": [
            { "kind": "session", "percent": "50", "resets_at": null },
            { "kind": "weekly_all", "percent": 28, "resets_at": null }
          ]
        }
        """;

        var result = ClaudeUsageParser.Parse(json, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(UsageWindow.Weekly, snapshot.Window);
        Assert.Equal(28, snapshot.UsedPercent);
    }

    [Fact]
    public void Parse_SetsNullResetsAt_WhenResetsAtIsUnparsable()
    {
        var json = """
        {
          "limits": [
            { "kind": "session", "percent": 10, "resets_at": "not-a-date" }
          ]
        }
        """;

        var result = ClaudeUsageParser.Parse(json, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(UsageWindow.FiveHour, snapshot.Window);
        Assert.Equal(10, snapshot.UsedPercent);
        Assert.Null(snapshot.ResetsAt);
    }

    [Fact]
    public void Parse_KeepsLastSnapshot_WhenLimitsHasDuplicateKinds()
    {
        var json = """
        {
          "limits": [
            { "kind": "session", "percent": 10, "resets_at": null },
            { "kind": "session", "percent": 40, "resets_at": null }
          ]
        }
        """;

        var result = ClaudeUsageParser.Parse(json, DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(UsageWindow.FiveHour, snapshot.Window);
        Assert.Equal(40, snapshot.UsedPercent);
    }
}
