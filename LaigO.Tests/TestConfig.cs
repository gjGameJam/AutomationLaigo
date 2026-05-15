using Microsoft.Extensions.Configuration;

namespace LaigO.Tests;

public static class TestConfig
{
    private static readonly IConfiguration _config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.test.json", optional: false)
        .Build();

    // LAIGO_BASE_URL env var overrides the JSON file (used in GitHub Actions via secret)
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("LAIGO_BASE_URL")
        ?? _config["LaigO:BaseUrl"]
        ?? throw new InvalidOperationException("LAIGO_BASE_URL is not configured");

    public static float DefaultTimeoutMs =>
        float.TryParse(_config["LaigO:DefaultTimeoutMs"], out var v) ? v : 30_000f;

    public static int GenerationTimeoutMs =>
        int.TryParse(_config["LaigO:GenerationTimeoutMs"], out var v) ? v : 600_000;
}
