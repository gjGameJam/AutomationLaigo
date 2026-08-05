using FluentAssertions;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Pipeline;

/// <summary>
/// The single-worker dispatch contract: a second /generate while one job is in
/// flight must enter the queue, not run concurrently, and must be observable
/// via /jobs/{id} (queue_position/queue_length) and the /queue aggregate
/// counts. Closes the dynamic-queue gaps (Part B §2.2/§2.3/§3.4).
/// Only meaningful when the instance runs MAX_WORKERS=1 — with more workers a
/// second job dispatches immediately, so the test Ignores up front (before
/// paying any generate cycles). The two submissions are spaced ~20s apart by
/// the client's per-IP rate-limit gate — "back-to-back" now means "as close as
/// the limit allows". /queue no longer lists queued job ids (security), so
/// membership is asserted via the per-job view + aggregate counts.
/// </summary>
[TestFixture]
[Category("Pipeline")]
[Category("Queue")]
public class QueueConcurrencyTests : LaigOTestBase
{
    [Test]
    public async Task Queue_TwoConcurrentJobs_SecondIsQueuedBehindFirst()
    {
        // Check the worker count BEFORE submitting: with >1 worker the second
        // job runs immediately instead of queueing, making the queue-ordering
        // premise unreachable — skip without burning two generate cycles.
        var capacity = await Client.GetQueueAsync();
        if (capacity.MaxWorkers != 1)
        {
            Assert.Ignore(
                $"queue-ordering needs a single-worker instance; this deployment runs " +
                $"max_workers={capacity.MaxWorkers}, so a second job dispatches instead of queueing");
            return;
        }

        // The gate delays the second submit until the 20s rate-limit window
        // elapses. The premise (second queues behind first) holds as long as
        // the first job is still queued/running ~22s in — generations take
        // minutes, so it usually is. The second submit lives inside the try so
        // a submit failure still drains the first job.
        var first = await Client.GenerateAsync(TestImagePath, blockWidth: 2);
        GenerateResponse? second = null;

        try
        {
            second = await Client.GenerateAsync(TestImagePath, blockWidth: 2);

            var secondStatus = await Client.GetJobAsync(second.JobId);

            if (secondStatus.Status != "queued")
            {
                Assert.Ignore(
                    $"second job was already '{secondStatus.Status}' before it could be observed queued " +
                    "(the first job finished within the ~20s rate-limit spacing, or the worker drained " +
                    "faster than the poll) — queue-ordering assertion skipped");
                return;
            }

            // While queued, the per-job view must expose its place in line.
            secondStatus.QueuePosition.Should().NotBeNull("a queued job must report queue_position");
            secondStatus.QueueLength.Should().NotBeNull("a queued job must report queue_length");
            secondStatus.QueuePosition!.Value.Should().BeGreaterThan(0, "a waiting job is at position ≥ 1");
            secondStatus.QueuePosition!.Value.Should().BeLessThanOrEqualTo(secondStatus.QueueLength!.Value,
                "queue_position cannot exceed queue_length");
            secondStatus.Progress.Should().Be(0, "a queued (not yet running) job has 0 progress");

            // And /queue's aggregate counts must reflect it, never exceeding
            // capacity. (/queue stopped listing job ids — security — so the
            // count is the strongest membership signal it still offers.)
            var queue = await Client.GetQueueAsync();
            queue.QueuedJobs.Should().BeGreaterThanOrEqualTo(1,
                "a job waiting behind a running one must be counted in queued_jobs");
            queue.ActiveJobs.Should().BeLessThanOrEqualTo(queue.MaxWorkers,
                "active jobs must never exceed max_workers");
        }
        finally
        {
            // Drain both so we don't leave work on the shared instance, and
            // confirm the queue actually flushes through to completion.
            var firstFinal = await Client.WaitForJobAsync(first.JobId);
            firstFinal.Status.Should().Be("complete", $"first job {first.JobId} should drain to complete");
            if (second is not null)
            {
                var secondFinal = await Client.WaitForJobAsync(second.JobId);
                secondFinal.Status.Should().Be("complete", $"second job {second.JobId} should drain to complete");
            }
        }
    }
}
