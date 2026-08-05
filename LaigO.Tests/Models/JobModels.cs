using System.Text.Json.Serialization;

namespace LaigO.Tests.Models;

public record GenerateResponse(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("status")] string Status
);

// Note: LAIGO `/jobs/{job_id}` does not echo `job_id` in the response body —
// the field is in the URL path only. Use the JobId from GenerateResponse for
// downstream URLs, not from any poll response.
//
// P0 fix (2026-06-03): the `traceback` field was REMOVED. `_store_row_to_response`
// (Main.py:526-567) never emits a `traceback` key — only `error` (from
// `error_message`). The old `Traceback` property always deserialized to null,
// so `finished.Traceback.Should().BeNull()` passed unconditionally for every
// status, including failed jobs. A failed job's detail lives in `error`; assert
// on that instead.
public record JobStatusResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("created_at")] double? CreatedAt,
    [property: JsonPropertyName("queued_at")] double? QueuedAt,
    [property: JsonPropertyName("started_at")] double? StartedAt,
    [property: JsonPropertyName("finished_at")] double? FinishedAt,
    [property: JsonPropertyName("queue_position")] int? QueuePosition,
    [property: JsonPropertyName("queue_length")] int? QueueLength,
    [property: JsonPropertyName("settings")] JobSettings? Settings
);

public record JobSettings(
    [property: JsonPropertyName("mosaic_block_width")] int MosaicBlockWidth,
    [property: JsonPropertyName("mosaic_type")] string MosaicType,
    [property: JsonPropertyName("background_color_percent")] double BackgroundColorPercent,
    [property: JsonPropertyName("to_frame")] bool ToFrame
);

// `queued_job_ids` was REMOVED from /queue (backend security hardening): a
// job_id is the de-facto capability token for /download, /artifacts, /preview
// and /pay on an unauthenticated API, so listing live ids let anyone harvest
// other customers' artifacts. Only aggregate counts are exposed now — per-job
// state comes from /jobs/{id}.
public record QueueResponse(
    [property: JsonPropertyName("queued_jobs")] int QueuedJobs,
    [property: JsonPropertyName("max_queue_size")] int MaxQueueSize,
    [property: JsonPropertyName("active_jobs")] int ActiveJobs,
    [property: JsonPropertyName("max_workers")] int MaxWorkers,
    [property: JsonPropertyName("counts")] JobCounts Counts
);

// Backend deliberately omits complete/failed counts — terminal rows are evicted
// after JOB_TTL_SECONDS, so reporting them would be misleading. See Main.py
// /queue endpoint docstring.
public record JobCounts(
    [property: JsonPropertyName("queued")] int Queued,
    [property: JsonPropertyName("running")] int Running
);

public record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message
);
