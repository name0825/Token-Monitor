namespace TokenMonitor.Providers.Cli;

public static class CliPromptDetector
{
    public static string? Detect(string screen)
    {
        if (string.IsNullOrEmpty(screen))
        {
            return null;
        }

        if (CliScreenPatterns.TrustPrompt().IsMatch(screen))
        {
            return "trust prompt";
        }

        if (CliScreenPatterns.LoginPrompt().IsMatch(screen))
        {
            return "login prompt";
        }

        if (CliScreenPatterns.UpdatePrompt().IsMatch(screen))
        {
            return "update prompt";
        }

        return null;
    }
}
