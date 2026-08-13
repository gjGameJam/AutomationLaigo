using Microsoft.Playwright;

namespace LaigO.Tests;

/// <summary>
/// Assembly-level setup: creates one IPlaywright instance shared across all test classes.
/// Each test class creates its own IAPIRequestContext (per-test isolation).
/// </summary>
[SetUpFixture]
public class GlobalSetup
{
    public static IPlaywright Playwright { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task GlobalOneTimeSetUp()
    {
        // Resolving BaseUrl here makes a misconfigured base URL abort the whole run
        // with one clear error instead of a per-test "Invalid URL" failure storm.
        TestContext.Progress.WriteLine($"LAIGO base URL: {TestConfig.BaseUrl}");

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    }

    [OneTimeTearDown]
    public void GlobalOneTimeTearDown()
    {
        // Null when OneTimeSetUp aborted before Playwright was created
        // (e.g. invalid base URL) — don't stack a NRE on top of the real error.
        Playwright?.Dispose();
    }
}
