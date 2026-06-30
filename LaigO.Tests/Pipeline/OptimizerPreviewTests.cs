using System.Linq;
using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.ApiClient;
using LaigO.Tests.Diagnostics;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Pipeline;

/// <summary>
/// /checkout-debug/job/{id}/order-list and /optimize against a real completed
/// job — one atomic test each (own job). 404/422 paths are in
/// Contract.DebugValidationTests.
/// </summary>
[TestFixture]
[Category("Pipeline")]
[Category("Debug")]
[Ignore("Checkout & quote flow is shelved/disabled (2026-06) — these /checkout-debug sourcing/optimizer tests are part of the shelved quote-pricing pipeline. Re-enable when the feature returns.")]
public class OptimizerPreviewTests : LaigOTestBase
{
    private static T Parse<T>(string body) =>
        JsonSerializer.Deserialize<T>(body, LaigOApiClient.JsonOptions)
        ?? throw new InvalidOperationException($"failed to deserialize {typeof(T).Name}: {body}");

    [Test]
    public async Task OrderList_CompletedJob_ReturnsConsistentTotals()
    {
        var (jobId, _) = await SubmitAndAwaitCompletionAsync(blockWidth: 2);

        var response = await Client.GetJobOrderListAsync(jobId);

        if (response.Status != 200)
        {
            // The job proved complete, so a missing order list means the mosaic
            // pipeline didn't produce/copy order_list.json. Attach a ZIP
            // diagnostic so the failure says *why*.
            var failBody = await response.TextAsync();
            string diagnostic;
            try
            {
                diagnostic = await ArtifactDiagnostics.DescribeAsync(await Client.DownloadArtifactAsync(jobId));
            }
            catch (Exception ex)
            {
                diagnostic = $"Artifact download failed: {ex.GetType().Name}: {ex.Message}";
            }
            Assert.Fail($"Job {jobId} complete but order-list returned {response.Status}.\nBody: {failBody}\n{diagnostic}");
        }

        var orderList = Parse<OrderListResponse>(await response.TextAsync());

        orderList.JobId.Should().Be(jobId, "order-list response must echo the requested job_id");
        orderList.Items.Should().NotBeEmpty("a real mosaic produces at least one piece");
        orderList.ItemCount.Should().Be(orderList.Items.Count, "item_count must match items length");

        orderList.Items.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.ElementId),
            "every order item must carry a non-empty elementId");
        orderList.Items.Should().OnlyContain(i => i.Quantity > 0,
            "every order item must have a positive quantity");
        orderList.TotalPieces.Should().Be(orderList.Items.Sum(i => i.Quantity),
            "total_pieces must equal the sum of item quantities");
        orderList.TotalPieces.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task OptimizerPreview_CompletedJob_ReturnsValidAllocation()
    {
        var (jobId, _) = await SubmitAndAwaitCompletionAsync(blockWidth: 2);

        var response = await Client.GetOptimizerPreviewAsync(jobId);
        var body = await response.TextAsync();

        if (response.Status == 502)
        {
            using var errDoc = JsonDocument.Parse(body);
            errDoc.RootElement.TryGetProperty("detail", out _).Should()
                .BeTrue("502 must include a 'detail' identifying the listings fetch error");
            Assert.Ignore("marketplace listings fetch returned 502 (third-party outage) — allocation assertions skipped");
            return;
        }

        response.Status.Should().Be(200,
            $"optimizer preview with a completed job + valid body must return 200 (or 502 on outage). " +
            $"Got {response.Status}. Body: {body}");

        var preview = Parse<OptimizePreviewResponse>(body);

        preview.JobId.Should().Be(jobId);
        preview.TotalItems.Should().BeGreaterThan(0, "a real mosaic has pieces");
        preview.LaigoFeeCents.Should().BeGreaterThanOrEqualTo(TestConstants.LaigoFeeFloorCents,
            "LAIGO fee floor is $3.00");

        // Count fields must agree with their lists.
        preview.UnsourceableCount.Should().Be(preview.UnsourceableItems.Count,
            "unsourceable_count must match unsourceable_items length");
        preview.LegoFallbackItemCount.Should().Be(preview.LegoFallbackItems.Count,
            "lego_fallback_item_count must match lego_fallback_items length");

        // Total arithmetic (/optimize field names differ from /quote).
        preview.GrandTotalCents.Should().Be(preview.TotalPieceCostCents + preview.TotalShippingCents,
            "grand_total_cents must equal total_piece_cost_cents + total_shipping_cents");
        preview.CustomerTotalCents.Should().Be(preview.GrandTotalCents + preview.LaigoFeeCents,
            "customer_total_cents must equal grand_total_cents + laigo_fee_cents");
        if (preview.Sellers.Count > 0)
            preview.Sellers.Sum(s => s.SubtotalCents).Should().Be(preview.GrandTotalCents,
                "grand_total_cents must equal the sum of seller subtotals");

        preview.CanProceed.Should().Be(preview.UnsourceableCount == 0,
            "can_proceed must be true iff there are no unsourceable items");

        foreach (var seller in preview.Sellers)
        {
            seller.SubtotalCents.Should().Be(seller.PieceCostCents + seller.ShippingCostCents,
                $"seller {seller.SellerId} subtotal must equal pieces + shipping");

            // Free-shipping invariant on the preview's seller list too.
            if (seller.SellerId == TestConstants.LegoSellerId
                && seller.PieceCostCents >= TestConstants.LegoFreeShippingThresholdCents)
            {
                seller.ShippingCostCents.Should().Be(0,
                    $"lego_official with piece_cost {seller.PieceCostCents}¢ ≥ threshold must ship free");
            }
        }
    }
}
