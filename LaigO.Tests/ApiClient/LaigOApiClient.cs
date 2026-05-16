using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Playwright;
using LaigO.Tests.Models;

namespace LaigO.Tests.ApiClient;

/// <summary>
/// Single typed client for all LAIGO endpoints.
/// IAPIRequestContext handles JSON endpoints; HttpClient handles multipart file upload.
/// </summary>
public class LaigOApiClient(IAPIRequestContext context, string baseUrl)
{
    // SnakeCaseLower maps constructor parameter names (e.g. JobId → job_id) so records
    // deserialize correctly without relying solely on [property: JsonPropertyName] targets,
    // which only annotate synthesized properties and are not seen by constructor-param matching.
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<T> ParseAsync<T>(IAPIResponse response)
    {
        var body = await response.TextAsync();
        if (!response.Ok)
            throw new InvalidOperationException(
                $"Request failed with status {response.Status}: {body}");
        return JsonSerializer.Deserialize<T>(body, _json)
            ?? throw new InvalidOperationException(
                $"Failed to deserialize {typeof(T).Name} from: {body}");
    }

    // ── Health ────────────────────────────────────────────────────────────────

    public Task<IAPIResponse> GetHealthRawAsync() =>
        context.GetAsync("/health");

    public async Task<HealthResponse> GetHealthAsync()
    {
        var r = await GetHealthRawAsync();
        return await ParseAsync<HealthResponse>(r);
    }

    public Task<IAPIResponse> GetRootRawAsync() =>
        context.GetAsync("/");

    public async Task<HealthResponse> GetRootAsync()
    {
        var r = await GetRootRawAsync();
        return await ParseAsync<HealthResponse>(r);
    }

    // ── Queue ─────────────────────────────────────────────────────────────────

    public Task<IAPIResponse> GetQueueRawAsync() =>
        context.GetAsync("/queue");

    public async Task<QueueResponse> GetQueueAsync()
    {
        var r = await GetQueueRawAsync();
        return await ParseAsync<QueueResponse>(r);
    }

    // ── Generate (multipart) ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the raw HTTP status code and response body.
    /// Use this to assert on error responses (400, 413, etc.).
    /// </summary>
    public async Task<(int Status, string Body)> GenerateRawAsync(
        string imagePath,
        int blockWidth = 2,
        string mosaicType = "2d",
        double bgPct = 100,
        bool toFrame = false)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        using var multipart = new MultipartFormDataContent();

        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        var fileContent = new ByteArrayContent(imageBytes);
        var contentType = Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        multipart.Add(fileContent, "file", Path.GetFileName(imagePath));
        multipart.Add(new StringContent(blockWidth.ToString()), "mosaic_block_width");
        multipart.Add(new StringContent(mosaicType), "mosaic_type");
        multipart.Add(new StringContent(bgPct.ToString("F2")), "background_color_percent");
        multipart.Add(new StringContent(toFrame ? "true" : "false"), "to_frame");

        var response = await http.PostAsync("generate", multipart);
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, body);
    }

    /// <summary>
    /// Submits a generate job and returns the parsed response.
    /// Throws if the server returns a non-200 status.
    /// </summary>
    public async Task<GenerateResponse> GenerateAsync(
        string imagePath,
        int blockWidth = 2,
        string mosaicType = "2d",
        double bgPct = 100,
        bool toFrame = false)
    {
        var (status, body) = await GenerateRawAsync(imagePath, blockWidth, mosaicType, bgPct, toFrame);
        if (status != 200)
            throw new InvalidOperationException(
                $"POST /generate returned {status}: {body}");
        return JsonSerializer.Deserialize<GenerateResponse>(body, _json)
            ?? throw new InvalidOperationException($"Failed to deserialize GenerateResponse: {body}");
    }

    /// <summary>
    /// Like <see cref="GenerateAsync"/> but never throws — returns the HTTP status and the
    /// parsed response (null on non-200). Use in discovery tests where the server may return
    /// 422 (validation) or 200 + a job that later fails.
    /// </summary>
    public async Task<(int Status, GenerateResponse? Job)> TryGenerateAsync(
        string imagePath,
        int blockWidth = 2,
        string mosaicType = "2d",
        double bgPct = 100,
        bool toFrame = false)
    {
        var (status, body) = await GenerateRawAsync(imagePath, blockWidth, mosaicType, bgPct, toFrame);
        if (status != 200) return (status, null);
        return (status, JsonSerializer.Deserialize<GenerateResponse>(body, _json));
    }

    // ── Job status ────────────────────────────────────────────────────────────

    public Task<IAPIResponse> GetJobRawAsync(string jobId) =>
        context.GetAsync($"/jobs/{jobId}");

    public async Task<JobStatusResponse> GetJobAsync(string jobId)
    {
        var r = await GetJobRawAsync(jobId);
        return await ParseAsync<JobStatusResponse>(r);
    }

    /// <summary>
    /// Polls /jobs/{jobId} every <paramref name="pollIntervalMs"/> ms until
    /// the job reaches "complete" or "failed", or the timeout is exceeded.
    /// </summary>
    public async Task<JobStatusResponse> WaitForJobAsync(
        string jobId,
        int timeoutMs = 0,
        int pollIntervalMs = 5_000)
    {
        var effectiveTimeout = timeoutMs > 0 ? timeoutMs : TestConfig.GenerationTimeoutMs;
        var deadline = DateTime.UtcNow.AddMilliseconds(effectiveTimeout);

        while (DateTime.UtcNow < deadline)
        {
            var status = await GetJobAsync(jobId);
            if (status.Status is "complete" or "failed")
                return status;
            await Task.Delay(pollIntervalMs);
        }

        throw new TimeoutException(
            $"Job {jobId} did not finish within {effectiveTimeout / 1000}s");
    }

    // ── Download ──────────────────────────────────────────────────────────────

    public Task<IAPIResponse> DownloadArtifactRawAsync(string jobId) =>
        context.GetAsync($"/jobs/{jobId}/download");

    public async Task<byte[]> DownloadArtifactAsync(string jobId)
    {
        var r = await DownloadArtifactRawAsync(jobId);
        return await r.BodyAsync();
    }

    // ── Checkout ──────────────────────────────────────────────────────────────

    public Task<IAPIResponse> GetQuoteRawAsync(string jobId, QuoteRequest request) =>
        context.PostAsync($"/jobs/{jobId}/checkout/quote", new APIRequestContextOptions
        {
            DataObject = request,
        });

    public async Task<QuoteResponse> GetQuoteAsync(string jobId, QuoteRequest request)
    {
        var r = await GetQuoteRawAsync(jobId, request);
        return await ParseAsync<QuoteResponse>(r);
    }

    public Task<IAPIResponse> ConfirmCheckoutRawAsync(string jobId, ConfirmRequest request) =>
        context.PostAsync($"/jobs/{jobId}/checkout/confirm", new APIRequestContextOptions
        {
            DataObject = request,
        });

    public Task<IAPIResponse> GetCheckoutStatusRawAsync(string jobId, string checkoutId) =>
        context.GetAsync($"/jobs/{jobId}/checkout/{checkoutId}/status");

    public async Task<CheckoutStatusResponse> GetCheckoutStatusAsync(string jobId, string checkoutId)
    {
        var r = await GetCheckoutStatusRawAsync(jobId, checkoutId);
        return await ParseAsync<CheckoutStatusResponse>(r);
    }

    // ── Debug ─────────────────────────────────────────────────────────────────

    public Task<IAPIResponse> GetBrickOwlElementAsync(string elementId) =>
        context.GetAsync($"/checkout-debug/brickowl/element/{elementId}");

    public Task<IAPIResponse> PostBrickOwlElementsAsync(IEnumerable<string> elementIds) =>
        context.PostAsync("/checkout-debug/brickowl/elements", new APIRequestContextOptions
        {
            DataObject = new { element_ids = elementIds.ToList(), shipping_country = "US", shipping_zip = "90210" },
        });

    public Task<IAPIResponse> GetLegoElementAsync(string elementId) =>
        context.GetAsync($"/checkout-debug/lego/element/{elementId}");

    public Task<IAPIResponse> PostLegoElementsAsync(IEnumerable<string> elementIds) =>
        context.PostAsync("/checkout-debug/lego/elements", new APIRequestContextOptions
        {
            DataObject = new { element_ids = elementIds.ToList() },
        });

    public Task<IAPIResponse> GetJobOrderListAsync(string jobId) =>
        context.GetAsync($"/checkout-debug/job/{jobId}/order-list");

    public Task<IAPIResponse> GetOptimizerPreviewAsync(string jobId) =>
        context.PostAsync($"/checkout-debug/job/{jobId}/optimize", new APIRequestContextOptions
        {
            DataObject = new { shipping_country = "US", shipping_zip = "90210" },
        });
}
