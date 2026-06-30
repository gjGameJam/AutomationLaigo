using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Quarantine;

/// <summary>
/// KNOWN-FAILING quarantine. This canary detects the live LEGO sourcing outage:
/// LEGO moved Pick-a-Brick from the REST search API to GraphQL, so the deployed
/// backend's _search() 404s for every element and all LEGO availability/price
/// lookups fail. The fix lives in the LAIGO backend (rewrite _search to POST the
/// GraphQL query + redeploy), not in this test suite.
///
/// It is [Explicit] + Category("KnownFailing") so it is EXCLUDED from the gating
/// nightly run — a permanently-red test in the main suite trains everyone to
/// ignore red. Run it on demand (or via the non-gating CI step) to detect the
/// moment LEGO sourcing is restored; when it goes green, promote it back into
/// Contract.DebugLookupTests and delete this folder.
/// </summary>
[TestFixture]
[Explicit("Known-failing: LEGO Pick-a-Brick moved to GraphQL; backend _search() fix pending + redeploy")]
[Category("KnownFailing")]
[Category("Debug")]
[Ignore("Checkout & quote flow is shelved/disabled (2026-06) — this canary relies on LEGO sourcing, part of the shelved quote-pricing pipeline. Re-enable when the feature returns.")]
public class LegoSourcingCanaryTests : LaigOTestBase
{
    [Test]
    public async Task LegoListing_CanaryElement_IsSourceableWithRealPrice()
    {
        // 302421 is a stable, always-stocked Pick-a-Brick element — the single
        // health check for the whole LEGO sourcing chain (_search →
        // _parse_available → _parse_price_cents). available==false here is a real
        // outage, not a stockout; available+price==0 is price-parser drift.
        var response = await Client.GetLegoElementListingAsync(TestConstants.PricedLegoElementId);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"LEGO listing debug returns 200 with listing=null on miss — only 200 is valid. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("element_id", out var echoedId).Should().BeTrue();
        echoedId.GetString().Should().Be(TestConstants.PricedLegoElementId, "the listing must echo the requested element");
        root.TryGetProperty("available", out var available).Should().BeTrue();
        root.TryGetProperty("listing", out var listing).Should().BeTrue();

        available.GetBoolean().Should().BeTrue(
            $"canary element {TestConstants.PricedLegoElementId} is known in-stock, so available=false means " +
            "LEGO sourcing is broken — most likely _LEGO_SEARCH_URL moved/404'd (LEGO migrated Pick-a-Brick " +
            "to GraphQL) or _parse_available() field names drifted. This empties every customer /quote. " +
            $"Body: {body}");

        listing.ValueKind.Should().Be(JsonValueKind.Object,
            "available=true must include a populated listing object");
        listing.TryGetProperty("price_per_cent", out var price).Should()
            .BeTrue("SellerListing must include price_per_cent");
        price.GetInt32().Should().BeGreaterThan(0,
            $"price_per_cent=0 for available element {TestConstants.PricedLegoElementId} means " +
            "_parse_price_cents() fell through every field-name candidate — LEGO's API shape drifted.");
    }
}
