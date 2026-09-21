using System.Text;
using TokenMonitor.Core;
using TokenMonitor.Providers.Codex;
using TokenMonitor.Tests.TestSupport;

namespace TokenMonitor.Tests.Providers;

public class CodexLogProviderTests
{
    private const string NonQualifyingLine =
        """{"timestamp":"2026-09-15T09:00:00.000Z","ordinal":0,"type":"session_meta","payload":{"session_id":"x"}}""";

    private const string LineA =
        """{"timestamp":"2026-09-15T10:00:00.000Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":10.0,"window_minutes":300,"resets_at":1000000000},"secondary":{"used_percent":20.0,"window_minutes":10080,"resets_at":2000000000}}}}""";

    private const string LineB =
        """{"timestamp":"2026-09-15T12:00:00.000Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":99.0,"window_minutes":300,"resets_at":3000000000},"secondary":{"used_percent":88.0,"window_minutes":10080,"resets_at":4000000000}}}}""";

    private const string CompactQualifyingLine =
        """{"timestamp":"2026-09-15T12:00:00.000Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":42.0,"window_minutes":300,"resets_at":1789500974}}}}""";

    private static string WriteRolloutFile(string sessionsDirectory, string fileName, string content, DateTime lastWriteTimeUtc)
    {
        var dayDirectory = Path.Combine(sessionsDirectory, "2026", "09", "15");
        Directory.CreateDirectory(dayDirectory);
        var path = Path.Combine(dayDirectory, fileName);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }

    private static string WriteRolloutFileBytes(string sessionsDirectory, string fileName, byte[] content, DateTime lastWriteTimeUtc)
    {
        var dayDirectory = Path.Combine(sessionsDirectory, "2026", "09", "15");
        Directory.CreateDirectory(dayDirectory);
        var path = Path.Combine(dayDirectory, fileName);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }

    private static byte[] Combine(params byte[][] arrays)
    {
        var result = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var array in arrays)
        {
            Buffer.BlockCopy(array, 0, result, offset, array.Length);
            offset += array.Length;
        }

        return result;
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsNotFound_WhenSessionsDirectoryMissing()
    {
        using var tempDir = new TempDirectory();
        var provider = new CodexLogProvider(Path.Combine(tempDir.Path, "missing"));

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsNoData_WhenDirectoryIsEmpty()
    {
        using var tempDir = new TempDirectory();
        var provider = new CodexLogProvider(tempDir.Path);

        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.NoData, result.FailureKind);
    }

    [Fact]
    public async Task GetUsageAsync_FallsBackToOlderFile_WhenNewestFileHasNoQualifyingLine()
    {
        using var tempDir = new TempDirectory();
        WriteRolloutFile(tempDir.Path, "rollout-old.jsonl", LineA, DateTime.UtcNow.AddMinutes(-10));
        WriteRolloutFile(tempDir.Path, "rollout-new.jsonl", NonQualifyingLine, DateTime.UtcNow);

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(10.0, fiveHour.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_PrefersNewestFile_WhenBothHaveData()
    {
        using var tempDir = new TempDirectory();
        WriteRolloutFile(tempDir.Path, "rollout-old.jsonl", LineA, DateTime.UtcNow.AddMinutes(-10));
        WriteRolloutFile(tempDir.Path, "rollout-new.jsonl", LineB, DateTime.UtcNow);

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(99.0, fiveHour.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_PrefersNewestEmbeddedTimestamp_OverFileWriteTime()
    {
        using var tempDir = new TempDirectory();
        WriteRolloutFile(tempDir.Path, "rollout-newer-event.jsonl", LineB, DateTime.UtcNow.AddMinutes(-10));
        WriteRolloutFile(tempDir.Path, "rollout-newer-write.jsonl", LineA, DateTime.UtcNow);

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(99.0, fiveHour.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-15T12:00:00.000Z"), fiveHour.ObservedAt);
    }

    [Fact]
    public async Task GetUsageAsync_SkipsFilesWrittenBeforeNewestObservedTimestamp()
    {
        using var tempDir = new TempDirectory();
        WriteRolloutFile(tempDir.Path, "rollout-current.jsonl", LineA, new DateTime(2026, 9, 15, 10, 0, 30, DateTimeKind.Utc));
        WriteRolloutFile(tempDir.Path, "rollout-stale.jsonl", LineB, new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc));

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(10.0, fiveHour.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_FindsNewestEmbeddedTimestamp_BeforeLargeOlderTail()
    {
        using var tempDir = new TempDirectory();
        var padding = string.Concat(Enumerable.Repeat(NonQualifyingLine + "\n", 11_000));
        var content = LineB + "\n" + padding + LineA;
        Assert.True(Encoding.UTF8.GetByteCount(padding) > 1_048_576);
        WriteRolloutFile(tempDir.Path, "rollout-out-of-order-large.jsonl", content, DateTime.UtcNow);

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(99.0, fiveHour.UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-15T12:00:00.000Z"), fiveHour.ObservedAt);
    }

    [Fact]
    public async Task GetUsageAsync_FindsQualifyingLine_NearStartOfLargeFile()
    {
        using var tempDir = new TempDirectory();
        var padding = string.Concat(Enumerable.Repeat(NonQualifyingLine + "\n", 10));
        var content = CompactQualifyingLine + "\n" + padding;
        Assert.True(content.Length > 256);
        WriteRolloutFile(tempDir.Path, "rollout-large.jsonl", content, DateTime.UtcNow);

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(42.0, fiveHour.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_FindsQualifyingLine_NearEndOfLargeFile()
    {
        using var tempDir = new TempDirectory();
        var padding = string.Concat(Enumerable.Repeat(NonQualifyingLine + "\n", 10));
        var content = padding + CompactQualifyingLine;
        Assert.True(content.Length > 256);
        WriteRolloutFile(tempDir.Path, "rollout-large.jsonl", content, DateTime.UtcNow);

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(42.0, fiveHour.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_ReadsFile_WhileHeldOpenByAnotherWriter()
    {
        using var tempDir = new TempDirectory();
        var path = WriteRolloutFile(tempDir.Path, "rollout-locked.jsonl", LineA, DateTime.UtcNow);

        using var lockStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(10.0, fiveHour.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_FindsQualifyingLine_WithMultibyteUtf8CharacterInContent()
    {
        using var tempDir = new TempDirectory();

        var prefix = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(NonQualifyingLine + "\n", 5)));
        var multibyteLineBytes = Encoding.UTF8.GetBytes(
            """{"timestamp":"2026-09-15T09:00:00.000Z","ordinal":0,"type":"session_meta","payload":{"note":"한글"}}""" + "\n");
        var qualifyingBytes = Encoding.UTF8.GetBytes(CompactQualifyingLine);

        var content = Combine(prefix, multibyteLineBytes, qualifyingBytes);

        WriteRolloutFileBytes(tempDir.Path, "rollout-multibyte.jsonl", content, DateTime.UtcNow);

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(42.0, fiveHour.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_FindsQualifyingLine_WithCrlfLineEndings()
    {
        using var tempDir = new TempDirectory();

        var prefix = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(NonQualifyingLine + "\r\n", 5)));
        var fillerLineBytes = Encoding.UTF8.GetBytes(NonQualifyingLine);
        var crlfBytes = Encoding.UTF8.GetBytes("\r\n");
        var qualifyingBytes = Encoding.UTF8.GetBytes(CompactQualifyingLine);

        var content = Combine(prefix, fillerLineBytes, crlfBytes, qualifyingBytes);

        WriteRolloutFileBytes(tempDir.Path, "rollout-crlf.jsonl", content, DateTime.UtcNow);

        var provider = new CodexLogProvider(tempDir.Path);
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fiveHour = Assert.Single(result.Snapshots!, s => s.Window == UsageWindow.FiveHour);
        Assert.Equal(42.0, fiveHour.UsedPercent);
    }

    [Fact]
    public void ResolveDefaultSessionsDirectory_UsesCodexHome_WhenSet()
    {
        var path = CodexLogProvider.ResolveDefaultSessionsDirectory(@"C:\custom\codex", @"C:\Users\someone");

        Assert.Equal(Path.Combine(@"C:\custom\codex", "sessions"), path);
    }

    [Fact]
    public void ResolveDefaultSessionsDirectory_FallsBackToUserProfile_WhenCodexHomeNotSet()
    {
        var path = CodexLogProvider.ResolveDefaultSessionsDirectory(null, @"C:\Users\someone");

        Assert.Equal(Path.Combine(@"C:\Users\someone", ".codex", "sessions"), path);
    }
}
