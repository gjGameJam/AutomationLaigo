using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Tests;

/// <summary>
/// Tests for GET /checkout/gate — the Layer 2 operational visibility endpoint.
/// Always returns 200 regardless of gate state; clients read the body to determine mode.
/// </summary>
[TestFixture]
[Category("CheckoutGate")]
public class CheckoutGateTests : LaigOTestBase
{
    private static readonly string[] ValidModes = ["disabled", "test", "live"];

    [Test]
    public async Task CheckoutGate_AlwaysReturns200()
    {
        var response = await Client.GetCheckoutGateRawAsync();

        response.Status.Should().Be(200,
            "the gate endpoint always returns 200 regardless of mode — it reports state in the body, not the HTTP status");
    }

    [Test]
    public async Task CheckoutGate_ModeIsRecognisedValue()
    {
        var gate = await Client.GetCheckoutGateAsync();

        gate.Mode.Should().BeOneOf(ValidModes,
            "mode must be one of 'disabled', 'test', or 'live'");
    }

    [Test]
    public async Task CheckoutGate_IsOpenMatchesMode()
    {
        var gate = await Client.GetCheckoutGateAsync();

        var expectedOpen = gate.Mode is "test" or "live";
        gate.IsOpen.Should().Be(expectedOpen,
            "is_open must be true iff mode is 'test' or 'live'");
    }

    [Test]
    public async Task CheckoutGate_HasNoStoreCacheControl()
    {
        var response = await Client.GetCheckoutGateRawAsync();

        response.Headers.TryGetValue("cache-control", out var cacheControl);
        cacheControl.Should().Be("no-store",
            "kill-switch state must propagate immediately — the gate must never be cached");
    }

    [Test]
    public async Task CheckoutGate_ReasonsAndMarketplacesAreLists()
    {
        var gate = await Client.GetCheckoutGateAsync();

        gate.Reasons.Should().NotBeNull("reasons must always be a list, even when empty");
        gate.MarketplacesLive.Should().NotBeNull("marketplaces_live must always be a list, even when empty");
    }

    [Test]
    public async Task CheckoutGate_PaymentProviderConsistentWithMode()
    {
        var gate = await Client.GetCheckoutGateAsync();

        if (gate.Mode is "test" or "live")
        {
            gate.PaymentProvider.Should().NotBeNullOrWhiteSpace(
                "an open gate (test/live) must declare which payment provider is active");
        }
    }

    [Test]
    public async Task CheckoutGate_DisabledGateExplainsWhy()
    {
        var gate = await Client.GetCheckoutGateAsync();

        if (gate.Mode == "disabled")
        {
            gate.Reasons.Should().NotBeEmpty(
                "disabled gate must list at least one reason so operators can diagnose");
        }
    }

    [Test]
    public async Task CheckoutGate_CommitIsShaLike()
    {
        var gate = await Client.GetCheckoutGateAsync();

        // commit is optional but if present must be a hex sha (short or full)
        if (!string.IsNullOrEmpty(gate.Commit))
        {
            gate.Commit.Length.Should().BeInRange(7, 40,
                "commit must be a git SHA fragment (7) or full SHA (40)");
            gate.Commit.Should().MatchRegex("^[a-f0-9]+$",
                "commit must be lowercase hex");
        }
    }
}
