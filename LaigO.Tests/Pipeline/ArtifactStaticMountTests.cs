using System.IO.Compression;
using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Pipeline;

/// <summary>
/// GET /artifacts/{job_id}/artifact.zip — the StaticFiles mount (Main.py:449),
/// a second, unauthenticated download path parallel to /jobs/{id}/download.
/// This test pins the INTENDED behavior so a mount removal is caught, and
/// documents the decision that the path is publicly readable by job_id (anyone
/// who learns a job_id can fetch its artifact). If that is ever deemed an
/// information-disclosure risk, this test should be inverted to assert the mount
/// is gone / protected.
/// </summary>
[TestFixture]
[Category("Pipeline")]
[Category("Generate")]
public class ArtifactStaticMountTests : LaigOTestBase
{
    [Test]
    public async Task Artifacts_StaticMount_ServesCompletedZip()
    {
        var (jobId, _) = await SubmitAndAwaitCompletionAsync(blockWidth: 2);

        var response = await Client.GetStaticArtifactRawAsync(jobId);
        response.Status.Should().Be(200, "the static mount must serve a completed job's artifact.zip");

        var bytes = await response.BodyAsync();
        bytes.Length.Should().BeGreaterThan(100, "a real ZIP is several KB");
        bytes[0].Should().Be(0x50, "ZIP magic byte 1 (P)");
        bytes[1].Should().Be(0x4B, "ZIP magic byte 2 (K)");

        // The static-served bytes must be a genuinely valid archive containing
        // the same canonical entries as the /download path, proving the mount
        // serves the real artifact (not a truncated/placeholder file).
        using var zipStream = new MemoryStream(bytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Should().NotBeEmpty("the served file must be a non-empty ZIP archive");
        archive.GetEntry("manifest.json").Should().NotBeNull(
            "the static-served artifact must contain manifest.json, same as /download");
    }

    [Test]
    public async Task Artifacts_StaticMount_NonExistentJob_Returns404()
    {
        var response = await Client.GetStaticArtifactRawAsync(TestConstants.NonExistentJobId);
        response.Status.Should().Be(404, "the static mount must 404 for an unknown job path");
    }
}
