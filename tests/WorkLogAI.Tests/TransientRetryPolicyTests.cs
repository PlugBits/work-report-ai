using System.Net;
using System.Net.Http.Headers;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class TransientRetryPolicyTests
{
    [Fact]
    public async Task Retries_after_429_then_succeeds()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        var result = await TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(attempts == 1
                    ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    : new HttpResponseMessage(HttpStatusCode.OK));
            },
            CancellationToken.None,
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, attempts);
        Assert.Equal([TransientRetryPolicy.DefaultDelays[0]], delays);
    }

    [Fact]
    public async Task Retries_twice_after_consecutive_500s_then_succeeds()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        var result = await TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(attempts <= 2
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : new HttpResponseMessage(HttpStatusCode.OK));
            },
            CancellationToken.None,
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(3, attempts);
        Assert.Equal(TransientRetryPolicy.DefaultDelays, delays);
    }

    [Fact]
    public async Task Non_retryable_401_returns_immediately_without_delay()
    {
        var attempts = 0;
        var delayed = false;

        var result = await TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            },
            CancellationToken.None,
            (_, _) => { delayed = true; return Task.CompletedTask; });

        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(1, attempts);
        Assert.False(delayed);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Other_non_retryable_statuses_also_return_immediately(HttpStatusCode statusCode)
    {
        var attempts = 0;

        var result = await TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(statusCode));
            },
            CancellationToken.None,
            (_, _) => throw new InvalidOperationException("must not delay"));

        Assert.Equal(statusCode, result.StatusCode);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Exhausted_retries_returns_the_final_transient_response()
    {
        var attempts = 0;
        var delays = new List<TimeSpan>();

        var result = await TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            },
            CancellationToken.None,
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Transport_exception_retries_then_succeeds()
    {
        var attempts = 0;

        var result = await TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new HttpRequestException("boom");
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            CancellationToken.None,
            (_, _) => Task.CompletedTask);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Transport_exception_exhausted_retries_rethrows()
    {
        var attempts = 0;

        await Assert.ThrowsAsync<HttpRequestException>(() => TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                throw new HttpRequestException("boom");
            },
            CancellationToken.None,
            (_, _) => Task.CompletedTask));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Retry_after_header_within_bounds_overrides_the_default_delay()
    {
        var attempts = 0;
        TimeSpan? usedDelay = null;

        await TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    return Task.FromResult(response);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            CancellationToken.None,
            (delay, _) => { usedDelay = delay; return Task.CompletedTask; });

        Assert.Equal(TimeSpan.FromSeconds(1), usedDelay);
    }

    [Fact]
    public async Task Retry_after_header_beyond_30_seconds_falls_back_to_the_default_delay()
    {
        var attempts = 0;
        TimeSpan? usedDelay = null;

        await TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
                    return Task.FromResult(response);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            },
            CancellationToken.None,
            (delay, _) => { usedDelay = delay; return Task.CompletedTask; });

        Assert.Equal(TransientRetryPolicy.DefaultDelays[0], usedDelay);
    }

    [Fact]
    public async Task Callers_own_cancellation_is_rethrown_without_retrying()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TransientRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
            cts.Token,
            (_, _) => throw new InvalidOperationException("must not delay")));

        Assert.Equal(1, attempts);
    }
}
