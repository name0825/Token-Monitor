using System.Text.RegularExpressions;

namespace TokenMonitor.Providers.Cli;

public static partial class AnsiText
{
    public static string StripEscapes(string text)
    {
        var withoutSequences = EscapeSequencePattern().Replace(text, string.Empty);
        return withoutSequences.Replace("", string.Empty);
    }

    [GeneratedRegex(
        "\\[[0-?]*[ -/]*[@-~]" +
        "|\\][^]*(?:|\\\\)" +
        "|[PX^_][\\s\\S]*?\\\\" +
        "|[@-Z\\\\-_]")]
    private static partial Regex EscapeSequencePattern();
}
