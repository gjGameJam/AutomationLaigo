using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Tests;

/// <summary>
/// Tests for /checkout-debug/* endpoints.
/// Element lookups use well-known LEGO element IDs.
/// Optimizer tests require a completed generate job.
/// </summary>
[TestFixture]
[Category("Debug")]
public class DebugTests : LaigOTestBase
{
    // 1x1 round plate — a common LEGO element present in most palettes
    private const string KnownLegoElementId = "4073";

    [Test]
    public async Task LegoElement_KnownId_ReturnsNon500()
    {
        var response = await Client.GetLegoElementAsync(KnownLegoElementId);

        // 200 = in stock, 404 = not available — both are acceptable structured responses
        response.Status.Should().NotBe(500, "unhandled server errors must never leak");
    }

    [Test]
    public async Task LegoElements_BatchLookup_ReturnsNon500()
    {
        var response = await Client.PostLegoElementsAsync([KnownLegoElementId]);

        response.Status.Should().NotBe(500, "unhandled server errors must never leak");
    }

    [Test]
    public async Task BrickOwlElement_KnownId_ApiKeyConfigured()
    {
        var response = await Client.GetBrickOwlElementAsync(KnownLegoElementId);

        // 200 = pricing found; 404 = element not in BrickOwl catalog
        // 503 means BRICKOWL_API_KEY is missing from the server environment — fail loudly
        response.Status.Should().BeOneOf(new[] { 200, 404 },
            "503 = BRICKOWL_API_KEY not configured on the server; 500 = unhandled server error");
    }

    [Test]
    public async Task BrickOwlElements_BatchLookup_ApiKeyConfigured()
    {
        var response = await Client.PostBrickOwlElementsAsync([KnownLegoElementId]);

        response.Status.Should().BeOneOf(new[] { 200, 404 },
            "503 = BRICKOWL_API_KEY not configured on the server; 500 = unhandled server error");
    }

    [Test]
    public async Task OptimizerPreview_WithCompletedJob_ReturnsStructuredResponse()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2);
        var finished = await Client.WaitForJobAsync(submitted.JobId);

        Assume.That(finished.Status, Is.EqualTo("complete"),
            "Skipping optimizer test: generate job failed, cannot test optimizer without order_list.json");

        var optimizerResponse = await Client.GetOptimizerPreviewAsync(submitted.JobId);
        // 200 = allocation computed; 404 = order list not yet available; 422 = pieces unsourceable; 503 = sourcing API down
        optimizerResponse.Status.Should().NotBe(500,
            "optimizer preview must return a structured response, not an unhandled server error");
    }
}
