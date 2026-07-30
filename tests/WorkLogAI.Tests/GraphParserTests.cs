using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.Tests;

public sealed class GraphParserTests
{
    private const string MailPage = """
        {
          "@odata.nextLink": "https://graph.microsoft.com/v1.0/me/mailFolders/SentItems/messages?$skip=50",
          "value": [
            {
              "id": "msg-1",
              "subject": "作業完了報告",
              "sentDateTime": "2026-07-28T01:23:45Z",
              "toRecipients": [ { "emailAddress": { "name": "田中太郎", "address": "tanaka@example.com" } } ],
              "ccRecipients": [ { "emailAddress": { "name": "", "address": "cc@example.com" } } ],
              "body": { "contentType": "html", "content": "<p>作業完了しました。</p><br>From: 田中太郎<br>過去のメール本文をここに含みます" }
            },
            {
              "id": "msg-2",
              "subject": "自動応答: 休暇中です",
              "sentDateTime": "2026-07-28T02:00:00Z",
              "body": { "contentType": "text", "content": "休暇中のため返信できません。" }
            },
            {
              "id": "msg-3",
              "subject": "壊れたメッセージ"
            },
            {
              "id": "msg-4",
              "subject": "RE: 会議について",
              "sentDateTime": "2026-07-28T03:00:00Z",
              "body": { "contentType": "text", "content": "> 会議は来週です。" }
            }
          ]
        }
        """;

    [Fact]
    public void Mail_parser_keeps_only_real_new_content_excludes_autoreply_and_empty_reply_and_surfaces_paging_and_errors()
    {
        var result = GraphMailParser.Parse(MailPage);

        var kept = Assert.Single(result.Events);
        Assert.Equal(SourceTypes.OutlookMail, kept.SourceType);
        Assert.Equal("作業完了報告", kept.Title);
        Assert.Equal("作業完了しました。", kept.Body);
        Assert.DoesNotContain("過去のメール本文", kept.Body);
        Assert.Equal("宛先: 田中太郎, cc@example.com", kept.Evidence);
        Assert.Equal("msg-1", kept.SourceRef);
        Assert.Equal(0.7, kept.Confidence);

        Assert.Single(result.Errors);
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/me/mailFolders/SentItems/messages?$skip=50",
            result.NextLink);
    }

    [Fact]
    public void Mail_parser_returns_no_events_and_no_next_link_when_value_array_is_missing()
    {
        var result = GraphMailParser.Parse("""{"unexpected":true}""");

        Assert.Empty(result.Events);
        Assert.Empty(result.Errors);
        Assert.Null(result.NextLink);
    }

    [Fact]
    public void Mail_parser_reports_an_error_for_malformed_json_instead_of_throwing()
    {
        var result = GraphMailParser.Parse("not-json");

        Assert.Empty(result.Events);
        Assert.Single(result.Errors);
    }

    private const string CalendarPage = """
        {
          "value": [
            {
              "id": "evt-1",
              "subject": "定例会議",
              "start": { "dateTime": "2026-07-28T09:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-07-28T10:00:00.0000000", "timeZone": "UTC" },
              "location": { "displayName": "会議室A" },
              "bodyPreview": "アジェンダを確認する",
              "isAllDay": false,
              "isCancelled": false
            },
            {
              "id": "evt-2",
              "subject": "キャンセル済み予定",
              "start": { "dateTime": "2026-07-29T09:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-07-29T10:00:00.0000000", "timeZone": "UTC" },
              "isCancelled": true
            },
            {
              "id": "evt-3",
              "subject": "終日イベント",
              "start": { "dateTime": "2026-07-30T00:00:00.0000000", "timeZone": "UTC" },
              "end": { "dateTime": "2026-07-31T00:00:00.0000000", "timeZone": "UTC" },
              "isAllDay": true,
              "isCancelled": false
            },
            {
              "id": "evt-4"
            }
          ]
        }
        """;

    [Fact]
    public void Calendar_parser_skips_cancelled_events_reports_time_range_evidence_and_all_day_evidence()
    {
        var result = GraphCalendarParser.Parse(CalendarPage);

        Assert.Equal(2, result.Events.Count);
        Assert.DoesNotContain(result.Events, item => item.SourceRef == "evt-2");

        var normal = Assert.Single(result.Events, item => item.SourceRef == "evt-1");
        Assert.Equal(SourceTypes.Calendar, normal.SourceType);
        Assert.Equal("09:00〜10:00 会議室A", normal.Evidence);
        Assert.Equal(0.5, normal.Confidence);

        var allDay = Assert.Single(result.Events, item => item.SourceRef == "evt-3");
        Assert.Equal("終日", allDay.Evidence);

        Assert.Single(result.Errors);
    }
}
