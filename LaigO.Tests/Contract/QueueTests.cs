using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Contract;

/// <summary>
/// GET /queue — static structure + internal-consistency invariants.
/// The dynamic FIFO/ordering contract is exercised in
/// Pipeline.QueueConcurrencyTests (it needs two in-flight jobs).
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Queue")]
public class QueueTests : LaigOTestBase
{
    [Test]
    public async Task Queue_WhenPolled_HasConsistentStructureAndCounts()
    {
        var queue = await Client.GetQueueAsync();

        queue.Should().NotBeNull();

        // Capacity constants are fixed in Main.py (MAX_WORKERS=1, MAX_QUEUE_SIZE=20).
        // Pinning the exact values catches an accidental config change.
        queue.MaxWorkers.Should().Be(2, "the backend runs 2 workers");
        queue.MaxQueueSize.Should().Be(20, "MAX_QUEUE_SIZE is 20");

        queue.QueuedJobIds.Should().NotBeNull();
        queue.QueuedJobIds.Should().OnlyHaveUniqueItems("the same job must not appear twice in the queue");
        queue.Counts.Should().NotBeNull();
        queue.Counts.Queued.Should().BeGreaterThanOrEqualTo(0);
        queue.Counts.Running.Should().BeGreaterThanOrEqualTo(0);
        queue.ActiveJobs.Should().BeGreaterThanOrEqualTo(0);

        // Invariants — these must hold or the queue state is internally inconsistent.
        queue.QueuedJobIds.Count.Should().Be(queue.Counts.Queued,
            "queued_job_ids list length must match counts.queued");
        queue.QueuedJobs.Should().Be(queue.Counts.Queued,
            "top-level queued_jobs must match counts.queued");
        queue.Counts.Queued.Should().BeLessThanOrEqualTo(queue.MaxQueueSize,
            "queued count cannot exceed max_queue_size");
        queue.Counts.Running.Should().BeLessThanOrEqualTo(queue.MaxWorkers,
            "running count cannot exceed max_workers");
        queue.ActiveJobs.Should().BeLessThanOrEqualTo(queue.MaxWorkers,
            "active jobs cannot exceed max_workers");
    }
}
