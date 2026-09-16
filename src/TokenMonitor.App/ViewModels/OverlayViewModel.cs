using TokenMonitor.Core;

namespace TokenMonitor.App.ViewModels;

public sealed class OverlayViewModel
{
    public OverlayViewModel()
    {
        Claude = new ToolUsageViewModel(Tool.Claude);
        Codex = new ToolUsageViewModel(Tool.Codex);
        Tools = [Claude, Codex];
    }

    public ToolUsageViewModel Claude { get; }

    public ToolUsageViewModel Codex { get; }

    public IReadOnlyList<ToolUsageViewModel> Tools { get; }

    public void Apply(Tool tool, UsageResult result, DateTimeOffset observedAt) =>
        (tool == Tool.Claude ? Claude : Codex).Apply(result, observedAt);

    public void Tick(DateTimeOffset now)
    {
        Claude.Render(now);
        Codex.Render(now);
    }
}
