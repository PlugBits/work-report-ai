using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Formats a meeting's approved lines into <see cref="MeetingFormattedResult"/> via
/// the OpenAI Responses API. Mirrors <see cref="OpenAiResponsesClient"/>: strict
/// Structured Outputs, store:false, no tools, Authorization from
/// <see cref="ICredentialStore"/>, output_text aggregated across items, and a bounded
/// response read. Every failure mode (refusal, incomplete, error status, malformed
/// JSON, HTTP failure) returns a generic Japanese message — never the request or
/// response body, which could contain the meeting's own content.
/// </summary>
public sealed class MeetingFormatClient(
    HttpClient httpClient,
    ICredentialStore credentials,
    TimeSpan? timeout = null) : IMeetingFormatClient
{
    public static readonly Uri Endpoint = new("https://api.openai.com/v1/responses");

    private const string Instructions = """
        入力は会議の生ログです。各行は "HH:mm [宿題] text" または "HH:mm [決定] text"、
        あるいはマーカーなしの "HH:mm text" の形式です。
        「宿題」マーカーが付いた行の内容は必ず action_items に含めてください。
        「決定」マーカーが付いた行の内容は必ず decisions に含めてください。
        summary_line は週次業務報告で使う、日本語で約50文字の1行要約にしてください。
        ログに書かれていない事実・決定・完了は絶対に創作しないでください。
        due（期限）はログに明示的な日付が書かれている場合のみ設定し、YYYY-MM-DD形式にしてください。
        明示的な日付がなければ due は null にしてください。
        """;

    public async Task<MeetingFormatResult> FormatAsync(
        MeetingFormatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = await credentials.GetAsync(CredentialTargets.OpenAiApiKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new MeetingFormatResult(null, "OpenAI APIキーが設定されていません。");
        }

        var payload = MeetingFormatPayloadBuilder.Build(request.Session, request.Lines);
        if (payload.Utf8ByteCount > MeetingFormatPayloadBuilder.MaximumUtf8Bytes)
        {
            return new MeetingFormatResult(
                null,
                "議事録の内容が大きすぎるため送信できません。対象行を減らしてください。");
        }

        var body = BuildRequest(request.Model, payload.Input);
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new MeetingFormatResult(null, "OpenAIへの接続に失敗しました。");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new MeetingFormatResult(
                    null,
                    $"OpenAIへのリクエストが失敗しました（{(int)response.StatusCode}）。");
            }

            string responseText;
            try
            {
                responseText = await ReadBoundedAsync(response.Content, timeoutSource.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new MeetingFormatResult(null, "OpenAIの応答を安全に読み取れませんでした。");
            }

            return Parse(responseText);
        }
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
                    ["name"] = "meeting_minutes",
                    ["strict"] = true,
                    ["schema"] = MeetingFormatJsonSchema.Create()
                }
            }
        };

    private static MeetingFormatResult Parse(string responseText)
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

            MeetingFormatEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<MeetingFormatEnvelope>(
                    string.Concat(texts),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = false });
            }
            catch (JsonException)
            {
                return Failure("OpenAIが不正な構造化出力を返しました。");
            }

            return MeetingFormatValidator.Validate(envelope);
        }
        catch (JsonException)
        {
            return Failure("OpenAIが不正な構造化出力を返しました。");
        }
    }

    private static MeetingFormatResult Failure(string error) => new(null, error);

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

public static class MeetingFormatJsonSchema
{
    public static JsonObject Create() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("summary_line", "overview", "decisions", "action_items", "topics"),
            ["properties"] = new JsonObject
            {
                ["summary_line"] = new JsonObject { ["type"] = "string" },
                ["overview"] = new JsonObject { ["type"] = "string" },
                ["decisions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = Decision()
                },
                ["action_items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = ActionItem()
                },
                ["topics"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = Topic()
                }
            }
        };

    private static JsonObject Decision() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("text"),
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string" }
            }
        };

    private static JsonObject ActionItem() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("text", "owner", "due"),
            ["properties"] = new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string" },
                ["owner"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["due"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
            }
        };

    private static JsonObject Topic() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("title", "detail"),
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "string" },
                ["detail"] = new JsonObject { ["type"] = "string" }
            }
        };
}
