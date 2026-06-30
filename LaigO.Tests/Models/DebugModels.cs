using System.Text.Json.Serialization;

namespace LaigO.Tests.Models;

// Typed records for the /checkout-debug/* responses. Previously every debug
// test hand-parsed responses with JsonDocument.TryGetProperty, which skipped
// the field-name/type drift protection the typed models give the other
// endpoints. These mirror the Pydantic models in debug_router.py / models.py.

public record SellerListingModel(
    [property: JsonPropertyName("seller_id")] string SellerId,
    [property: JsonPropertyName("seller_name")] string SellerName,
    [property: JsonPropertyName("price_per_cent")] int PricePerCent,
    [property: JsonPropertyName("available_qty")] int AvailableQty,
    [property: JsonPropertyName("shipping_cost_cents")] int ShippingCostCents,
    [property: JsonPropertyName("lot_id")] string LotId
);

// GET /checkout-debug/lego/element/{id}
public record LegoAvailabilityResponse(
    [property: JsonPropertyName("element_id")] string ElementId,
    [property: JsonPropertyName("available_on_lego")] bool AvailableOnLego
);

// POST /checkout-debug/lego/elements
public record LegoAvailabilityBatchResponse(
    [property: JsonPropertyName("available")] List<string> Available,
    [property: JsonPropertyName("unavailable")] List<string> Unavailable,
    [property: JsonPropertyName("results")] Dictionary<string, bool> Results
);

// GET /checkout-debug/lego/element/{id}/listing
public record LegoListingDebugResponse(
    [property: JsonPropertyName("element_id")] string ElementId,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("listing")] SellerListingModel? Listing
);

// GET /checkout-debug/brickowl/element/{id}
public record ElementListingsResponse(
    [property: JsonPropertyName("element_id")] string ElementId,
    [property: JsonPropertyName("listing_count")] int ListingCount,
    [property: JsonPropertyName("cheapest_price_cents")] int? CheapestPriceCents,
    [property: JsonPropertyName("most_stock")] int? MostStock,
    [property: JsonPropertyName("listings")] List<SellerListingModel> Listings
);

// POST /checkout-debug/brickowl/elements
public record BatchListingsResponse(
    [property: JsonPropertyName("requested")] int Requested,
    [property: JsonPropertyName("found")] int Found,
    [property: JsonPropertyName("not_found")] List<string> NotFound,
    [property: JsonPropertyName("results")] Dictionary<string, ElementListingsResponse> Results
);

// GET /checkout-debug/job/{id}/order-list
public record OrderListResponse(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("item_count")] int ItemCount,
    [property: JsonPropertyName("total_pieces")] int TotalPieces,
    [property: JsonPropertyName("items")] List<OrderItem> Items
);

// Order items use camelCase keys (elementId) — they come straight from the
// mosaic pipeline's order_list.json, not a Pydantic snake_case model.
public record OrderItem(
    [property: JsonPropertyName("elementId")] string ElementId,
    [property: JsonPropertyName("quantity")] int Quantity
);

// POST /checkout-debug/job/{id}/optimize
// NOTE: /optimize uses DIFFERENT field names than /quote for the same numbers.
// /optimize: grand_total_cents (pieces+shipping pre-fee), laigo_fee_cents, customer_total_cents.
// /quote:    total_cost_cents (pieces+shipping pre-fee), laigo_service_fee_cents, grand_total_cents.
public record OptimizePreviewResponse(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("sellers")] List<SellerAllocationResponse> Sellers,
    [property: JsonPropertyName("lego_fallback_items")] List<Dictionary<string, object>> LegoFallbackItems,
    [property: JsonPropertyName("lego_fallback_item_count")] int LegoFallbackItemCount,
    [property: JsonPropertyName("unsourceable_items")] List<Dictionary<string, object>> UnsourceableItems,
    [property: JsonPropertyName("unsourceable_count")] int UnsourceableCount,
    [property: JsonPropertyName("can_proceed")] bool CanProceed,
    [property: JsonPropertyName("total_piece_cost_cents")] int TotalPieceCostCents,
    [property: JsonPropertyName("total_shipping_cents")] int TotalShippingCents,
    [property: JsonPropertyName("grand_total_cents")] int GrandTotalCents,
    [property: JsonPropertyName("laigo_fee_cents")] int LaigoFeeCents,
    [property: JsonPropertyName("customer_total_cents")] int CustomerTotalCents
);

// Shared FastAPI structured-error detail: {detail: {error, code}} used by
// /jobs/{id}/preview (PREVIEW_NOT_AVAILABLE / PREVIEW_CORRUPTED) and
// /confirm gate-closed (CHECKOUT_GATE_CLOSED).
public record StructuredErrorEnvelope(
    [property: JsonPropertyName("detail")] StructuredErrorDetail Detail
);

public record StructuredErrorDetail(
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("mode")] string? Mode
);
