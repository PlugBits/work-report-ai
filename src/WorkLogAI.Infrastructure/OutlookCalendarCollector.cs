using System.Net.Http.Headers;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Collects the current week's Outlook calendar events via Microsoft Graph
/// (delegated, read-only). Never performs interactive sign-in itself: when no
/// silent token is available it reports a single Japanese error instead.
/// </summary>
public sealed class OutlookCalendarCollector(
    IGraphTokenProvider tokenProvider,
    HttpClient httpClient,
    bool enabled) : ISourceCollector
{
    private const string CalendarEndpoint = "https://graph.microsoft.com/v1.0/me/calendarView";

    public string SourceName => SourceTypes.Calendar;

    public async Task<SourceCollectionResult> CollectAsync(
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        if (!enabled)
        {
            return SourceCollectionResult.Empty(SourceName);
        }

        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new SourceCollectionResult(
                SourceName,
                [],
                ["Microsoftサインインが必要です（設定画面からサインイン）"]);
        }

        var (startUtc, endUtc) = GraphCollectorSupport.WeekBoundsUtc(range);
        var query =
            $"startDateTime={Uri.EscapeDataString(startUtc)}" +
            $"&endDateTime={Uri.EscapeDataString(endUtc)}" +
            "&$select=subject,start,end,location,bodyPreview,isAllDay,isCancelled" +
            "&$top=50";

        var events = new List<SourceEvent>();
        var errors = new List<string>();
        string? nextLink = $"{CalendarEndpoint}?{query}";
        var pages = 0;

        while (!string.IsNullOrWhiteSpace(nextLink) && pages < GraphCollectorSupport.MaximumPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pages++;

            using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                errors.Add("カレンダー取得への接続に失敗しました。");
                break;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"カレンダー取得に失敗しました（{(int)response.StatusCode}）。");
                    break;
                }

                string body;
                try
                {
                    body = await GraphCollectorSupport.ReadBoundedAsync(response.Content, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    errors.Add("カレンダー応答を読み取れませんでした。");
                    break;
                }

                var parsed = GraphCalendarParser.Parse(body);
                events.AddRange(parsed.Events);
                errors.AddRange(parsed.Errors);
                nextLink = parsed.NextLink;
            }
        }

        return new SourceCollectionResult(SourceName, events, errors);
    }
}
