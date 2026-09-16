using TokenMonitor.Providers.Cli;

namespace TokenMonitor.Tests.Providers;

public class CliPromptDetectorTests
{
    [Fact]
    public void Detect_ReturnsTrustPrompt_WhenTrustPhrasePresent()
    {
        var result = CliPromptDetector.Detect("Do you trust the files in this folder?\n1. Yes\n2. No");

        Assert.Equal("trust prompt", result);
    }

    [Fact]
    public void Detect_ReturnsLoginPrompt_WhenLoginPhrasePresent()
    {
        var result = CliPromptDetector.Detect("You are not logged in. Please run /login to continue.");

        Assert.Equal("login prompt", result);
    }

    [Fact]
    public void Detect_ReturnsUpdatePrompt_ForCodexUpdateScreen()
    {
        var screen = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cli", "codex-status.screen.txt"));

        var result = CliPromptDetector.Detect(screen);

        Assert.Equal("update prompt", result);
    }

    [Fact]
    public void Detect_ReturnsNull_ForNormalScreen()
    {
        var result = CliPromptDetector.Detect("Current session\n40% used\nResets 8pm (Asia/Seoul)");

        Assert.Null(result);
    }
}
