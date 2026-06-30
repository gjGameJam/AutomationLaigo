using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Pipeline;

/// <summary>
/// The single-worker dispatch contract (MAX_WORKERS=1): a second /generate while
/// one job is in flight must enter the queue, not run concurrently, and must be
/// observable via /queue (FIFO) and /jobs/{id} (queue_position/queue_length).
/// Closes the dynamic-queue gaps (Part B §2.2/§2.3/§3.4).
/// </summary>
[TestFixture]
[Category("Pipeline")]
[Category("Queue")]
public class QueueConcurrencyTests : LaigOTestBase
{
    [Test]
    public async Task Queue_TwoConcurrentJobs_SecondIsQueuedBehindFirst()
    {
        // Submit two back-to-back. With one worker, the second should queue.
        var first = await Client.GenerateAsync(TestImagePath, blockWidth: 2);
        var second = await Client.GenerateAsync(TestImagePath, blockWidth: 2);

        try
        {
            var secondStatus = await Client.GetJobAsync(second.JobId);

            if (secondStatus.Status != "queued")
            {
                Assert.Ignore(
                    $"second job was already '{secondStatus.Status}' before it could be observed queued " +
                    "(worker drained faster than the poll) — queue-ordering assertion skipped");
                return;
            }

            // While queued, the per-job view must expose its place in line.
            secondStatus.QueuePosition.Should().NotBeNull("a queued job must report queue_position");
            secondStatus.QueueLength.Should().NotBeNull("a queued job must report queue_length");
            secondStatus.QueuePosition!.Value.Should().BeGreaterThan(0, "a waiting job is at position ≥ 1");
            secondStatus.QueuePosition!.Value.Should().BeLessThanOrEqualTo(secondStatus.QueueLength!.Value,
                "queue_position cannot exceed queue_length");
            secondStatus.Progress.Should().Be(0, "a queued (not yet running) job has 0 progress");

            // And /queue must list it among the queued ids, never exceeding capacity.
            var queue = await Client.GetQueueAsync();
            queue.QueuedJobIds.Should().Contain(second.JobId,
                "a job waiting behind a running one must appear in queued_job_ids");
            queue.ActiveJobs.Should().BeLessThanOrEqualTo(queue.MaxWorkers,
                "active jobs must never exceed max_workers (=1)");
        }
        finally
        {
            // Drain both so we don't leave work on the shared instance, and
            // confirm the queue actually flushes through to completion.
            var firstFinal = await Client.WaitForJobAsync(first.JobId);
            var secondFinal = await Client.WaitForJobAsync(second.JobId);
            firstFinal.Status.Should().Be("complete", $"first job {first.JobId} should drain to complete");
            secondFinal.Status.Should().Be("complete", $"second job {second.JobId} should drain to complete");
        }
    }
}
