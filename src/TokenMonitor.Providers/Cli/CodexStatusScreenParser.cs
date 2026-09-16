using TokenMonitor.Core;

namespace TokenMonitor.Providers.Cli;

public static class CodexStatusScreenParser
{
    public static UsageResult Parse(string screen, DateTimeOffset observedAt, TimeZoneInfo? localZone = null)
    {
        if (string.IsNullOrWhiteSpace(screen))
        {
            return UsageResult.Failure(UsageFailureKind.InvalidData, "Empty CLI screen");
        }

        var zone = localZone ?? TimeZoneInfo.Local;
        var snapshots = new List<UsageSnapshot>();

        foreach (System.Text.RegularExpressions.Match match in CliScreenPatterns.CodexLimitLine().Matches(screen))
        {
            if (!double.TryParse(match.Groups["percent"].Value, out var leftPercent)
                || !int.TryParse(match.Groups["hour"].Value, out var hour)
                || !int.TryParse(match.Groups["minute"].Value, out var minute))
            {
                continue;
            }

            var window = match.Groups["label"].Value.Equals("5h limit", StringComparison.OrdinalIgnoreCase)
                ? UsageWindow.FiveHour
                : UsageWindow.Weekly;

            int? month = null;
            int? day = null;
            if (match.Groups["day"].Success && match.Groups["month"].Success)
            {
                month = CliResetTimeResolver.ParseAbbreviatedMonth(match.Groups["month"].Value);
                if (month is null)
                {
                    continue;
                }

                day = int.Parse(match.Groups["day"].Value);
            }

            var resetsAt = CliResetTimeResolver.ResolveNextOccurrence(observedAt, zone, hour, minute, month, day);
            snapshots.Add(new UsageSnapshot(Tool.Codex, window, 100 - leftPercent, resetsAt, observedAt, UsageOrigin.Cli));
        }

        return snapshots.Count == 0
            ? UsageResult.Failure(UsageFailureKind.InvalidData, "No usage lines found in CLI screen")
            : UsageResult.Success(snapshots);
    }
}
