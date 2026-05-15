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
        var response = await Client.GetHealthRawAsync();

        response.Status.Should().Be(200);

        var health = await Client.GetHealthAsync();
        health.Status.Should().Be("running");
    }

    [Test]
    public async Task Root_Returns200WithRunningStatus()
    {
        var response = await Client.GetRootRawAsync();

        response.Status.Should().Be(200);

        var root = await Client.GetRootAsync();
        root.Status.Should().Be("running");
        root.Message.Should().NotBeNullOrWhiteSpace();
    }
}
