using System.Globalization;
using System.Text;

namespace WorkLogAI.Core;

/// <summary>
/// Pure Markdown rendering for a meeting session. No IO — callers own writing the
/// result to disk (see MeetingMarkdownWriter in Infrastructure). When
/// <paramref name="formatted"/> is null the 概要/決定事項/宿題/論点 sections are
/// omitted entirely and only the raw log is emitted (subject to
/// <paramref name="includeRawLog"/>); once a later phase populates AI output, all
/// sections are emitted.
/// </summary>
public static class MeetingMarkdownBuilder
{
    private const string NoContentPlaceholder = "（記載なし）";

    public static string Build(
        MeetingSession session,
        IReadOnlyList<MeetingLine> lines,
        MeetingFormattedResult? formatted,
        bool includeRawLog)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(lines);

        var builder = new StringBuilder();
        AppendFrontMatter(builder, session);
        builder.Append("# ").Append(session.Title).Append('\n');

        if (formatted is not null)
        {
            builder.Append('\n').Append("## 概要").Append('\n');
            builder.Append(string.IsNullOrWhiteSpace(formatted.Overview)
                ? NoContentPlaceholder
                : formatted.Overview.Trim());
            builder.Append('\n');

            AppendListSection(builder, "決定事項", formatted.Decisions);
            AppendActionItemsSection(builder, formatted.ActionItems);
            AppendTopicsSection(builder, formatted.Topics);
        }

        if (includeRawLog)
        {
            AppendRawLogSection(builder, lines);
        }

        return builder.ToString().TrimEnd('\n') + "\n";
    }

    private static void AppendFrontMatter(StringBuilder builder, MeetingSession session)
    {
        var date = DateOnly.FromDateTime(session.StartedAt.DateTime);
        var participants = MeetingParticipantsFormatter.Split(session.Participants);

        builder.Append("---").Append('\n');
        builder.Append("date: ").Append(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("type: ").Append(MeetingKindStrings.ToStorageString(session.Kind)).Append('\n');
        builder.Append("participants: ").Append(MeetingParticipantsFormatter.ToYamlInlineList(participants)).Append('\n');
        builder.Append("tags: [worklog, meeting]").Append('\n');
        builder.Append("---").Append('\n').Append('\n');
    }

    private static void AppendListSection(StringBuilder builder, string heading, IReadOnlyList<string> items)
    {
        builder.Append('\n').Append("## ").Append(heading).Append('\n');
        if (items.Count == 0)
        {
            builder.Append("- ").Append(NoContentPlaceholder).Append('\n');
            return;
        }

        foreach (var item in items)
        {
            builder.Append("- ").Append(item).Append('\n');
        }
    }

    private static void AppendActionItemsSection(StringBuilder builder, IReadOnlyList<MeetingActionItem> items)
    {
        builder.Append('\n').Append("## 宿題").Append('\n');
        if (items.Count == 0)
        {
            builder.Append("- ").Append(NoContentPlaceholder).Append('\n');
            return;
        }

        foreach (var item in items)
        {
            builder.Append("- [ ] ").Append(item.Text).Append(FormatActionItemSuffix(item)).Append('\n');
        }
    }

    private static string FormatActionItemSuffix(MeetingActionItem item)
    {
        var hasOwner = !string.IsNullOrWhiteSpace(item.Owner);
        var hasDue = !string.IsNullOrWhiteSpace(item.Due);
        if (!hasOwner && !hasDue)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (hasOwner)
        {
            parts.Add($"担当: {item.Owner}");
        }

        if (hasDue)
        {
            parts.Add($"期限: {item.Due}");
        }

        return $"（{string.Join(" / ", parts)}）";
    }

    private static void AppendTopicsSection(StringBuilder builder, IReadOnlyList<MeetingTopic> topics)
    {
        builder.Append('\n').Append("## 論点").Append('\n');
        if (topics.Count == 0)
        {
            builder.Append(NoContentPlaceholder).Append('\n');
            return;
        }

        foreach (var topic in topics)
        {
            builder.Append("### ").Append(topic.Title).Append('\n');
            builder.Append(string.IsNullOrWhiteSpace(topic.Detail) ? NoContentPlaceholder : topic.Detail.Trim());
            builder.Append('\n');
        }
    }

    private static void AppendRawLogSection(StringBuilder builder, IReadOnlyList<MeetingLine> lines)
    {
        builder.Append('\n').Append("## 生ログ").Append('\n');
        if (lines.Count == 0)
        {
            builder.Append("- ").Append(NoContentPlaceholder).Append('\n');
            return;
        }

        foreach (var line in lines.OrderBy(item => item.LineNo))
        {
            var time = line.LoggedAt.DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            builder.Append("- ").Append(time).Append(' ').Append(RestoreMarkerPrefix(line)).Append('\n');
        }
    }

    private static string RestoreMarkerPrefix(MeetingLine line) => line.Marker switch
    {
        MeetingMarker.Todo => $"@{line.Text}",
        MeetingMarker.Decision => $"!{line.Text}",
        _ => line.Text
    };
}
