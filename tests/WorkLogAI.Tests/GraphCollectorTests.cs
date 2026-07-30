using System.Net;
using System.Net.Http.Headers;
using System.Text;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class GraphCollectorTests
{
    private const string SimpleMailPage = """
        {
          "value": [
            {
              "id": "simple-1",
              "subject": "進捗共有",
              "sentDateTime": "2026-07-28T04:00:00Z",
              "toRecipients": [ { "emailAddress": { "name": "鈴木一郎" } } ],
              "body": { "contentType": "text", "content": "本日の進捗を共有します。" }
            }
          ]
        }
        """;

    private const string MailPageWithNext = """
        {
          "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/mailFolders/SentItems/messages?$skip=1",
          "value": [
            {
              "id": "page1-1",
              "subject": "1ページ目",
              "sentDateTime": "2026-07-28T05:00:00Z",
              "body": { "contentType": "text", "content": "1ページ目の内容です。" }
            }
          ]
        }
        """;

    private const string MailPageTwo = """
        {
          "value": [
            {
              "id": "page2-1",
              "subject": "2ページ目",
              "sentDateTime": "2026-07-29T05:00:00Z",
              "body": { "contentType": "text", "content": "2ページ目の内容です。" }
            }
          ]
        }
        """;

    [Fact]
    public async Task Mail_collector_disabled_returns_empty_without_calling_the_token_provider()
    {
        var tokenProvider = new FakeTokenProvider(null);
        var collector = new OutlookSentMailCollector(tokenProvider, new HttpClient(new QueueHandler()), enabled: false);

        var result = await collector.CollectAsync(Week());

        Assert.Empty(result.Events);
        Assert.Empty(result.Errors);
        Assert.Equal(0, tokenProvider.CallCount);
    }

    [Fact]
    public async Task Mail_collector_null_token_reports_a_single_japanese_signin_error()
    {
        var collector = new OutlookSentMailCollector(
            new FakeTokenProvider(null),
            new HttpClient(new QueueHandler()),
            enabled: true);

        var result = await collector.CollectAsync(Week());

        Assert.Empty(result.Events);
        Assert.Equal("Microsoftサインインが必要です（設定画面からサインイン）", Assert.Single(result.Errors));
    }

    [Fact]
    public async Task Mail_collector_non_success_status_reports_status_code_and_never_echoes_the_body()
    {
        var handler = new QueueHandler(
            (HttpStatusCode.Unauthorized, """{"error":{"message":"PRIVATE_EVIDENCE"}}"""));
        var collector = new OutlookSentMailCollector(
            new FakeTokenProvider("token-abc"),
            new HttpClient(handler),
            enabled: true);

        var result = await collector.CollectAsync(Week());

        Assert.Empty(result.Events);
        var error = Assert.Single(result.Errors);
        Assert.Contains("401", error);
        Assert.DoesNotContain("PRIVATE_EVIDENCE", error);
    }

    [Fact]
    public async Task Mail_collector_single_page_returns_parsed_events_with_bearer_auth()
    {
        var handler = new QueueHandler((HttpStatusCode.OK, SimpleMailPage));
        var collector = new OutlookSentMailCollector(
            new FakeTokenProvider("token-abc"),
            new HttpClient(handler),
            enabled: true);

        var result = await collector.CollectAsync(Week());

        var kept = Assert.Single(result.Events);
        Assert.Equal("simple-1", kept.SourceRef);
        Assert.Empty(result.Errors);
        var authorization = Assert.Single(handler.Authorizations);
        Assert.Equal("Bearer", authorization?.Scheme);
        Assert.Equal("token-abc", authorization?.Parameter);
    }

    [Fact]
    public async Task Mail_collector_follows_next_link_and_aggregates_both_pages()
    {
        var handler = new QueueHandler(
            (HttpStatusCode.OK, MailPageWithNext),
            (HttpStatusCode.OK, MailPageTwo));
        var collector = new OutlookSentMailCollector(
            new FakeTokenProvider("token-abc"),
            new HttpClient(handler),
            enabled: true);

        var result = await collector.CollectAsync(Week());

        Assert.Equal(2, result.Events.Count);
        Assert.Contains(result.Events, item => item.SourceRef == "page1-1");
        Assert.Contains(result.Events, item => item.SourceRef == "page2-1");
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/me/mailFolders/SentItems/messages?$skip=1",
            handler.Requests[1].ToString());
    }

    private const string SimpleCalendarPage = """
        {
          "value": [
            {
              "id": "cal-1",
              "subject": "定例会議",
              "start": { "dateTime": "2026-07-28T09:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-07-28T10:00:00.0000000", "timeZone": "UTC" },
              "location": { "displayName": "会議室A" },
              "bodyPreview": "アジェンダを確認する",
              "isAllDay": false,
              "isCancelled": false
            }
          ]
        }
        """;

    private const string CalendarPageWithNext = """
        {
          "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/calendarView?$skip=1",
          "value": [
            {
              "id": "cal-page1",
              "subject": "1ページ目の予定",
              "start": { "dateTime": "2026-07-28T09:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-07-28T10:00:00.0000000", "timeZone": "UTC" },
              "isAllDay": false,
              "isCancelled": false
            }
          ]
        }
        """;

    private const string CalendarPageTwo = """
        {
          "value": [
            {
              "id": "cal-page2",
              "subject": "2ページ目の予定",
              "start": { "dateTime": "2026-07-29T09:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-07-29T10:00:00.0000000", "timeZone": "UTC" },
              "isAllDay": false,
              "isCancelled": false
            }
          ]
        }
        """;

    [Fact]
    public async Task Calendar_collector_disabled_returns_empty_without_calling_the_token_provider()
    {
        var tokenProvider = new FakeTokenProvider(null);
        var collector = new OutlookCalendarCollector(tokenProvider, new HttpClient(new QueueHandler()), enabled: false);

        var result = await collector.CollectAsync(Week());

        Assert.Empty(result.Events);
        Assert.Empty(result.Errors);
        Assert.Equal(0, tokenProvider.CallCount);
    }

    [Fact]
    public async Task Calendar_collector_null_token_reports_a_single_japanese_signin_error()
    {
        var collector = new OutlookCalendarCollector(
            new FakeTokenProvider(null),
            new HttpClient(new QueueHandler()),
            enabled: true);

        var result = await collector.CollectAsync(Week());

        Assert.Empty(result.Events);
        Assert.Equal("Microsoftサインインが必要です（設定画面からサインイン）", Assert.Single(result.Errors));
    }

    [Fact]
    public async Task Calendar_collector_non_success_status_reports_status_code_and_never_echoes_the_body()
    {
        var handler = new QueueHandler(
            (HttpStatusCode.Forbidden, """{"error":{"message":"PRIVATE_EVIDENCE"}}"""));
        var collector = new OutlookCalendarCollector(
            new FakeTokenProvider("token-abc"),
            new HttpClient(handler),
            enabled: true);

        var result = await collector.CollectAsync(Week());

        Assert.Empty(result.Events);
        var error = Assert.Single(result.Errors);
        Assert.Contains("403", error);
        Assert.DoesNotContain("PRIVATE_EVIDENCE", error);
    }

    [Fact]
    public async Task Calendar_collector_single_page_returns_parsed_events_with_timezone_preference_header()
    {
        var handler = new QueueHandler((HttpStatusCode.OK, SimpleCalendarPage));
        var collector = new OutlookCalendarCollector(
            new FakeTokenProvider("token-abc"),
            new HttpClient(handler),
            enabled: true);

        var result = await collector.CollectAsync(Week());

        var kept = Assert.Single(result.Events);
        Assert.Equal("cal-1", kept.SourceRef);
        Assert.Empty(result.Errors);
        Assert.Equal("outlook.timezone=\"UTC\"", Assert.Single(handler.PreferHeaders));
    }

    [Fact]
    public async Task Calendar_collector_follows_next_link_and_aggregates_both_pages()
    {
        var handler = new QueueHandler(
            (HttpStatusCode.OK, CalendarPageWithNext),
            (HttpStatusCode.OK, CalendarPageTwo));
        var collector = new OutlookCalendarCollector(
            new FakeTokenProvider("token-abc"),
            new HttpClient(handler),
            enabled: true);

        var result = await collector.CollectAsync(Week());

        Assert.Equal(2, result.Events.Count);
        Assert.Contains(result.Events, item => item.SourceRef == "cal-page1");
        Assert.Contains(result.Events, item => item.SourceRef == "cal-page2");
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/me/calendarView?$skip=1",
            handler.Requests[1].ToString());
    }

    private static WeekRange Week() => new(new DateOnly(2026, 7, 27), new DateOnly(2026, 8, 2));

    private sealed class FakeTokenProvider(string? token) : IGraphTokenProvider
    {
        public int CallCount { get; private set; }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(token);
        }
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

        public QueueHandler(params (HttpStatusCode Status, string Body)[] responses) =>
            _responses = new Queue<(HttpStatusCode, string)>(responses);

        public List<Uri> Requests { get; } = [];
        public List<AuthenticationHeaderValue?> Authorizations { get; } = [];
        public List<string?> PreferHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            Authorizations.Add(request.Headers.Authorization);
            PreferHeaders.Add(
                request.Headers.TryGetValues("Prefer", out var values) ? values.FirstOrDefault() : null);

            var (status, body) = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.OK, """{"value":[]}""");
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
