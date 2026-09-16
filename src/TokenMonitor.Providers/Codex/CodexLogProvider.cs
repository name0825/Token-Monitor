using System.Text;
using TokenMonitor.Core;

namespace TokenMonitor.Providers.Codex;

public sealed class CodexLogProvider : IUsageProvider
{
    private readonly string _sessionsDirectory;
    private readonly int _tailBytes;

    public CodexLogProvider(string sessionsDirectory, int tailBytes = 1_048_576)
    {
        _sessionsDirectory = sessionsDirectory;
        _tailBytes = tailBytes;
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

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await TryParseFileAsync(file, cancellationToken);
            if (result is { IsSuccess: true })
            {
                return result;
            }
        }

        return UsageResult.Failure(UsageFailureKind.NoData, "No qualifying rate limit data found in rollout files");
    }

    private async Task<UsageResult?> TryParseFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;

            if (length > _tailBytes)
            {
                stream.Seek(-_tailBytes, SeekOrigin.End);
                var buffer = new byte[_tailBytes];
                await stream.ReadExactlyAsync(buffer, cancellationToken);
                var tailText = Encoding.UTF8.GetString(buffer);
                var newlineIndex = tailText.IndexOf('\n');
                var trimmed = newlineIndex >= 0 ? tailText[(newlineIndex + 1)..] : string.Empty;

                var tailResult = CodexRolloutParser.ParseLatest(trimmed.Split('\n'));
                if (tailResult.IsSuccess)
                {
                    return tailResult;
                }

                stream.Seek(0, SeekOrigin.Begin);
                var fullText = await ReadAllTextAsync(stream, cancellationToken);
                return CodexRolloutParser.ParseLatest(fullText.Split('\n'));
            }

            var text = await ReadAllTextAsync(stream, cancellationToken);
            return CodexRolloutParser.ParseLatest(text.Split('\n'));
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

    private static async Task<string> ReadAllTextAsync(FileStream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
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
