using TokenMonitor.Providers.Claude;

namespace TokenMonitor.Tests.Providers;

public class ClaudeCredentialsParserTests
{
    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "claude", "credentials.sample.json"));

    [Fact]
    public void Parse_ReadsTokenAndExpiry_FromFixture()
    {
        var credentials = ClaudeCredentialsParser.Parse(ReadFixture());

        Assert.NotNull(credentials);
        Assert.Equal("sk-ant-oat01-FAKE", credentials!.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1789514488600), credentials.ExpiresAt);
    }

    [Fact]
    public void ToString_DoesNotContainAccessToken()
    {
        var credentials = ClaudeCredentialsParser.Parse(ReadFixture());

        Assert.NotNull(credentials);
        Assert.DoesNotContain(credentials!.AccessToken, credentials.ToString());
    }

    [Fact]
    public void Parse_ReturnsNull_ForMalformedJson()
    {
        var credentials = ClaudeCredentialsParser.Parse("{ not valid json");

        Assert.Null(credentials);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenOauthSectionMissing()
    {
        var credentials = ClaudeCredentialsParser.Parse("{}");

        Assert.Null(credentials);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenExpiresAtIsString()
    {
        var json = """
        {
          "claudeAiOauth": {
            "accessToken": "sk-ant-oat01-FAKE",
            "expiresAt": "1789514488600"
          }
        }
        """;

        var credentials = ClaudeCredentialsParser.Parse(json);

        Assert.Null(credentials);
    }
}
