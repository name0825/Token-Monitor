namespace TokenMonitor.Core;

public static class UsageFormatting
{
    public static string FormatPercent(double usedPercent)
    {
        if (double.IsNaN(usedPercent) || double.IsInfinity(usedPercent))
        {
            return "—";
        }

        var clamped = Math.Clamp(usedPercent, 0, 100);
        var rounded = Math.Round(clamped, MidpointRounding.AwayFromZero);
        return $"{rounded}%";
    }

    public static string FormatRemaining(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null)
        {
            return "—";
        }

        if (resetsAt.Value <= now)
        {
            return "리셋됨";
        }

        var remaining = resetsAt.Value - now;

        if (remaining.TotalDays >= 1)
        {
            return $"{remaining.Days}d{remaining.Hours}h";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{remaining.Hours}h{remaining.Minutes:D2}m";
        }

        if (remaining.TotalMinutes >= 1)
        {
            return $"{remaining.Minutes}m";
        }

        return "<1m";
    }

    public static string FormatFreshness(DateTimeOffset observedAt, DateTimeOffset now)
    {
        var age = now - observedAt;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalSeconds < 60)
        {
            return "방금";
        }

        if (age.TotalMinutes < 60)
        {
            return $"{(int)age.TotalMinutes}분 전";
        }

        if (age.TotalHours < 24)
        {
            return $"{(int)age.TotalHours}시간 전";
        }

        return $"{(int)age.TotalDays}일 전";
    }

    public static string FormatOrigin(UsageOrigin origin)
    {
        return origin switch
        {
            UsageOrigin.Api => "API",
            UsageOrigin.Log => "로그",
            UsageOrigin.Cli => "CLI",
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
    }
}
