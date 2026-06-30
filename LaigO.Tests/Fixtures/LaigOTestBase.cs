using FluentAssertions;
using Microsoft.Playwright;
using LaigO.Tests.ApiClient;
using LaigO.Tests.Models;

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

    /// <summary>
    /// Submit a generate job and poll until it completes, hard-failing (not
    /// skipping) if it doesn't — a broken pipeline must surface, not hide.
    /// Each call creates a fresh, independent job (no shared state between
    /// tests). Returns the submitted job_id and the terminal status response.
    ///
    /// NOTE: every pipeline test that needs a completed job calls this and pays
    /// a full generation cycle. A shared one-time completed-job fixture for the
    /// read-only consumers (quote / optimize / order-list / artifact / preview)
    /// would cut several cycles from the nightly run — see the audit's A4.
    /// </summary>
    protected async Task<(string JobId, JobStatusResponse Finished)> SubmitAndAwaitCompletionAsync(
        int blockWidth = 2,
        string mosaicType = "2d",
        double bgPct = 100,
        bool toFrame = false)
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth, mosaicType, bgPct, toFrame);
        var finished = await Client.WaitForJobAsync(submitted.JobId);
        finished.Status.Should().Be("complete",
            $"job {submitted.JobId} must complete. status={finished.Status} error={finished.Error}");
        return (submitted.JobId, finished);
    }
}
