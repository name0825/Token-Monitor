namespace TokenMonitor.App.Settings;

public sealed record OverlaySettings
{
    public double? Left { get; init; }

    public double? Top { get; init; }

    public bool ClickThrough { get; init; }

    public bool AlwaysOnTop { get; init; } = true;

    public double Opacity { get; init; } = 0.85;

    public bool ShowClaude { get; init; } = true;

    public bool ShowCodex { get; init; } = true;

    public int PollingIntervalSeconds { get; init; } = 180;
}
