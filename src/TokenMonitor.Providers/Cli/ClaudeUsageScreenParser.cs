using TokenMonitor.Core;

namespace TokenMonitor.Providers.Cli;

public static class ClaudeUsageScreenParser
{
    private const int SectionLookaheadLines = 4;

    public static UsageResult Parse(string screen, DateTimeOffset observedAt, TimeZoneInfo? localZone = null)
    {
        if (string.IsNullOrWhiteSpace(screen))
        {
            return UsageResult.Failure(UsageFailureKind.InvalidData, "Empty CLI screen");
        }

        var zone = localZone ?? TimeZoneInfo.Local;
        var lines = screen.Replace("\r\n", "\n").Split('\n');

        var snapshots = new List<UsageSnapshot>();
        TryAddSection(lines, "Current session", UsageWindow.FiveHour, observedAt, zone, snapshots);
        TryAddSection(lines, "Current week (all models)", UsageWindow.Weekly, observedAt, zone, snapshots);

        return snapshots.Count == 0
            ? UsageResult.Failure(UsageFailureKind.InvalidData, "No usage sections found in CLI screen")
            : UsageResult.Success(snapshots);
    }

    private static void TryAddSection(
        string[] lines,
        string title,
        UsageWindow window,
        DateTimeOffset observedAt,
        TimeZoneInfo zone,
        List<UsageSnapshot> snapshots)
    {
        var titleIndex = Array.FindIndex(lines, line => line.Trim().Equals(title, StringComparison.Ordinal));
        if (titleIndex < 0)
        {
            return;
        }

        double? usedPercent = null;
        string? resetsText = null;

        var end = Math.Min(lines.Length, titleIndex + 1 + SectionLookaheadLines);
        for (var i = titleIndex + 1; i < end; i++)
        {
            if (usedPercent is null)
            {
                var percentMatch = CliScreenPatterns.ClaudeUsedPercent().Match(lines[i]);
                if (percentMatch.Success && double.TryParse(percentMatch.Groups["percent"].Value, out var percent))
                {
                    usedPercent = percent;
                    continue;
                }
            }

            if (usedPercent is not null)
            {
                var resetsMatch = CliScreenPatterns.ClaudeResetsLine().Match(lines[i]);
                if (resetsMatch.Success)
                {
                    resetsText = resetsMatch.Groups["value"].Value.Trim();
                    break;
                }
            }
        }

        if (usedPercent is null)
        {
            return;
        }

        var resetsAt = resetsText is null ? null : ParseResetValue(resetsText, observedAt, zone);
        snapshots.Add(new UsageSnapshot(Tool.Claude, window, usedPercent.Value, resetsAt, observedAt, UsageOrigin.Cli));
    }

    private static DateTimeOffset? ParseResetValue(string text, DateTimeOffset observedAt, TimeZoneInfo fallbackZone)
    {
        var match = CliScreenPatterns.ClaudeResetValue().Match(text);
        if (!match.Success || !int.TryParse(match.Groups["hour"].Value, out var hour))
        {
            return null;
        }

        var minute = match.Groups["minute"].Success ? int.Parse(match.Groups["minute"].Value) : 0;
        var isPm = match.Groups["ampm"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase);

        hour %= 12;
        if (isPm)
        {
            hour += 12;
        }

        int? month = null;
        int? day = null;
        if (match.Groups["month"].Success && match.Groups["day"].Success)
        {
            month = CliResetTimeResolver.ParseAbbreviatedMonth(match.Groups["month"].Value);
            if (month is null)
            {
                return null;
            }

            day = int.Parse(match.Groups["day"].Value);
        }

        var zone = fallbackZone;
        if (match.Groups["zone"].Success)
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(match.Groups["zone"].Value.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return CliResetTimeResolver.ResolveNextOccurrence(observedAt, zone, hour, minute, month, day);
    }
}
