using System.Globalization;
using System.Text.Json;
using TokenMonitor.Core;

namespace TokenMonitor.Providers.Claude;

public static class ClaudeUsageParser
{
    public static UsageResult Parse(string json, DateTimeOffset observedAt)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return UsageResult.Failure(UsageFailureKind.InvalidData, ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
            {
                var snapshotsByWindow = new Dictionary<UsageWindow, UsageSnapshot>();

                foreach (var limit in limits.EnumerateArray())
                {
                    if (!limit.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    UsageWindow? window = kindElement.GetString() switch
                    {
                        "session" => UsageWindow.FiveHour,
                        "weekly_all" => UsageWindow.Weekly,
                        _ => null
                    };

                    if (window is null)
                    {
                        continue;
                    }

                    if (!limit.TryGetProperty("percent", out var percentElement) || percentElement.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    var percent = percentElement.GetDouble();
                    var resetsAt = ParseNullableDateTimeOffset(limit, "resets_at");

                    snapshotsByWindow[window.Value] = new UsageSnapshot(Tool.Claude, window.Value, percent, resetsAt, observedAt, UsageOrigin.Api);
                }

                if (snapshotsByWindow.Count > 0)
                {
                    return UsageResult.Success(snapshotsByWindow.Values.ToList());
                }
            }

            var fallbackSnapshotsByWindow = new Dictionary<UsageWindow, UsageSnapshot>();

            if (root.TryGetProperty("five_hour", out var fiveHour) && fiveHour.ValueKind == JsonValueKind.Object)
            {
                var snapshot = BuildSnapshot(fiveHour, UsageWindow.FiveHour, observedAt);
                if (snapshot is not null)
                {
                    fallbackSnapshotsByWindow[UsageWindow.FiveHour] = snapshot;
                }
            }

            if (root.TryGetProperty("seven_day", out var sevenDay) && sevenDay.ValueKind == JsonValueKind.Object)
            {
                var snapshot = BuildSnapshot(sevenDay, UsageWindow.Weekly, observedAt);
                if (snapshot is not null)
                {
                    fallbackSnapshotsByWindow[UsageWindow.Weekly] = snapshot;
                }
            }

            if (fallbackSnapshotsByWindow.Count > 0)
            {
                return UsageResult.Success(fallbackSnapshotsByWindow.Values.ToList());
            }

            return UsageResult.Failure(UsageFailureKind.InvalidData, "No usable usage data found");
        }
    }

    private static UsageSnapshot? BuildSnapshot(JsonElement section, UsageWindow window, DateTimeOffset observedAt)
    {
        if (!section.TryGetProperty("utilization", out var utilization) || utilization.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var percent = utilization.GetDouble();
        var resetsAt = ParseNullableDateTimeOffset(section, "resets_at");
        return new UsageSnapshot(Tool.Claude, window, percent, resetsAt, observedAt, UsageOrigin.Api);
    }

    private static DateTimeOffset? ParseNullableDateTimeOffset(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();
            if (text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
