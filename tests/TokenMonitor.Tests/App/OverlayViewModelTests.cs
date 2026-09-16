using TokenMonitor.App.ViewModels;
using TokenMonitor.Core;

namespace TokenMonitor.Tests.App;

public class OverlayViewModelTests
{
    [Fact]
    public void Apply_RoutesToMatchingToolViewModel()
    {
        var vm = new OverlayViewModel();
        var now = DateTimeOffset.UtcNow;
        var claudeResult = UsageResult.Success([new UsageSnapshot(Tool.Claude, UsageWindow.FiveHour, 30, now.AddHours(1), now, UsageOrigin.Api)]);
        var codexResult = UsageResult.Success([new UsageSnapshot(Tool.Codex, UsageWindow.FiveHour, 70, now.AddHours(1), now, UsageOrigin.Log)]);

        vm.Apply(Tool.Claude, claudeResult, now);
        vm.Apply(Tool.Codex, codexResult, now);

        Assert.Equal(30, vm.Claude.FiveHourPercent);
        Assert.False(vm.Claude.HasError);
        Assert.Equal(70, vm.Codex.FiveHourPercent);
        Assert.False(vm.Codex.HasError);
    }

    [Fact]
    public void Tools_ContainsClaudeThenCodex()
    {
        var vm = new OverlayViewModel();

        Assert.Equal([vm.Claude, vm.Codex], vm.Tools);
    }

    [Fact]
    public void Tick_RerendersBothToolsForCurrentTime()
    {
        var vm = new OverlayViewModel();
        var observedAt = DateTimeOffset.UtcNow;
        var resetsAt = observedAt.AddMinutes(1);
        vm.Apply(Tool.Claude, UsageResult.Success([new UsageSnapshot(Tool.Claude, UsageWindow.FiveHour, 50, resetsAt, observedAt, UsageOrigin.Api)]), observedAt);

        vm.Tick(resetsAt.AddSeconds(1));

        Assert.Equal(0, vm.Claude.FiveHourPercent);
        Assert.Equal("—", vm.Claude.FiveHourReset);
    }
}
