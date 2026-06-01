using System.IO.Compression;
using System.Text.Json;
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

    // Price-parser canary. A high-volume Pick-a-Brick element used to verify that
    // LEGO.com's search-API price field is still being parsed. If the search-API
    // JSON shape drifts, _parse_price_cents() (clients/lego_client.py) falls through
    // every field-name candidate, the backend logs a warning and substitutes
    // price_per_cent=0 while keeping available=true. So available+price==0 is the
    // drift signal.
    private const string PricedLegoElementId = "302421";

    // Expected statuses per endpoint, derived from what the backend actually raises:
    //
    // LEGO endpoints (availability single+batch, listing): the routes never raise
    // HTTPException — `check_element_available` and `get_listing_for_element`
    // return bool/None even on Playwright failure. So the contract is 200; any
    // other status means an unhandled exception escaped to the global handler.
    //
    // BrickOwl endpoints: the listing routes explicitly translate client errors
    // to 502 ("BrickOwl API error: ..."). 502 is the documented "API not yet
    // configured" state per project memory. The raw debug endpoint captures
    // errors into step1_error/step2_error fields and returns 200 unconditionally.
    private static readonly int[] BrickOwlListingValidStatuses = [200, 502];

    [Test]
    public async Task LegoElement_KnownId_ReturnsStockStatus()
    {
        var response = await Client.GetLegoElementAsync(KnownLegoElementId);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"LEGO element lookup never raises HTTPException — only 200 is valid. Got {response.Status}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object,
            "LegoAvailabilityResponse must be a JSON object");
        doc.RootElement.TryGetProperty("element_id", out var echoedId).Should().BeTrue();
        echoedId.GetString().Should().Be(KnownLegoElementId);
        doc.RootElement.TryGetProperty("available_on_lego", out var available).Should().BeTrue();
        available.ValueKind.Should().BeOneOf(new[] { JsonValueKind.True, JsonValueKind.False },
            "available_on_lego must be a boolean");
    }

    [Test]
    public async Task LegoElements_BatchLookup_ReturnsResultsArray()
    {
        var response = await Client.PostLegoElementsAsync([KnownLegoElementId]);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"batch LEGO lookup never raises HTTPException — only 200 is valid. Got {response.Status}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Object,
            "LegoAvailabilityBatchResponse must be a JSON object");

        // Shape: {available: [...], unavailable: [...], results: {element_id: bool}}
        root.TryGetProperty("available", out var available).Should().BeTrue();
        available.ValueKind.Should().Be(JsonValueKind.Array);
        root.TryGetProperty("unavailable", out var unavailable).Should().BeTrue();
        unavailable.ValueKind.Should().Be(JsonValueKind.Array);
        root.TryGetProperty("results", out var results).Should()
            .BeTrue("batch response must include a 'results' map");
        results.TryGetProperty(KnownLegoElementId, out var entry).Should()
            .BeTrue($"results map must include the requested element {KnownLegoElementId}");
        entry.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    [Test]
    public async Task BrickOwlElement_KnownId_ReturnsStructuredResponse()
    {
        var response = await Client.GetBrickOwlElementAsync(KnownLegoElementId);
        var body = await response.TextAsync();

        response.Status.Should().BeOneOf(BrickOwlListingValidStatuses,
            $"BrickOwl lookup must return 200 (sourced) or 502 (API pending). Got {response.Status}. Body: {body}");

        if (response.Status == 502)
        {
            // Documented degraded state: BrickOwl API access pending. Confirm the
            // 502 body still includes 'detail' so the failure is operator-readable.
            using var errDoc = JsonDocument.Parse(body);
            errDoc.RootElement.TryGetProperty("detail", out _).Should()
                .BeTrue("502 must include a 'detail' field identifying the BrickOwl error");
            return;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Object,
            "ElementListingsResponse must be a JSON object");
        root.TryGetProperty("element_id", out _).Should().BeTrue();
        root.TryGetProperty("listing_count", out _).Should().BeTrue();
        root.TryGetProperty("listings", out var listings).Should().BeTrue();
        listings.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Test]
    public async Task BrickOwlElements_BatchLookup_ReturnsResultsArray()
    {
        var response = await Client.PostBrickOwlElementsAsync([KnownLegoElementId]);
        var body = await response.TextAsync();

        response.Status.Should().BeOneOf(BrickOwlListingValidStatuses,
            $"batch BrickOwl lookup must return 200 (sourced) or 502 (API pending). Got {response.Status}. Body: {body}");

        if (response.Status == 502)
        {
            using var errDoc = JsonDocument.Parse(body);
            errDoc.RootElement.TryGetProperty("detail", out _).Should().BeTrue();
            return;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Object,
            "BatchListingsResponse must be a JSON object");
        root.TryGetProperty("requested", out var requested).Should().BeTrue();
        requested.GetInt32().Should().Be(1, "we requested one element");
        root.TryGetProperty("results", out var results).Should().BeTrue();
        results.TryGetProperty(KnownLegoElementId, out _).Should()
            .BeTrue("results map must include the requested element");
    }

    [Test]
    public async Task BrickOwlElementRaw_KnownId_ReturnsDebugPayload()
    {
        var response = await Client.GetBrickOwlElementRawAsync(KnownLegoElementId);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"raw BrickOwl debug captures errors into step1_error/step2_error — only 200 is valid. Got {response.Status}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        // raw debug envelope always populates step1_id_lookup OR step1_error.
        var hasStep1 = doc.RootElement.TryGetProperty("step1_id_lookup", out var lookup)
                   && lookup.ValueKind != JsonValueKind.Null
                   || doc.RootElement.TryGetProperty("step1_error", out var err)
                   && err.ValueKind != JsonValueKind.Null;
        hasStep1.Should().BeTrue(
            "raw debug must populate step1_id_lookup or step1_error");
    }

    [Test]
    public async Task LegoElementListing_KnownId_ReturnsStructuredResponse()
    {
        var response = await Client.GetLegoElementListingAsync(KnownLegoElementId);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"LEGO listing debug returns 200 with listing=null on miss — only 200 is valid. Got {response.Status}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Object,
            "listing response must be a JSON object");
        root.TryGetProperty("element_id", out var echoedId).Should().BeTrue();
        echoedId.GetString().Should().Be(KnownLegoElementId);
        root.TryGetProperty("available", out var available).Should().BeTrue();
        available.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        root.TryGetProperty("listing", out var listing).Should().BeTrue();
        // Contract: listing is null iff available is false.
        if (available.GetBoolean())
            listing.ValueKind.Should().Be(JsonValueKind.Object,
                "available=true must include a populated listing object");
        else
            listing.ValueKind.Should().Be(JsonValueKind.Null,
                "available=false must have listing=null");
    }

    [Test]
    public async Task LegoListing_CanaryElement_IsSourceableWithRealPrice()
    {
        // 302421 is a stable, always-stocked Pick-a-Brick element. It is the
        // single-element health check for the entire LEGO sourcing path, which
        // chains: _search() (live LEGO search endpoint) → _parse_available() →
        // _parse_price_cents(). A break in ANY link makes this element look
        // unsourceable, which silently empties every customer /quote.
        //
        // The backend collapses three very different states into the response:
        //   available=true,  price_per_cent>0   → healthy
        //   available=true,  price_per_cent==0  → price parser drifted
        //                                          (_parse_price_cents field names)
        //   available=false, listing=null       → search endpoint dead/moved
        //                                          (_LEGO_SEARCH_URL) OR availability
        //                                          parser drifted OR a genuine stockout
        // Because 302421 is known-stocked, available=false here is a real outage,
        // not a stockout — so we fail loudly rather than tolerate it.
        var response = await Client.GetLegoElementListingAsync(PricedLegoElementId);
        var body = await response.TextAsync();

        response.Status.Should().Be(200,
            $"LEGO listing debug returns 200 with listing=null on miss — only 200 is valid. Got {response.Status}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.TryGetProperty("available", out var available).Should().BeTrue();
        available.ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        root.TryGetProperty("listing", out var listing).Should().BeTrue();

        available.GetBoolean().Should().BeTrue(
            $"canary element {PricedLegoElementId} is known to be in stock on LEGO.com Pick-a-Brick, " +
            "so available=false means LEGO sourcing is broken — most likely the internal search " +
            "endpoint (_LEGO_SEARCH_URL in scripts/checkout/clients/lego_client.py) has moved/404'd, " +
            "or _parse_available()'s field names drifted. This empties every customer /quote. " +
            $"Body: {body}");

        // available == true: a populated listing with a real price must be present.
        // price_per_cent == 0 means _parse_price_cents() fell through every field-name
        // candidate, i.e. LEGO.com's search-API JSON shape drifted.
        listing.ValueKind.Should().Be(JsonValueKind.Object,
            "available=true must include a populated listing object");
        listing.TryGetProperty("price_per_cent", out var price).Should()
            .BeTrue("SellerListing must include price_per_cent");
        price.ValueKind.Should().Be(JsonValueKind.Number, "price_per_cent must be numeric");
        price.GetInt32().Should().BeGreaterThan(0,
            $"price_per_cent=0 for available element {PricedLegoElementId} means LEGO.com's " +
            "search-API JSON shape drifted — update the field-name candidates in " +
            "_parse_price_cents() (scripts/checkout/clients/lego_client.py) against the live API.");
    }

    [Test]
    public async Task OptimizerPreview_WithCompletedJob_ReturnsAllocation()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2);
        var finished = await Client.WaitForJobAsync(submitted.JobId);

        // Hard-fail (not skip) — if generation broke, this test must surface that.
        finished.Status.Should().Be("complete",
            $"optimizer test requires a completed job. Job {submitted.JobId} status={finished.Status} error={finished.Error}");

        var orderListResponse = await Client.GetJobOrderListAsync(submitted.JobId);

        // Diagnostic block: on non-200, inspect the artifact ZIP so the failure
        // tells us *why* — was order_list.json never produced (pic_to_mosaic
        // crashed silently), or was it produced but never copied to the stable
        // path?
        if (orderListResponse.Status != 200)
        {
            var orderListBody = await orderListResponse.TextAsync();

            string zipDiagnostic;
            try
            {
                var zipBytes = await Client.DownloadArtifactAsync(submitted.JobId);
                using var zipStream = new MemoryStream(zipBytes);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

                var entries = string.Join(
                    ", ",
                    archive.Entries.Select(e => $"{e.FullName} ({e.Length}B)"));

                var manifestEntry = archive.GetEntry("manifest.json");
                string manifestText;
                if (manifestEntry is null)
                {
                    manifestText = "(no manifest.json in zip)";
                }
                else
                {
                    using var reader = new StreamReader(manifestEntry.Open());
                    manifestText = await reader.ReadToEndAsync();
                }

                var hasOrderList = archive.GetEntry("OrderLists/order_list.json") != null;

                zipDiagnostic =
                    $"Artifact ZIP entries: [{entries}]\n" +
                    $"ZIP contains OrderLists/order_list.json: {hasOrderList}\n" +
                    $"Manifest: {manifestText}";
            }
            catch (Exception ex)
            {
                zipDiagnostic = $"Artifact download/inspect failed: {ex.GetType().Name}: {ex.Message}";
            }

            Assert.Fail(
                $"Job {submitted.JobId} reported complete but /checkout-debug/job/{{id}}/order-list returned {orderListResponse.Status}.\n" +
                $"Response body: {orderListBody}\n" +
                $"{zipDiagnostic}");
        }

        // Order-list response shape: {job_id, item_count, total_pieces, items: [...]}
        var orderBody = await orderListResponse.TextAsync();
        orderBody.Should().NotBeNullOrWhiteSpace();
        using (var orderDoc = JsonDocument.Parse(orderBody))
        {
            var root = orderDoc.RootElement;

            root.TryGetProperty("job_id", out var echoedJobId).Should()
                .BeTrue("order-list response must echo job_id");
            echoedJobId.GetString().Should().Be(submitted.JobId,
                "echoed job_id must match the requested job");

            root.TryGetProperty("items", out var items).Should()
                .BeTrue("order-list response must include 'items'");
            items.ValueKind.Should().Be(JsonValueKind.Array);
            items.GetArrayLength().Should().BeGreaterThan(0,
                "a real mosaic produces at least one piece");

            root.TryGetProperty("item_count", out var itemCount).Should().BeTrue();
            itemCount.GetInt32().Should().Be(items.GetArrayLength(),
                "item_count must match items array length");

            root.TryGetProperty("total_pieces", out var totalPieces).Should().BeTrue();
            var computedTotal = 0;
            foreach (var item in items.EnumerateArray())
            {
                item.TryGetProperty("elementId", out _).Should()
                    .BeTrue("each order item must have 'elementId'");
                item.TryGetProperty("quantity", out var qty).Should()
                    .BeTrue("each order item must have 'quantity'");
                qty.GetInt32().Should().BeGreaterThan(0);
                computedTotal += qty.GetInt32();
            }
            totalPieces.GetInt32().Should().Be(computedTotal,
                "total_pieces must equal sum of all item quantities");
        }

        // Optimizer preview, with a body now sent. Expected statuses:
        //   200 — full allocation computed (LEGO is the primary source and is live)
        //   502 — marketplace listings fetch raised (genuine third-party outage)
        // Anything else — 404/422/500/503 — indicates a real bug: the job was
        // proven complete above, the body validates, the order list is non-empty.
        var optimizerResponse = await Client.GetOptimizerPreviewAsync(submitted.JobId);
        var optimizerBody = await optimizerResponse.TextAsync();

        optimizerResponse.Status.Should().BeOneOf(new[] { 200, 502 },
            $"optimizer preview with a completed job + valid body must return 200, " +
            $"or 502 on a marketplace outage. Got {optimizerResponse.Status}. Body: {optimizerBody}");

        if (optimizerResponse.Status == 502)
        {
            using var errDoc = JsonDocument.Parse(optimizerBody);
            errDoc.RootElement.TryGetProperty("detail", out _).Should()
                .BeTrue("502 must include a 'detail' field identifying the listings fetch error");
            return;
        }

        using var optDoc = JsonDocument.Parse(optimizerBody);
        var optRoot = optDoc.RootElement;

        // Note: /optimize uses DIFFERENT field names than /quote.
        // /optimize: grand_total_cents (pieces+shipping pre-fee), laigo_fee_cents, customer_total_cents
        // /quote:    total_cost_cents (pieces+shipping pre-fee), laigo_service_fee_cents, grand_total_cents
        optRoot.TryGetProperty("job_id", out var optJobId).Should().BeTrue();
        optJobId.GetString().Should().Be(submitted.JobId);

        optRoot.TryGetProperty("grand_total_cents", out var grandTotal).Should()
            .BeTrue("optimizer 200 must include grand_total_cents (pieces+shipping pre-fee)");
        optRoot.TryGetProperty("laigo_fee_cents", out var laigoFee).Should()
            .BeTrue("optimizer 200 must include laigo_fee_cents");
        optRoot.TryGetProperty("customer_total_cents", out var customerTotal).Should()
            .BeTrue("optimizer 200 must include customer_total_cents");

        laigoFee.GetInt32().Should().BeGreaterThanOrEqualTo(300,
            "LAIGO fee floor is $3.00");
        customerTotal.GetInt32().Should().Be(
            grandTotal.GetInt32() + laigoFee.GetInt32(),
            "customer_total_cents must equal grand_total_cents + laigo_fee_cents");

        optRoot.TryGetProperty("can_proceed", out var canProceed).Should().BeTrue();
        optRoot.TryGetProperty("unsourceable_items", out var unsourceable).Should().BeTrue();
        canProceed.GetBoolean().Should().Be(unsourceable.GetArrayLength() == 0,
            "can_proceed must be true iff unsourceable_items is empty");
    }
}
