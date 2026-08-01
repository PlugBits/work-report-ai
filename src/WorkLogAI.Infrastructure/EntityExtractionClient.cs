using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Extracts named entities (customers, projects, part numbers, people, systems)
/// from a batch of work-memo/meeting-log fragments via the OpenAI Responses API.
/// Mirrors <see cref="MeetingFormatClient"/>: strict Structured Outputs, store:false,
/// no tools, Authorization from <see cref="ICredentialStore"/>, output_text
/// aggregated across items, and a bounded response read. Every failure mode
/// (refusal, incomplete, error status, malformed JSON, HTTP failure) returns a
/// generic Japanese message — never the request or response body, which could
/// contain the source texts.
/// </summary>
public sealed class EntityExtractionClient(
    HttpClient httpClient,
    ICredentialStore credentials,
    TimeSpan? timeout = null,
    Func<TimeSpan, CancellationToken, Task>? retryDelay = null) : IEntityExtractionClient
{
    public static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");

    private const string Instructions = """
        入力は業務メモ・議事録の断片です。固有名詞（顧客・案件・型番・人名・設備/システム名）を
        抽出し、既知辞書に同一実体があればその正規名へ寄せてください。
        一般名詞・日付・数量は除外してください。
        occurrences は入力テキスト群の中で言及された件数にしてください。
        """;

    public async Task<EntityExtractionResult> ExtractAsync(
        EntityExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Texts.Count > EntityExtractionPayloadBuilder.MaximumTexts)
        {
            return new EntityExtractionResult(
                null,
                $"一度に送信できるテキストは{EntityExtractionPayloadBuilder.MaximumTexts}件までです。");
        }

        var key = await credentials.GetAsync(CredentialTargets.OpenAiApiKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new EntityExtractionResult(null, "OpenAI APIキーが設定されていません。");
        }

        var payload = EntityExtractionPayloadBuilder.Build(request.KnownCanonicalNames, request.Texts);
        if (payload.Utf8ByteCount > EntityExtractionPayloadBuilder.MaximumUtf8Bytes)
        {
            return new EntityExtractionResult(
                null,
                "送信内容が大きすぎるため送信できません。対象テキストを減らしてください。");
        }

        var body = BuildRequest(request.Model, payload.Input);

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
            return new EntityExtractionResult(null, "OpenAIへの接続に失敗しました。");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new EntityExtractionResult(
                    null,
                    $"OpenAIへのリクエストが失敗しました（{(int)response.StatusCode}）。");
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
                return new EntityExtractionResult(null, "OpenAIの応答を安全に読み取れませんでした。");
            }

            return Parse(responseText);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        string key,
        JsonObject body,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        return await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);
    }

    internal static JsonObject BuildRequest(string model, string input) =>
        new()
        {
            ["model"] = model,
            ["store"] = false,
            ["instructions"] = Instructions,
            ["input"] = input,
            ["reasoning"] = new JsonObject { ["effort"] = "low" },
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "entity_extraction",
                    ["strict"] = true,
                    ["schema"] = EntityExtractionJsonSchema.Create()
                }
            }
        };

    private static EntityExtractionResult Parse(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error)
                && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return Failure("OpenAIがエラーを返しました。");
            }

            if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
            {
                var statusValue = status.GetString();
                if (statusValue == "incomplete")
                {
                    return Failure("OpenAIの応答が不完全でした。");
                }

                if (statusValue is "failed" or "cancelled")
                {
                    return Failure("OpenAIの応答が失敗しました。");
                }
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
                            return Failure("OpenAIがこのリクエストを拒否しました。");
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
                return Failure("OpenAIの応答に構造化出力が含まれていませんでした。");
            }

            EntityExtractionEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<EntityExtractionEnvelope>(
                    string.Concat(texts),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = false });
            }
            catch (JsonException)
            {
                return Failure("OpenAIが不正な構造化出力を返しました。");
            }

            return EntityExtractionValidator.Validate(envelope);
        }
        catch (JsonException)
        {
            return Failure("OpenAIが不正な構造化出力を返しました。");
        }
    }

    private static EntityExtractionResult Failure(string error) => new(null, error);

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

public static class EntityExtractionJsonSchema
{
    public static JsonObject Create() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("entities"),
            ["properties"] = new JsonObject
            {
                ["entities"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = Entity()
                }
            }
        };

    private static JsonObject Entity() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("canonical", "aliases", "kind", "occurrences"),
            ["properties"] = new JsonObject
            {
                ["canonical"] = new JsonObject { ["type"] = "string" },
                ["aliases"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                },
                ["kind"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(
                        EntityKinds.Customer,
                        EntityKinds.Project,
                        EntityKinds.Part,
                        EntityKinds.Person,
                        EntityKinds.System,
                        EntityKinds.Other)
                },
                ["occurrences"] = new JsonObject { ["type"] = "integer" }
            }
        };
}
