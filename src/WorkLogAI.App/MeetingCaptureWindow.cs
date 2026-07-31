using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkLogAI.App;

/// <summary>
/// 議事録モード capture window. Lines are persisted through <see cref="IMeetingRepository"/>
/// immediately on Enter — there is no separate "save" step. Closing the window (X)
/// leaves the session as a draft; 会議終了 closes it and, when an API key is
/// configured, offers AI整形 (see <see cref="RunAiFormatFlowAsync"/>) before falling
/// back to a raw-log-only Markdown export.
/// </summary>
public sealed class MeetingCaptureWindow : Window
{
    private static readonly (string Label, MeetingKind Kind)[] KindOptions =
    [
        ("会議", MeetingKind.Meeting),
        ("来客", MeetingKind.Visitor),
        ("電話", MeetingKind.Call)
    ];

    private readonly AppServices _services;
    private readonly MeetingSession? _initialSession;

    private readonly TextBox _titleBox = new();
    private readonly TextBox _participantsBox = new();
    private readonly ComboBox _kindBox = new();
    private readonly ListBox _lines = new() { DisplayMemberPath = nameof(LineItem.Label) };
    private readonly TextBox _input = new() { FontSize = 14, AcceptsReturn = false, MaxLines = 1 };
    private readonly Button _aiFormatButton = new()
    {
        Content = "AI整形",
        Padding = new Thickness(14, 6, 14, 6),
        Margin = new Thickness(0, 0, 8, 0),
        IsEnabled = false
    };
    private readonly Button _endButton = new() { Content = "会議終了", Padding = new Thickness(14, 6, 14, 6) };

    private Guid? _sessionId;
    private bool _ended;
    private bool _closeArmed;
    private bool _savingLine;

    public MeetingCaptureWindow(AppServices services, MeetingSession? existingSession)
    {
        AppTheme.Apply(this);
        _services = services;
        _initialSession = existingSession;

        Title = "議事録モード - WorkLog AI";
        MinWidth = 340;
        MinHeight = 360;
        Width = 420;
        Height = 520;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = true;

        var header = new StackPanel { Margin = new Thickness(12, 12, 12, 4) };
        header.Children.Add(Label("件名"));
        header.Children.Add(_titleBox);
        header.Children.Add(Label("相手先/参加者"));
        header.Children.Add(_participantsBox);
        header.Children.Add(Label("種別"));
        _kindBox.ItemsSource = KindOptions.Select(option => option.Label).ToArray();
        _kindBox.SelectedIndex = 0;
        header.Children.Add(_kindBox);

        var endRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _aiFormatButton.Click += AiFormatButtonClick;
        endRow.Children.Add(_aiFormatButton);
        _endButton.Style = (Style)System.Windows.Application.Current.FindResource("AccentButton");
        _endButton.Click += EndMeetingAsync;
        endRow.Children.Add(_endButton);
        header.Children.Add(endRow);
        DockPanel.SetDock(header, Dock.Top);

        var hint = new TextBlock
        {
            Text = "行頭 @ = 宿題 / 行頭 ! = 決定 （全角＠／！も可）",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(12, 4, 12, 2)
        };
        DockPanel.SetDock(hint, Dock.Bottom);

        _input.Margin = new Thickness(12, 0, 12, 12);
        _input.PreviewKeyDown += OnInputPreviewKeyDown;
        DockPanel.SetDock(_input, Dock.Bottom);

        _lines.Margin = new Thickness(12, 0, 12, 8);
        _lines.MouseDoubleClick += OnLineDoubleClick;
        _lines.PreviewKeyDown += OnLinesPreviewKeyDown;

        // DockPanel stacks same-edge children from the outer edge inward in the
        // order they are added, so _input (the outermost/bottom-most element) must
        // be added before hint (which then lands directly above it).
        var root = new DockPanel();
        root.Children.Add(header);
        root.Children.Add(_input);
        root.Children.Add(hint);
        root.Children.Add(_lines);
        Content = root;

        Loaded += OnLoadedAsync;
        Closing += OnClosingAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        ApplyWindowPlacement(await ReadWindowPlacementAsync());

        if (_initialSession is { } existing)
        {
            _sessionId = existing.Id;
            _titleBox.Text = existing.Title;
            _participantsBox.Text = existing.Participants;
            var kindIndex = Array.FindIndex(KindOptions, option => option.Kind == existing.Kind);
            _kindBox.SelectedIndex = kindIndex >= 0 ? kindIndex : 0;
            await RefreshLinesAsync();
        }
        else
        {
            try
            {
                var created = await _services.Meetings.CreateSessionAsync(
                    string.Empty,
                    string.Empty,
                    MeetingKind.Meeting,
                    DateTimeOffset.Now);
                _sessionId = created.Id;
            }
            catch (Exception exception)
            {
                ErrorLog.Log("MeetingCaptureWindow.CreateSession", exception);
                MessageBox.Show(
                    this,
                    $"議事録セッションを作成できませんでした。\n{exception.Message}",
                    "WorkLog AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
                return;
            }
        }

        _input.Focus();
    }

    private async Task<string?> ReadWindowPlacementAsync()
    {
        try
        {
            return await _services.SettingsStore.GetAsync(AppSettingKeys.MeetingWindowPlacement);
        }
        catch (Exception exception)
        {
            ErrorLog.Log("MeetingCaptureWindow.ReadPlacement", exception);
            return null;
        }
    }

    private void ApplyWindowPlacement(string? raw)
    {
        var workArea = SystemParameters.WorkArea;
        const double defaultWidth = 420;
        const double defaultHeight = 520;

        if (WindowPlacementFormat.TryParse(raw, out var left, out var top, out var width, out var height)
            && width >= MinWidth && height >= MinHeight)
        {
            Width = Math.Min(width, workArea.Width);
            Height = Math.Min(height, workArea.Height);
            Left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
            Top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
            return;
        }

        Width = defaultWidth;
        Height = defaultHeight;
        Left = workArea.Left + ((workArea.Width - Width) / 2);
        Top = workArea.Top + ((workArea.Height - Height) / 2);
    }

    private async Task RefreshLinesAsync()
    {
        if (_sessionId is not { } id)
        {
            return;
        }

        var lines = await _services.Meetings.ListLinesAsync(id);
        _lines.ItemsSource = lines.Select(line => new LineItem(line)).ToList();
        _aiFormatButton.IsEnabled = lines.Count > 0;
    }

    private async void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _savingLine || _sessionId is not { } id)
        {
            return;
        }

        e.Handled = true;
        var parsed = MeetingLineParser.Parse(_input.Text);
        if (parsed is null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        _savingLine = true;
        try
        {
            await _services.Meetings.AddLineAsync(id, parsed.Value.Marker, parsed.Value.Text, DateTimeOffset.Now);
            _input.Clear();
            await RefreshLinesAsync();
        }
        catch (Exception exception)
        {
            ErrorLog.Log("MeetingCaptureWindow.AddLine", exception);
            MessageBox.Show(
                this,
                $"記録できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _savingLine = false;
            _input.Focus();
        }
    }

    private async void OnLineDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_lines.SelectedItem is not LineItem item)
        {
            return;
        }

        var dialog = new MeetingLineEditWindow(RestoreMarkerPrefix(item.Line)) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var parsed = MeetingLineParser.Parse(dialog.ResultText);
        if (parsed is null)
        {
            MessageBox.Show(this, "内容を入力してください。", "WorkLog AI");
            return;
        }

        try
        {
            await _services.Meetings.UpdateLineAsync(item.Line.Id, parsed.Value.Marker, parsed.Value.Text);
            await RefreshLinesAsync();
        }
        catch (Exception exception)
        {
            ErrorLog.Log("MeetingCaptureWindow.UpdateLine", exception);
            MessageBox.Show(
                this,
                $"更新できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnLinesPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || _lines.SelectedItem is not LineItem item)
        {
            return;
        }

        e.Handled = true;
        var answer = MessageBox.Show(
            this,
            "この行を削除しますか？",
            "WorkLog AI",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _services.Meetings.DeleteLineAsync(item.Line.Id);
            await RefreshLinesAsync();
        }
        catch (Exception exception)
        {
            ErrorLog.Log("MeetingCaptureWindow.DeleteLine", exception);
            MessageBox.Show(
                this,
                $"削除できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void EndMeetingAsync(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_titleBox.Text))
        {
            MessageBox.Show(this, "件名を入力してください。", "WorkLog AI");
            return;
        }

        if (_sessionId is not { } id)
        {
            return;
        }

        _endButton.IsEnabled = false;
        _aiFormatButton.IsEnabled = false;
        try
        {
            await _services.Meetings.UpdateSessionAsync(
                id,
                _titleBox.Text.Trim(),
                _participantsBox.Text.Trim(),
                CurrentKind(),
                MeetingStatus.Closed,
                DateTimeOffset.Now);
            _ended = true;

            await OfferAiFormatOrRawExportAsync(id);

            try
            {
                await _services.SettingsStore.SetAsync(AppSettingKeys.MeetingWindowPlacement, FormatPlacement());
            }
            catch (Exception exception)
            {
                ErrorLog.Log("MeetingCaptureWindow.SavePlacement", exception);
            }

            _closeArmed = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorLog.Log("MeetingCaptureWindow.EndMeeting", exception);
            MessageBox.Show(
                this,
                $"会議を終了できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _endButton.IsEnabled = true;
            _aiFormatButton.IsEnabled = _lines.Items.Count > 0;
        }
    }

    /// <summary>Called right after 会議終了 persists the session as closed. When an
    /// API key is configured, asks once whether to run the exact same AI整形 flow as
    /// the button (<see cref="RunAiFormatFlowAsync"/>); declining, or having no API
    /// key at all, falls back to the raw-log-only export offered in part 1.</summary>
    private async Task OfferAiFormatOrRawExportAsync(Guid sessionId)
    {
        var lines = await _services.Meetings.ListLinesAsync(sessionId);
        if (lines.Count == 0)
        {
            return;
        }

        var hasCredential = !string.IsNullOrWhiteSpace(
            await _services.Credentials.GetAsync(CredentialTargets.OpenAiApiKey));
        if (hasCredential)
        {
            var answer = MessageBox.Show(
                this,
                "AI整形しますか？",
                "WorkLog AI",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                await RunAiFormatFlowAsync();
                return;
            }
        }

        await OfferRawMarkdownExportAsync(sessionId);
    }

    private async Task OfferRawMarkdownExportAsync(Guid sessionId)
    {
        var settings = await _services.Settings.LoadAsync();
        if (string.IsNullOrWhiteSpace(settings.MeetingOutputFolder))
        {
            MessageBox.Show(this, "議事録出力フォルダ未設定（書き出しスキップ）。", "WorkLog AI");
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Markdownを書き出しますか？",
            "WorkLog AI",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var lines = await _services.Meetings.ListLinesAsync(sessionId);
            var session = await _services.Meetings.GetSessionAsync(sessionId)
                ?? throw new InvalidOperationException("セッションが見つかりません。");
            var markdown = MeetingMarkdownBuilder.Build(session, lines, null, settings.MeetingIncludeRawLog);
            var path = new MeetingMarkdownWriter().Write(
                settings.MeetingOutputFolder,
                DateOnly.FromDateTime(session.StartedAt.DateTime),
                session.Title,
                markdown);
            ExportResultPrompt.OfferToOpen(this, path);
        }
        catch (Exception exception)
        {
            ErrorLog.Log("MeetingCaptureWindow.ExportMarkdown", exception);
            MessageBox.Show(
                this,
                $"Markdownを書き出せませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AiFormatButtonClick(object sender, RoutedEventArgs e)
    {
        _aiFormatButton.IsEnabled = false;
        try
        {
            await RunAiFormatFlowAsync();
        }
        finally
        {
            _aiFormatButton.IsEnabled = _lines.Items.Count > 0;
        }
    }

    /// <summary>
    /// The one AI整形 flow, reused verbatim by both the AI整形 button and 会議終了's
    /// follow-up prompt: check for an API key, show the mandatory
    /// <see cref="MeetingSendPreviewWindow"/>, call the AI formatting client, confirm
    /// before overwriting an existing summary, persist it, then export Markdown
    /// (formatted result + raw log per settings) when an output folder is configured.
    /// Returns true only once a summary has actually been saved.
    /// </summary>
    private async Task<bool> RunAiFormatFlowAsync()
    {
        if (_sessionId is not { } id)
        {
            return false;
        }

        try
        {
            var hasCredential = !string.IsNullOrWhiteSpace(
                await _services.Credentials.GetAsync(CredentialTargets.OpenAiApiKey));
            if (!hasCredential)
            {
                MessageBox.Show(
                    this,
                    "OpenAI APIキーが設定されていません。設定画面でCredential Managerへ保存してください。",
                    "WorkLog AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var lines = await _services.Meetings.ListLinesAsync(id);
            if (lines.Count == 0)
            {
                MessageBox.Show(this, "記録がありません。", "WorkLog AI");
                return false;
            }

            var session = await _services.Meetings.GetSessionAsync(id)
                ?? throw new InvalidOperationException("セッションが見つかりません。");
            var settings = await _services.Settings.LoadAsync();

            var preview = new MeetingSendPreviewWindow(session, lines, settings.OpenAiModel) { Owner = this };
            if (preview.ShowDialog() != true)
            {
                return false;
            }

            var includedLines = preview.IncludedLines;
            if (includedLines.Count == 0)
            {
                MessageBox.Show(this, "送信する行がありません。", "WorkLog AI");
                return false;
            }

            var result = await _services.FormatMeetingAsync(session, includedLines);
            if (!result.Succeeded || result.Formatted is not { } formatted)
            {
                MessageBox.Show(
                    this,
                    result.Error ?? "AI整形に失敗しました。",
                    "WorkLog AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var existing = await _services.Meetings.GetLatestSummaryAsync(id);
            if (existing is not null)
            {
                var overwrite = MessageBox.Show(
                    this,
                    "既存の整形結果を上書きしますか？",
                    "WorkLog AI",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes)
                {
                    return false;
                }
            }

            await _services.Meetings.SaveSummaryAsync(
                id,
                MeetingFormattedResultJson.Serialize(formatted),
                formatted.SummaryLine);

            await ExportFormattedMarkdownAsync(session, lines, formatted, settings);
            return true;
        }
        catch (Exception exception)
        {
            ErrorLog.Log("MeetingCaptureWindow.AiFormat", exception);
            MessageBox.Show(
                this,
                $"AI整形に失敗しました。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async Task ExportFormattedMarkdownAsync(
        MeetingSession session,
        IReadOnlyList<MeetingLine> lines,
        MeetingFormattedResult formatted,
        AppSettingsSnapshot settings)
    {
        if (string.IsNullOrWhiteSpace(settings.MeetingOutputFolder))
        {
            MessageBox.Show(
                this,
                "整形結果を保存しました。週報には『週報候補を生成…』を実行すると反映されます。\n" +
                "（出力フォルダ未設定のためMarkdownは書き出されません。）",
                "WorkLog AI");
            return;
        }

        try
        {
            var markdown = MeetingMarkdownBuilder.Build(session, lines, formatted, settings.MeetingIncludeRawLog);
            var path = new MeetingMarkdownWriter().Write(
                settings.MeetingOutputFolder,
                DateOnly.FromDateTime(session.StartedAt.DateTime),
                session.Title,
                markdown);
            ExportResultPrompt.OfferToOpen(this, path);
        }
        catch (Exception exception)
        {
            ErrorLog.Log("MeetingCaptureWindow.ExportFormattedMarkdown", exception);
            MessageBox.Show(
                this,
                $"Markdownを書き出せませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_closeArmed)
        {
            return;
        }

        e.Cancel = true;

        try
        {
            try
            {
                await _services.SettingsStore.SetAsync(AppSettingKeys.MeetingWindowPlacement, FormatPlacement());
            }
            catch (Exception exception)
            {
                ErrorLog.Log("MeetingCaptureWindow.SavePlacement", exception);
            }

            if (_sessionId is { } id && !_ended)
            {
                try
                {
                    await _services.Meetings.UpdateSessionAsync(
                        id,
                        _titleBox.Text.Trim(),
                        _participantsBox.Text.Trim(),
                        CurrentKind(),
                        MeetingStatus.Draft,
                        null);
                }
                catch (Exception exception)
                {
                    ErrorLog.Log("MeetingCaptureWindow.PersistDraft", exception);
                }
            }
        }
        catch (Exception exception)
        {
            // Defense in depth: a failed draft save must never trap the user in an
            // unclosable window. The dispatcher-deferred Close below always runs.
            ErrorLog.Log("MeetingCaptureWindow.Closing", exception);
        }
        finally
        {
            _closeArmed = true;
            // Re-entering Close() synchronously from inside this still-active Closing
            // handler is rejected by WPF whenever every preceding await above completed
            // synchronously (e.g. a warm SQLite connection). Deferring to a fresh
            // dispatcher frame guarantees Close() runs only after this handler returns.
            await Dispatcher.InvokeAsync(Close, DispatcherPriority.Background);
        }
    }

    private string FormatPlacement() => WindowPlacementFormat.Format(Left, Top, Width, Height);

    private MeetingKind CurrentKind() =>
        _kindBox.SelectedIndex >= 0 && _kindBox.SelectedIndex < KindOptions.Length
            ? KindOptions[_kindBox.SelectedIndex].Kind
            : MeetingKind.Meeting;

    private static string RestoreMarkerPrefix(MeetingLine line) => line.Marker switch
    {
        MeetingMarker.Todo => $"@{line.Text}",
        MeetingMarker.Decision => $"!{line.Text}",
        _ => line.Text
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 6, 0, 2)
    };

    private sealed record LineItem(MeetingLine Line)
    {
        public string Label
        {
            get
            {
                var badge = Line.Marker switch
                {
                    MeetingMarker.Todo => "[宿題] ",
                    MeetingMarker.Decision => "[決定] ",
                    _ => string.Empty
                };
                return $"{Line.LoggedAt:HH:mm} {badge}{Line.Text}";
            }
        }
    }
}

/// <summary>
/// Pure "L,T,W,H" codec for the meeting window's remembered placement. Tolerant of
/// missing/garbled state — TryParse simply returns false and the caller falls back
/// to a centered default size.
/// </summary>
internal static class WindowPlacementFormat
{
    public static string Format(double left, double top, double width, double height) =>
        FormattableString.Invariant($"{left:0.##},{top:0.##},{width:0.##},{height:0.##}");

    public static bool TryParse(string? value, out double left, out double top, out double width, out double height)
    {
        left = top = width = height = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(',');
        if (parts.Length != 4)
        {
            return false;
        }

        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
            && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out height);
    }
}
