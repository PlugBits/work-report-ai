using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class EntityExtractionClientTests
{
    private const string Secret = "sk-test-never-in-body-123456";

    [Fact]
    public async Task Request_uses_responses_strict_schema_authorization_only_and_sanitized_input()
    {
        var handler = new CaptureHandler(SuccessResponse());
        var client = Client(handler);
        var request = new EntityExtractionRequest(
            ["既知顧客A"],
            ["token=work-secret ACME Corpに連絡", @"path=C:\Users\person\repo K3071を交換"],
            "gpt-5.6-sol");

        var result = await client.ExtractAsync(request);

        Assert.True(result.Succeeded);
        Assert.Equal(EntityExtractionClient.Endpoint, handler.Uri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal(Secret, handler.Authorization?.Parameter);
        Assert.DoesNotContain(Secret, handler.Body);
        Assert.DoesNotContain("work-secret", handler.Body);
        Assert.DoesNotContain(@"C:\Users\person", handler.Body);

        using var document = JsonDocument.Parse(handler.Body);
        var root = document.RootElement;
        Assert.Equal("gpt-5.6-sol", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.TryGetProperty("tools", out _));
        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var itemSchema = schema.GetProperty("properties").GetProperty("entities").GetProperty("items");
        Assert.False(itemSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(4, itemSchema.GetProperty("required").GetArrayLength());
    }

    [Fact]
    public async Task No_api_key_fails_safely_without_a_network_call()
    {
        var handler = new CaptureHandler(SuccessResponse());
        var client = new EntityExtractionClient(new HttpClient(handler), new FakeCredentialStore());

        var result = await client.ExtractAsync(new EntityExtractionRequest([], ["text"], "gpt-5.6-sol"));

        Assert.False(result.Succeeded);
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task Oversized_text_count_fails_safely_without_a_network_call()
    {
        var handler = new CaptureHandler(SuccessResponse());
        var client = Client(handler);
        var texts = Enumerable.Range(1, 101).Select(i => $"text {i}").ToArray();

        var result = await client.ExtractAsync(new EntityExtractionRequest([], texts, "gpt-5.6-sol"));

        Assert.False(result.Succeeded);
        Assert.Null(handler.Uri);
    }

    [Fact]
    public async Task Oversized_payload_bytes_fails_safely_without_a_network_call()
    {
        var handler = new CaptureHandler(SuccessResponse());
        var client = Client(handler);
        // SafeTextSanitizer caps each individual line at 500 characters, so a
        // single huge text cannot exceed the 64 KiB cap on its own — many
        // maximal-length texts (under the 100-text count cap) can.
        var texts = Enumerable.Range(1, 50).Select(_ => new string('あ', 500)).ToArray();
        Assert.True(EntityExtractionPayloadBuilder.Build([], texts).Utf8ByteCount
            > EntityExtractionPayloadBuilder.MaximumUtf8Bytes);

        var result = await client.ExtractAsync(new EntityExtractionRequest([], texts, "gpt-5.6-sol"));

        Assert.False(result.Succeeded);
        Assert.Null(handler.Uri);
    }

    [Theory]
    [InlineData("""{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"},"output":[]}""")]
    [InlineData("""{"status":"failed","error":{"message":"PRIVATE_MEMO_CONTENT"},"output":[]}""")]
    [InlineData("""{"status":"completed","output":[{"content":[{"type":"refusal","refusal":"PRIVATE_MEMO_CONTENT"}]}]}""")]
    [InlineData("""{"status":"completed","output":[{"content":[{"type":"output_text","text":"not-json"}]}]}""")]
    [InlineData("""{"status":"completed","output":[]}""")]
    public async Task Refusal_incomplete_failed_and_malformed_outputs_fail_safely(string response)
    {
        var handler = new CaptureHandler(response);

        var result = await Client(handler).ExtractAsync(
            new EntityExtractionRequest([], ["safe text"], "gpt-5.6-sol"));

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("PRIVATE_MEMO_CONTENT", result.Error);
    }

    [Fact]
    public async Task Unknown_kind_falls_back_to_other_and_nonpositive_occurrences_are_coerced_to_one()
    {
        var response = SuccessResponseWithEntities(new[]
        {
            new { canonical = "謎の設備", aliases = Array.Empty<string>(), kind = "spaceship", occurrences = 0 }
        });

        var result = await Client(new CaptureHandler(response)).ExtractAsync(
            new EntityExtractionRequest([], ["safe text"], "gpt-5.6-sol"));

        Assert.True(result.Succeeded);
        var observation = Assert.Single(result.Observations!);
        Assert.Equal(EntityKinds.Other, observation.Kind);
        Assert.Equal(1, observation.Occurrences);
    }

    [Fact]
    public async Task Blank_or_overlong_canonical_is_dropped_but_other_entities_in_the_batch_survive()
    {
        var response = SuccessResponseWithEntities(new object[]
        {
            new { canonical = "", aliases = Array.Empty<string>(), kind = "other", occurrences = 1 },
            new { canonical = new string('あ', 101), aliases = Array.Empty<string>(), kind = "other", occurrences = 1 },
            new { canonical = "有効な顧客", aliases = Array.Empty<string>(), kind = "customer", occurrences = 2 }
        });

        var result = await Client(new CaptureHandler(response)).ExtractAsync(
            new EntityExtractionRequest([], ["safe text"], "gpt-5.6-sol"));

        Assert.True(result.Succeeded);
        var observation = Assert.Single(result.Observations!);
        Assert.Equal("有効な顧客", observation.CanonicalName);
    }

    [Fact]
    public async Task Duplicate_canonical_names_are_deduped_case_insensitively_keeping_the_first()
    {
        var response = SuccessResponseWithEntities(new[]
        {
            new { canonical = "ACME", aliases = Array.Empty<string>(), kind = "customer", occurrences = 2 },
            new { canonical = "acme", aliases = Array.Empty<string>(), kind = "other", occurrences = 9 }
        });

        var result = await Client(new CaptureHandler(response)).ExtractAsync(
            new EntityExtractionRequest([], ["safe text"], "gpt-5.6-sol"));

        Assert.True(result.Succeeded);
        var observation = Assert.Single(result.Observations!);
        Assert.Equal("ACME", observation.CanonicalName);
        Assert.Equal(2, observation.Occurrences);
    }

    [Fact]
    public async Task Alias_equal_to_canonical_is_dropped_from_the_alias_list()
    {
        var response = SuccessResponseWithEntities(new[]
        {
            new { canonical = "ACME Corp", aliases = new[] { "ACME Corp", "ACME" }, kind = "customer", occurrences = 1 }
        });

        var result = await Client(new CaptureHandler(response)).ExtractAsync(
            new EntityExtractionRequest([], ["safe text"], "gpt-5.6-sol"));

        Assert.True(result.Succeeded);
        var observation = Assert.Single(result.Observations!);
        Assert.Equal(["ACME"], observation.Aliases);
    }

    [Fact]
    public async Task Transient_500_is_retried_twice_and_then_succeeds()
    {
        var handler = new SequenceHandler(
            [HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK],
            SuccessResponse());
        var delays = new List<TimeSpan>();
        var client = new EntityExtractionClient(
            new HttpClient(handler),
            new FakeCredentialStore(Secret),
            retryDelay: (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        var result = await client.ExtractAsync(new EntityExtractionRequest([], ["safe text"], "gpt-5.6-sol"));

        Assert.True(result.Succeeded);
        Assert.Equal(3, handler.Attempts);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task Unauthorized_401_fails_immediately_without_retry()
    {
        var handler = new SequenceHandler([HttpStatusCode.Unauthorized], "");
        var client = new EntityExtractionClient(
            new HttpClient(handler),
            new FakeCredentialStore(Secret),
            retryDelay: (_, _) => throw new InvalidOperationException("must not delay"));

        var result = await client.ExtractAsync(new EntityExtractionRequest([], ["safe text"], "gpt-5.6-sol"));

        Assert.False(result.Succeeded);
        Assert.Equal(1, handler.Attempts);
    }

    private static EntityExtractionClient Client(CaptureHandler handler) =>
        new(new HttpClient(handler), new FakeCredentialStore(Secret));

    private static string SuccessResponse() => SuccessResponseWithEntities(new[]
    {
        new { canonical = "ACME Corp", aliases = new[] { "ACME" }, kind = "customer", occurrences = 3 }
    });

    private static string SuccessResponseWithEntities(IEnumerable<object> entities) => JsonSerializer.Serialize(new
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
                        text = JsonSerializer.Serialize(new { entities })
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
