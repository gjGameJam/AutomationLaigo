using System.Linq;
using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.Fixtures;
using Microsoft.Playwright;

namespace LaigO.Tests.Contract;

/// <summary>
/// /checkout-debug/* input validation (Pydantic 422) and missing-resource 404s.
/// Body validation runs before any marketplace call, so these never touch
/// BrickOwl/LEGO and never create a job.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Debug")]
[Ignore("Checkout & quote flow is shelved/disabled (2026-06) — these /checkout-debug sourcing/optimizer tests are part of the shelved quote-pricing pipeline. Re-enable when the feature returns.")]
public class DebugValidationTests : LaigOTestBase
{
    private static List<string> ManyIds(int n) =>
        Enumerable.Range(0, n).Select(i => i.ToString()).ToList();

    private static async Task AssertPydantic422Async(IAPIResponse response, string? namingField = null)
    {
        response.Status.Should().Be(422);
        var body = await response.TextAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out var detail).Should()
            .BeTrue("a Pydantic 422 must carry a 'detail' list");
        detail.ValueKind.Should().Be(JsonValueKind.Array);
        detail.GetArrayLength().Should().BeGreaterThan(0);
        if (namingField is not null)
            body.Should().Contain(namingField, $"the validation error must identify '{namingField}'");
    }

    private static async Task Assert404WithDetailAsync(IAPIResponse response)
    {
        response.Status.Should().Be(404);
        var body = await response.TextAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out _).Should()
            .BeTrue("a 404 must carry a 'detail' body");
    }

    // ── BrickOwl batch validators (BatchListingsRequest) ────────────────────

    [Test]
    public async Task BrickOwlElements_EmptyList_Returns422()
    {
        var response = await Client.PostBrickOwlElementsAsync([]);
        await AssertPydantic422Async(response, "element_ids");
    }

    [Test]
    public async Task BrickOwlElements_TooManyIds_Returns422()
    {
        var response = await Client.PostBrickOwlElementsAsync(ManyIds(101));
        await AssertPydantic422Async(response, "element_ids");
    }

    [Test]
    public async Task BrickOwlElements_InvalidShippingCountry_Returns422()
    {
        var response = await Client.PostBrickOwlElementsAsync(
            [TestConstants.KnownLegoElementId], shippingCountry: "USA");
        await AssertPydantic422Async(response, "shipping_country");
    }

    [Test]
    public async Task BrickOwlElements_ShippingZipTooLong_Returns422()
    {
        var response = await Client.PostBrickOwlElementsAsync(
            [TestConstants.KnownLegoElementId], shippingZip: new string('9', 21));
        await AssertPydantic422Async(response, "shipping_zip");
    }

    // ── LEGO batch validators (LegoAvailabilityBatchRequest) ────────────────

    [Test]
    public async Task LegoElements_EmptyList_Returns422()
    {
        var response = await Client.PostLegoElementsAsync([]);
        await AssertPydantic422Async(response, "element_ids");
    }

    [Test]
    public async Task LegoElements_TooManyIds_Returns422()
    {
        var response = await Client.PostLegoElementsAsync(ManyIds(101));
        await AssertPydantic422Async(response, "element_ids");
    }

    // ── Optimize body validator (OptimizePreviewRequest) ────────────────────

    [Test]
    public async Task Optimize_InvalidShippingCountry_Returns422()
    {
        // Body validation fires before the order-list read, so a malformed body
        // 422s even against a non-existent job.
        var response = await Client.PostJsonRawAsync(
            $"/checkout-debug/job/{TestConstants.NonExistentJobId}/optimize",
            new { shipping_country = "USA", shipping_zip = "90210" });
        await AssertPydantic422Async(response, "shipping_country");
    }

    // ── Missing-resource 404s ───────────────────────────────────────────────

    [Test]
    public async Task OrderList_NonExistentJob_Returns404()
    {
        var response = await Client.GetJobOrderListAsync(TestConstants.NonExistentJobId);
        await Assert404WithDetailAsync(response);
    }

    [Test]
    public async Task OptimizerPreview_NonExistentJob_Returns404()
    {
        // Valid default body → reaches the handler → order-list read → 404.
        var response = await Client.GetOptimizerPreviewAsync(TestConstants.NonExistentJobId);
        await Assert404WithDetailAsync(response);
    }
}
