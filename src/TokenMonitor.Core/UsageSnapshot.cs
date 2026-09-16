namespace TokenMonitor.Core;

public record UsageSnapshot(Tool Tool, UsageWindow Window, double UsedPercent, DateTimeOffset? ResetsAt, DateTimeOffset ObservedAt, UsageOrigin Origin)
{
    public UsageSnapshot AsOf(DateTimeOffset now)
    {
        if (ResetsAt is { } resetsAt && resetsAt <= now)
        {
            return this with { UsedPercent = 0, ResetsAt = null };
        }

        return this;
    }
}
