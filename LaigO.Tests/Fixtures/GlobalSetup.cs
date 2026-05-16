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
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    }

    [OneTimeTearDown]
    public void GlobalOneTimeTearDown()
    {
        Playwright.Dispose();
    }
}
