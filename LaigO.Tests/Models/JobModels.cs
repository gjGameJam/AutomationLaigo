using System.Text.Json.Serialization;

namespace LaigO.Tests.Models;

public record GenerateResponse(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("status")] string Status
);

public record JobStatusResponse(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("traceback")] string? Traceback,
    [property: JsonPropertyName("created_at")] double? CreatedAt,
    [property: JsonPropertyName("queued_at")] double? QueuedAt,
    [property: JsonPropertyName("finished_at")] double? FinishedAt,
    [property: JsonPropertyName("queue_position")] int? QueuePosition,
    [property: JsonPropertyName("queue_size")] int? QueueSize,
    [property: JsonPropertyName("settings")] JobSettings? Settings
);

public record JobSettings(
    [property: JsonPropertyName("mosaic_block_width")] int MosaicBlockWidth,
    [property: JsonPropertyName("mosaic_type")] string MosaicType,
    [property: JsonPropertyName("background_color_percent")] double BackgroundColorPercent,
    [property: JsonPropertyName("to_frame")] bool ToFrame
);

public record QueueResponse(
    [property: JsonPropertyName("queued_jobs")] int QueuedJobs,
    [property: JsonPropertyName("queued_job_ids")] List<string> QueuedJobIds,
    [property: JsonPropertyName("max_queue_size")] int MaxQueueSize,
    [property: JsonPropertyName("active_jobs")] int ActiveJobs,
    [property: JsonPropertyName("max_workers")] int MaxWorkers,
    [property: JsonPropertyName("known_jobs")] int KnownJobs,
    [property: JsonPropertyName("counts")] JobCounts Counts
);

public record JobCounts(
    [property: JsonPropertyName("queued")] int Queued,
    [property: JsonPropertyName("running")] int Running,
    [property: JsonPropertyName("complete")] int Complete,
    [property: JsonPropertyName("failed")] int Failed
);

public record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message
);
