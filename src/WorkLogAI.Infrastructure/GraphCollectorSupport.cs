using System.Text;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

internal static class GraphCollectorSupport
{
    public const int MaximumPages = 10;

    public static (string StartUtc, string EndUtc) WeekBoundsUtc(WeekRange range)
    {
        var bounds = WeekRangeTimeBounds.Local(range);
        return (
            bounds.From.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            bounds.To.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    public static async Task<string> ReadBoundedAsync(
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
            if (read == 0)
            {
                break;
            }
            if (builder.Length + read > 4 * 1024 * 1024)
            {
                throw new InvalidDataException("Graph response too large.");
            }
            builder.Append(buffer, 0, read);
        }
        return builder.ToString();
    }
}
