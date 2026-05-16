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
    // 1x1 plate — universally stocked by both LEGO and BrickOwl sellers
    private const string KnownLegoElementId = "3024";

    [Test]
    public async Task LegoElement_KnownId_Returns200()
    {
        var response = await Client.GetLegoElementAsync(KnownLegoElementId);

        // 3024 (1x1 plate) is one of the most fundamental LEGO elements — 404 means the
        // LEGO availability API is misconfigured or the element mapping is broken
        response.Status.Should().Be(200,
            "element 3024 must be found; 404 = LEGO API misconfigured or element unavailable; 503 = API key missing");
    }

    [Test]
    public async Task LegoElements_BatchLookup_Returns200()
    {
        var response = await Client.PostLegoElementsAsync([KnownLegoElementId]);

        response.Status.Should().Be(200,
            "element 3024 must be found; 404 = LEGO API misconfigured or element unavailable; 503 = API key missing");
    }

    [Test]
    public async Task BrickOwlElement_KnownId_ApiKeyConfigured()
    {
        var response = await Client.GetBrickOwlElementAsync(KnownLegoElementId);

        // 3024 is universally listed in BrickOwl — 404 means the lookup is broken, not that the element is absent
        response.Status.Should().Be(200,
            "element 3024 must be found; 404 = BrickOwl API misconfigured; 503 = BRICKOWL_API_KEY not set on server");
    }

    [Test]
    public async Task BrickOwlElements_BatchLookup_ApiKeyConfigured()
    {
        var response = await Client.PostBrickOwlElementsAsync([KnownLegoElementId]);

        response.Status.Should().Be(200,
            "element 3024 must be found; 404 = BrickOwl API misconfigured; 503 = BRICKOWL_API_KEY not set on server");
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
