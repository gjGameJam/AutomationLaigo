using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Pipeline;

/// <summary>
/// Per-IP /generate rate limit (Main.py:172-198): one allowed submission per
/// 20s; a second within the window gets 429 + Retry-After (1..21). Distinct
/// from the queue-full 429, which carries no Retry-After. This is the backend
/// behavior the whole suite paces around via GenerateRateLimitGate — pin it so
/// a limiter change is caught here, not as mystery 429s elsewhere.
/// </summary>
[TestFixture]
[Category("Pipeline")]
[Category("Generate")]
public class GenerateRateLimitTests : LaigOTestBase
{
    [Test]
    public async Task Generate_SecondSubmissionWithinWindow_Returns429WithRetryAfter()
    {
        // Own job = own cooldown burn (setup). The rejected probe below does
        // NOT extend the cooldown (the backend records allowed requests only),
        // so this costs one generate cycle and adds no delay to later tests.
        var first = await Client.GenerateAsync(TestImagePath, blockWidth: 2);
        try
        {
            // Deliberately ungated: we WANT to hit the limiter, with no retry.
            var probe = await Client.GenerateDetailedRawAsync(TestImagePath, blockWidth: 2, gated: false);

            probe.Status.Should().Be(429,
                $"a second /generate within {TestConstants.GenerateRateLimitSeconds}s must be rate-limited. Body: {probe.Body}");
            probe.RetryAfterSeconds.Should().NotBeNull(
                "the rate-limit 429 must carry Retry-After (the queue-full 429 does not)");
            probe.RetryAfterSeconds!.Value.Should().BeInRange(1, TestConstants.GenerateRateLimitSeconds + 1,
                "Retry-After is max(1, remaining+1) and cannot exceed window+1");

            using var doc = JsonDocument.Parse(probe.Body);
            doc.RootElement.TryGetProperty("detail", out var detail).Should()
                .BeTrue("the 429 must carry a 'detail'");
            detail.GetString().Should()
                .Contain("Rate limit", "the detail must identify the rejection as rate limiting")
                .And.Contain($"{TestConstants.GenerateRateLimitSeconds}s", "the detail must state the window");
        }
        finally
        {
            // Don't leave work on the shared instance.
            var final = await Client.WaitForJobAsync(first.JobId);
            final.Status.Should().Be("complete", $"job {first.JobId} should drain to complete");
        }
    }
}
