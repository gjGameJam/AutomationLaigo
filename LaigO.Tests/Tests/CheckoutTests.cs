using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.ApiClient;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Tests;

/// <summary>
/// Tests for the checkout quote flow.
/// Confirm/payment tests are deliberately excluded from nightly CI to avoid
/// placing real Stripe charges or LEGO.com orders.
/// </summary>
[TestFixture]
[Category("Checkout")]
public class CheckoutTests : LaigOTestBase
{
    [Test]
    public async Task Quote_InvalidJobId_Returns404WithDetail()
    {
        var request = new QuoteRequest("US", "10001", "test@example.com");
        var response = await Client.GetQuoteRawAsync("00000000-0000-0000-0000-000000000000", request);

        response.Status.Should().Be(404);

        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace("404 must include a detail body");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out _).Should()
            .BeTrue("FastAPI 404 must include a 'detail' field");
    }

    [Test]
    public async Task Quote_WithCompletedJob_ReturnsValidQuote()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2);
        var finished = await Client.WaitForJobAsync(submitted.JobId);

        // Hard-fail (not skip) if generation didn't complete — the quote flow
        // depends on a real order_list.json. If generation broke, this test
        // *should* surface that, not silently skip.
        finished.Status.Should().Be("complete",
            $"quote test requires a completed job. Job {submitted.JobId} status={finished.Status} error={finished.Error}");

        var request = new QuoteRequest("US", "10001", "test@example.com");
        var response = await Client.GetQuoteRawAsync(submitted.JobId, request);
        var body = await response.TextAsync();

        // LEGO.com is the primary source and is live in MVP; with a completed
        // job + valid order list + valid request body, 200 is the only normal
        // response. The /quote endpoint never raises 503 in the current code —
        // a sourcing exception would surface as 500 via the global handler.
        // Any non-200 is a real signal: 404=order list missing, 422=empty list,
        // 500=sourcing crash. Surface the body so the failure is diagnosable.
        response.Status.Should().Be(200,
            $"quote endpoint must return 200 with a completed job + valid request. " +
            $"Got {response.Status}. Body: {body}");

        var quote = JsonSerializer.Deserialize<QuoteResponse>(body, LaigOApiClient.JsonOptions)!;

        quote.CheckoutId.Should().NotBeNullOrWhiteSpace("quote must include a checkout_id");
        quote.PiecesTotal.Should().BeGreaterThan(0, "a real mosaic has pieces");
        quote.ExpiresAt.Should().BeGreaterThan(
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            "expires_at must be in the future");

        quote.LaigoServiceFeeCents.Should().BeGreaterThanOrEqualTo(300,
            "LAIGO fee floor is $3.00 per pricing rule");
        quote.LaigoServiceFeeCents.Should().BeGreaterThanOrEqualTo(
            quote.TotalCostCents / 20,
            "LAIGO fee is max($3.00, 5% of total_cost) — must be ≥ 5%");

        quote.GrandTotalCents.Should().Be(
            quote.TotalCostCents + quote.LaigoServiceFeeCents,
            "grand_total_cents must equal total_cost_cents + laigo_service_fee_cents");

        quote.Sellers.Should().NotBeNull();
        quote.UnsourceableItems.Should().NotBeNull();

        // The optimizer's core contract: every piece in the order list lands in
        // EITHER a seller allocation OR unsourceable_items (or, when BrickOwl is
        // live, lego_fallback_items). If both sellers and unsourceable are empty
        // for a non-empty order list, the optimizer didn't process the order at
        // all — that's a real bug. An empty sellers list with a populated
        // unsourceable list is a legitimate degraded state (LEGO scraping down,
        // BrickOwl not yet configured) and the test must not false-alarm on it.
        (quote.Sellers.Count + quote.UnsourceableItems.Count + quote.LegoFallbackItems.Count)
            .Should().BeGreaterThan(0,
                "the optimizer must route every piece into sellers, lego_fallback, " +
                "or unsourceable — all three empty means the order list never reached " +
                "the optimizer");

        foreach (var seller in quote.Sellers)
        {
            seller.SellerId.Should().NotBeNullOrWhiteSpace();
            seller.PiecesCount.Should().BeGreaterThan(0,
                $"seller {seller.SellerId} listed with zero pieces");
            seller.SubtotalCents.Should().Be(
                seller.PieceCostCents + seller.ShippingCostCents,
                $"seller {seller.SellerId} subtotal must equal pieces + shipping");
        }

        quote.CanProceed.Should().Be(
            quote.UnsourceableItems.Count == 0,
            "can_proceed must be true iff unsourceable_items is empty");
    }

    [Test]
    public async Task CheckoutStatus_InvalidIds_Returns404WithDetail()
    {
        var response = await Client.GetCheckoutStatusRawAsync(
            "00000000-0000-0000-0000-000000000000",
            "nonexistent-checkout-id");

        response.Status.Should().Be(404);

        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out _).Should().BeTrue();
    }
}
