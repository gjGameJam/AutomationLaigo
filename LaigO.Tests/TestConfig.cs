using Microsoft.Extensions.Configuration;

namespace LaigO.Tests;

public static class TestConfig
{
    private static readonly IConfiguration _config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.test.json", optional: false)
        .Build();

    // LAIGO_BASE_URL env var overrides the JSON file (used in GitHub Actions via secret).
    // GitHub Actions exports the var as "" when the secret is unset, so an empty or
    // whitespace value must fall back to the JSON default rather than win the coalesce.
    private static readonly Lazy<string> _baseUrl = new(() =>
    {
        var fromEnv = Environment.GetEnvironmentVariable("LAIGO_BASE_URL");
        var raw = !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : _config["LaigO:BaseUrl"];
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"LAIGO base URL is not a valid absolute http(s) URL. " +
                $"LAIGO_BASE_URL env var: '{fromEnv ?? "<null>"}', " +
                $"appsettings LaigO:BaseUrl: '{_config["LaigO:BaseUrl"] ?? "<null>"}'.");
        return trimmed;
    });

    public static string BaseUrl => _baseUrl.Value;

    public static float DefaultTimeoutMs =>
        float.TryParse(_config["LaigO:DefaultTimeoutMs"], out var v) ? v : 30_000f;

    public static int GenerationTimeoutMs =>
        int.TryParse(_config["LaigO:GenerationTimeoutMs"], out var v) ? v : 600_000;
}
