using System.Text.RegularExpressions;

namespace TokenMonitor.Providers.Cli;

internal static partial class CliScreenPatterns
{
    [GeneratedRegex(@"(?<percent>\d{1,3})%\s+used")]
    public static partial Regex ClaudeUsedPercent();

    [GeneratedRegex(@"Resets\s+(?<value>.+)")]
    public static partial Regex ClaudeResetsLine();

    [GeneratedRegex(
        @"^(?:(?<month>[A-Za-z]{3})\s+(?<day>\d{1,2}),\s*)?(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<ampm>am|pm)\s*(?:\((?<zone>[^)]+)\))?$",
        RegexOptions.IgnoreCase)]
    public static partial Regex ClaudeResetValue();

    [GeneratedRegex(
        @"(?<label>5h limit|Weekly limit):.*?(?<percent>\d{1,3})%\s+left\s*\(resets\s+(?<hour>\d{1,2}):(?<minute>\d{2})(?:\s+on\s+(?<day>\d{1,2})\s+(?<month>[A-Za-z]{3}))?\)",
        RegexOptions.IgnoreCase)]
    public static partial Regex CodexLimitLine();

    [GeneratedRegex(@"do you trust the files in this folder|trust this folder|trust the files in this folder", RegexOptions.IgnoreCase)]
    public static partial Regex TrustPrompt();

    [GeneratedRegex(@"please run\s*/login|not logged in|log in to (?:continue|use)", RegexOptions.IgnoreCase)]
    public static partial Regex LoginPrompt();

    [GeneratedRegex(@"update available!?", RegexOptions.IgnoreCase)]
    public static partial Regex UpdatePrompt();
}
