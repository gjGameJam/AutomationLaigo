using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Contract;

/// <summary>
/// CORS allow-list enforcement (Main.py:454-463). allow_origins is restricted
/// to the frontend + localhost:5173 with allow_credentials=true. A regression
/// loosening this to ["*"] would be invisible without these tests.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Cors")]
public class CorsTests : LaigOTestBase
{
    private const string AllowedOrigin = "https://laigo-frontend.onrender.com";
    private const string DisallowedOrigin = "https://attacker.example.com";

    [Test]
    public async Task Cors_AllowedOrigin_IsEchoedWithCredentials()
    {
        var response = await Client.FetchRawAsync("/health", "GET",
            new Dictionary<string, string> { ["Origin"] = AllowedOrigin });

        response.Headers.TryGetValue("access-control-allow-origin", out var allowOrigin);
        allowOrigin.Should().Be(AllowedOrigin,
            "the configured frontend origin must be echoed in Access-Control-Allow-Origin");

        // allow_credentials=true → the credentials header must accompany the echo,
        // otherwise the browser drops credentialed responses.
        response.Headers.TryGetValue("access-control-allow-credentials", out var allowCreds);
        allowCreds.Should().Be("true",
            "allow_credentials=true must surface as Access-Control-Allow-Credentials: true");
    }

    [Test]
    public async Task Cors_DisallowedOrigin_IsNotEchoed()
    {
        var response = await Client.FetchRawAsync("/health", "GET",
            new Dictionary<string, string> { ["Origin"] = DisallowedOrigin });

        response.Headers.TryGetValue("access-control-allow-origin", out var allowOrigin);
        allowOrigin.Should().NotBe(DisallowedOrigin,
            "a disallowed origin must never be echoed — that would be an open CORS policy");
        allowOrigin.Should().NotBe("*",
            "the allow-list must never collapse to a wildcard (allow_credentials=true makes '*' a real leak)");
    }
}
