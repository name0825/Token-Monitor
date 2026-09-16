using System.Globalization;

namespace TokenMonitor.Providers.Cli;

internal static class CliResetTimeResolver
{
    public static DateTimeOffset? ResolveNextOccurrence(DateTimeOffset observedAt, TimeZoneInfo zone, int hour, int minute, int? month, int? day)
    {
        try
        {
            var nowLocal = TimeZoneInfo.ConvertTime(observedAt, zone);

            DateTime candidate;
            if (month is int m && day is int d)
            {
                candidate = new DateTime(nowLocal.Year, m, d, hour, minute, 0);
                if (candidate.Date < nowLocal.Date)
                {
                    candidate = candidate.AddYears(1);
                }
            }
            else
            {
                candidate = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, hour, minute, 0);
                if (candidate <= nowLocal.DateTime)
                {
                    candidate = candidate.AddDays(1);
                }
            }

            var offset = zone.GetUtcOffset(candidate);
            return new DateTimeOffset(candidate, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static int? ParseAbbreviatedMonth(string abbreviation)
    {
        for (var m = 1; m <= 12; m++)
        {
            if (CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m).Equals(abbreviation, StringComparison.OrdinalIgnoreCase))
            {
                return m;
            }
        }

        return null;
    }
}
