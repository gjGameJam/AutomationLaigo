using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Tests;

/// <summary>
/// End-to-end pipeline tests: POST /generate → poll /jobs/{id} → GET /jobs/{id}/download.
/// Generation takes 30–120s on Render's free tier, so these tests have a long timeout.
/// </summary>
[TestFixture]
[Category("Generate")]
public class GenerateTests : LaigOTestBase
{
    [Test]
    public async Task Generate_ValidImage_JobQueuesAndCompletes()
    {
        // Use a 2-block-wide 2D mosaic — smallest possible to finish quickly
        var (status, _) = await Client.GenerateRawAsync(
            TestImagePath, blockWidth: 2, mosaicType: "2d", bgPct: 100, toFrame: false);

        status.Should().Be(200, "valid image should be accepted");

        var submitted = await Client.GenerateAsync(
            TestImagePath, blockWidth: 2, mosaicType: "2d", bgPct: 100, toFrame: false);

        submitted.JobId.Should().NotBeNullOrWhiteSpace();
        submitted.Status.Should().Be("queued");

        var finished = await Client.WaitForJobAsync(submitted.JobId);

        finished.Status.Should().Be("complete",
            $"job {submitted.JobId} should complete. Error: {finished.Error}");
        finished.Progress.Should().BeApproximately(100, 0.1);
    }

    [Test]
    public async Task Generate_JobStatus_ReturnsValidShape()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d");

        var status = await Client.GetJobAsync(submitted.JobId);

        status.JobId.Should().Be(submitted.JobId);
        status.Status.Should().BeOneOf("queued", "running", "complete");
        status.Progress.Should().BeInRange(0, 100);

        // Clean up — wait for completion so we don't clog the queue
        await Client.WaitForJobAsync(submitted.JobId);
    }

    [Test]
    public async Task Generate_CompletedJob_ArtifactDownloadsAsZip()
    {
        var submitted = await Client.GenerateAsync(TestImagePath, blockWidth: 2, mosaicType: "2d");
        var finished = await Client.WaitForJobAsync(submitted.JobId);
        finished.Status.Should().Be("complete");

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
}
