using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Tests;

[TestFixture]
[Category("Queue")]
public class QueueTests : LaigOTestBase
{
    [Test]
    public async Task Queue_Returns200WithValidStructure()
    {
        var response = await Client.GetQueueRawAsync();

        response.Status.Should().Be(200);

        var queue = await Client.GetQueueAsync();
        queue.Should().NotBeNull();
        queue.MaxQueueSize.Should().BeGreaterThan(0);
        queue.MaxWorkers.Should().BeGreaterThan(0);
        queue.QueuedJobIds.Should().NotBeNull();
        queue.Counts.Should().NotBeNull();
        queue.Counts.Queued.Should().BeGreaterThanOrEqualTo(0);
        queue.Counts.Running.Should().BeGreaterThanOrEqualTo(0);
        queue.Counts.Complete.Should().BeGreaterThanOrEqualTo(0);
        queue.Counts.Failed.Should().BeGreaterThanOrEqualTo(0);
    }
}
