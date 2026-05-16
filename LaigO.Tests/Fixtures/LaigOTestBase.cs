using Microsoft.Playwright;
using LaigO.Tests.ApiClient;

namespace LaigO.Tests.Fixtures;

/// <summary>
/// Base class for all LAIGO API tests.
/// Creates a fresh IAPIRequestContext per test for isolation, and exposes
/// a pre-configured LaigOApiClient.
/// </summary>
[TestFixture]
public abstract class LaigOTestBase
{
    private IAPIRequestContext _context = null!;
    protected LaigOApiClient Client { get; private set; } = null!;

    [SetUp]
    public async Task BaseSetUp()
    {
        _context = await GlobalSetup.Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL = TestConfig.BaseUrl,
            Timeout = TestConfig.DefaultTimeoutMs,
            IgnoreHTTPSErrors = true,
        });
        Client = new LaigOApiClient(_context, TestConfig.BaseUrl);
    }

    [TearDown]
    public async Task BaseTearDown()
    {
        await _context.DisposeAsync();
    }

    /// <summary>
    /// Absolute path to the small test JPEG committed to the repo.
    /// </summary>
    protected static string TestImagePath =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "TestImages", "test_image.jpg");
}
