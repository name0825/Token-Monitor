using System.ComponentModel;
using TokenMonitor.App.ViewModels;
using TokenMonitor.Core;

namespace TokenMonitor.Tests.App;

public class ToolUsageViewModelTests
{
    private static UsageSnapshot Snapshot(Tool tool, UsageWindow window, double usedPercent, DateTimeOffset? resetsAt, DateTimeOffset observedAt, UsageOrigin origin = UsageOrigin.Api) =>
        new(tool, window, usedPercent, resetsAt, observedAt, origin);

    [Fact]
    public void Apply_Success_UpdatesPercentTextAndBarWidth()
    {
        var vm = new ToolUsageViewModel(Tool.Claude);
        var now = DateTimeOffset.UtcNow;
        var resetsAt = now.AddHours(2);
        var result = UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 42, resetsAt, now)]);

        vm.Apply(result, now);

        Assert.Equal(42, vm.FiveHourPercent);
        Assert.Equal(UsageFormatting.FormatPercent(42), vm.FiveHourText);
        Assert.Equal(UsageFormatting.FormatRemaining(resetsAt, now), vm.FiveHourReset);
        Assert.Equal(ToolUsageViewModel.BarTrackWidth * 42 / 100, vm.FiveHourBarWidth);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void Apply_KeepsNewerApiResult_WhenStaleCliFallbackAppliedAfter()
    {
        var vm = new ToolUsageViewModel(Tool.Claude);
        var now = DateTimeOffset.UtcNow;
        var apiObservedAt = now;
        var cliObservedAt = now.AddMinutes(-5);
        var apiResult = UsageResult.Success([
            Snapshot(Tool.Claude, UsageWindow.FiveHour, 42, now.AddHours(2), apiObservedAt, UsageOrigin.Api),
            Snapshot(Tool.Claude, UsageWindow.Weekly, 60, now.AddDays(2), apiObservedAt, UsageOrigin.Api),
        ]);
        var cliResult = UsageResult.Success([
            Snapshot(Tool.Claude, UsageWindow.FiveHour, 10, now.AddHours(2), cliObservedAt, UsageOrigin.Cli),
            Snapshot(Tool.Claude, UsageWindow.Weekly, 20, now.AddDays(2), cliObservedAt, UsageOrigin.Cli),
        ]);

        vm.Apply(apiResult, now);
        vm.Apply(cliResult, now);

        Assert.Equal(42, vm.FiveHourPercent);
        Assert.Equal(60, vm.WeeklyPercent);
        Assert.Equal(UsageFormatting.FormatOrigin(UsageOrigin.Api) + " · " + UsageFormatting.FormatFreshness(apiObservedAt, now), vm.StatusText);
    }

    [Fact]
    public void Apply_ReplacesOlderSnapshot_WhenNewerAppliedAfter()
    {
        var vm = new ToolUsageViewModel(Tool.Claude);
        var now = DateTimeOffset.UtcNow;
        var olderObservedAt = now.AddMinutes(-5);
        var olderResult = UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 10, now.AddHours(2), olderObservedAt)]);
        var newerResult = UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 42, now.AddHours(2), now)]);

        vm.Apply(olderResult, now);
        vm.Apply(newerResult, now);

        Assert.Equal(42, vm.FiveHourPercent);
    }

    [Fact]
    public void Apply_IgnoresSnapshotsForOtherTool()
    {
        var vm = new ToolUsageViewModel(Tool.Claude);
        var now = DateTimeOffset.UtcNow;
        var result = UsageResult.Success([Snapshot(Tool.Codex, UsageWindow.FiveHour, 90, now.AddHours(1), now)]);

        vm.Apply(result, now);

        Assert.Equal(0, vm.FiveHourPercent);
        Assert.Equal("—", vm.FiveHourText);
    }

    [Fact]
    public void Apply_Failure_SetsHasErrorAndErrorText()
    {
        var vm = new ToolUsageViewModel(Tool.Claude);
        var now = DateTimeOffset.UtcNow;
        var result = UsageResult.Failure(UsageFailureKind.Unauthorized, "expired");

        vm.Apply(result, now);

        Assert.True(vm.HasError);
        Assert.Equal("인증 만료", vm.ErrorText);
    }

    [Fact]
    public void Render_ClampsToZero_WhenResetsAtAlreadyPassed()
    {
        var vm = new ToolUsageViewModel(Tool.Claude);
        var now = DateTimeOffset.UtcNow;
        var result = UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 75, now.AddMinutes(-1), now.AddHours(-1))]);

        vm.Apply(result, now);

        Assert.Equal(0, vm.FiveHourPercent);
        Assert.Equal(UsageFormatting.FormatPercent(0), vm.FiveHourText);
        Assert.Equal("—", vm.FiveHourReset);
    }

    [Fact]
    public void FiveHourBrush_IsSameInstance_ForPercentsInSameBand()
    {
        var vmA = new ToolUsageViewModel(Tool.Claude);
        var vmB = new ToolUsageViewModel(Tool.Claude);
        var now = DateTimeOffset.UtcNow;

        vmA.Apply(UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 10, now.AddHours(1), now)]), now);
        vmB.Apply(UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 59, now.AddHours(1), now)]), now);

        Assert.Same(vmA.FiveHourBrush, vmB.FiveHourBrush);
    }

    [Fact]
    public void FiveHourBrush_DiffersAcrossThresholdBands()
    {
        var vm = new ToolUsageViewModel(Tool.Claude);
        var now = DateTimeOffset.UtcNow;

        vm.Apply(UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 59, now.AddHours(1), now)]), now);
        var lowBrush = vm.FiveHourBrush;

        vm.Apply(UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 60, now.AddHours(1), now)]), now);
        var mediumBrush = vm.FiveHourBrush;

        vm.Apply(UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 85, now.AddHours(1), now)]), now);
        var mediumBrushAtBoundary = vm.FiveHourBrush;

        vm.Apply(UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 86, now.AddHours(1), now)]), now);
        var highBrush = vm.FiveHourBrush;

        Assert.NotSame(lowBrush, mediumBrush);
        Assert.Same(mediumBrush, mediumBrushAtBoundary);
        Assert.NotSame(mediumBrushAtBoundary, highBrush);
    }

    [Fact]
    public void Apply_RaisesPropertyChanged_ForPercentBarWidthAndBrush()
    {
        var vm = new ToolUsageViewModel(Tool.Claude);
        var now = DateTimeOffset.UtcNow;
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Apply(UsageResult.Success([Snapshot(Tool.Claude, UsageWindow.FiveHour, 42, now.AddHours(1), now)]), now);

        Assert.Contains(nameof(vm.FiveHourPercent), raised);
        Assert.Contains(nameof(vm.FiveHourBarWidth), raised);
        Assert.Contains(nameof(vm.FiveHourBrush), raised);
    }
}
