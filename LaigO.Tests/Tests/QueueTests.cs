using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Tests;

[TestFixture]
[Category("Queue")]
public class QueueTests : LaigOTestBase
{
    [Test]
    public async Task Queue_Returns200WithValidStructureAndConsistentCounts()
    {
        var queue = await Client.GetQueueAsync();

        queue.Should().NotBeNull();
        queue.MaxQueueSize.Should().BeGreaterThan(0);
        queue.MaxWorkers.Should().BeGreaterThan(0);
        queue.QueuedJobIds.Should().NotBeNull();
        queue.Counts.Should().NotBeNull();
        queue.Counts.Queued.Should().BeGreaterThanOrEqualTo(0);
        queue.Counts.Running.Should().BeGreaterThanOrEqualTo(0);

        // Invariants — these must hold or the queue state is internally inconsistent.
        queue.QueuedJobIds.Count.Should().Be(queue.Counts.Queued,
            "queued_job_ids list length must match counts.queued");
        queue.QueuedJobs.Should().Be(queue.Counts.Queued,
            "top-level queued_jobs must match counts.queued");
        queue.Counts.Queued.Should().BeLessThanOrEqualTo(queue.MaxQueueSize,
            "queued count cannot exceed max_queue_size");
        queue.ActiveJobs.Should().BeLessThanOrEqualTo(queue.MaxWorkers,
            "active jobs cannot exceed max_workers");
    }
}
