using TokenMonitor.Providers.Cli;

namespace TokenMonitor.Tests.Cli;

public class TerminalScreenTests
{
    [Fact]
    public void CursorForward_InsertsGapBetweenWords()
    {
        var screen = new TerminalScreen(20, 5);

        screen.Write("Accessing[1Cworkspace:");

        Assert.Equal("Accessing workspace:", screen.Render());
    }

    [Fact]
    public void AbsolutePositioning_PlacesTextAtRowAndColumn()
    {
        var screen = new TerminalScreen(20, 5);

        screen.Write("[3;2Hhi");

        var lines = screen.Render().Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal(" hi", lines[2]);
    }

    [Fact]
    public void EscapeSequence_SplitAcrossWriteCalls_StillRenders()
    {
        var screen = new TerminalScreen(20, 5);

        screen.Write("a[1");
        screen.Write("Cb");

        Assert.Equal("a b", screen.Render());
    }

    [Fact]
    public void CarriageReturn_OverwritesFromStartOfLine()
    {
        var screen = new TerminalScreen(20, 5);

        screen.Write("abc\rX");

        Assert.Equal("Xbc", screen.Render());
    }

    [Fact]
    public void EraseDisplay_ClearsWholeScreen()
    {
        var screen = new TerminalScreen(20, 5);

        screen.Write("hello[2J");

        Assert.Equal(string.Empty, screen.Render());
    }

    [Fact]
    public void EraseLine_ClearsFromCursorToEndOfLine()
    {
        var screen = new TerminalScreen(20, 5);

        screen.Write("hello\rhe[K");

        Assert.Equal("he", screen.Render());
    }

    [Fact]
    public void LineFeed_OnLastRow_ScrollsScreenUp()
    {
        var screen = new TerminalScreen(20, 3);

        screen.Write("line1\r\nline2\r\nline3\r\nline4");

        Assert.Equal("line2\nline3\nline4", screen.Render());
    }

    [Fact]
    public void PrintableText_WrapsAtColumnLimit()
    {
        var screen = new TerminalScreen(5, 3);

        screen.Write("abcdefg");

        Assert.Equal("abcde\nfg", screen.Render());
    }

    [Fact]
    public void SgrColorSequence_EmitsNoLiteralCharacters()
    {
        var screen = new TerminalScreen(20, 5);

        screen.Write("[38;2;255;193;7mred[m");

        Assert.Equal("red", screen.Render());
    }
}
