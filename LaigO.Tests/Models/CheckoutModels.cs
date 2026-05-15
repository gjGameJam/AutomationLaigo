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

public record CheckoutStatusResponse(
    [property: JsonPropertyName("checkout_id")] string CheckoutId,
    [property: JsonPropertyName("saga_status")] string SagaStatus,
    [property: JsonPropertyName("brickowl_order_ids")] List<string> BrickowlOrderIds,
    [property: JsonPropertyName("lego_order_id")] string? LegoOrderId,
    [property: JsonPropertyName("stripe_payment_intent_id")] string? StripePaymentIntentId,
    [property: JsonPropertyName("total_charged_cents")] int? TotalChargedCents,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("completed_at")] string? CompletedAt
);
