using TokenMonitor.Core;
using TokenMonitor.Providers.Codex;

namespace TokenMonitor.Tests.Providers;

public class CodexRolloutParserTests
{
    private static string[] ReadFixtureLines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "codex", "rollout-sample.jsonl"));

    [Fact]
    public void ParseLatest_UsesLastQualifyingLine()
    {
        var result = CodexRolloutParser.ParseLatest(ReadFixtureLines());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);

        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(84.0, fiveHour.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789500974), fiveHour.ResetsAt);
        Assert.Equal(Tool.Codex, fiveHour.Tool);
        Assert.Equal(UsageOrigin.Log, fiveHour.Origin);
        Assert.Equal(DateTimeOffset.Parse("2026-09-15T15:01:09.778Z"), fiveHour.ObservedAt);

        var weekly = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.Weekly);
        Assert.Equal(78.0, weekly.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789905762), weekly.ResetsAt);
    }

    [Fact]
    public void ParseLatest_MapsByWindowMinutes_EvenWhenPrimaryAndSecondarySwapped()
    {
        var line = """{"timestamp":"2026-09-15T15:01:09.778Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":10.0,"window_minutes":10080,"resets_at":1789905762},"secondary":{"used_percent":20.0,"window_minutes":300,"resets_at":1789500974}}}}""";

        var result = CodexRolloutParser.ParseLatest(new[] { line });

        Assert.True(result.IsSuccess);

        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(20.0, fiveHour.UsedPercent);

        var weekly = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.Weekly);
        Assert.Equal(10.0, weekly.UsedPercent);
    }

    [Fact]
    public void ParseLatest_SkipsNullRateLimitsLine_AfterValidOne()
    {
        var validLine = """{"timestamp":"2026-09-15T15:01:09.778Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":50.0,"window_minutes":300,"resets_at":1789500974}}}}""";
        var nullLine = """{"timestamp":"2026-09-15T15:02:00.000Z","type":"event_msg","payload":{"type":"token_count","rate_limits":null}}""";

        var result = CodexRolloutParser.ParseLatest(new[] { validLine, nullLine });

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(50.0, fiveHour.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-15T15:01:09.778Z"), fiveHour.ObservedAt);
    }

    [Fact]
    public void ParseLatest_IgnoresTruncatedFinalLine()
    {
        var validLine = """{"timestamp":"2026-09-15T15:01:09.778Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":50.0,"window_minutes":300,"resets_at":1789500974}}}}""";
        var truncatedLine = """{"timestamp":"2026-09-15T15:02:00.000Z","type":"event_msg","payload":{"type":"token_count","rate""";

        var result = CodexRolloutParser.ParseLatest(new[] { validLine, truncatedLine });

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(50.0, fiveHour.UsedPercent);
    }

    [Fact]
    public void ParseLatest_IgnoresUnknownWindowMinutes()
    {
        var line = """{"timestamp":"2026-09-15T15:01:09.778Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":50.0,"window_minutes":60,"resets_at":1789500974}}}}""";

        var result = CodexRolloutParser.ParseLatest(new[] { line });

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.NoData, result.FailureKind);
    }

    [Fact]
    public void ParseLatest_ReturnsNoData_WhenNoQualifyingLines()
    {
        var result = CodexRolloutParser.ParseLatest(Array.Empty<string>());

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.NoData, result.FailureKind);
    }

    [Fact]
    public void ParseLatest_SkipsWindow_WhenUsedPercentIsString()
    {
        var line = """{"timestamp":"2026-09-15T15:01:09.778Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":"80","window_minutes":300,"resets_at":1789500974},"secondary":{"used_percent":77.0,"window_minutes":10080,"resets_at":1789905762}}}}""";

        var result = CodexRolloutParser.ParseLatest(new[] { line });

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.Snapshots!);
        Assert.Equal(UsageWindow.Weekly, snapshot.Window);
        Assert.Equal(77.0, snapshot.UsedPercent);
    }

    [Fact]
    public void ParseLatest_SkipsNonObjectLine_BetweenValidLines()
    {
        var firstLine = """{"timestamp":"2026-09-15T15:01:09.778Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":50.0,"window_minutes":300,"resets_at":1789500974}}}}""";
        var nonObjectLine = "[]";
        var secondLine = """{"timestamp":"2026-09-15T15:02:00.000Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":60.0,"window_minutes":300,"resets_at":1789500974}}}}""";

        var result = CodexRolloutParser.ParseLatest(new[] { firstLine, nonObjectLine, secondLine });

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(60.0, fiveHour.UsedPercent);
    }

    [Fact]
    public void ParseLatest_EarlierValidLineWins_WhenLastQualifyingLineHasBadTimestamp()
    {
        var validLine = """{"timestamp":"2026-09-15T15:01:09.778Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":50.0,"window_minutes":300,"resets_at":1789500974}}}}""";
        var badTimestampLine = """{"timestamp":"not-a-timestamp","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":99.0,"window_minutes":300,"resets_at":1789500974}}}}""";

        var result = CodexRolloutParser.ParseLatest(new[] { validLine, badTimestampLine });

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(50.0, fiveHour.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-15T15:01:09.778Z"), fiveHour.ObservedAt);
    }
}
