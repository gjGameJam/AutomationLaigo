using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.ApiClient;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Contract;

/// <summary>
/// GET /health and GET / — liveness endpoints. Fast, stateless, read-only.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Health")]
public class HealthTests : LaigOTestBase
{
    [Test]
    public async Task Health_WhenUp_ReportsRunningStatus()
    {
        // Single round-trip: assert status code + content-type + body together
        // rather than a raw call followed by a typed call (which would double the
        // network hit and observe two different server snapshots).
        var raw = await Client.GetHealthRawAsync();

        raw.Status.Should().Be(200, "a healthy instance must return 200");
        raw.Headers.TryGetValue("content-type", out var contentType);
        contentType.Should().Contain("application/json", "health must be served as JSON");

        var body = await raw.TextAsync();
        var health = JsonSerializer.Deserialize<HealthResponse>(body, LaigOApiClient.JsonOptions)!;
        health.Status.Should().Be("running");
    }

    [Test]
    public async Task Root_WhenUp_ReportsRunningStatusAndMessage()
    {
        var raw = await Client.GetRootRawAsync();

        raw.Status.Should().Be(200);

        var body = await raw.TextAsync();
        var root = JsonSerializer.Deserialize<HealthResponse>(body, LaigOApiClient.JsonOptions)!;
        root.Status.Should().Be("running");
        root.Message.Should().NotBeNullOrWhiteSpace(
            "root endpoint must include a human-readable message");
        root.Message.Should().Contain("LAIGO",
            "the root message identifies the service");
    }
}
