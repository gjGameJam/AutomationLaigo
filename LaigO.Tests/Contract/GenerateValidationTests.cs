using System.Text.Json;
using FluentAssertions;
using LaigO.Tests.Fixtures;

namespace LaigO.Tests.Contract;

/// <summary>
/// POST /generate input validation. Every case here is rejected by the backend
/// BEFORE the upload is read and BEFORE any job is created (B54 ordering:
/// value validation → shutdown → queue-full → size → image-verify), so these
/// are fast and leave no job behind. The happy paths live in
/// Pipeline.GenerateLifecycleTests.
/// </summary>
[TestFixture]
[Category("Contract")]
[Category("Generate")]
public class GenerateValidationTests : LaigOTestBase
{
    /// <summary>The custom range guards raise HTTPException(422, detail="string").</summary>
    private static string ParseStringDetail(string body)
    {
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out var detail).Should()
            .BeTrue("a 422 must carry a 'detail'");
        detail.ValueKind.Should().Be(JsonValueKind.String,
            "the range-guard 422s use a string detail (not the Pydantic error list)");
        return detail.GetString()!;
    }

    // MIN_BLOCK_WIDTH=1, MAX_BLOCK_WIDTH=40 → 0, -1, 41 are all out of range.
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(41)]
    public async Task Generate_BlockWidthOutOfRange_Returns422(int blockWidth)
    {
        var (status, body) = await Client.GenerateRawAsync(TestImagePath, blockWidth: blockWidth);

        status.Should().Be(422,
            $"mosaic_block_width={blockWidth} is outside [{TestConstants.MinBlockWidth},{TestConstants.MaxBlockWidth}]. Body: {body}");

        var detail = ParseStringDetail(body);
        detail.Should().Contain("mosaic_block_width", "the error must name the offending field");
        // The error must state the accepted bounds so the caller can fix the request.
        detail.Should().ContainAll(
            TestConstants.MinBlockWidth.ToString(),
            TestConstants.MaxBlockWidth.ToString());
    }

    [Test]
    public async Task Generate_InvalidMosaicType_Returns422()
    {
        var (status, body) = await Client.GenerateRawAsync(TestImagePath, mosaicType: "cube");

        status.Should().Be(422, $"mosaic_type must be '2d' or '3d'. Body: {body}");

        var detail = ParseStringDetail(body);
        detail.Should().Contain("mosaic_type", "the error must name the offending field");
        // The error must list the valid enum values.
        detail.Should().ContainAll("2d", "3d");
    }

    [TestCase(-1.0)]
    [TestCase(101.0)]
    public async Task Generate_BackgroundPercentOutOfRange_Returns422(double bgPct)
    {
        var (status, body) = await Client.GenerateRawAsync(TestImagePath, bgPct: bgPct);

        status.Should().Be(422,
            $"background_color_percent={bgPct} is outside [0,100]. Body: {body}");

        var detail = ParseStringDetail(body);
        detail.Should().Contain("background_color_percent", "the error must name the offending field");
        // The error must state the accepted [0,100] bounds.
        detail.Should().ContainAll("0", "100");
    }

    [Test]
    public async Task Generate_InvalidImageData_Returns400()
    {
        var tempFile = Path.GetTempFileName() + ".jpg";
        await File.WriteAllTextAsync(tempFile, "this is not an image");

        try
        {
            var (status, body) = await Client.GenerateRawAsync(tempFile, blockWidth: 2);

            status.Should().Be(400, "invalid image data must be rejected by PIL verification");
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.TryGetProperty("detail", out var detail).Should()
                .BeTrue("FastAPI 400 must include 'detail'");
            detail.GetString().Should().Contain("image",
                "the 400 must identify that the uploaded file is not a valid image");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Generate_MissingRequiredField_Returns422()
    {
        // Omit mosaic_block_width entirely → FastAPI Pydantic validation 422.
        // Routed through the client's low-level multipart sender so the request
        // plumbing isn't duplicated in the test body.
        var fields = new Dictionary<string, string>
        {
            ["mosaic_type"] = "2d",
            // deliberately no mosaic_block_width
        };

        var (status, body) = await Client.GenerateMultipartRawAsync(TestImagePath, fields);

        status.Should().Be(422);

        // FastAPI 422 body shape: {"detail":[{"loc":[...],"msg":...,"type":"missing"}]}
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("detail", out var detail).Should().BeTrue();
        detail.ValueKind.Should().Be(JsonValueKind.Array,
            "FastAPI 422 detail must be a list of validation errors");
        detail.GetArrayLength().Should().BeGreaterThan(0);

        // The validation entry must point at the missing field with type "missing".
        var entry = detail[0];
        entry.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("missing", "the omitted required field must report type 'missing'");
        entry.GetRawText().Should().Contain("mosaic_block_width",
            "the loc must identify which field is missing");
    }
}
