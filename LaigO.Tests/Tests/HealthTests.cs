using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Tests;

[TestFixture]
[Category("Health")]
public class HealthTests : LaigOTestBase
{
    [Test]
    public async Task Health_Returns200WithRunningStatus()
    {
        // GetHealthAsync throws via ParseAsync on non-200, so a separate raw
        // fetch + status assertion would double the network round-trips.
        var health = await Client.GetHealthAsync();
        health.Status.Should().Be("running");
    }

    [Test]
    public async Task Root_Returns200WithRunningStatusAndMessage()
    {
        var root = await Client.GetRootAsync();
        root.Status.Should().Be("running");
        root.Message.Should().NotBeNullOrWhiteSpace(
            "root endpoint must include a human-readable message");
    }
}
