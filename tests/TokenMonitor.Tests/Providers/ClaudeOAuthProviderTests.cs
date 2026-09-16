using System.Net;
using TokenMonitor.Core;
using TokenMonitor.Providers.Claude;
using TokenMonitor.Tests.TestSupport;

namespace TokenMonitor.Tests.Providers;

public class ClaudeOAuthProviderTests
{
    private const string Token = "sk-ant-oat01-super-secret-token";

    private static string ReadUsageFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude", "usage-response.json"));

    private static string WriteCredentialsFile(string directory, DateTimeOffset expiresAt, string accessToken = Token)
    {
        var path = Path.Combine(directory, "credentials.json");
        var json = $$"""
        {
          "claudeAiOauth": {
            "accessToken": "{{accessToken}}",
            "expiresAt": {{expiresAt.ToUnixTimeMilliseconds()}}
          }
        }
        """;
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsSuccess_AndSendsExpectedRequest()
    {
        using var tempDir = new TempDirectory();
        var now = DateTimeOffset.Parse("2026-09-16T00:00:00+00:00");
        var credentialsPath = WriteCredentialsFile(tempDir.Path, now.AddHours(1));

        var handler = new FakeHttpMessageHandler((request, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ReadUsageFixture())
            }));
        using var httpClient = new HttpClient(handler);

        var provider = new ClaudeOAuthProvider(httpClient, credentialsPath, new FakeTimeProvider(now));
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Snapshots!.Count);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(Token, request.Headers.Authorization!.Parameter);
        Assert.True(request.Headers.TryGetValues("anthropic-beta", out var betaValues));
        Assert.Equal("oauth-2025-04-20", Assert.Single(betaValues));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, UsageFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, UsageFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, UsageFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, UsageFailureKind.Unavailable)]
    public async Task GetUsageAsync_MapsHttpStatusCode_ToExpectedFailureKind(HttpStatusCode statusCode, UsageFailureKind expectedKind)
    {
        using var tempDir = new TempDirectory();
        var now = DateTimeOffset.Parse("2026-09-16T00:00:00+00:00");
        var credentialsPath = WriteCredentialsFile(tempDir.Path, now.AddHours(1));

        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode)));
        using var httpClient = new HttpClient(handler);

        var provider = new ClaudeOAuthProvider(httpClient, credentialsPath, new FakeTimeProvider(now));
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedKind, result.FailureKind);
        Assert.DoesNotContain(Token, result.Message!);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsUnavailable_WhenHandlerThrowsHttpRequestException()
    {
        using var tempDir = new TempDirectory();
        var now = DateTimeOffset.Parse("2026-09-16T00:00:00+00:00");
        var credentialsPath = WriteCredentialsFile(tempDir.Path, now.AddHours(1));

        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("boom"));
        using var httpClient = new HttpClient(handler);

        var provider = new ClaudeOAuthProvider(httpClient, credentialsPath, new FakeTimeProvider(now));
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unavailable, result.FailureKind);
        Assert.DoesNotContain(Token, result.Message!);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsUnavailable_WhenRequestTimesOut_AndCallerTokenNotCancelled()
    {
        using var tempDir = new TempDirectory();
        var now = DateTimeOffset.Parse("2026-09-16T00:00:00+00:00");
        var credentialsPath = WriteCredentialsFile(tempDir.Path, now.AddHours(1));

        var handler = new FakeHttpMessageHandler((_, _) => throw new TaskCanceledException("timed out"));
        using var httpClient = new HttpClient(handler);

        var provider = new ClaudeOAuthProvider(httpClient, credentialsPath, new FakeTimeProvider(now));
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unavailable, result.FailureKind);
        Assert.DoesNotContain(Token, result.Message!);
    }

    [Fact]
    public async Task GetUsageAsync_PropagatesOperationCanceledException_WhenCallerTokenIsCancelled()
    {
        using var tempDir = new TempDirectory();
        var now = DateTimeOffset.Parse("2026-09-16T00:00:00+00:00");
        var credentialsPath = WriteCredentialsFile(tempDir.Path, now.AddHours(1));

        using var cts = new CancellationTokenSource();
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            cts.Cancel();
            throw new TaskCanceledException("cancelled", null, cts.Token);
        });
        using var httpClient = new HttpClient(handler);

        var provider = new ClaudeOAuthProvider(httpClient, credentialsPath, new FakeTimeProvider(now));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetUsageAsync(cts.Token));
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsUnavailable_WhenResponseBodyReadThrowsIOException()
    {
        using var tempDir = new TempDirectory();
        var now = DateTimeOffset.Parse("2026-09-16T00:00:00+00:00");
        var credentialsPath = WriteCredentialsFile(tempDir.Path, now.AddHours(1));

        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowingHttpContent()
            }));
        using var httpClient = new HttpClient(handler);

        var provider = new ClaudeOAuthProvider(httpClient, credentialsPath, new FakeTimeProvider(now));
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unavailable, result.FailureKind);
        Assert.DoesNotContain(Token, result.Message!);
    }

    private sealed class ThrowingHttpContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("Simulated read failure");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            throw new IOException("Simulated read failure");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsUnauthorized_AndSendsNoRequest_WhenCredentialsExpired()
    {
        using var tempDir = new TempDirectory();
        var now = DateTimeOffset.Parse("2026-09-16T00:00:00+00:00");
        var credentialsPath = WriteCredentialsFile(tempDir.Path, now.AddMinutes(-1));

        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);

        var provider = new ClaudeOAuthProvider(httpClient, credentialsPath, new FakeTimeProvider(now));
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.Unauthorized, result.FailureKind);
        Assert.Empty(handler.Requests);
        Assert.DoesNotContain(Token, result.Message!);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsNotFound_WhenCredentialsFileMissing()
    {
        using var tempDir = new TempDirectory();
        var missingPath = Path.Combine(tempDir.Path, "does-not-exist.json");

        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);

        var provider = new ClaudeOAuthProvider(httpClient, missingPath, new FakeTimeProvider(DateTimeOffset.UtcNow));
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.NotFound, result.FailureKind);
        Assert.DoesNotContain(Token, result.Message!);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsInvalidData_WhenCredentialsFileIsMalformed()
    {
        using var tempDir = new TempDirectory();
        var path = Path.Combine(tempDir.Path, "credentials.json");
        File.WriteAllText(path, "{ not valid json");

        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);

        var provider = new ClaudeOAuthProvider(httpClient, path, new FakeTimeProvider(DateTimeOffset.UtcNow));
        var result = await provider.GetUsageAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFailureKind.InvalidData, result.FailureKind);
        Assert.Empty(handler.Requests);
        Assert.DoesNotContain(Token, result.Message!);
    }

    [Fact]
    public void ResolveDefaultCredentialsPath_UsesClaudeConfigDir_WhenSet()
    {
        var path = ClaudeOAuthProvider.ResolveDefaultCredentialsPath(@"C:\custom\config", @"C:\Users\someone");

        Assert.Equal(Path.Combine(@"C:\custom\config", ".credentials.json"), path);
    }

    [Fact]
    public void ResolveDefaultCredentialsPath_FallsBackToUserProfile_WhenClaudeConfigDirNotSet()
    {
        var path = ClaudeOAuthProvider.ResolveDefaultCredentialsPath(null, @"C:\Users\someone");

        Assert.Equal(Path.Combine(@"C:\Users\someone", ".claude", ".credentials.json"), path);
    }
}
