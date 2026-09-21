using TokenMonitor.Core;

namespace TokenMonitor.Providers.Codex;

public sealed class CodexLogProvider : IUsageProvider
{
    private static readonly TimeSpan WriteTimeSlack = TimeSpan.FromMinutes(1);
    private readonly string _sessionsDirectory;

    public CodexLogProvider(string sessionsDirectory)
    {
        _sessionsDirectory = sessionsDirectory;
    }

    public Tool Tool => Tool.Codex;

    public async Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_sessionsDirectory))
        {
            return UsageResult.Failure(UsageFailureKind.NotFound, "Codex sessions directory not found");
        }

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(_sessionsDirectory, "rollout-*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(GetLastWriteTimeUtcSafe)
                .ThenByDescending(path => path, StringComparer.Ordinal)
                .ToList();
        }
        catch (IOException)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, "Failed to enumerate rollout files");
        }
        catch (UnauthorizedAccessException)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, "Failed to enumerate rollout files");
        }

        if (files.Count == 0)
        {
            return UsageResult.Failure(UsageFailureKind.NoData, "No rollout files found");
        }

        UsageResult? latestResult = null;
        DateTimeOffset? latestObservedAt = null;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (latestObservedAt is not null &&
                GetLastWriteTimeUtcSafe(file) < latestObservedAt.Value.UtcDateTime - WriteTimeSlack)
            {
                break;
            }

            var result = await TryParseFileAsync(file, cancellationToken);
            if (result is not { IsSuccess: true, Snapshots.Count: > 0 })
            {
                continue;
            }

            var observedAt = result.Snapshots.Max(snapshot => snapshot.ObservedAt);
            if (latestObservedAt is null || observedAt > latestObservedAt.Value)
            {
                latestResult = result;
                latestObservedAt = observedAt;
            }
        }

        return latestResult ?? UsageResult.Failure(UsageFailureKind.NoData, "No qualifying rate limit data found in rollout files");
    }

    private async Task<UsageResult?> TryParseFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return await CodexRolloutParser.ParseLatestAsync(reader, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTime GetLastWriteTimeUtcSafe(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    public static string ResolveDefaultSessionsDirectory()
    {
        return ResolveDefaultSessionsDirectory(
            Environment.GetEnvironmentVariable("CODEX_HOME"),
            Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty);
    }

    public static string ResolveDefaultSessionsDirectory(string? codexHome, string userProfile)
    {
        if (!string.IsNullOrEmpty(codexHome))
        {
            return Path.Combine(codexHome, "sessions");
        }

        return Path.Combine(userProfile, ".codex", "sessions");
    }
}
