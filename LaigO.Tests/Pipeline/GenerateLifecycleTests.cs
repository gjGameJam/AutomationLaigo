using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Pipeline;

/// <summary>
/// End-to-end pipeline: POST /generate → poll /jobs/{id} → download / preview.
/// Each test runs its own real generation (atomic — no shared job state). Pure
/// 404 paths for these endpoints live in Contract (no job created).
/// </summary>
[TestFixture]
[Category("Pipeline")]
[Category("Generate")]
public class GenerateLifecycleTests : LaigOTestBase
{
    [Test]
    public async Task Generate_ValidImage_CompletesWithEchoedSettings()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d", bgPct: 100, toFrame: false);

        submitted.JobId.Should().NotBeNullOrWhiteSpace();
        submitted.Status.Should().Be("queued");

        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("complete", $"job {submitted.JobId} should complete. Error: {finished.Error}");
        finished.Progress.Should().Be(100, "the backend forces progress to exactly 100 on completion");
        finished.Error.Should().BeNull("a completed job has no error");

        // Timestamp ordering: created_at ≤ started_at ≤ finished_at.
        finished.CreatedAt.Should().NotBeNull();
        finished.StartedAt.Should().NotBeNull("a completed job must have run, so started_at must be present");
        finished.FinishedAt.Should().NotBeNull("complete jobs must have finished_at");
        finished.StartedAt.Should().BeGreaterThanOrEqualTo(finished.CreatedAt!.Value,
            "started_at cannot precede created_at");
        finished.FinishedAt.Should().BeGreaterThanOrEqualTo(finished.StartedAt!.Value,
            "finished_at cannot precede started_at");

        // Settings must round-trip — the server should echo what we submitted.
        finished.Settings.Should().NotBeNull();
        finished.Settings!.MosaicBlockWidth.Should().Be(2);
        finished.Settings.MosaicType.Should().Be("2d");
        finished.Settings.BackgroundColorPercent.Should().BeApproximately(100, 0.01);
        finished.Settings.ToFrame.Should().BeFalse();
    }

    [Test]
    public async Task JobStatus_DuringGeneration_HasValidShapeAndEchoesSettings()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d");

        var status = await Client.GetJobAsync(submitted.JobId);

        status.Status.Should().BeOneOf("queued", "running", "complete", "failed");
        status.Progress.Should().BeInRange(0, 100);
        status.CreatedAt.Should().NotBeNull("every job must have a created_at timestamp");
        status.QueuedAt.Should().NotBeNull("every job must have a queued_at timestamp");
        status.CreatedAt.Should().Be(status.QueuedAt, "created_at and queued_at are the same instant");

        status.Settings.Should().NotBeNull();
        status.Settings!.MosaicBlockWidth.Should().Be(2);
        status.Settings.MosaicType.Should().Be("2d");
        status.Settings.BackgroundColorPercent.Should().BeApproximately(100, 0.01);

        if (status.Status == "failed")
            status.Error.Should().NotBeNullOrWhiteSpace("failed jobs must carry an error message");

        await Client.WaitForJobAsync(submitted.JobId); // drain so we don't clog the queue
    }

    [Test]
    public async Task JobStatus_DuringGeneration_IsNotCacheable()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2);

        // §3.5 — the frontend polls this every 2-5s; a proxy caching it would
        // freeze status. The endpoint must not advertise itself as cacheable.
        var raw = await Client.GetJobRawAsync(submitted.JobId);
        raw.Status.Should().Be(200);
        if (raw.Headers.TryGetValue("cache-control", out var cacheControl))
            cacheControl.Should().Contain("no-store", "a polled status endpoint must not be cacheable");

        await Client.WaitForJobAsync(submitted.JobId); // drain
    }

    [Test]
    public async Task Generate_3DMosaic_Completes()
    {
        // The only other MosaicType enum value — previously never exercised.
        var (jobId, finished) = await SubmitAndAwaitCompletionAsync(blockWidth: 2, mosaicType: "3d");

        finished.Progress.Should().Be(100, $"3d job {jobId} should reach 100%");
        finished.Error.Should().BeNull("a completed 3d job has no error");
        finished.FinishedAt.Should().NotBeNull();
        finished.Settings.Should().NotBeNull();
        finished.Settings!.MosaicType.Should().Be("3d", "mosaic_type=3d must round-trip");
        finished.Settings.MosaicBlockWidth.Should().Be(2, "block width must round-trip");
    }

    [Test]
    public async Task Generate_WithToFrame_CompletesAndEchoesSetting()
    {
        var (jobId, finished) = await SubmitAndAwaitCompletionAsync(blockWidth: 2, mosaicType: "2d", toFrame: true);

        finished.Progress.Should().Be(100, $"to_frame job {jobId} should reach 100%");
        finished.Error.Should().BeNull();
        finished.Settings.Should().NotBeNull();
        finished.Settings!.ToFrame.Should().BeTrue("to_frame=true must round-trip through the settings echo");
        finished.Settings.MosaicType.Should().Be("2d");
    }

    [Test]
    public async Task Generate_CompletedJob_ArtifactIsValidMosaicZip()
    {
        var (jobId, _) = await SubmitAndAwaitCompletionAsync(blockWidth: 2, mosaicType: "2d");

        var downloadResponse = await Client.DownloadArtifactRawAsync(jobId);
        downloadResponse.Status.Should().Be(200);

        // Download must be served as a named ZIP attachment.
        downloadResponse.Headers.TryGetValue("content-type", out var contentType);
        contentType.Should().Contain("zip", "the artifact must be served with a zip content-type");
        downloadResponse.Headers.TryGetValue("content-disposition", out var disposition);
        disposition.Should().Contain("mosaic_",
            "the download must carry a Content-Disposition filename of mosaic_{job}.zip");

        var bytes = await downloadResponse.BodyAsync();
        bytes.Should().NotBeEmpty();
        bytes.Length.Should().BeGreaterThan(100,
            "a real mosaic ZIP is at least a few KB; a tiny response is a stub or error");

        // ZIP magic bytes: PK\x03\x04
        bytes[0].Should().Be(0x50);
        bytes[1].Should().Be(0x4B);

        using var zipStream = new MemoryStream(bytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var entryNames = archive.Entries.Select(e => e.FullName).ToHashSet();

        entryNames.Should().Contain("manifest.json",
            $"artifact must contain manifest.json. Entries: [{string.Join(", ", entryNames)}]");
        entryNames.Should().Contain("OrderLists/order_list.json",
            $"artifact must contain OrderLists/order_list.json. Entries: [{string.Join(", ", entryNames)}]");
        entryNames.Any(n => n.StartsWith("Instructions/") && n.EndsWith(".pdf")).Should().BeTrue(
            $"artifact must contain an Instructions/*.pdf. Entries: [{string.Join(", ", entryNames)}]");

        var manifestEntry = archive.GetEntry("manifest.json")!;
        using (var manifestReader = new StreamReader(manifestEntry.Open()))
        {
            var manifestText = await manifestReader.ReadToEndAsync();
            using var manifestDoc = JsonDocument.Parse(manifestText);
            manifestDoc.RootElement.TryGetProperty("job_id", out var manifestJobId).Should()
                .BeTrue("manifest must include job_id");
            manifestJobId.GetString().Should().Be(jobId, "manifest job_id must match the job we submitted");
        }

        var orderListEntry = archive.GetEntry("OrderLists/order_list.json")!;
        using (var orderReader = new StreamReader(orderListEntry.Open()))
        {
            var orderText = await orderReader.ReadToEndAsync();
            using var orderDoc = JsonDocument.Parse(orderText);
            orderDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            orderDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0,
                "order_list.json must contain at least one piece");

            // Every piece — not just the first — must be well-formed.
            foreach (var item in orderDoc.RootElement.EnumerateArray())
            {
                item.TryGetProperty("elementId", out var elementId).Should().BeTrue();
                elementId.GetString().Should().NotBeNullOrWhiteSpace("each piece must have a non-empty elementId");
                item.TryGetProperty("quantity", out var qty).Should().BeTrue();
                qty.GetInt32().Should().BeGreaterThan(0, "each piece must have a positive quantity");
            }
        }
    }

    [Test]
    public async Task Download_LifecycleFromQueuedToComplete_Returns404Then200()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2);

        // Before completion no artifact.zip exists yet. Safe because generation
        // takes multiple seconds; the artifact is only written on completion.
        var preComplete = await Client.DownloadArtifactRawAsync(submitted.JobId);
        preComplete.Status.Should().Be(404, "artifact does not exist until the job completes");

        var finished = await Client.WaitForJobAsync(submitted.JobId);
        finished.Status.Should().Be("complete", $"job {submitted.JobId} must complete. Error: {finished.Error}");

        // Same job_id, post-completion → must now serve a real ZIP.
        var postComplete = await Client.DownloadArtifactRawAsync(submitted.JobId);
        postComplete.Status.Should().Be(200, "the same job_id must return 200 once complete");
        var bytes = await postComplete.BodyAsync();
        bytes.Length.Should().BeGreaterThan(100, "the served artifact must be a real ZIP");
        bytes[0].Should().Be(0x50, "ZIP magic byte 1 (P)");
        bytes[1].Should().Be(0x4B, "ZIP magic byte 2 (K)");
    }

    [Test]
    public async Task Preview_CompletedJob_ReturnsJsonObject()
    {
        var (jobId, _) = await SubmitAndAwaitCompletionAsync(blockWidth: 2, mosaicType: "2d");

        var response = await Client.GetJobPreviewRawAsync(jobId);
        var body = await response.TextAsync();

        response.Status.Should().Be(200, $"preview must be available for a completed job. Body: {body}");

        // It uses Response (not FileResponse) → Content-Type must be JSON, not octet-stream.
        response.Headers.TryGetValue("content-type", out var contentType);
        contentType.Should().Contain("application/json",
            "preview is returned via Response(media_type='application/json'), not FileResponse");

        // Body must be a non-empty JSON object.
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object,
            "the 3D-preview payload must be a JSON object");
        doc.RootElement.EnumerateObject().Should().NotBeEmpty(
            "the preview object must carry at least one field");
    }
}
