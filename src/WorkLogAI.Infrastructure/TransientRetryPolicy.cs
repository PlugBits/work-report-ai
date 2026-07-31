using System.Net;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Shared bounded retry policy for OpenAI HTTP calls (weekly generation and meeting
/// formatting). Retries a transient failure — 429, any 5xx, or a transport-level
/// exception/timeout — up to <see cref="DefaultDelays"/>.Count more times, honoring a
/// short Retry-After header when the server sends one. Non-retryable statuses
/// (400/401/403/404, or any other non-transient status) are returned immediately on the
/// first attempt, unchanged from pre-retry behavior. The delay function is injectable so
/// tests never actually wait.
/// </summary>
public static class TransientRetryPolicy
{
    public static readonly IReadOnlyList<TimeSpan> DefaultDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(30);

    /// <param name="sendAttempt">
    /// Builds and sends one HTTP attempt. Must construct a fresh
    /// <see cref="HttpRequestMessage"/> per call — a message cannot be sent twice.
    /// </param>
    /// <param name="cancellationToken">
    /// The caller's outer token. Propagated to <paramref name="sendAttempt"/> and used
    /// for the inter-attempt delay; a cancellation caused by this token is always
    /// rethrown rather than retried.
    /// </param>
    /// <param name="delayAsync">Injectable delay, defaulting to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    /// <param name="delays">Injectable per-attempt fallback delays, defaulting to <see cref="DefaultDelays"/>.</param>
    public static async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAttempt,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        IReadOnlyList<TimeSpan>? delays = null)
    {
        ArgumentNullException.ThrowIfNull(sendAttempt);
        delayAsync ??= (delay, token) => Task.Delay(delay, token);
        delays ??= DefaultDelays;
        var maximumAttempts = delays.Count + 1;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            var isLastAttempt = attempt == maximumAttempts - 1;
            HttpResponseMessage response;
            try
            {
                response = await sendAttempt(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException) when (!isLastAttempt)
            {
                await delayAsync(delays[attempt], cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException) when (!isLastAttempt)
            {
                // A per-attempt timeout (not the caller's own cancellation, handled above).
                await delayAsync(delays[attempt], cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!isLastAttempt && IsTransient(response.StatusCode))
            {
                var wait = RetryAfterOrDefault(response, delays[attempt]);
                response.Dispose();
                await delayAsync(wait, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return response;
        }

        throw new InvalidOperationException("Retry loop exited without a response.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan RetryAfterOrDefault(HttpResponseMessage response, TimeSpan fallback)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return fallback;
        }
        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero && delta <= MaximumRetryAfter)
        {
            return delta;
        }
        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero && wait <= MaximumRetryAfter)
            {
                return wait;
            }
        }
        return fallback;
    }
}
