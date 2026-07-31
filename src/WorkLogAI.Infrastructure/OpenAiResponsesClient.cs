using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed class OpenAiResponsesClient(
    HttpClient httpClient,
    ICredentialStore credentials,
    AiPromptBuilder promptBuilder,
    AiCandidateValidator validator,
    TimeSpan? timeout = null,
    Func<TimeSpan, CancellationToken, Task>? retryDelay = null) : IAiCandidateGenerator
{
    public static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");

    public async Task<AiGenerationResult> GenerateAsync(
        AiGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var key = await credentials.GetAsync(CredentialTargets.OpenAiApiKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new AiGenerationResult([], false, 0, "OpenAI APIキーが設定されていません。");
        }

        var prompt = promptBuilder.Build(request.Range, request.SourceEvents);
        var body = BuildRequest(request.Model, prompt);

        HttpResponseMessage response;
        try
        {
            response = await TransientRetryPolicy.ExecuteAsync(
                ct => SendAsync(key, body, ct),
                cancellationToken,
                retryDelay);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new AiGenerationResult(
                [], prompt.Truncated, prompt.IncludedEventIds.Count,
                "OpenAIへの接続に失敗しました。");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new AiGenerationResult(
                    [], prompt.Truncated, prompt.IncludedEventIds.Count,
                    $"OpenAI request failed ({(int)response.StatusCode}).");
            }

            string responseText;
            using var readTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
            try
            {
                responseText = await ReadBoundedAsync(response.Content, readTimeoutSource.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new AiGenerationResult(
                    [], prompt.Truncated, prompt.IncludedEventIds.Count,
                    "OpenAI response could not be read safely.");
            }
            return Parse(responseText, request, prompt);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        string key,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(
            body.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        return await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);
    }

    internal static JsonObject BuildRequest(string model, AiPromptPackage prompt) =>
        new()
        {
            ["model"] = model,
            ["store"] = false,
            ["instructions"] = prompt.Instructions,
            ["input"] = prompt.Input,
            ["reasoning"] = new JsonObject { ["effort"] = "low" },
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "weekly_report_candidates",
                    ["strict"] = true,
                    ["schema"] = WeeklyCandidateJsonSchema.Create()
                }
            }
        };

    private AiGenerationResult Parse(
        string responseText,
        AiGenerationRequest request,
        AiPromptPackage prompt)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error)
                && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return Failure("OpenAI returned an error.", prompt);
            }
            if (root.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && status.GetString() == "incomplete")
            {
                return Failure("OpenAI response was incomplete.", prompt);
            }
            if (status.ValueKind == JsonValueKind.String
                && status.GetString() is "failed" or "cancelled")
            {
                return Failure("OpenAI response failed.", prompt);
            }

            var texts = new List<string>();
            if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content)
                        || content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }
                    foreach (var part in content.EnumerateArray())
                    {
                        var type = part.TryGetProperty("type", out var typeElement)
                            ? typeElement.GetString()
                            : null;
                        if (type == "refusal")
                        {
                            return Failure("OpenAI declined this request.", prompt);
                        }
                        if (type == "output_text"
                            && part.TryGetProperty("text", out var text)
                            && text.ValueKind == JsonValueKind.String)
                        {
                            texts.Add(text.GetString()!);
                        }
                    }
                }
            }
            if (texts.Count == 0)
            {
                return Failure("OpenAI response contained no structured output.", prompt);
            }

            var envelope = JsonSerializer.Deserialize<AiCandidateEnvelope>(
                string.Concat(texts),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = false });
            if (envelope is null)
            {
                return Failure("OpenAI structured output was empty.", prompt);
            }
            var validation = validator.Validate(envelope, request.Range, prompt.IncludedEventIds);
            if (validation.Accepted.Count == 0 && envelope.Candidates.Count > 0)
            {
                return Failure("OpenAI candidates failed independent validation.", prompt);
            }
            return new AiGenerationResult(
                validation.Accepted,
                prompt.Truncated,
                prompt.IncludedEventIds.Count,
                null);
        }
        catch (JsonException)
        {
            return Failure("OpenAI returned malformed structured output.", prompt);
        }
    }

    private static AiGenerationResult Failure(string error, AiPromptPackage prompt) =>
        new([], prompt.Truncated, prompt.IncludedEventIds.Count, error);

    private static async Task<string> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var builder = new StringBuilder();
        var buffer = new char[8 * 1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (builder.Length + read > 1024 * 1024)
            {
                throw new InvalidDataException("Response too large.");
            }
            builder.Append(buffer, 0, read);
        }
        return builder.ToString();
    }
}
