using System.Text.Json.Serialization;

namespace LaigO.Tests.Models;

public class GenerateResponse
{
    [JsonPropertyName("job_id")]
    public string JobId { get; init; } = null!;

    [JsonPropertyName("status")]
    public string Status { get; init; } = null!;
}

public class JobStatusResponse
{
    // job_id is NOT returned by GET /jobs/{id} — use the id you already have from POST /generate.
    [JsonPropertyName("status")]
    public string Status { get; init; } = null!;

    [JsonPropertyName("progress")]
    public double Progress { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("traceback")]
    public string? Traceback { get; init; }

    [JsonPropertyName("created_at")]
    public double? CreatedAt { get; init; }

    [JsonPropertyName("queued_at")]
    public double? QueuedAt { get; init; }

    [JsonPropertyName("started_at")]
    public double? StartedAt { get; init; }

    [JsonPropertyName("finished_at")]
    public double? FinishedAt { get; init; }

    [JsonPropertyName("deadline")]
    public double? Deadline { get; init; }

    [JsonPropertyName("queue_position")]
    public int? QueuePosition { get; init; }

    [JsonPropertyName("queue_length")]
    public int? QueueLength { get; init; }

    [JsonPropertyName("settings")]
    public JobSettings? Settings { get; init; }
}

public class JobSettings
{
    [JsonPropertyName("mosaic_block_width")]
    public int MosaicBlockWidth { get; init; }

    [JsonPropertyName("mosaic_type")]
    public string MosaicType { get; init; } = null!;

    [JsonPropertyName("background_color_percent")]
    public double BackgroundColorPercent { get; init; }

    [JsonPropertyName("to_frame")]
    public bool ToFrame { get; init; }
}

public class QueueResponse
{
    [JsonPropertyName("queued_jobs")]
    public int QueuedJobs { get; init; }

    [JsonPropertyName("queued_job_ids")]
    public List<string> QueuedJobIds { get; init; } = [];

    [JsonPropertyName("max_queue_size")]
    public int MaxQueueSize { get; init; }

    [JsonPropertyName("active_jobs")]
    public int ActiveJobs { get; init; }

    [JsonPropertyName("max_workers")]
    public int MaxWorkers { get; init; }

    [JsonPropertyName("known_jobs")]
    public int KnownJobs { get; init; }

    [JsonPropertyName("counts")]
    public JobCounts Counts { get; init; } = null!;
}

public class JobCounts
{
    [JsonPropertyName("queued")]
    public int Queued { get; init; }

    [JsonPropertyName("running")]
    public int Running { get; init; }

    [JsonPropertyName("complete")]
    public int Complete { get; init; }

    [JsonPropertyName("failed")]
    public int Failed { get; init; }
}

public class HealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = null!;

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
