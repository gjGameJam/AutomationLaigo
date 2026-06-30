using System.Linq;
using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.ApiClient;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Contract;

/// <summary>
/// /checkout-debug/* element lookups against live LEGO.com / BrickOwl. These
/// are read-only and create no job. BrickOwl is unconfigured in MVP (its
/// listing routes translate client errors to 502); those tests Assert.Ignore on
/// 502 — a 502 means "API access pending", which is a skip, not a pass.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Debug")]
[Ignore("Checkout & quote flow is shelved/disabled (2026-06) — these /checkout-debug sourcing/optimizer tests are part of the shelved quote-pricing pipeline. Re-enable when the feature returns.")]
public class DebugLookupTests : LaigOTestBase
{
    private static T Parse<T>(string body) =>
        JsonSerializer.Deserialize<T>(body, LaigOApiClient.JsonOptions)
        ?? throw new InvalidOperationException($"failed to deserialize {typeof(T).Name}: {body}");

    [Test]
    public async Task LegoElement_KnownId_ReturnsAvailabilityFlag()
    {
        var response = await Client.GetLegoElementAsync(TestConstants.KnownLegoElementId);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"LEGO element lookup never raises HTTPException — only 200 is valid. Body: {body}");

        var result = Parse<LegoAvailabilityResponse>(body);
        result.ElementId.Should().Be(TestConstants.KnownLegoElementId,
            "the response must echo the requested element_id");
        // available_on_lego is a bool — deserialization guarantees its type.
    }

    [Test]
    public async Task LegoElements_BatchLookup_PartitionsConsistently()
    {
        var response = await Client.PostLegoElementsAsync([TestConstants.KnownLegoElementId]);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"batch LEGO lookup never raises HTTPException — only 200 is valid. Body: {body}");

        var result = Parse<LegoAvailabilityBatchResponse>(body);
        var id = TestConstants.KnownLegoElementId;

        result.Results.Should().ContainKey(id, "the results map must include the requested element");
        result.Results.Should().HaveCount(1, "we requested exactly one element");

        // available/unavailable must be a clean partition of results, and the
        // map value must agree with which bucket the id landed in.
        (result.Available.Contains(id) ^ result.Unavailable.Contains(id)).Should().BeTrue(
            "each element must appear in exactly one of available/unavailable");
        result.Results[id].Should().Be(result.Available.Contains(id),
            "results[id] must agree with the available/unavailable partition");
    }

    [Test]
    public async Task LegoElementListing_KnownId_HasListingIffAvailable()
    {
        var response = await Client.GetLegoElementListingAsync(TestConstants.KnownLegoElementId);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"LEGO listing debug returns 200 with listing=null on miss — only 200 is valid. Body: {body}");

        var result = Parse<LegoListingDebugResponse>(body);
        result.ElementId.Should().Be(TestConstants.KnownLegoElementId);

        // Contract: listing is populated iff available is true.
        if (result.Available)
        {
            result.Listing.Should().NotBeNull("available=true must include a populated listing");
            result.Listing!.SellerId.Should().Be(TestConstants.LegoSellerId,
                "a LEGO listing must be attributed to the lego_official seller");
            result.Listing.PricePerCent.Should().BeGreaterThanOrEqualTo(0);
            result.Listing.AvailableQty.Should().BeGreaterThanOrEqualTo(0);
        }
        else
        {
            result.Listing.Should().BeNull("available=false must have listing=null");
        }
    }

    [Test]
    public async Task BrickOwlElement_KnownId_ReturnsStructuredListings()
    {
        var response = await Client.GetBrickOwlElementAsync(TestConstants.KnownLegoElementId);
        var body = await response.TextAsync();

        if (response.Status == 502)
        {
            using var errDoc = JsonDocument.Parse(body);
            errDoc.RootElement.TryGetProperty("detail", out _).Should()
                .BeTrue("502 must carry a 'detail' identifying the BrickOwl error");
            Assert.Ignore("BrickOwl API access pending (502) — structured-listing assertions skipped");
            return;
        }

        response.Status.Should().Be(200, $"Body: {body}");
        var result = Parse<ElementListingsResponse>(body);

        result.ElementId.Should().Be(TestConstants.KnownLegoElementId);
        result.ListingCount.Should().Be(result.Listings.Count,
            "listing_count must match the listings array length");

        // cheapest_price_cents / most_stock must be derived consistently from listings.
        if (result.Listings.Count == 0)
        {
            result.CheapestPriceCents.Should().BeNull("no listings → no cheapest price");
            result.MostStock.Should().BeNull("no listings → no stock figure");
        }
        else
        {
            result.CheapestPriceCents.Should().Be(result.Listings.Min(l => l.PricePerCent),
                "cheapest_price_cents must be the min listing price");
            result.MostStock.Should().Be(result.Listings.Max(l => l.AvailableQty),
                "most_stock must be the max listing quantity");
            result.Listings.Should().OnlyContain(l => l.PricePerCent >= 0 && l.AvailableQty >= 0);
        }
    }

    [Test]
    public async Task BrickOwlElements_BatchLookup_HasConsistentCounts()
    {
        var response = await Client.PostBrickOwlElementsAsync([TestConstants.KnownLegoElementId]);
        var body = await response.TextAsync();

        if (response.Status == 502)
        {
            Assert.Ignore("BrickOwl API access pending (502) — batch assertions skipped");
            return;
        }

        response.Status.Should().Be(200, $"Body: {body}");
        var result = Parse<BatchListingsResponse>(body);

        result.Requested.Should().Be(1, "we requested one element");
        result.Results.Should().ContainKey(TestConstants.KnownLegoElementId);
        result.Results[TestConstants.KnownLegoElementId].ElementId.Should()
            .Be(TestConstants.KnownLegoElementId, "each result must echo its element_id");
        (result.Found + result.NotFound.Count).Should().Be(result.Requested,
            "found + not_found must account for every requested element");
    }

    [TestCase("item_no")]
    [TestCase("design_id")]
    [TestCase("bl_item_no")]
    [TestCase("set_number")]
    public async Task BrickOwlElementRaw_IdTypeVariants_ReturnDebugEnvelope(string idType)
    {
        // The raw debug route captures errors into step1_error/step2_error and
        // returns 200 unconditionally, for every id_type variant.
        var response = await Client.GetBrickOwlElementRawAsync(TestConstants.KnownLegoElementId, idType);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"raw BrickOwl debug returns 200 regardless of API state (id_type={idType}). Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.TryGetProperty("element_id", out var echoedId).Should().BeTrue("the envelope must echo element_id");
        echoedId.GetString().Should().Be(TestConstants.KnownLegoElementId);

        var hasStep1 = root.TryGetProperty("step1_id_lookup", out var lookup)
                       && lookup.ValueKind != JsonValueKind.Null
                   || root.TryGetProperty("step1_error", out var err)
                       && err.ValueKind != JsonValueKind.Null;
        hasStep1.Should().BeTrue("raw debug must populate step1_id_lookup or step1_error");
    }
}
