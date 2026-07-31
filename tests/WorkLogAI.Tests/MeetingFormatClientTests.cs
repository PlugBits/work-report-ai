using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class MeetingFormatClientTests
{
    private const string Secret = "sk-test-never-in-body-123456";

    [Fact]
    public async Task Request_uses_responses_strict_schema_authorization_only_and_sanitized_input()
    {
        var session = Session();
        var lines = new[]
        {
            Line(1, MeetingMarker.Todo, "token=work-secret 資料を送る", 9, 0),
            Line(2, MeetingMarker.Decision, "path=C:\\Users\\person\\repo 予算を承認", 9, 5)
        };
        var handler = new CaptureHandler(SuccessResponse());
        var client = Client(handler);

        var result = await client.FormatAsync(new MeetingFormatRequest(session, lines, "gpt-5.6-sol"));

        Assert.True(result.Succeeded);
        Assert.Equal(MeetingFormatClient.Endpoint, handler.Uri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal(Secret, handler.Authorization?.Parameter);
        Assert.DoesNotContain(Secret, handler.Body);
        Assert.DoesNotContain("work-secret", handler.Body);
        Assert.DoesNotContain(@"C:\Users\person", handler.Body);
        Assert.DoesNotContain(session.Id.ToString(), handler.Body);

        using var request = JsonDocument.Parse(handler.Body);
        var root = request.RootElement;
        Assert.Equal("gpt-5.6-sol", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.TryGetProperty("tools", out _));
        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(5, schema.GetProperty("required").GetArrayLength());
        var actionItemSchema = schema.GetProperty("properties").GetProperty("action_items").GetProperty("items");
        Assert.False(actionItemSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(3, actionItemSchema.GetProperty("required").GetArrayLength());
    }

    [Fact]
    public async Task No_api_key_fails_safely_without_a_network_call()
    {
        var handler = new CaptureHandler(SuccessResponse());
        var client = new MeetingFormatClient(new HttpClient(handler), new FakeCredentialStore());

        var result = await client.FormatAsync(new MeetingFormatRequest(Session(), [], "gpt-5.6-sol"));

        Assert.False(result.Succeeded);
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task Oversized_payload_fails_safely_without_a_network_call()
    {
        var handler = new CaptureHandler(SuccessResponse());
        var client = Client(handler);
        // SafeTextSanitizer caps each individual line at 500 characters, so a
        // single huge line cannot exceed the 256 KiB cap — many long lines can.
        var manyLongLines = Enumerable.Range(1, 400)
            .Select(lineNo => Line(lineNo, MeetingMarker.None, new string('あ', 500), 9, 0))
            .ToArray();
        Assert.True(MeetingFormatPayloadBuilder.Build(Session(), manyLongLines).Utf8ByteCount
            > MeetingFormatPayloadBuilder.MaximumUtf8Bytes);

        var result = await client.FormatAsync(new MeetingFormatRequest(Session(), manyLongLines, "gpt-5.6-sol"));

        Assert.False(result.Succeeded);
        Assert.Null(handler.Uri);
    }

    [Theory]
    [InlineData("""{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[]}""")]
    [InlineData("""{"status":"failed","error":{"message":"PRIVATE_MEETING_CONTENT"},"output":[]}""")]
    [InlineData("""{"status":"completed","output":[{"content":[{"type":"refusal","refusal":"PRIVATE_MEETING_CONTENT"}]}]}""")]
    [InlineData("""{"status":"completed","output":[{"content":[{"type":"output_text","text":"not-json"}]}]}""")]
    [InlineData("""{"status":"completed","output":[]}""")]
    public async Task Refusal_incomplete_failed_and_malformed_outputs_fail_safely(string response)
    {
        var handler = new CaptureHandler(response);

        var result = await Client(handler).FormatAsync(
            new MeetingFormatRequest(Session(), [Line(1, MeetingMarker.None, "safe", 9, 0)], "gpt-5.6-sol"));

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("PRIVATE_MEETING_CONTENT", result.Error);
    }

    [Fact]
    public async Task Invalid_structured_output_from_the_model_fails_independent_validation()
    {
        var response = JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new[]
            {
                new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "output_text",
                            text = JsonSerializer.Serialize(new
                            {
                                summary_line = "",
                                overview = "overview",
                                decisions = Array.Empty<object>(),
                                action_items = Array.Empty<object>(),
                                topics = Array.Empty<object>()
                            })
                        }
                    }
                }
            }
        });

        var result = await Client(new CaptureHandler(response)).FormatAsync(
            new MeetingFormatRequest(Session(), [Line(1, MeetingMarker.None, "safe", 9, 0)], "gpt-5.6-sol"));

        Assert.False(result.Succeeded);
        Assert.Null(result.Formatted);
    }

    [Fact]
    public async Task Output_text_split_across_multiple_items_is_aggregated()
    {
        var full = JsonSerializer.Serialize(new
        {
            summary_line = "要約",
            overview = "overview",
            decisions = Array.Empty<object>(),
            action_items = Array.Empty<object>(),
            topics = Array.Empty<object>()
        });
        var response = JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new object[]
            {
                new { content = new[] { new { type = "output_text", text = full[..(full.Length / 2)] } } },
                new { content = new[] { new { type = "output_text", text = full[(full.Length / 2)..] } } }
            }
        });

        var result = await Client(new CaptureHandler(response)).FormatAsync(
            new MeetingFormatRequest(Session(), [Line(1, MeetingMarker.None, "safe", 9, 0)], "gpt-5.6-sol"));

        Assert.True(result.Succeeded);
        Assert.Equal("要約", result.Formatted!.SummaryLine);
    }

    private static MeetingFormatClient Client(CaptureHandler handler) =>
        new(new HttpClient(handler), new FakeCredentialStore(Secret));

    private static MeetingSession Session() => new(
        Guid.NewGuid(),
        "定例会議",
        "田中、鈴木",
        MeetingKind.Meeting,
        new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.FromHours(9)),
        null,
        MeetingStatus.Closed,
        new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.FromHours(9)));

    private static MeetingLine Line(int lineNo, MeetingMarker marker, string text, int hour, int minute) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        lineNo,
        marker,
        text,
        new DateTimeOffset(2026, 7, 31, hour, minute, 0, TimeSpan.FromHours(9)));

    private static string SuccessResponse() => JsonSerializer.Serialize(new
    {
        status = "completed",
        output = new[]
        {
            new
            {
                type = "message",
                content = new[]
                {
                    new
                    {
                        type = "output_text",
                        text = JsonSerializer.Serialize(new
                        {
                            summary_line = "定例会議で予算を承認し資料送付を宿題とした。",
                            overview = "overview",
                            decisions = new[] { new { text = "予算を承認" } },
                            action_items = new[] { new { text = "資料を送る", owner = (string?)null, due = (string?)null } },
                            topics = Array.Empty<object>()
                        })
                    }
                }
            }
        }
    });

    private sealed class CaptureHandler(string response) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeCredentialStore(string? value = null) : ICredentialStore
    {
        private string? _value = value;

        public Task<string?> GetAsync(string target, CancellationToken cancellationToken = default) =>
            Task.FromResult(_value);

        public Task SetAsync(string target, string secret, CancellationToken cancellationToken = default)
        {
            _value = secret;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string target, CancellationToken cancellationToken = default)
        {
            var existed = _value is not null;
            _value = null;
            return Task.FromResult(existed);
        }
    }
}
