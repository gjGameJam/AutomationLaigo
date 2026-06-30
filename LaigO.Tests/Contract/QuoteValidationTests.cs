using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Contract;

/// <summary>
/// POST /jobs/{job_id}/checkout/quote — request validation, no completed job
/// required. Pydantic body validation (422) runs before the route handler, so
/// a malformed body returns 422 even against a non-existent job. A *valid* body
/// against a non-existent job reaches the handler and 404s on the missing order
/// list. The priced-quote happy path lives in Pipeline.QuoteTests.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Checkout")]
[Ignore("Checkout & quote flow is shelved/disabled (2026-06) — re-enable these fixtures when the feature returns.")]
public class QuoteValidationTests : LaigOTestBase
{
    /// <summary>Assert a FastAPI/Pydantic 422 that names <paramref name="field"/>.</summary>
    private static async Task AssertPydantic422NamingAsync(Microsoft.Playwright.IAPIResponse response, string field)
    {
        response.Status.Should().Be(422);
        var body = await response.TextAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out var detail).Should()
            .BeTrue("a Pydantic 422 must carry a 'detail' list");
        detail.ValueKind.Should().Be(JsonValueKind.Array);
        body.Should().Contain(field, $"the validation error must identify '{field}'");
    }

    [Test]
    public async Task Quote_NonExistentJob_Returns404WithDetail()
    {
        var request = new QuoteRequest("US", "10001", "test@example.com");
        var response = await Client.GetQuoteRawAsync(TestConstants.NonExistentJobId, request);

        response.Status.Should().Be(404);

        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace("404 must include a detail body");
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out _).Should()
            .BeTrue("FastAPI 404 must include a 'detail' field");
    }

    [TestCase("USA", TestName = "Quote_ShippingCountryTooLong_Returns422")]
    [TestCase("U", TestName = "Quote_ShippingCountryTooShort_Returns422")]
    public async Task Quote_InvalidShippingCountry_Returns422(string country)
    {
        var request = new QuoteRequest(country, "10001", "test@example.com");
        var response = await Client.GetQuoteRawAsync(TestConstants.NonExistentJobId, request);

        await AssertPydantic422NamingAsync(response, "shipping_country");
    }

    [Test]
    public async Task Quote_ShippingZipTooLong_Returns422()
    {
        var request = new QuoteRequest("US", new string('9', 21), "test@example.com");
        var response = await Client.GetQuoteRawAsync(TestConstants.NonExistentJobId, request);

        await AssertPydantic422NamingAsync(response, "shipping_zip");
    }

    [Test]
    public async Task Quote_MissingCustomerEmail_Returns422()
    {
        // customer_email is a required field — omit it entirely.
        var response = await Client.PostJsonRawAsync(
            $"/jobs/{TestConstants.NonExistentJobId}/checkout/quote",
            new { shipping_country = "US", shipping_zip = "10001" });

        await AssertPydantic422NamingAsync(response, "customer_email");
    }
}
