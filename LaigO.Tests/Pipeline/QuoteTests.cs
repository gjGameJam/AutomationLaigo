using System.Linq;
using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.ApiClient;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Pipeline;

/// <summary>
/// POST /jobs/{job_id}/checkout/quote happy path against a real completed job.
/// Confirm/payment is excluded from CI (real money). Request-validation 422s and
/// the invalid-job 404 are in Contract.QuoteValidationTests.
/// </summary>
[TestFixture]
[Category("Pipeline")]
[Category("Checkout")]
[Ignore("Checkout & quote flow is shelved/disabled (2026-06) — re-enable these fixtures when the feature returns.")]
public class QuoteTests : LaigOTestBase
{
    private static readonly QuoteRequest UsRequest = new("US", "10001", "test@example.com");

    [Test]
    public async Task Quote_CompletedJob_ReturnsPricedQuoteWithInvariants()
    {
        var (jobId, _) = await SubmitAndAwaitCompletionAsync(blockWidth: 2);

        var response = await Client.GetQuoteRawAsync(jobId, UsRequest);
        var body = await response.TextAsync();

        // With a completed job + valid order list + valid body, 200 is the only
        // normal response. Any non-200 is a real signal (404 order list missing,
        // 422 empty, 500 sourcing crash). Surface the body.
        response.Status.Should().Be(200,
            $"quote endpoint must return 200 with a completed job + valid request. Got {response.Status}. Body: {body}");

        var quote = JsonSerializer.Deserialize<QuoteResponse>(body, LaigOApiClient.JsonOptions)!;

        quote.CheckoutId.Should().NotBeNullOrWhiteSpace("quote must include a checkout_id");
        quote.CheckoutId.Should().StartWith("co_", "checkout ids are minted as co_<hex> (router.py:130)");
        quote.PiecesTotal.Should().BeGreaterThan(0, "a real mosaic has pieces");

        // expires_at must be bounded near now + 600s (router.py:131), not just
        // "in the future" — a TTL accidentally set to 1s would still be future.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        quote.ExpiresAt.Should().BeInRange(now + 300, now + 900,
            "expires_at must be ~600s out (quote TTL); allow slack for clock skew + latency");

        // Fee floor + 5% rule.
        quote.LaigoServiceFeeCents.Should().BeGreaterThanOrEqualTo(TestConstants.LaigoFeeFloorCents,
            "LAIGO fee floor is $3.00 per pricing rule");
        quote.LaigoServiceFeeCents.Should().BeGreaterThanOrEqualTo(quote.TotalCostCents / 20,
            "LAIGO fee is max($3.00, 5% of total_cost) — must be ≥ 5%");

        quote.GrandTotalCents.Should().Be(quote.TotalCostCents + quote.LaigoServiceFeeCents,
            "grand_total_cents must equal total_cost_cents + laigo_service_fee_cents");
        quote.TotalCostCents.Should().BeGreaterThanOrEqualTo(0);

        quote.Sellers.Should().NotBeNull();
        quote.UnsourceableItems.Should().NotBeNull();

        // Optimizer core contract: every piece lands in a seller allocation,
        // lego_fallback, or unsourceable. All three empty for a non-empty order
        // list means the optimizer never processed it.
        (quote.Sellers.Count + quote.UnsourceableItems.Count + quote.LegoFallbackItems.Count)
            .Should().BeGreaterThan(0,
                "the optimizer must route every piece into sellers, lego_fallback, or unsourceable");

        foreach (var seller in quote.Sellers)
        {
            seller.SellerId.Should().NotBeNullOrWhiteSpace();
            seller.SellerName.Should().NotBeNullOrWhiteSpace($"seller {seller.SellerId} must carry a display name");
            seller.PiecesCount.Should().BeGreaterThan(0, $"seller {seller.SellerId} listed with zero pieces");
            seller.PieceCostCents.Should().BeGreaterThanOrEqualTo(0);
            seller.ShippingCostCents.Should().BeGreaterThanOrEqualTo(0);
            seller.SubtotalCents.Should().Be(seller.PieceCostCents + seller.ShippingCostCents,
                $"seller {seller.SellerId} subtotal must equal pieces + shipping");

            // LEGO free-shipping invariant (optimizer.py:207-252): a lego_official
            // seller whose piece cost meets the threshold must ship free.
            if (seller.SellerId == TestConstants.LegoSellerId
                && seller.PieceCostCents >= TestConstants.LegoFreeShippingThresholdCents)
            {
                seller.ShippingCostCents.Should().Be(0,
                    $"lego_official with piece_cost {seller.PieceCostCents}¢ ≥ " +
                    $"{TestConstants.LegoFreeShippingThresholdCents}¢ must have free shipping");
            }
        }

        // total_cost_cents is pieces+shipping across all sellers (fallback cost
        // is always 0 in the response), so it must equal the sum of subtotals.
        if (quote.Sellers.Count > 0)
        {
            quote.Sellers.Sum(s => s.SubtotalCents).Should().Be(quote.TotalCostCents,
                "total_cost_cents must equal the sum of seller subtotals");
        }

        quote.CanProceed.Should().Be(quote.UnsourceableItems.Count == 0,
            "can_proceed must be true iff unsourceable_items is empty");
    }

    [Test]
    public async Task Quote_RepeatedCalls_MintDistinctCheckoutIds()
    {
        var (jobId, _) = await SubmitAndAwaitCompletionAsync(blockWidth: 2);

        var first = await Client.GetQuoteAsync(jobId, UsRequest);
        var second = await Client.GetQuoteAsync(jobId, UsRequest);

        first.CheckoutId.Should().NotBeNullOrWhiteSpace();
        second.CheckoutId.Should().NotBeNullOrWhiteSpace();
        second.CheckoutId.Should().NotBe(first.CheckoutId,
            "each /quote call mints a distinct checkout_id (secrets.token_hex) — quotes are not idempotent");

        // The priced result for the same job + request should be stable.
        second.GrandTotalCents.Should().Be(first.GrandTotalCents,
            "the same job + request must price identically across repeated quotes");
    }
}
