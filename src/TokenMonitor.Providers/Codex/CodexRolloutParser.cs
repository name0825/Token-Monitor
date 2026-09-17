using System.Globalization;
using System.Text.Json;
using TokenMonitor.Core;

namespace TokenMonitor.Providers.Codex;

public static class CodexRolloutParser
{
    public static UsageResult ParseLatest(IEnumerable<string> lines)
    {
        JsonElement? latestRateLimits = null;
        var latestTimestamp = default(DateTimeOffset);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String || typeElement.GetString() != "event_msg")
                {
                    continue;
                }

                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!payload.TryGetProperty("type", out var payloadTypeElement) || payloadTypeElement.ValueKind != JsonValueKind.String || payloadTypeElement.GetString() != "token_count")
                {
                    continue;
                }

                if (!payload.TryGetProperty("rate_limits", out var rateLimits) || rateLimits.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!root.TryGetProperty("timestamp", out var timestampElement) || timestampElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var timestampText = timestampElement.GetString();
                if (timestampText is null || !DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
                {
                    continue;
                }

                latestTimestamp = timestamp;
                latestRateLimits = rateLimits.Clone();
            }
        }

        if (latestRateLimits is null)
        {
            return UsageResult.Failure(UsageFailureKind.NoData, "No qualifying token_count line found");
        }

        var snapshotsByWindow = new Dictionary<UsageWindow, UsageSnapshot>();

        var primarySnapshot = BuildSnapshot(latestRateLimits.Value, "primary", latestTimestamp);
        if (primarySnapshot is not null)
        {
            snapshotsByWindow[primarySnapshot.Window] = primarySnapshot;
        }

        var secondarySnapshot = BuildSnapshot(latestRateLimits.Value, "secondary", latestTimestamp);
        if (secondarySnapshot is not null)
        {
            snapshotsByWindow[secondarySnapshot.Window] = secondarySnapshot;
        }

        if (snapshotsByWindow.Count == 0)
        {
            return UsageResult.Failure(UsageFailureKind.NoData, "No usable rate limit windows found");
        }

        return UsageResult.Success(snapshotsByWindow.Values.ToList());
    }

    private static UsageSnapshot? BuildSnapshot(JsonElement rateLimits, string propertyName, DateTimeOffset observedAt)
    {
        if (!rateLimits.TryGetProperty(propertyName, out var windowElement) || windowElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!windowElement.TryGetProperty("window_minutes", out var windowMinutesElement) || windowMinutesElement.ValueKind != JsonValueKind.Number || !windowMinutesElement.TryGetInt32(out var windowMinutes))
        {
            return null;
        }

        UsageWindow? window = windowMinutes switch
        {
            300 => UsageWindow.FiveHour,
            10080 => UsageWindow.Weekly,
            _ => null
        };

        if (window is null)
        {
            return null;
        }

        if (!windowElement.TryGetProperty("used_percent", out var usedPercentElement) || usedPercentElement.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var usedPercent = usedPercentElement.GetDouble();

        DateTimeOffset? resetsAt = null;
        if (windowElement.TryGetProperty("resets_at", out var resetsAtElement) && resetsAtElement.ValueKind == JsonValueKind.Number && resetsAtElement.TryGetInt64(out var resetsAtSeconds))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetsAtSeconds);
        }

        return new UsageSnapshot(Tool.Codex, window.Value, usedPercent, resetsAt, observedAt, UsageOrigin.Log);
    }
}
