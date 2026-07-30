using System.Text;
using System.Text.Json;
using WorkLogAI.Core;

namespace WorkLogAI.Infrastructure;

public sealed record ParsedCodexSession(
    DateTimeOffset OccurredAt,
    string WorkingDirectory,
    IReadOnlyList<string> UserInstructions,
    IReadOnlyList<string> FinalResponses,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> CommandNames,
    int SelectedUtf8Bytes,
    bool ContentLimitReached);

public sealed class CodexSessionParser
{
    public const int MaximumSelectedBytes = 256 * 1024;
    public const int MaximumLineBytes = 64 * 1024;

    private static readonly HashSet<string> AllowedCommandNames = new(
        [
            "shell_command",
            "exec_command",
            "apply_patch",
            "git",
            "dotnet",
            "powershell",
            "read_file",
            "write_file"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ForbiddenTypeFragments =
    [
        "reasoning",
        "compacted",
        "summary",
        "function_call_output",
        "tool_output",
        "environment"
    ];

    public async Task<ParsedCodexSession?> ParseAsync(
        string path,
        WeekRange range,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var lines = new BoundedUtf8LineReader(stream, MaximumLineBytes);
        var selection = new CodexSelection();

        while (!selection.ContentLimitReached)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await lines.ReadLineAsync(cancellationToken);
            if (!line.HasLine)
            {
                break;
            }
            if (line.Oversized || string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line.Text);
                SelectAllowedRecord(document.RootElement, selection);
            }
            catch (JsonException)
            {
                // A malformed JSONL record is isolated to its line.
            }
        }

        if (selection.OccurredAt is null
            || selection.SelectedUtf8Bytes == 0
            || !range.Contains(DateOnly.FromDateTime(selection.OccurredAt.Value.DateTime)))
        {
            return null;
        }

        return new ParsedCodexSession(
            selection.OccurredAt.Value,
            selection.WorkingDirectory,
            selection.UserInstructions,
            selection.FinalResponses,
            selection.ChangedFiles,
            selection.CommandNames,
            selection.SelectedUtf8Bytes,
            selection.ContentLimitReached);
    }

    private static void SelectAllowedRecord(JsonElement root, CodexSelection selection)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !TryGetString(root, "type", out var topLevelType)
            || IsForbiddenType(topLevelType))
        {
            return;
        }

        var timestamp = ReadTimestamp(root);
        if (timestamp is not null)
        {
            selection.SetTimestamp(timestamp.Value);
        }

        if (topLevelType is "session_meta" or "session_metadata")
        {
            if (TryGetObject(root, "payload", out var payload))
            {
                selection.SetTimestamp(ReadTimestamp(payload));
                selection.TrySetWorkingDirectory(ReadString(payload, "cwd"));
            }
            return;
        }

        if (topLevelType == "turn_context")
        {
            if (TryGetObject(root, "payload", out var payload))
            {
                selection.TrySetWorkingDirectory(ReadString(payload, "cwd"));
            }
            return;
        }

        if (!TryGetObject(root, "payload", out var item)
            || !TryGetString(item, "type", out var itemType)
            || IsForbiddenType(itemType))
        {
            return;
        }

        switch (topLevelType)
        {
            case "response_item":
                SelectResponseItem(itemType, item, selection);
                break;
            case "event_msg":
                SelectEventMessage(itemType, item, selection);
                break;
        }
    }

    private static void SelectResponseItem(
        string itemType,
        JsonElement item,
        CodexSelection selection)
    {
        if (itemType == "function_call")
        {
            selection.TryAddCommand(ReadString(item, "name"));
            return;
        }

        if (itemType is "file_change" or "file_changes")
        {
            selection.TryAddFiles(ReadStringArray(item, "paths"));
            selection.TryAddFiles(ReadStringArray(item, "changed_files"));
            return;
        }

        if (itemType != "message" || !TryGetString(item, "role", out var role))
        {
            return;
        }

        if (role == "user")
        {
            foreach (var text in ReadMessageContent(item, "input_text"))
            {
                selection.TryAddUser(text);
            }
            return;
        }

        var phase = ReadString(item, "phase");
        var status = ReadString(item, "status");
        if (role == "assistant"
            && (phase is "final_answer" or "completion"
                || status is "completed" or "complete"))
        {
            foreach (var text in ReadMessageContent(item, "output_text"))
            {
                selection.TryAddFinal(text);
            }
        }
    }

    private static void SelectEventMessage(
        string itemType,
        JsonElement item,
        CodexSelection selection)
    {
        switch (itemType)
        {
            case "user_message":
                selection.TryAddUser(ReadString(item, "message") ?? ReadString(item, "text"));
                break;
            case "agent_message" when ReadString(item, "phase") is "final_answer" or "completion":
                selection.TryAddFinal(ReadString(item, "message") ?? ReadString(item, "text"));
                break;
            case "task_complete":
                selection.TryAddFinal(
                    ReadString(item, "final_response") ?? ReadString(item, "message"));
                selection.TryAddFiles(ReadStringArray(item, "changed_files"));
                break;
        }
    }

    private static IEnumerable<string> ReadMessageContent(JsonElement item, string allowedType)
    {
        if (!item.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object
                && TryGetString(part, "type", out var type)
                && type == allowedType
                && TryGetString(part, "text", out var text))
            {
                yield return text;
            }
        }
    }

    private static IEnumerable<string> ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                yield return value.GetString()!;
            }
        }
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element)
    {
        var value = ReadString(element, "timestamp");
        return DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        TryGetString(element, property, out var value) ? value : null;

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(property, out var item)
            || item.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = item.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetObject(JsonElement element, string property, out JsonElement value)
    {
        if (element.TryGetProperty(property, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool IsForbiddenType(string type) =>
        ForbiddenTypeFragments.Any(
            fragment => type.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private sealed class CodexSelection
    {
        private readonly List<string> _userInstructions = [];
        private readonly List<string> _finalResponses = [];
        private readonly List<string> _changedFiles = [];
        private readonly List<string> _commandNames = [];

        public DateTimeOffset? OccurredAt { get; private set; }
        public string WorkingDirectory { get; private set; } = string.Empty;
        public IReadOnlyList<string> UserInstructions => _userInstructions;
        public IReadOnlyList<string> FinalResponses => _finalResponses;
        public IReadOnlyList<string> ChangedFiles => _changedFiles;
        public IReadOnlyList<string> CommandNames => _commandNames;
        public int SelectedUtf8Bytes { get; private set; }
        public bool ContentLimitReached { get; private set; }

        public void SetTimestamp(DateTimeOffset? timestamp)
        {
            if (timestamp is not null && OccurredAt is null)
            {
                OccurredAt = timestamp;
            }
        }

        public void TrySetWorkingDirectory(string? value)
        {
            if (string.IsNullOrEmpty(WorkingDirectory)
                && TrySelect(value, 1_000, out var selected))
            {
                WorkingDirectory = selected;
            }
        }

        public void TryAddUser(string? value)
        {
            if (TrySelect(value, 4_000, out var selected))
            {
                _userInstructions.Add(selected);
            }
        }

        public void TryAddFinal(string? value)
        {
            if (TrySelect(value, 4_000, out var selected))
            {
                _finalResponses.Add(selected);
            }
        }

        public void TryAddFiles(IEnumerable<string> values)
        {
            foreach (var value in values.Take(500))
            {
                if (!SafePathPolicy.IsSensitive(value)
                    && TrySelect(value, 500, out var selected)
                    && !_changedFiles.Contains(selected, StringComparer.OrdinalIgnoreCase))
                {
                    _changedFiles.Add(selected);
                }
            }
        }

        public void TryAddCommand(string? value)
        {
            if (value is null
                || !AllowedCommandNames.Contains(value)
                || !TrySelect(value, 64, out var selected)
                || _commandNames.Contains(selected, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            _commandNames.Add(selected);
        }

        private bool TrySelect(string? value, int maximumCharacters, out string selected)
        {
            selected = SafeTextSanitizer.Sanitize(value, maximumCharacters);
            if (ContentLimitReached || string.IsNullOrWhiteSpace(selected))
            {
                return false;
            }

            var bytes = Encoding.UTF8.GetByteCount(selected) + 1;
            if (SelectedUtf8Bytes + bytes > MaximumSelectedBytes)
            {
                ContentLimitReached = true;
                selected = string.Empty;
                return false;
            }

            SelectedUtf8Bytes += bytes;
            return true;
        }
    }
}

internal readonly record struct BoundedLine(bool HasLine, bool Oversized, string? Text);

internal sealed class BoundedUtf8LineReader(Stream stream, int maximumLineBytes)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] _readBuffer = new byte[8 * 1024];
    private readonly byte[] _lineBuffer = new byte[maximumLineBytes];
    private int _readOffset;
    private int _readCount;
    private bool _endOfStream;

    public async ValueTask<BoundedLine> ReadLineAsync(CancellationToken cancellationToken)
    {
        var length = 0;
        var oversized = false;
        var sawByte = false;

        while (true)
        {
            var next = await ReadByteAsync(cancellationToken);
            if (next < 0)
            {
                if (!sawByte)
                {
                    return new BoundedLine(false, false, null);
                }
                break;
            }

            sawByte = true;
            if (next == '\n')
            {
                break;
            }
            if (next == '\r')
            {
                continue;
            }
            if (length < maximumLineBytes)
            {
                _lineBuffer[length++] = (byte)next;
            }
            else
            {
                oversized = true;
            }
        }

        if (oversized)
        {
            return new BoundedLine(true, true, null);
        }

        try
        {
            return new BoundedLine(true, false, StrictUtf8.GetString(_lineBuffer, 0, length));
        }
        catch (DecoderFallbackException)
        {
            return new BoundedLine(true, true, null);
        }
    }

    private async ValueTask<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        if (_readOffset >= _readCount)
        {
            if (_endOfStream)
            {
                return -1;
            }

            _readCount = await stream.ReadAsync(_readBuffer.AsMemory(), cancellationToken);
            _readOffset = 0;
            if (_readCount == 0)
            {
                _endOfStream = true;
                return -1;
            }
        }

        return _readBuffer[_readOffset++];
    }
}
