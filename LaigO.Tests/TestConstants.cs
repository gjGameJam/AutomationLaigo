namespace LaigO.Tests;

/// <summary>
/// Shared, well-known values used across the suite. Centralized so a single
/// edit updates every test (e.g. swapping the canary element if 302421 is ever
/// retired) and so the intent of each magic value is documented in one place.
/// </summary>
public static class TestConstants
{
    /// <summary>A job_id guaranteed never to exist — drives every 404 path.</summary>
    public const string NonExistentJobId = "00000000-0000-0000-0000-000000000000";

    /// <summary>1x1 round plate — a common LEGO element present in most palettes.</summary>
    public const string KnownLegoElementId = "4073";

    /// <summary>
    /// Price-parser canary. A high-volume Pick-a-Brick element used to verify
    /// the LEGO sourcing chain end to end (_search → _parse_available →
    /// _parse_price_cents). available+price==0 is the price-drift signal;
    /// available==false for this known-stocked piece is a sourcing outage.
    /// </summary>
    public const string PricedLegoElementId = "302421";

    // Backend constants mirrored from the FastAPI source for boundary tests.
    public const int MinBlockWidth = 1;   // picToMosiac.MIN_BLOCK_WIDTH
    public const int MaxBlockWidth = 40;  // picToMosiac.MAX_BLOCK_WIDTH (env default)

    /// <summary>LEGO.com seller id (lego_client.SELLER_ID).</summary>
    public const string LegoSellerId = "lego_official";

    /// <summary>Free-shipping threshold in cents (LEGO_FREE_SHIPPING_THRESHOLD_CENTS).</summary>
    public const int LegoFreeShippingThresholdCents = 3500;

    /// <summary>LAIGO service-fee floor in cents (max($3.00, 5% of total)).</summary>
    public const int LaigoFeeFloorCents = 300;
}
