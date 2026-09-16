using System.IO;
using System.Text.Json;

namespace TokenMonitor.App.Settings;

public sealed class SettingsStore
{
    private const double MinimumOpacity = 0.2;
    private const double MaximumOpacity = 1.0;
    private const int DefaultPollingIntervalSeconds = 180;
    private static readonly int[] AllowedPollingIntervalsSeconds = [3, 5, 10, 15, 30, 60, 180, 300, 600, 900, 1800];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public SettingsStore(string? path = null)
    {
        Path = path ?? ResolveDefaultPath();
    }

    public string Path { get; }

    public static string ResolveDefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TokenMonitor",
        "settings.json");

    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new OverlaySettings();
            }

            OverlaySettings? settings = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(Path), SerializerOptions);
            if (settings is null)
            {
                return new OverlaySettings();
            }

            return settings with
            {
                Opacity = ClampOpacity(settings.Opacity),
                PollingIntervalSeconds = ValidatePollingInterval(settings.PollingIntervalSeconds),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        try
        {
            string? directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = Path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
        }
    }

    private static double ClampOpacity(double value) =>
        double.IsNaN(value) ? MaximumOpacity : Math.Clamp(value, MinimumOpacity, MaximumOpacity);

    private static int ValidatePollingInterval(int seconds) =>
        Array.IndexOf(AllowedPollingIntervalsSeconds, seconds) >= 0 ? seconds : DefaultPollingIntervalSeconds;
}
