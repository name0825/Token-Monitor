namespace TokenMonitor.Providers.Claude;

public sealed class ClaudeCredentials
{
    public ClaudeCredentials(string accessToken, DateTimeOffset expiresAt)
    {
        AccessToken = accessToken;
        ExpiresAt = expiresAt;
    }

    public string AccessToken { get; }
    public DateTimeOffset ExpiresAt { get; }

    public override string ToString() => $"ClaudeCredentials {{ ExpiresAt = {ExpiresAt} }}";
}
