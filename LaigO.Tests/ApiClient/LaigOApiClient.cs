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
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static JsonSerializerOptions JsonOptions => _json;

    // One shared HttpClient for all multipart uploads. Creating a new
    // HttpClient per request (the old pattern) leaks sockets in TIME_WAIT under
    // load — the canonical .NET footgun. Multipart goes through HttpClient
    // rather than IAPIRequestContext because Playwright's API request context
    // does not build multipart/form-data bodies.
    private static readonly HttpClient _http = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<T> ParseAsync<T>(IAPIResponse response)
    {
        var body = await response.TextAsync();
        // Surface the HTTP status before deserializing — otherwise a 4xx/5xx with
        // a FastAPI {"detail": ...} body throws a confusing JsonException for the
        // missing target field instead of "Expected 200, got 500".
        if (!response.Ok)
            throw new InvalidOperationException(
                $"{typeof(T).Name} request failed with status {response.Status}: {body}");
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
    /// Use this to assert on error responses (400, 422, etc.).
    /// Accepts string-typed values so callers can submit deliberately invalid
    /// inputs (e.g. mosaic_block_width="0", mosaic_type="cube") to exercise the
    /// backend's validation 422s.
    /// </summary>
    public Task<(int Status, string Body)> GenerateRawAsync(
        string imagePath,
        int blockWidth = 2,
        string mosaicType = "2d",
        double bgPct = 100,
        bool toFrame = false)
    {
        var fields = new Dictionary<string, string>
        {
            ["mosaic_block_width"] = blockWidth.ToString(),
            ["mosaic_type"] = mosaicType,
            ["background_color_percent"] = bgPct.ToString("F2"),
            ["to_frame"] = toFrame ? "true" : "false",
        };
        return GenerateMultipartRawAsync(imagePath, fields);
    }

    /// <summary>
    /// Low-level multipart POST to /generate with caller-supplied form fields.
    /// Omitting a key lets tests exercise the missing-required-field 422 without
    /// duplicating the HttpClient/multipart plumbing in the test body. Pass
    /// <paramref name="fieldOverrides"/> with raw string values to send invalid
    /// shapes.
    /// </summary>
    public async Task<(int Status, string Body)> GenerateMultipartRawAsync(
        string imagePath,
        IDictionary<string, string> formFields)
    {
        using var multipart = new MultipartFormDataContent();

        var imageBytes = await File.ReadAllBytesAsync(imagePath);
        var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        multipart.Add(fileContent, "file", Path.GetFileName(imagePath));
        foreach (var (key, value) in formFields)
            multipart.Add(new StringContent(value), key);

        var url = baseUrl.TrimEnd('/') + "/generate";
        var response = await _http.PostAsync(url, multipart);
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

    /// <summary>GET /jobs/{jobId}/preview — 3D-preview JSON payload.</summary>
    public Task<IAPIResponse> GetJobPreviewRawAsync(string jobId) =>
        context.GetAsync($"/jobs/{jobId}/preview");

    /// <summary>
    /// GET /artifacts/{jobId}/artifact.zip — the StaticFiles mount, a second
    /// (unauthenticated) download path parallel to /jobs/{id}/download.
    /// </summary>
    public Task<IAPIResponse> GetStaticArtifactRawAsync(string jobId) =>
        context.GetAsync($"/artifacts/{jobId}/artifact.zip");

    // ── Generic fetch (arbitrary method + headers) ──────────────────────────
    // For CORS preflight (OPTIONS), HEAD prechecks, and method-not-allowed
    // (405) assertions that the typed GET/POST helpers can't express.

    public Task<IAPIResponse> FetchRawAsync(
        string path,
        string method,
        IDictionary<string, string>? headers = null) =>
        context.FetchAsync(path, new APIRequestContextOptions
        {
            Method = method,
            Headers = headers,
        });

    // ── Checkout ──────────────────────────────────────────────────────────────

    /// <summary>
    /// POST an arbitrary JSON body to any path. For malformed-body validation
    /// tests (missing/extra/wrong-typed fields) that the typed request records
    /// can't express.
    /// </summary>
    public Task<IAPIResponse> PostJsonRawAsync(string path, object body) =>
        context.PostAsync(path, new APIRequestContextOptions { DataObject = body });

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

    public Task<IAPIResponse> PostBrickOwlElementsAsync(
        IEnumerable<string> elementIds,
        string shippingCountry = "US",
        string shippingZip = "90210") =>
        context.PostAsync("/checkout-debug/brickowl/elements", new APIRequestContextOptions
        {
            DataObject = new
            {
                element_ids = elementIds.ToList(),
                shipping_country = shippingCountry,
                shipping_zip = shippingZip,
            },
        });

    public Task<IAPIResponse> GetBrickOwlElementRawAsync(
        string elementId,
        string idType = "item_no") =>
        context.GetAsync($"/checkout-debug/brickowl/element/{elementId}/raw?id_type={idType}");

    public Task<IAPIResponse> GetLegoElementAsync(string elementId) =>
        context.GetAsync($"/checkout-debug/lego/element/{elementId}");

    public Task<IAPIResponse> PostLegoElementsAsync(IEnumerable<string> elementIds) =>
        context.PostAsync("/checkout-debug/lego/elements", new APIRequestContextOptions
        {
            // Backend is Pydantic LegoAvailabilityBatchRequest{element_ids: list[str]},
            // not a bare array. Sending [..] directly produces 422 model_attributes_type.
            DataObject = new { element_ids = elementIds.ToList() },
        });

    public Task<IAPIResponse> GetJobOrderListAsync(string jobId) =>
        context.GetAsync($"/checkout-debug/job/{jobId}/order-list");

    public Task<IAPIResponse> GetOptimizerPreviewAsync(
        string jobId,
        string shippingCountry = "US",
        string shippingZip = "90210") =>
        context.PostAsync($"/checkout-debug/job/{jobId}/optimize", new APIRequestContextOptions
        {
            // OptimizePreviewRequest fields have defaults server-side, but the body
            // is a required parameter — POSTing with no body yields 422 "Field
            // required", which silently passed the old BeOneOf(200,422,502,503).
            DataObject = new { shipping_country = shippingCountry, shipping_zip = shippingZip },
        });

    public Task<IAPIResponse> GetLegoElementListingAsync(string elementId) =>
        context.GetAsync($"/checkout-debug/lego/element/{elementId}/listing");

    // ── Checkout gate ─────────────────────────────────────────────────────────

    public Task<IAPIResponse> GetCheckoutGateRawAsync() =>
        context.GetAsync("/checkout/gate");

    public async Task<CheckoutGateResponse> GetCheckoutGateAsync()
    {
        var r = await GetCheckoutGateRawAsync();
        return await ParseAsync<CheckoutGateResponse>(r);
    }
}
