using System.Net.Http.Headers;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Collects the current week's sent Outlook mail via Microsoft Graph
/// (delegated, read-only). Never performs interactive sign-in itself: when no
/// silent token is available it reports a single Japanese error instead.
/// </summary>
public sealed class OutlookSentMailCollector(
    IGraphTokenProvider tokenProvider,
    HttpClient httpClient,
    bool enabled) : ISourceCollector
{
    private const string MailEndpoint = "https://graph.microsoft.com/v1.0/me/mailFolders/SentItems/messages";

    public string SourceName => SourceTypes.OutlookMail;

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
        var filter = $"sentDateTime ge {startUtc} and sentDateTime lt {endUtc}";
        var query =
            "$select=subject,sentDateTime,toRecipients,ccRecipients,body,bodyPreview" +
            "&$top=50" +
            $"&$filter={Uri.EscapeDataString(filter)}";

        var events = new List<SourceEvent>();
        var errors = new List<string>();
        string? nextLink = $"{MailEndpoint}?{query}";
        var pages = 0;

        while (!string.IsNullOrWhiteSpace(nextLink) && pages < GraphCollectorSupport.MaximumPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pages++;

            using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

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
                errors.Add("Outlookメール取得への接続に失敗しました。");
                break;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"Outlookメール取得に失敗しました（{(int)response.StatusCode}）。");
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
                    errors.Add("Outlookメール応答を読み取れませんでした。");
                    break;
                }

                var parsed = GraphMailParser.Parse(body);
                events.AddRange(parsed.Events);
                errors.AddRange(parsed.Errors);
                nextLink = parsed.NextLink;
            }
        }

        return new SourceCollectionResult(SourceName, events, errors);
    }
}
