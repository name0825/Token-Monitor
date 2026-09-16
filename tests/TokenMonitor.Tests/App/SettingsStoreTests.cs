using TokenMonitor.App.Settings;
using TokenMonitor.Tests.TestSupport;

namespace TokenMonitor.Tests.App;

public class SettingsStoreTests
{
    [Fact]
    public void Load_ReturnsDefaults_WhenFileMissing()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"));

        var settings = store.Load();

        Assert.Equal(new OverlaySettings(), settings);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(Path.Combine(temp.Path, "settings.json"));
        var original = new OverlaySettings
        {
            Left = 12.5,
            Top = 34.5,
            ClickThrough = true,
            AlwaysOnTop = false,
            Opacity = 0.5,
            ShowClaude = false,
            ShowCodex = true,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenJsonIsCorrupted()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ not valid json");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal(new OverlaySettings(), settings);
    }

    [Theory]
    [InlineData(0.0, 0.2)]
    [InlineData(1.5, 1.0)]
    public void Load_ClampsOpacity(double storedOpacity, double expectedOpacity)
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, $"{{\"opacity\":{storedOpacity}}}");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal(expectedOpacity, settings.Opacity);
    }

    [Fact]
    public void Save_DoesNotLeaveTemporaryFile()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new SettingsStore(path);

        store.Save(new OverlaySettings());

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
