using FluentAssertions;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Tests;

/// <summary>
/// Tests for the checkout quote flow.
/// Confirm/payment tests are deliberately excluded from nightly CI to avoid
/// placing real Stripe charges or LEGO.com orders.
/// </summary>
[TestFixture]
[Category("Checkout")]
public class CheckoutTests : LaigOTestBase
{
    [Test]
    public async Task Quote_InvalidJobId_Returns404()
    {
        var request = new QuoteRequest { ShippingCountry = "US", ShippingZip = "10001", CustomerEmail = "test@example.com" };
        var response = await Client.GetQuoteRawAsync("00000000-0000-0000-0000-000000000000", request);

        response.Status.Should().Be(404);
    }

    [Test]
    public async Task Quote_WithCompletedJob_ReturnsStructuredResponse()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2);
        var finished = await Client.WaitForJobAsync(submitted.JobId);

        Assume.That(finished.Status, Is.EqualTo("complete"),
            "Skipping checkout test: generate job failed");

        var request = new QuoteRequest { ShippingCountry = "US", ShippingZip = "10001", CustomerEmail = "test@example.com" };
        var response = await Client.GetQuoteRawAsync(submitted.JobId, request);

        // 200 = sourced; 422 = pieces unsourceable; 503 = sourcing APIs unavailable
        response.Status.Should().NotBe(500,
            "quote endpoint must return a structured response, not an unhandled server error");

        if (response.Status == 200)
        {
            var quote = await Client.GetQuoteAsync(submitted.JobId, request);
            quote.CheckoutId.Should().NotBeNullOrWhiteSpace();
            quote.PiecesTotal.Should().BeGreaterThan(0);
            quote.ExpiresAt.Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            quote.GrandTotalCents.Should().BeGreaterThanOrEqualTo(0);
            quote.LaigoServiceFeeCents.Should().BeGreaterThanOrEqualTo(300,
                "LAIGO fee minimum is $3.00 (300 cents)");
        }
    }

    [Test]
    public async Task CheckoutStatus_InvalidIds_Returns404()
    {
        var response = await Client.GetCheckoutStatusRawAsync(
            "00000000-0000-0000-0000-000000000000",
            "nonexistent-checkout-id");

        response.Status.Should().Be(404);
    }
}
