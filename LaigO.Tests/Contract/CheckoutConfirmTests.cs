using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.ApiClient;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Contract;

/// <summary>
/// POST /jobs/{job_id}/checkout/confirm — the CI-safe (pure-read, no money)
/// branches only. The happy path stages a real Stripe hold + marketplace
/// orders and is intentionally excluded from nightly CI.
///
/// Ordering matters: the gate dependency (Layer 3) fires BEFORE the
/// quote-lookup branches. So when the gate is closed every confirm returns 503,
/// and the 409/404/422 branches are only reachable with the gate open. Each
/// test reads /checkout/gate first and asserts the branch that the current gate
/// state can actually reach, ignoring otherwise.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Checkout")]
[Ignore("Checkout & quote flow is shelved/disabled (2026-06) — re-enable these fixtures when the feature returns.")]
public class CheckoutConfirmTests : LaigOTestBase
{
    private static readonly ConfirmRequest FakeConfirm =
        new("co_does_not_exist", "pm_fake_test_token");

    [Test]
    public async Task Confirm_GateClosed_Returns503WithCode()
    {
        var gate = await Client.GetCheckoutGateAsync();
        if (gate.IsOpen)
        {
            Assert.Ignore($"gate is open (mode='{gate.Mode}') — the gate-closed 503 branch is unreachable");
            return;
        }

        var response = await Client.ConfirmCheckoutRawAsync(TestConstants.NonExistentJobId, FakeConfirm);
        var body = await response.TextAsync();

        response.Status.Should().Be(503, $"a disabled gate must reject confirm. Body: {body}");

        var envelope = JsonSerializer.Deserialize<StructuredErrorEnvelope>(body, LaigOApiClient.JsonOptions)!;
        envelope.Detail.Should().NotBeNull("503 detail must be the structured {error, code, mode} object");
        envelope.Detail.Code.Should().Be("CHECKOUT_GATE_CLOSED",
            "the frontend matches exactly on this code to render the 'unavailable' UI");
        envelope.Detail.Mode.Should().Be(gate.Mode,
            "the 503 must echo the current gate mode that caused the rejection");
        envelope.Detail.Error.Should().NotBeNullOrWhiteSpace(
            "the 503 must include a customer-facing 'error' string");
        envelope.Detail.Error.Should().NotContain("CHECKOUT_GATE_CLOSED",
            "the customer-facing error must not leak the operator-facing code");
    }

    [Test]
    public async Task Confirm_QuoteNotFound_Returns409()
    {
        var gate = await Client.GetCheckoutGateAsync();
        if (!gate.IsOpen)
        {
            Assert.Ignore($"gate is closed (mode='{gate.Mode}') — confirm 503s before reaching the quote lookup");
            return;
        }

        // Gate open + a checkout_id that was never quoted → 409 "Quote expired or not found".
        var response = await Client.ConfirmCheckoutRawAsync(TestConstants.NonExistentJobId, FakeConfirm);
        var body = await response.TextAsync();

        response.Status.Should().Be(409, $"an unknown checkout_id must 409. Body: {body}");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out var detail).Should().BeTrue();
        detail.GetString().Should().Contain("Quote",
            "the 409 must explain that the quote was not found / expired");
    }
}
