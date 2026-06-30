using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Contract;

/// <summary>
/// GET /checkout/gate — the Layer 2 operational visibility endpoint.
/// Always returns 200 regardless of gate state; clients read the body to
/// determine mode.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("CheckoutGate")]
[Category("Checkout")]
[Ignore("Checkout & quote flow is shelved/disabled (2026-06) — re-enable these fixtures when the feature returns.")]
public class CheckoutGateTests : LaigOTestBase
{
    private static readonly string[] ValidModes = ["disabled", "test", "live"];

    [Test]
    public async Task Gate_WhenPolled_Returns200Json()
    {
        var response = await Client.GetCheckoutGateRawAsync();

        response.Status.Should().Be(200,
            "the gate endpoint always returns 200 regardless of mode — it reports state in the body, not the HTTP status");
        response.Headers.TryGetValue("content-type", out var contentType);
        contentType.Should().Contain("application/json", "the gate snapshot is JSON");
    }

    [Test]
    public async Task Gate_Mode_IsRecognisedValue()
    {
        var gate = await Client.GetCheckoutGateAsync();

        gate.Mode.Should().BeOneOf(ValidModes,
            "mode must be one of 'disabled', 'test', or 'live'");
    }

    [Test]
    public async Task Gate_IsOpen_MatchesMode()
    {
        var gate = await Client.GetCheckoutGateAsync();

        var expectedOpen = gate.Mode is "test" or "live";
        gate.IsOpen.Should().Be(expectedOpen,
            "is_open must be true iff mode is 'test' or 'live'");
    }

    [Test]
    public async Task Gate_Response_HasNoStoreCacheControl()
    {
        var response = await Client.GetCheckoutGateRawAsync();

        response.Headers.TryGetValue("cache-control", out var cacheControl);
        cacheControl.Should().Be("no-store",
            "kill-switch state must propagate immediately — the gate must never be cached");
    }

    [Test]
    public async Task Gate_ReasonsAndMarketplaces_AreWellFormedLists()
    {
        var gate = await Client.GetCheckoutGateAsync();

        gate.Reasons.Should().NotBeNull("reasons must always be a list, even when empty");
        gate.Reasons.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r),
            "no reason entry may be blank");

        gate.MarketplacesLive.Should().NotBeNull("marketplaces_live must always be a list, even when empty");
        gate.MarketplacesLive.Should().OnlyHaveUniqueItems("marketplaces must not be listed twice");
        gate.MarketplacesLive.Should().BeInAscendingOrder(
            "the backend sorts marketplaces_live alphabetically for stable client-side equality");
    }

    [Test]
    public async Task Gate_WhenOpen_DeclaresPaymentProvider()
    {
        var gate = await Client.GetCheckoutGateAsync();

        if (gate.Mode is "test" or "live")
        {
            gate.PaymentProvider.Should().NotBeNullOrWhiteSpace(
                "an open gate (test/live) must declare which payment provider is active");
        }
        else
        {
            Assert.Ignore($"gate mode is '{gate.Mode}' — payment-provider assertion only applies when open");
        }
    }

    [Test]
    public async Task Gate_WhenDisabled_ListsReasons()
    {
        var gate = await Client.GetCheckoutGateAsync();

        if (gate.Mode == "disabled")
        {
            gate.Reasons.Should().NotBeEmpty(
                "disabled gate must list at least one reason so operators can diagnose");
            gate.IsOpen.Should().BeFalse("a disabled gate is never open");
        }
        else
        {
            Assert.Ignore($"gate mode is '{gate.Mode}' — disabled-reasons assertion only applies when disabled");
        }
    }

    [Test]
    public async Task Gate_Commit_IsShaLikeWhenPresent()
    {
        var gate = await Client.GetCheckoutGateAsync();

        // commit is optional but if present must be a hex sha (short or full)
        if (string.IsNullOrEmpty(gate.Commit))
        {
            Assert.Ignore("commit is unset on this instance (RENDER_GIT_COMMIT not present)");
            return;
        }

        gate.Commit.Length.Should().BeInRange(7, 40,
            "commit must be a git SHA fragment (7) or full SHA (40)");
        gate.Commit.Should().MatchRegex("^[a-f0-9]+$",
            "commit must be lowercase hex");
    }
}
