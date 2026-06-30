using System.Text.Json.Serialization;

namespace LaigO.Tests.Models;

public record QuoteRequest(
    [property: JsonPropertyName("shipping_country")] string ShippingCountry,
    [property: JsonPropertyName("shipping_zip")] string ShippingZip,
    [property: JsonPropertyName("customer_email")] string CustomerEmail
);

public record ConfirmRequest(
    [property: JsonPropertyName("checkout_id")] string CheckoutId,
    [property: JsonPropertyName("stripe_payment_method_id")] string StripePaymentMethodId
);

public record QuoteResponse(
    [property: JsonPropertyName("checkout_id")] string CheckoutId,
    [property: JsonPropertyName("expires_at")] long ExpiresAt,
    [property: JsonPropertyName("pieces_total")] int PiecesTotal,
    [property: JsonPropertyName("sellers")] List<SellerAllocationResponse> Sellers,
    [property: JsonPropertyName("lego_fallback_items")] List<Dictionary<string, object>> LegoFallbackItems,
    [property: JsonPropertyName("lego_fallback_cost_cents")] int LegoFallbackCostCents,
    [property: JsonPropertyName("unsourceable_items")] List<Dictionary<string, object>> UnsourceableItems,
    [property: JsonPropertyName("can_proceed")] bool CanProceed,
    [property: JsonPropertyName("total_cost_cents")] int TotalCostCents,
    [property: JsonPropertyName("laigo_service_fee_cents")] int LaigoServiceFeeCents,
    [property: JsonPropertyName("grand_total_cents")] int GrandTotalCents
);

public record SellerAllocationResponse(
    [property: JsonPropertyName("seller_id")] string SellerId,
    [property: JsonPropertyName("seller_name")] string SellerName,
    [property: JsonPropertyName("pieces_count")] int PiecesCount,
    [property: JsonPropertyName("piece_cost_cents")] int PieceCostCents,
    [property: JsonPropertyName("shipping_cost_cents")] int ShippingCostCents,
    [property: JsonPropertyName("subtotal_cents")] int SubtotalCents
);

public record ConfirmResponse(
    [property: JsonPropertyName("checkout_id")] string CheckoutId,
    [property: JsonPropertyName("saga_status")] string SagaStatus,
    [property: JsonPropertyName("poll_url")] string PollUrl
);

// P0 fix (2026-06-03): aligned with the L5 provider-agnostic rename in the
// backend (models.py:170-196). Previously this record declared
// `stripe_payment_intent_id` (which the backend no longer emits → always null)
// and was missing `payment_authorized_cents`, `customer_message`, and
// `manual_review_reason`. `/status` is the saga's only observable contract, so
// the drift silently nulled the customer-facing message and the MANUAL_REVIEW
// branch. SagaStatus is kept as a string here — the round-trip of every enum
// value is asserted in CheckoutStatusTests.Status_DeserializesAllSagaStatusValues.
public record CheckoutStatusResponse(
    [property: JsonPropertyName("checkout_id")] string CheckoutId,
    [property: JsonPropertyName("saga_status")] string SagaStatus,
    [property: JsonPropertyName("brickowl_order_ids")] List<string> BrickowlOrderIds,
    [property: JsonPropertyName("lego_order_id")] string? LegoOrderId,
    // Renamed from stripe_payment_intent_id (L5). pi_... for Stripe; an opaque
    // hold id for future providers.
    [property: JsonPropertyName("payment_hold_id")] string? PaymentHoldId,
    // Amount authorized at hold time; may exceed total_charged_cents (1.05x buffer).
    [property: JsonPropertyName("payment_authorized_cents")] int? PaymentAuthorizedCents,
    [property: JsonPropertyName("total_charged_cents")] int? TotalChargedCents,
    // Operator-facing raw error text. Frontend MUST NOT render verbatim.
    [property: JsonPropertyName("error")] string? Error,
    // Customer-facing translated message (B12/H1) — the only string safe to
    // surface to a user. One of ERROR_MESSAGES, or null when no error occurred.
    [property: JsonPropertyName("customer_message")] string? CustomerMessage,
    // Populated when saga_status == manual_review; operator runbook text.
    [property: JsonPropertyName("manual_review_reason")] string? ManualReviewReason,
    [property: JsonPropertyName("completed_at")] string? CompletedAt
);

public record CheckoutGateResponse(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("is_open")] bool IsOpen,
    [property: JsonPropertyName("payment_provider")] string? PaymentProvider,
    [property: JsonPropertyName("marketplaces_live")] List<string> MarketplacesLive,
    [property: JsonPropertyName("reasons")] List<string> Reasons,
    [property: JsonPropertyName("commit")] string? Commit
);
