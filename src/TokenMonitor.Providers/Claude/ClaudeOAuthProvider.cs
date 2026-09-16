using System.Net;
using System.Net.Http.Headers;
using TokenMonitor.Core;

namespace TokenMonitor.Providers.Claude;

public sealed class ClaudeOAuthProvider : IUsageProvider
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";

    private readonly HttpClient _httpClient;
    private readonly string _credentialsPath;
    private readonly TimeProvider _timeProvider;

    public ClaudeOAuthProvider(HttpClient httpClient, string credentialsPath, TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _credentialsPath = credentialsPath;
        _timeProvider = timeProvider;
    }

    public Tool Tool => Tool.Claude;

    public async Task<UsageResult> GetUsageAsync(CancellationToken cancellationToken)
    {
        string json;
        try
        {
            using var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            json = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return UsageResult.Failure(UsageFailureKind.NotFound, "Credentials file not found");
        }
        catch (DirectoryNotFoundException)
        {
            return UsageResult.Failure(UsageFailureKind.NotFound, "Credentials file not found");
        }
        catch (IOException)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, "Failed to read credentials file");
        }
        catch (UnauthorizedAccessException)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, "Failed to read credentials file");
        }

        var credentials = ClaudeCredentialsParser.Parse(json);
        if (credentials is null)
        {
            return UsageResult.Failure(UsageFailureKind.InvalidData, "Failed to parse credentials file");
        }

        var now = _timeProvider.GetUtcNow();
        if (credentials.ExpiresAt <= now)
        {
            return UsageResult.Failure(UsageFailureKind.Unauthorized, "Credentials are expired");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, "Request to usage endpoint failed");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UsageResult.Failure(UsageFailureKind.Unavailable, "Request to usage endpoint timed out");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return UsageResult.Failure(UsageFailureKind.Unauthorized, $"Usage endpoint returned {(int)response.StatusCode}");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return UsageResult.Failure(UsageFailureKind.RateLimited, "Usage endpoint rate limited the request");
            }

            if (!response.IsSuccessStatusCode)
            {
                return UsageResult.Failure(UsageFailureKind.Unavailable, $"Usage endpoint returned {(int)response.StatusCode}");
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException)
            {
                return UsageResult.Failure(UsageFailureKind.Unavailable, "Failed to read usage endpoint response");
            }
            catch (IOException)
            {
                return UsageResult.Failure(UsageFailureKind.Unavailable, "Failed to read usage endpoint response");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return UsageResult.Failure(UsageFailureKind.Unavailable, "Reading usage endpoint response timed out");
            }

            return ClaudeUsageParser.Parse(body, now);
        }
    }

    public static string ResolveDefaultCredentialsPath()
    {
        return ResolveDefaultCredentialsPath(
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),
            Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty);
    }

    public static string ResolveDefaultCredentialsPath(string? claudeConfigDir, string userProfile)
    {
        if (!string.IsNullOrEmpty(claudeConfigDir))
        {
            return Path.Combine(claudeConfigDir, ".credentials.json");
        }

        return Path.Combine(userProfile, ".claude", ".credentials.json");
    }
}
