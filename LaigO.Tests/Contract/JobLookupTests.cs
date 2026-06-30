using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.ApiClient;
using LaigO.Tests.Fixtures;
using LaigO.Tests.Models;

namespace LaigO.Tests.Contract;

/// <summary>
/// 404 paths for the per-job read endpoints, exercised against a job_id that
/// never existed (no job created). The positive shapes live in
/// Pipeline.GenerateLifecycleTests.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Generate")]
public class JobLookupTests : LaigOTestBase
{
    [Test]
    public async Task JobStatus_NonExistentJob_Returns404WithDetail()
    {
        var response = await Client.GetJobRawAsync(TestConstants.NonExistentJobId);

        response.Status.Should().Be(404);
        var body = await response.TextAsync();
        body.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out var detail).Should().BeTrue();
        detail.GetString().Should().Contain("not found",
            "the 404 detail must explain that the job is unknown");
    }

    [Test]
    public async Task Download_NonExistentJob_Returns404WithDetail()
    {
        var response = await Client.DownloadArtifactRawAsync(TestConstants.NonExistentJobId);

        response.Status.Should().Be(404);
        var body = await response.TextAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out var detail).Should()
            .BeTrue("a 404 must carry a detail body");
        detail.GetString().Should().Contain("not found");
    }

    [Test]
    public async Task Preview_NonExistentJob_Returns404WithCode()
    {
        var response = await Client.GetJobPreviewRawAsync(TestConstants.NonExistentJobId);
        var body = await response.TextAsync();

        response.Status.Should().Be(404, $"no preview exists for an unknown job. Body: {body}");

        // Contract the frontend matches on: detail.code == "PREVIEW_NOT_AVAILABLE".
        var envelope = JsonSerializer.Deserialize<StructuredErrorEnvelope>(body, LaigOApiClient.JsonOptions)!;
        envelope.Detail.Should().NotBeNull("preview 404 detail must be the structured {error, code} object");
        envelope.Detail.Code.Should().Be("PREVIEW_NOT_AVAILABLE",
            "the frontend matches on detail.code — a rename breaks it invisibly");
        envelope.Detail.Error.Should().NotBeNullOrWhiteSpace(
            "the structured error must carry a human-readable 'error' alongside the code");
    }
}
