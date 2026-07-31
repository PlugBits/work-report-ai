using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class OpenAiResponsesClientTests
{
    private const string Secret = "sk-test-never-in-body-123456";

    [Fact]
    public async Task Request_uses_responses_strict_schema_authorization_only_and_bounded_redacted_input()
    {
        var source = Event(
            "token=work-secret C:\\Users\\person\\repo\\file.cs",
            "path=C:\\Users\\person\\repo; API_KEY=another-secret");
        var responsePayload = CandidateJson(source.Id);
        var handler = new CaptureHandler(JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new object[]
            {
                new { type = "reasoning", content = Array.Empty<object>() },
                new
                {
                    type = "message",
                    content = new[] { new { type = "output_text", text = responsePayload } }
                }
            }
        }));
        var client = Client(handler);
        var range = new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));

        var result = await client.GenerateAsync(
            new AiGenerationRequest(range, "gpt-5.6-sol", [source]));

        Assert.True(result.Succeeded);
        Assert.Equal(source.Id, Assert.Single(Assert.Single(result.Candidates).SourceEventIds));
        Assert.Equal(OpenAiResponsesClient.Endpoint, handler.Uri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal(Secret, handler.Authorization?.Parameter);
        Assert.DoesNotContain(Secret, handler.Body);
        Assert.DoesNotContain("work-secret", handler.Body);
        Assert.DoesNotContain("another-secret", handler.Body);
        Assert.DoesNotContain(@"C:\\Users\\person\\repo", handler.Body);
        Assert.DoesNotContain("source-ref-never-sent", handler.Body);

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
        var candidateSchema = schema.GetProperty("properties")
            .GetProperty("candidates").GetProperty("items");
        Assert.False(candidateSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(9, candidateSchema.GetProperty("required").GetArrayLength());
    }

    [Theory]
    [InlineData("""{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[]}""")]
    [InlineData("""{"status":"failed","error":{"message":"PRIVATE_EVIDENCE"},"output":[]}""")]
    [InlineData("""{"status":"completed","output":[{"content":[{"type":"refusal","refusal":"PRIVATE_EVIDENCE"}]}]}""")]
    [InlineData("""{"status":"completed","output":[{"content":[{"type":"output_text","text":"not-json"}]}]}""")]
    [InlineData("""{"status":"completed","output":[]}""")]
    public async Task Refusal_incomplete_failed_malformed_and_empty_outputs_fail_safely(string response)
    {
        var source = Event("safe", "safe");
        var handler = new CaptureHandler(response);

        var result = await Client(handler).GenerateAsync(new AiGenerationRequest(
            new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)),
            "gpt-5.6-sol",
            [source]));

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("PRIVATE_EVIDENCE", result.Error);
    }

    [Fact]
    public async Task Unknown_evidence_from_response_is_rejected()
    {
        var source = Event("safe", "safe");
        var response = JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new[]
            {
                new
                {
                    content = new[]
                    {
                        new { type = "output_text", text = CandidateJson(Guid.NewGuid()) }
                    }
                }
            }
        });

        var result = await Client(new CaptureHandler(response)).GenerateAsync(
            new AiGenerationRequest(
                new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)),
                "gpt-5.6-sol",
                [source]));

        Assert.False(result.Succeeded);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task Transient_429_is_retried_and_then_succeeds()
    {
        var source = Event("safe", "safe");
        var handler = new SequenceHandler(
            [HttpStatusCode.TooManyRequests, HttpStatusCode.OK],
            EnvelopeResponse(CandidateJson(source.Id)));
        var delays = new List<TimeSpan>();
        var client = new OpenAiResponsesClient(
            new HttpClient(handler),
            new FakeCredentialStore(Secret),
            new AiPromptBuilder(maximumEvents: 10, maximumUtf8Bytes: 32 * 1024),
            new AiCandidateValidator(),
            retryDelay: (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        var result = await client.GenerateAsync(new AiGenerationRequest(
            new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)), "gpt-5.6-sol", [source]));

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.Attempts);
        Assert.Single(delays);
    }

    [Fact]
    public async Task Unauthorized_401_fails_immediately_without_retry()
    {
        var source = Event("safe", "safe");
        var handler = new SequenceHandler([HttpStatusCode.Unauthorized], "");
        var client = new OpenAiResponsesClient(
            new HttpClient(handler),
            new FakeCredentialStore(Secret),
            new AiPromptBuilder(maximumEvents: 10, maximumUtf8Bytes: 32 * 1024),
            new AiCandidateValidator(),
            retryDelay: (_, _) => throw new InvalidOperationException("must not delay"));

        var result = await client.GenerateAsync(new AiGenerationRequest(
            new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)), "gpt-5.6-sol", [source]));

        Assert.False(result.Succeeded);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task Fake_credential_store_behaves_without_persisting_secret_in_settings()
    {
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(CredentialTargets.OpenAiApiKey, Secret);
        Assert.Equal(Secret, await credentials.GetAsync(CredentialTargets.OpenAiApiKey));
        Assert.True(await credentials.DeleteAsync(CredentialTargets.OpenAiApiKey));
        Assert.Null(await credentials.GetAsync(CredentialTargets.OpenAiApiKey));
        Assert.IsAssignableFrom<ICredentialStore>(new WindowsCredentialStore());
    }

    [Fact]
    public void Prompt_is_deterministically_event_and_byte_bounded()
    {
        var events = Enumerable.Range(0, 5)
            .Select(index => SourceEventFactory.Create(
                new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero).AddDays(index),
                SourceTypes.Manual,
                $"event {index}",
                new string('x', 2_000),
                "safe",
                $"ref:{index}",
                1))
            .ToArray();
        var prompt = new AiPromptBuilder(maximumEvents: 2, maximumUtf8Bytes: 8 * 1024)
            .Build(new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)), events);

        Assert.True(prompt.Truncated);
        Assert.Equal(2, prompt.IncludedEventIds.Count);
        Assert.Equal(events.Take(2).Select(item => item.Id), prompt.IncludedEventIds);
        Assert.InRange(Encoding.UTF8.GetByteCount(prompt.Input), 1, 8 * 1024);
    }

    [Fact]
    public void Instructions_require_full_manual_memo_coverage_rewriting_and_confirmation()
    {
        Assert.Contains("1件も漏らさず", AiPromptBuilder.Instructions);
        Assert.Contains("書き直す", AiPromptBuilder.Instructions);
        Assert.Contains("needsConfirmation", AiPromptBuilder.Instructions);
    }

    [Fact]
    public void Prompt_omits_the_git_file_list_but_keeps_subject_and_statistics()
    {
        var source = SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(-4)),
            SourceTypes.Git,
            "feat: 検査結果の入力を追加",
            "検査結果入力画面を実装。変更ファイル: src/Inspection.cs, src/Inspection.Tests.cs。統計: +40 / -5",
            "repository=C:\\work\\repo; commit=abc123; files=src/Inspection.cs,src/Inspection.Tests.cs",
            "git:repo:abc123",
            .9);
        var prompt = new AiPromptBuilder(maximumEvents: 10, maximumUtf8Bytes: 32 * 1024)
            .Build(new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)), [source]);

        using var document = JsonDocument.Parse(prompt.Input);
        var body = document.RootElement.GetProperty("events")[0].GetProperty("body").GetString();
        var title = document.RootElement.GetProperty("events")[0].GetProperty("title").GetString();

        Assert.DoesNotContain("変更ファイル", body);
        Assert.DoesNotContain("Inspection.cs", body);
        Assert.Contains("検査結果入力画面を実装", body);
        Assert.Contains("統計: +40 / -5", body);
        Assert.Equal("feat: 検査結果の入力を追加", title);
    }

    [Fact]
    public void Prompt_includes_only_the_latest_event_per_stable_source_ref()
    {
        var occurredAt = new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(-4));
        var stale = SourceEventFactory.Create(
            occurredAt, SourceTypes.Meeting, "meeting v1", "stale summary", "evidence",
            "meeting:stable-ref", .9, occurredAt);
        var fresh = SourceEventFactory.Create(
            occurredAt, SourceTypes.Meeting, "meeting v2", "fresh summary", "evidence",
            "meeting:stable-ref", .9, occurredAt.AddHours(2));

        var prompt = new AiPromptBuilder(maximumEvents: 10, maximumUtf8Bytes: 32 * 1024)
            .Build(new WeekRange(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2)), [stale, fresh]);

        Assert.Equal([fresh.Id], prompt.IncludedEventIds);
        Assert.Contains("fresh summary", prompt.Input);
        Assert.DoesNotContain("stale summary", prompt.Input);
    }

    [Fact]
    public async Task Secret_credential_is_absent_from_sqlite_settings_file()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "no-secret.db");
        var factory = new SqliteConnectionFactory(new FixedDatabasePathProvider(path));
        await new SqliteDatabaseInitializer(factory).InitializeAsync();
        var settings = new AppSettingsService(new SqliteSettingsStore(factory));
        await settings.SaveAsync(new AppSettingsSnapshot(
            "サンプル株式会社", "山田 太郎", DayOfWeek.Monday, temporary.Path,
            OpenAiModel: "gpt-5.6-sol", SendPreviewEnabled: true));
        var credentials = new FakeCredentialStore();
        await credentials.SetAsync(CredentialTargets.OpenAiApiKey, Secret);

        var databaseText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));

        Assert.DoesNotContain(Secret, databaseText);
        Assert.Equal(Secret, await credentials.GetAsync(CredentialTargets.OpenAiApiKey));
    }

    private static OpenAiResponsesClient Client(CaptureHandler handler) =>
        new(
            new HttpClient(handler),
            new FakeCredentialStore(Secret),
            new AiPromptBuilder(maximumEvents: 10, maximumUtf8Bytes: 32 * 1024),
            new AiCandidateValidator());

    private static SourceEvent Event(string body, string evidence) =>
        SourceEventFactory.Create(
            new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(-4)),
            SourceTypes.Git,
            "safe title",
            body,
            evidence,
            "source-ref-never-sent",
            .9);

    private static string EnvelopeResponse(string candidateJson) =>
        JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new object[]
            {
                new
                {
                    type = "message",
                    content = new[] { new { type = "output_text", text = candidateJson } }
                }
            }
        });

    private static string CandidateJson(Guid evidence) =>
        JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    date = "2026-07-30",
                    workItem = "システム開発",
                    activity = "安全な候補生成を実装。",
                    resultOrNext = "",
                    status = "pending",
                    confidence = .9,
                    sourceEventIds = new[] { evidence.ToString() },
                    needsConfirmation = false,
                    confirmationQuestion = (string?)null
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

    private sealed class SequenceHandler(IReadOnlyList<HttpStatusCode> statusCodes, string successBody)
        : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var statusCode = statusCodes[Math.Min(Attempts, statusCodes.Count - 1)];
            Attempts++;
            var response = new HttpResponseMessage(statusCode);
            if (statusCode == HttpStatusCode.OK)
            {
                response.Content = new StringContent(successBody, Encoding.UTF8, "application/json");
            }
            return Task.FromResult(response);
        }
    }

    private sealed class FakeCredentialStore(string? value = null) : ICredentialStore
    {
        private string? _value = value;

        public Task<string?> GetAsync(string target, CancellationToken cancellationToken = default) =>
            Task.FromResult(_value);

        public Task SetAsync(
            string target,
            string secret,
            CancellationToken cancellationToken = default)
        {
            _value = secret;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(
            string target,
            CancellationToken cancellationToken = default)
        {
            var existed = _value is not null;
            _value = null;
            return Task.FromResult(existed);
        }
    }
}
