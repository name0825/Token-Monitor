using TokenMonitor.Providers.Codex;
using TokenMonitor.Tests.TestSupport;

namespace TokenMonitor.Tests.Providers;

public class CodexSessionWatcherTests
{
    [Fact]
    public async Task Start_ActivatesAndRaisesChanged_WhenDirectoryIsCreatedLater()
    {
        using var tempDir = new TempDirectory();
        var sessionsDirectory = Path.Combine(tempDir.Path, "missing");
        using var watcher = new CodexSessionWatcher(sessionsDirectory, TimeSpan.FromMilliseconds(50));
        var tcs = new TaskCompletionSource();
        watcher.Changed += (_, _) => tcs.TrySetResult();

        watcher.Start();
        Assert.False(watcher.IsActive);

        Directory.CreateDirectory(sessionsDirectory);
        File.WriteAllText(Path.Combine(sessionsDirectory, "rollout-created-before-watch.jsonl"), "{}");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(tcs.Task, completed);
        Assert.True(watcher.IsActive);
    }

    [Fact]
    public async Task Start_RaisesChanged_WhenMatchingFileIsWritten()
    {
        using var tempDir = new TempDirectory();
        using var watcher = new CodexSessionWatcher(tempDir.Path, TimeSpan.FromMilliseconds(50));
        var tcs = new TaskCompletionSource();
        watcher.Changed += (_, _) => tcs.TrySetResult();

        watcher.Start();
        Assert.True(watcher.IsActive);

        File.WriteAllText(Path.Combine(tempDir.Path, "rollout-test.jsonl"), "{}");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(tcs.Task, completed);
    }

    [Fact]
    public async Task Start_DoesNotRaiseChanged_ForNonMatchingFile()
    {
        using var tempDir = new TempDirectory();
        using var watcher = new CodexSessionWatcher(tempDir.Path, TimeSpan.FromMilliseconds(50));
        var raised = false;
        watcher.Changed += (_, _) => raised = true;

        watcher.Start();

        File.WriteAllText(Path.Combine(tempDir.Path, "not-a-rollout.txt"), "{}");
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.False(raised);
    }

    [Fact]
    public async Task Dispose_StopsRaisingChanged()
    {
        using var tempDir = new TempDirectory();
        var watcher = new CodexSessionWatcher(tempDir.Path, TimeSpan.FromMilliseconds(50));
        var raised = false;
        watcher.Changed += (_, _) => raised = true;

        watcher.Start();
        watcher.Dispose();

        File.WriteAllText(Path.Combine(tempDir.Path, "rollout-after-dispose.jsonl"), "{}");
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.False(raised);
    }
}
