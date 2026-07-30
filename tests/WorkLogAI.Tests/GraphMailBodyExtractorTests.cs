using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class GraphMailBodyExtractorTests
{
    [Fact]
    public void Html_body_is_stripped_to_text_and_common_entities_are_decoded()
    {
        var html = "<div>Hello &amp; welcome&nbsp;&lt;here&gt; &quot;now&quot;</div>";

        var result = GraphMailBodyExtractor.ExtractNewContent(html, "html");

        Assert.Equal("Hello & welcome <here> \"now\"", result);
    }

    [Theory]
    [InlineData("本文です。\n-----Original Message-----\n差出人の続きは送らない")]
    [InlineData("本文です。\n________________________________\n差出人の続きは送らない")]
    [InlineData("本文です。\nFrom: someone@example.com\n差出人の続きは送らない")]
    [InlineData("本文です。\n差出人: someone@example.com\n差出人の続きは送らない")]
    [InlineData("本文です。\nOn Tuesday, July 28, 2026, someone wrote:\n差出人の続きは送らない")]
    [InlineData("本文です。\n> 引用された本文\n差出人の続きは送らない")]
    public void Each_quote_marker_truncates_the_new_content(string body)
    {
        var result = GraphMailBodyExtractor.ExtractNewContent(body, "text");

        Assert.Equal("本文です。", result);
        Assert.DoesNotContain("差出人の続きは送らない", result);
    }

    [Fact]
    public void Sanitizer_redacts_secret_looking_tokens_in_the_extracted_body()
    {
        var body = "APIキーは sk-aaaaaaaaaaaaaaaa です";

        var result = GraphMailBodyExtractor.ExtractNewContent(body, "text");

        Assert.DoesNotContain("sk-aaaaaaaaaaaaaaaa", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void Result_is_capped_at_the_requested_maximum_characters()
    {
        // Individual lines stay under SafeTextSanitizer's 500-char per-line
        // limit so this exercises the extractor/sanitizer's overall length cap.
        var body = string.Join('\n', Enumerable.Repeat(new string('あ', 400), 8));

        var defaultCap = GraphMailBodyExtractor.ExtractNewContent(body, "text");
        var customCap = GraphMailBodyExtractor.ExtractNewContent(body, "text", maximumCharacters: 500);

        Assert.Equal(2_000, defaultCap.Length);
        Assert.Equal(500, customCap.Length);
    }

    [Fact]
    public void Blank_or_missing_body_produces_empty_string()
    {
        Assert.Equal(string.Empty, GraphMailBodyExtractor.ExtractNewContent(null, "text"));
        Assert.Equal(string.Empty, GraphMailBodyExtractor.ExtractNewContent("   ", "html"));
    }
}
