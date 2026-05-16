using FluentAssertions;
using LaigO.Tests.Fixtures;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace LaigO.Tests.Tests;

/// <summary>
/// End-to-end pipeline tests: POST /generate → poll /jobs/{id} → GET /jobs/{id}/download.
/// Generation takes 30–120s on Render's paid plan.
/// </summary>
[TestFixture]
[Category("Generate")]
public class GenerateTests : LaigOTestBase
{
    private static readonly Regex UuidPattern = new(
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Posts to /generate with only the fields explicitly provided — used for 422 edge cases.
    private async Task<int> RawPostGenerateStatusAsync(
        bool includeFile = true,
        string? blockWidth = "2",
        string? mosaicType = "2d",
        string? bgPct = null,
        string? toFrame = null)
    {
        using var http = new HttpClient();
        using var multipart = new MultipartFormDataContent();

        if (includeFile)
        {
            var imageBytes = await File.ReadAllBytesAsync(TestImagePath);
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
            multipart.Add(fileContent, "file", "test_image.jpg");
        }

        if (blockWidth != null) multipart.Add(new StringContent(blockWidth), "mosaic_block_width");
        if (mosaicType != null) multipart.Add(new StringContent(mosaicType), "mosaic_type");
        if (bgPct != null) multipart.Add(new StringContent(bgPct), "background_color_percent");
        if (toFrame != null) multipart.Add(new StringContent(toFrame), "to_frame");

        var response = await http.PostAsync(TestConfig.BaseUrl.TrimEnd('/') + "/generate", multipart);
        return (int)response.StatusCode;
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Test]
    public async Task Generate_ValidImage_JobQueuesAndCompletes()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d", bgPct: 100, toFrame: false);

        submitted.JobId.Should().NotBeNullOrWhiteSpace();
        submitted.Status.Should().Be("queued");

        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("complete",
            $"job {submitted.JobId} should complete. Error: {finished.Error}");
        finished.Progress.Should().BeApproximately(100, 0.1);
    }

    [Test]
    public async Task Generate_With3dType_CompletesSuccessfully()
    {
        // Exercises the background-removal (mediapipe) branch — an entirely separate pipeline from 2d
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "3d", bgPct: 100, toFrame: false);

        submitted.JobId.Should().NotBeNullOrWhiteSpace();
        submitted.Status.Should().Be("queued");

        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("complete",
            $"3d job {submitted.JobId} should complete. Error: {finished.Error}");
        finished.Progress.Should().BeApproximately(100, 0.1);
    }

    [Test]
    public async Task Generate_ZeroBackground_CompletesSuccessfully()
    {
        // background_color_percent=0 has never been exercised below 100
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d", bgPct: 0, toFrame: false);

        submitted.JobId.Should().NotBeNullOrWhiteSpace();

        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("complete",
            $"zero-background job {submitted.JobId} should complete. Error: {finished.Error}");
        finished.Progress.Should().BeApproximately(100, 0.1);
    }

    [Test]
    public async Task Generate_WithToFrame_CompletesSuccessfully()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d", toFrame: true);

        submitted.JobId.Should().NotBeNullOrWhiteSpace();
        submitted.Status.Should().Be("queued");

        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("complete",
            $"to_frame job {submitted.JobId} should complete. Error: {finished.Error}");
        finished.Progress.Should().BeApproximately(100, 0.1);
    }

    // ── Job response shape ────────────────────────────────────────────────────

    [Test]
    public async Task Generate_ResponseJobIdIsUUID()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d");

        UuidPattern.IsMatch(submitted.JobId).Should().BeTrue(
            $"job_id must be a valid UUID, got: '{submitted.JobId}'");

        await Client.WaitForJobAsync(submitted.JobId);
    }

    [Test]
    public async Task Generate_JobStatus_ReturnsValidShape()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d");

        var status = await Client.GetJobAsync(submitted.JobId);

        // job_id is not returned in GET /jobs/{id} responses — use submitted.JobId from POST /generate
        status.Status.Should().BeOneOf("queued", "running", "complete", "failed");
        status.Progress.Should().BeInRange(0, 100);

        await Client.WaitForJobAsync(submitted.JobId);
    }

    [Test]
    public async Task Generate_CompletedJob_SettingsRoundTrip()
    {
        // Verify that submitted parameters are faithfully stored and returned by GET /jobs/{id}
        const int blockWidth = 4;
        const string mosaicType = "2d";
        const double bgPct = 75.0;
        const bool toFrame = false;

        var submitted = await Client.GenerateAsync(TestImagePath,
            blockWidth: blockWidth, mosaicType: mosaicType, bgPct: bgPct, toFrame: toFrame);
        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Settings.Should().NotBeNull("completed job must include settings");
        finished.Settings!.MosaicBlockWidth.Should().Be(blockWidth);
        finished.Settings.MosaicType.Should().Be(mosaicType);
        finished.Settings.BackgroundColorPercent.Should().BeApproximately(bgPct, 0.01);
        finished.Settings.ToFrame.Should().Be(toFrame);
    }

    [Test]
    public async Task Generate_CompletedJob_HasTimestamps()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d");
        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.CreatedAt.Should().NotBeNull("completed job must have created_at");
        finished.QueuedAt.Should().NotBeNull("completed job must have queued_at");
        finished.FinishedAt.Should().NotBeNull("completed job must have finished_at");
        finished.FinishedAt!.Value.Should().BeGreaterThan(finished.CreatedAt!.Value,
            "finished_at must be a later Unix timestamp than created_at");
    }

    // ── Download ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Generate_CompletedJob_ArtifactDownloadsAsZip()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d");
        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("complete",
            $"job {submitted.JobId} should complete. Error: {finished.Error}");

        var downloadResponse = await Client.DownloadArtifactRawAsync(submitted.JobId);
        downloadResponse.Status.Should().Be(200);

        var bytes = await downloadResponse.BodyAsync();
        bytes.Should().NotBeEmpty();

        // ZIP magic bytes: PK\x03\x04
        bytes[0].Should().Be(0x50);
        bytes[1].Should().Be(0x4B);
    }

    [Test]
    public async Task Generate_NonExistentJob_Returns404()
    {
        var response = await Client.GetJobRawAsync("00000000-0000-0000-0000-000000000000");

        response.Status.Should().Be(404);
    }

    [Test]
    public async Task Download_NonExistentJob_Returns404()
    {
        var response = await Client.DownloadArtifactRawAsync("00000000-0000-0000-0000-000000000000");

        response.Status.Should().Be(404);
    }

    [Test]
    public async Task Download_IncompleteJob_Returns404()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2);

        // Immediately request the artifact before the job finishes — no artifact.zip exists yet
        var response = await Client.DownloadArtifactRawAsync(submitted.JobId);
        response.Status.Should().Be(404, "artifact does not exist until the job completes");

        await Client.WaitForJobAsync(submitted.JobId);
    }

    // ── File content validation (400) ─────────────────────────────────────────

    [Test]
    public async Task Generate_InvalidFile_Returns400()
    {
        var tempFile = Path.GetTempFileName() + ".jpg";
        await File.WriteAllTextAsync(tempFile, "this is not an image");

        try
        {
            var (status, _) = await Client.GenerateRawAsync(tempFile, blockWidth: 2);
            status.Should().Be(400, "invalid image data must be rejected by PIL verification");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Generate_EmptyFile_Returns400()
    {
        var tempFile = Path.GetTempFileName() + ".jpg";
        await File.WriteAllBytesAsync(tempFile, Array.Empty<byte>());

        try
        {
            var (status, _) = await Client.GenerateRawAsync(tempFile, blockWidth: 2);
            status.Should().Be(400, "empty file must be rejected by PIL verification");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Generate_PngExtensionWrongContent_Returns400()
    {
        var tempFile = Path.GetTempFileName() + ".png";
        await File.WriteAllTextAsync(tempFile, "this is not a png");

        try
        {
            var (status, _) = await Client.GenerateRawAsync(tempFile, blockWidth: 2);
            status.Should().Be(400, "non-image content must be rejected regardless of extension");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Form validation (422) ─────────────────────────────────────────────────

    [Test]
    public async Task Generate_MissingFile_Returns422()
    {
        var status = await RawPostGenerateStatusAsync(includeFile: false, blockWidth: "2", mosaicType: "2d");
        status.Should().Be(422);
    }

    [Test]
    public async Task Generate_MissingBlockWidth_Returns422()
    {
        var status = await RawPostGenerateStatusAsync(blockWidth: null, mosaicType: "2d");
        status.Should().Be(422);
    }

    [Test]
    public async Task Generate_MissingMosaicType_Returns422()
    {
        var status = await RawPostGenerateStatusAsync(blockWidth: "2", mosaicType: null);
        status.Should().Be(422);
    }

    [Test]
    public async Task Generate_NonIntegerBlockWidth_Returns422()
    {
        // FastAPI parses mosaic_block_width as int — sending a string must be rejected before handler runs
        var status = await RawPostGenerateStatusAsync(blockWidth: "abc", mosaicType: "2d");
        status.Should().Be(422);
    }

    [Test]
    public async Task Generate_NonFloatBackground_Returns422()
    {
        // FastAPI parses background_color_percent as float — sending a non-numeric string must be rejected
        var status = await RawPostGenerateStatusAsync(blockWidth: "2", mosaicType: "2d", bgPct: "banana");
        status.Should().Be(422);
    }

    // ── Worker validation (job queued, then fails) ────────────────────────────

    [Test]
    public async Task Generate_InvalidMosaicType_JobFails()
    {
        // FastAPI accepts any string for mosaic_type; the MosaicType enum cast fails inside the worker.
        // This is the primary way to exercise the status="failed" + error field code path.
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "invalid_type");
        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("failed",
            "an unrecognised mosaic_type is only caught inside the worker, causing the job to fail");
        finished.Error.Should().NotBeNullOrWhiteSpace("a failed job must expose an error message");
    }

    [Test]
    public async Task Generate_ZeroBlockWidth_JobFails()
    {
        // blockWidth=0 is a valid int so FastAPI accepts it; the pipeline fails when it tries
        // to produce a 0-pixel-wide mosaic (numpy/PIL will raise on zero-dimension arrays).
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 0, mosaicType: "2d");
        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("failed",
            "zero block width produces a 0-pixel mosaic which the pipeline cannot process");
        finished.Error.Should().NotBeNullOrWhiteSpace();
    }
}
