namespace LaigO.Tests.ApiClient;

/// <summary>
/// Raw /generate outcome including the Retry-After header. Retry-After is only
/// present on the per-IP rate-limit 429; the queue-full 429 carries none —
/// that header is what distinguishes the two.
/// </summary>
public sealed record GenerateRawResult(int Status, string Body, int? RetryAfterSeconds)
{
    public bool IsRateLimited => Status == 429 && RetryAfterSeconds is not null;
}

/// <summary>
/// Serializes every POST /generate in the run so the suite never trips the
/// backend's per-IP rate limit (one submission per 20s, Main.py:146/172-198).
/// The gate owns the whole wait → send → record → retry cycle under one mutex;
/// tests never Task.Delay to space submissions themselves.
/// </summary>
public static class GenerateRateLimitGate
{
    // Recorded at response time, which is strictly after the server stamps its
    // own cooldown (Main.py:198 runs mid-request), so our window is already a
    // superset of the server's; the margin covers clock-rate drift on top.
    private const int SafetyMarginMs = 2_000;
    private const int MaxAttempts = 3;

    // SemaphoreSlim, not lock: the critical section awaits (lock can't span
    // await), and this stays correct if [Parallelizable] ever appears.
    private static readonly SemaphoreSlim _mutex = new(1, 1);

    // Environment.TickCount64: monotonic, immune to NTP wall-clock jumps.
    // 0 = "no burn recorded"; TickCount64 >= 0 so the first wait is a no-op.
    private static long _nextAllowedAtMs;

    /// <summary>
    /// Sends via <paramref name="send"/> under the gate. With
    /// <paramref name="waitForSlot"/> the call first waits out the cooldown and
    /// retries (bounded) on a rate-limit 429; without it the request goes out
    /// immediately and a 429 is returned as-is — for pre-limiter validation
    /// probes and tests that deliberately observe the 429 contract.
    /// The lambda may be invoked more than once (retry), so it must build a
    /// fresh request body each time.
    /// </summary>
    public static async Task<GenerateRawResult> SendAsync(
        bool waitForSlot, Func<Task<GenerateRawResult>> send)
    {
        await _mutex.WaitAsync();
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                if (waitForSlot)
                {
                    var waitMs = _nextAllowedAtMs - Environment.TickCount64;
                    if (waitMs > 0)
                        await Task.Delay((int)waitMs);
                }

                var result = await send();

                if (result.IsRateLimited)
                {
                    // A rate-limit 429 never burns cooldown server-side (the
                    // backend records allowed requests only). If the caller
                    // wanted a slot, someone OUTSIDE this process (dev browser,
                    // aborted previous run) holds the per-IP cooldown — honor
                    // Retry-After and retry, bounded.
                    if (waitForSlot && attempt < MaxAttempts)
                    {
                        var retrySecs = result.RetryAfterSeconds
                            ?? TestConstants.GenerateRateLimitSeconds + 1;
                        await Task.Delay(retrySecs * 1000 + SafetyMarginMs);
                        continue;
                    }
                    return result;
                }

                // The backend stamps the cooldown for every request that PASSES
                // the limiter — including ones that later fail queue-full/413/
                // 400/500. Only the param-value 422s and the 503 shutdown check
                // run before it, so anything else means "cooldown burned now".
                if (result.Status is not (422 or 503))
                    _nextAllowedAtMs = Environment.TickCount64
                        + TestConstants.GenerateRateLimitSeconds * 1000
                        + SafetyMarginMs;
                return result;
            }
        }
        finally
        {
            _mutex.Release();
        }
    }
}
