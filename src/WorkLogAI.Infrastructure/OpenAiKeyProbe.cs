using System.Net;
using System.Net.Http.Headers;

namespace WorkLogAI.Infrastructure;

public enum OpenAiKeyProbeStatus
{
    Ok,
    Unauthorized,
    NetworkError
}

/// <summary>
/// Lightweight liveness check for an OpenAI API key: GETs /v1/models with the key as a
/// bearer token and reports only ok/unauthorized/network-error — never the key, and
/// never any response body (which the settings UI must not display or log).
/// </summary>
public sealed class OpenAiKeyProbe(HttpMessageHandler? handler = null)
{
    public static readonly Uri Endpoint = new("https://api.openai.com/v1/models");

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public async Task<OpenAiKeyProbeStatus> ProbeAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        using var httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        using var message = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(DefaultTimeout);
        try
        {
            using var response = await httpClient.SendAsync(message, timeoutSource.Token);
            if (response.IsSuccessStatusCode)
            {
                return OpenAiKeyProbeStatus.Ok;
            }
            return response.StatusCode == HttpStatusCode.Unauthorized
                ? OpenAiKeyProbeStatus.Unauthorized
                : OpenAiKeyProbeStatus.NetworkError;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return OpenAiKeyProbeStatus.NetworkError;
        }
    }
}
