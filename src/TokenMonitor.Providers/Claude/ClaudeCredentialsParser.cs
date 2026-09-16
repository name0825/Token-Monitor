using System.Text.Json;

namespace TokenMonitor.Providers.Claude;

public static class ClaudeCredentialsParser
{
    public static ClaudeCredentials? Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!oauth.TryGetProperty("accessToken", out var tokenElement) || tokenElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            if (!oauth.TryGetProperty("expiresAt", out var expiresAtElement) || expiresAtElement.ValueKind != JsonValueKind.Number || !expiresAtElement.TryGetInt64(out var expiresAtMs))
            {
                return null;
            }

            return new ClaudeCredentials(tokenElement.GetString()!, DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs));
        }
    }
}
