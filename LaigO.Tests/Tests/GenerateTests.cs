using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Tests;

/// <summary>
/// End-to-end pipeline tests: POST /generate → poll /jobs/{id} → GET /jobs/{id}/download.
/// Runs against the deployed LAIGO instance; generation timeout is configured in appsettings.test.json.
/// </summary>
[TestFixture]
[Category("Generate")]
public class GenerateTests : LaigOTestBase
{
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
        finished.Error.Should().BeNull("a completed job has no error");
        finished.Traceback.Should().BeNull("a completed job has no traceback");
        finished.FinishedAt.Should().NotBeNull("complete jobs must have finished_at");
        finished.CreatedAt.Should().NotBeNull();
        finished.FinishedAt.Should().BeGreaterThan(finished.CreatedAt!.Value,
            "finished_at must be after created_at");

        // Settings must round-trip — the server should echo what we submitted.
        finished.Settings.Should().NotBeNull();
        finished.Settings!.MosaicBlockWidth.Should().Be(2);
        finished.Settings.MosaicType.Should().Be("2d");
        finished.Settings.BackgroundColorPercent.Should().BeApproximately(100, 0.01);
        finished.Settings.ToFrame.Should().BeFalse();
    }

    [Test]
    public async Task Generate_JobStatus_ReturnsValidShape()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d");

        var status = await Client.GetJobAsync(submitted.JobId);

        status.Status.Should().BeOneOf("queued", "running", "complete", "failed");
        status.Progress.Should().BeInRange(0, 100);
        status.CreatedAt.Should().NotBeNull("every job must have a created_at timestamp");
        status.QueuedAt.Should().NotBeNull("every job must have a queued_at timestamp");

        // Settings must echo what was submitted, even mid-flight.
        status.Settings.Should().NotBeNull();
        status.Settings!.MosaicBlockWidth.Should().Be(2);
        status.Settings.MosaicType.Should().Be("2d");

        // Failure-mode invariants
        if (status.Status == "complete")
        {
            status.Progress.Should().BeApproximately(100, 0.1);
            status.FinishedAt.Should().NotBeNull();
        }
        if (status.Status == "failed")
        {
            // Failed jobs should at least carry an error explanation.
            status.Error.Should().NotBeNullOrWhiteSpace("failed jobs must carry an error message");
        }

        // Clean up — wait for completion so we don't clog the queue
        await Client.WaitForJobAsync(submitted.JobId);
    }

    [Test]
    public async Task Generate_CompletedJob_ArtifactIsValidMosaicZip()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d");
        var finished = await Client.WaitForJobAsync(submitted.JobId);
        finished.Status.Should().Be("complete",
            $"job {submitted.JobId} must complete. Error: {finished.Error}");

        var downloadResponse = await Client.DownloadArtifactRawAsync(submitted.JobId);
        downloadResponse.Status.Should().Be(200);

        var bytes = await downloadResponse.BodyAsync();
        bytes.Should().NotBeEmpty();
        bytes.Length.Should().BeGreaterThan(100,
            "a real mosaic ZIP is at least a few KB; a tiny response is a stub or error");

        // ZIP magic bytes: PK\x03\x04
        bytes[0].Should().Be(0x50);
        bytes[1].Should().Be(0x4B);

        // Validate ZIP contents — the artifact must have the deliverable mosaic
        // (instructions PDF), the canonical order list, and the manifest.
        using var zipStream = new MemoryStream(bytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var entryNames = archive.Entries.Select(e => e.FullName).ToHashSet();

        entryNames.Should().Contain("manifest.json",
            $"artifact must contain manifest.json. Entries: [{string.Join(", ", entryNames)}]");
        entryNames.Should().Contain("OrderLists/order_list.json",
            $"artifact must contain OrderLists/order_list.json. Entries: [{string.Join(", ", entryNames)}]");
        entryNames.Any(n => n.StartsWith("Instructions/") && n.EndsWith(".pdf")).Should().BeTrue(
            $"artifact must contain an Instructions/*.pdf. Entries: [{string.Join(", ", entryNames)}]");

        // Manifest must be parseable JSON and reference the same job.
        var manifestEntry = archive.GetEntry("manifest.json")!;
        using (var manifestReader = new StreamReader(manifestEntry.Open()))
        {
            var manifestText = await manifestReader.ReadToEndAsync();
            using var manifestDoc = JsonDocument.Parse(manifestText);
            manifestDoc.RootElement.TryGetProperty("job_id", out var manifestJobId).Should()
                .BeTrue("manifest must include job_id");
            manifestJobId.GetString().Should().Be(submitted.JobId,
                "manifest job_id must match the job we submitted");
        }

        // Order list inside the ZIP must be a non-empty array of pieces.
        var orderListEntry = archive.GetEntry("OrderLists/order_list.json")!;
        using (var orderReader = new StreamReader(orderListEntry.Open()))
        {
            var orderText = await orderReader.ReadToEndAsync();
            using var orderDoc = JsonDocument.Parse(orderText);
            orderDoc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            orderDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0,
                "order_list.json must contain at least one piece");
            var first = orderDoc.RootElement[0];
            first.TryGetProperty("elementId", out _).Should().BeTrue();
            first.TryGetProperty("quantity", out var qty).Should().BeTrue();
            qty.GetInt32().Should().BeGreaterThan(0);
        }
    }

    [Test]
    public async Task Generate_NonExistentJob_Returns404WithDetail()
    {
        var response = await Client.GetJobRawAsync("00000000-0000-0000-0000-000000000000");

        response.Status.Should().Be(404);

        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out _).Should().BeTrue();
    }

    [Test]
    public async Task Download_NonExistentJob_Returns404()
    {
        var response = await Client.DownloadArtifactRawAsync("00000000-0000-0000-0000-000000000000");

        response.Status.Should().Be(404);
    }

    [Test]
    public async Task Download_IncompleteJob_Returns404_ThenSucceedsWhenComplete()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2);

        // Immediately request the artifact before the job finishes — no artifact.zip exists yet
        var preCompleteResponse = await Client.DownloadArtifactRawAsync(submitted.JobId);
        preCompleteResponse.Status.Should().Be(404,
            "artifact does not exist until the job completes");

        var finished = await Client.WaitForJobAsync(submitted.JobId);
        finished.Status.Should().Be("complete",
            $"job {submitted.JobId} must complete. Error: {finished.Error}");

        // Same job_id, post-completion → must now succeed
        var postCompleteResponse = await Client.DownloadArtifactRawAsync(submitted.JobId);
        postCompleteResponse.Status.Should().Be(200,
            "the same job_id must return 200 once complete");
    }

    [Test]
    public async Task Generate_InvalidFile_Returns400WithDetail()
    {
        var tempFile = Path.GetTempFileName() + ".jpg";
        await File.WriteAllTextAsync(tempFile, "this is not an image");

        try
        {
            var (status, body) = await Client.GenerateRawAsync(tempFile, blockWidth: 2);
            status.Should().Be(400, "invalid image data must be rejected by PIL verification");

            body.Should().NotBeNullOrWhiteSpace("400 must include a detail body");
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.TryGetProperty("detail", out _).Should()
                .BeTrue("FastAPI 400 must include 'detail'");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Generate_MissingRequiredField_Returns422WithValidationError()
    {
        using var http = new HttpClient();
        using var multipart = new MultipartFormDataContent();

        var imageBytes = await File.ReadAllBytesAsync(TestImagePath);
        var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        multipart.Add(fileContent, "file", "test_image.jpg");
        // deliberately omit mosaic_block_width — FastAPI must reject with 422
        multipart.Add(new StringContent("2d"), "mosaic_type");

        var response = await http.PostAsync(TestConfig.BaseUrl.TrimEnd('/') + "/generate", multipart);

        ((int)response.StatusCode).Should().Be(422);

        // FastAPI 422 body shape: {"detail":[{"loc":[...],"msg":...,"type":...}]}
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out var detail).Should().BeTrue();
        detail.ValueKind.Should().Be(JsonValueKind.Array,
            "FastAPI 422 detail must be a list of validation errors");
        detail.GetArrayLength().Should().BeGreaterThan(0);
        // The validation error should mention the missing field
        body.Should().Contain("mosaic_block_width",
            "validation error must identify the missing field");
    }

    [Test]
    public async Task Generate_WithToFrame_CompletesSuccessfullyAndEchoesSetting()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d", toFrame: true);

        submitted.JobId.Should().NotBeNullOrWhiteSpace();
        submitted.Status.Should().Be("queued");

        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("complete",
            $"to_frame job {submitted.JobId} should complete. Error: {finished.Error}");
        finished.Progress.Should().BeApproximately(100, 0.1);
        finished.Settings.Should().NotBeNull();
        finished.Settings!.ToFrame.Should().BeTrue(
            "to_frame=true must round-trip through the settings echo");
    }
}
