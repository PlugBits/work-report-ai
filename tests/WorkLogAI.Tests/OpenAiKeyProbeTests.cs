using System.Net;
using System.Net.Http.Headers;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class OpenAiKeyProbeTests
{
    [Fact]
    public async Task Successful_response_reports_ok_and_sends_only_the_bearer_key()
    {
        var handler = new StatusHandler(HttpStatusCode.OK);

        var status = await new OpenAiKeyProbe(handler).ProbeAsync("sk-test-secret");

        Assert.Equal(OpenAiKeyProbeStatus.Ok, status);
        Assert.Equal(OpenAiKeyProbe.Endpoint, handler.Uri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("sk-test-secret", handler.Authorization?.Parameter);
    }

    [Fact]
    public async Task Unauthorized_response_is_reported_distinctly()
    {
        var handler = new StatusHandler(HttpStatusCode.Unauthorized);

        var status = await new OpenAiKeyProbe(handler).ProbeAsync("sk-bad-key");

        Assert.Equal(OpenAiKeyProbeStatus.Unauthorized, status);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Other_non_success_statuses_are_reported_as_network_error(HttpStatusCode statusCode)
    {
        var handler = new StatusHandler(statusCode);

        var status = await new OpenAiKeyProbe(handler).ProbeAsync("sk-test-secret");

        Assert.Equal(OpenAiKeyProbeStatus.NetworkError, status);
    }

    [Fact]
    public async Task Transport_failure_is_reported_as_network_error()
    {
        var handler = new ThrowingHandler();

        var status = await new OpenAiKeyProbe(handler).ProbeAsync("sk-test-secret");

        Assert.Equal(OpenAiKeyProbeStatus.NetworkError, status);
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("no network");
    }
}
