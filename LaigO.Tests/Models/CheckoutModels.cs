using System.Text.Json.Serialization;

namespace LaigO.Tests.Models;

public class QuoteRequest
{
    [JsonPropertyName("shipping_country")]
    public string ShippingCountry { get; init; } = null!;

    [JsonPropertyName("shipping_zip")]
    public string ShippingZip { get; init; } = null!;

    [JsonPropertyName("customer_email")]
    public string CustomerEmail { get; init; } = null!;
}

public class ConfirmRequest
{
    [JsonPropertyName("checkout_id")]
    public string CheckoutId { get; init; } = null!;

    [JsonPropertyName("stripe_payment_method_id")]
    public string StripePaymentMethodId { get; init; } = null!;
}

public class QuoteResponse
{
    [JsonPropertyName("checkout_id")]
    public string CheckoutId { get; init; } = null!;

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; init; }

    [JsonPropertyName("pieces_total")]
    public int PiecesTotal { get; init; }

    [JsonPropertyName("sellers")]
    public List<SellerAllocationResponse> Sellers { get; init; } = [];

    [JsonPropertyName("lego_fallback_items")]
    public List<Dictionary<string, object>> LegoFallbackItems { get; init; } = [];

    [JsonPropertyName("lego_fallback_cost_cents")]
    public int LegoFallbackCostCents { get; init; }

    [JsonPropertyName("unsourceable_items")]
    public List<Dictionary<string, object>> UnsourceableItems { get; init; } = [];

    [JsonPropertyName("can_proceed")]
    public bool CanProceed { get; init; }

    [JsonPropertyName("total_cost_cents")]
    public int TotalCostCents { get; init; }

    [JsonPropertyName("laigo_service_fee_cents")]
    public int LaigoServiceFeeCents { get; init; }

    [JsonPropertyName("grand_total_cents")]
    public int GrandTotalCents { get; init; }
}

public class SellerAllocationResponse
{
    [JsonPropertyName("seller_id")]
    public string SellerId { get; init; } = null!;

    [JsonPropertyName("seller_name")]
    public string SellerName { get; init; } = null!;

    [JsonPropertyName("pieces_count")]
    public int PiecesCount { get; init; }

    [JsonPropertyName("piece_cost_cents")]
    public int PieceCostCents { get; init; }

    [JsonPropertyName("shipping_cost_cents")]
    public int ShippingCostCents { get; init; }

    [JsonPropertyName("subtotal_cents")]
    public int SubtotalCents { get; init; }
}

public class ConfirmResponse
{
    [JsonPropertyName("checkout_id")]
    public string CheckoutId { get; init; } = null!;

    [JsonPropertyName("saga_status")]
    public string SagaStatus { get; init; } = null!;

    [JsonPropertyName("poll_url")]
    public string PollUrl { get; init; } = null!;
}

public class CheckoutStatusResponse
{
    [JsonPropertyName("checkout_id")]
    public string CheckoutId { get; init; } = null!;

    [JsonPropertyName("saga_status")]
    public string SagaStatus { get; init; } = null!;

    [JsonPropertyName("brickowl_order_ids")]
    public List<string> BrickowlOrderIds { get; init; } = [];

    [JsonPropertyName("lego_order_id")]
    public string? LegoOrderId { get; init; }

    [JsonPropertyName("stripe_payment_intent_id")]
    public string? StripePaymentIntentId { get; init; }

    [JsonPropertyName("total_charged_cents")]
    public int? TotalChargedCents { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("completed_at")]
    public string? CompletedAt { get; init; }
}
