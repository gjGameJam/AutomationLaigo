using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.ApiClient;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Contract;

/// <summary>
/// GET /jobs/{job_id}/checkout/{checkout_id}/status.
/// The live integration path for terminal saga states moves real money and is
/// out of scope for CI. What we CAN guard cheaply is model fidelity: every
/// SagaStatus value the backend can emit must deserialize, and the L5
/// provider-agnostic fields (payment_hold_id, customer_message,
/// manual_review_reason) must round-trip — this is exactly the class of break
/// the old stripe_payment_intent_id drift caused.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Checkout")]
[Ignore("Checkout & quote flow is shelved/disabled (2026-06) — re-enable these fixtures when the feature returns.")]
public class CheckoutStatusTests : LaigOTestBase
{
    // Every value of SagaStatus (models.py:85-106).
    private static readonly string[] AllSagaStatuses =
    [
        "initiated", "stripe_held", "orders_placed", "fallback_ordered",
        "payment_captured", "compensated", "failed", "manual_review",
    ];

    [Test]
    public async Task CheckoutStatus_InvalidIds_Returns404WithDetail()
    {
        var response = await Client.GetCheckoutStatusRawAsync(
            TestConstants.NonExistentJobId,
            "nonexistent-checkout-id");

        response.Status.Should().Be(404);

        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out _).Should().BeTrue();
    }

    [TestCaseSource(nameof(AllSagaStatuses))]
    public void Status_DeserializesEverySagaStatusValue(string sagaStatus)
    {
        // Pure unit test — no network. Canned JSON matching the backend's
        // CheckoutStatusResponse for a saga in this state.
        var json = $$"""
        {
          "checkout_id": "co_abc123",
          "saga_status": "{{sagaStatus}}",
          "brickowl_order_ids": ["bo_1", "bo_2"],
          "lego_order_id": "lego_9",
          "payment_hold_id": "pi_test_123",
          "payment_authorized_cents": 5250,
          "total_charged_cents": 5000,
          "error": "operator-only detail",
          "customer_message": "Your payment method was declined. Please use a different card.",
          "manual_review_reason": "capture exhausted retries with orders placed",
          "completed_at": "2026-06-03T12:00:00Z"
        }
        """;

        var status = JsonSerializer.Deserialize<CheckoutStatusResponse>(json, LaigOApiClient.JsonOptions)!;

        status.SagaStatus.Should().Be(sagaStatus, "saga_status must round-trip");
        status.CheckoutId.Should().Be("co_abc123");
        status.PaymentHoldId.Should().Be("pi_test_123",
            "payment_hold_id (renamed from stripe_payment_intent_id in L5) must deserialize");
        status.PaymentAuthorizedCents.Should().Be(5250,
            "payment_authorized_cents (added in L5) must deserialize");
        status.TotalChargedCents.Should().Be(5000);
        status.LegoOrderId.Should().Be("lego_9");
        status.CustomerMessage.Should().Be(
            "Your payment method was declined. Please use a different card.",
            "customer_message is the only string safe to surface to a user — it must deserialize verbatim");
        status.ManualReviewReason.Should().Be("capture exhausted retries with orders placed",
            "manual_review_reason must deserialize so the MANUAL_REVIEW branch is observable");
        status.CompletedAt.Should().Be("2026-06-03T12:00:00Z");
        status.BrickowlOrderIds.Should().ContainInOrder("bo_1", "bo_2")
            .And.HaveCount(2, "brickowl_order_ids must round-trip exactly");
    }

    [Test]
    public void Status_OmittedOptionalFields_DeserializeAsNull()
    {
        // A status poll arriving before Step 1 of the saga has the optional
        // provider fields absent — they must default to null, not throw.
        var json = """
        {
          "checkout_id": "co_early",
          "saga_status": "initiated",
          "brickowl_order_ids": []
        }
        """;

        var status = JsonSerializer.Deserialize<CheckoutStatusResponse>(json, LaigOApiClient.JsonOptions)!;

        status.SagaStatus.Should().Be("initiated");
        status.BrickowlOrderIds.Should().BeEmpty();
        status.PaymentHoldId.Should().BeNull();
        status.PaymentAuthorizedCents.Should().BeNull();
        status.TotalChargedCents.Should().BeNull();
        status.CustomerMessage.Should().BeNull("no error yet → no customer_message");
        status.ManualReviewReason.Should().BeNull();
        status.CompletedAt.Should().BeNull();
    }
}
