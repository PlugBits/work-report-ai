using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
/// leaves the session as a draft; 会議終了 closes it and optionally exports a
/// Markdown file (raw log only — AI formatting is out of scope for this phase, see
/// MeetingMarkdownBuilder's formatted:null path).
/// </summary>
public sealed class MeetingCaptureWindow : Window
{
    private static readonly (string Label, MeetingKind Kind)[] KindOptions =
    [
        ("会議", MeetingKind.Meeting),
        ("来客", MeetingKind.Visitor),
        ("電話", MeetingKind.Call)
    ];

    private readonly IMeetingRepository _meetings;
    private readonly ISettingsStore _settingsStore;
    private readonly AppSettingsService _settings;
    private readonly MeetingSession? _initialSession;

    private readonly TextBox _titleBox = new();
    private readonly TextBox _participantsBox = new();
    private readonly ComboBox _kindBox = new();
    private readonly ListBox _lines = new() { DisplayMemberPath = nameof(LineItem.Label) };
    private readonly TextBox _input = new() { FontSize = 14, AcceptsReturn = false, MaxLines = 1 };
    private readonly Button _endButton = new() { Content = "会議終了", Padding = new Thickness(14, 6, 14, 6) };

    private Guid? _sessionId;
    private bool _ended;
    private bool _closeArmed;
    private bool _savingLine;

    public MeetingCaptureWindow(
        IMeetingRepository meetings,
        ISettingsStore settingsStore,
        AppSettingsService settings,
        MeetingSession? existingSession)
    {
        _meetings = meetings;
        _settingsStore = settingsStore;
        _settings = settings;
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
                var created = await _meetings.CreateSessionAsync(
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
            return await _settingsStore.GetAsync(AppSettingKeys.MeetingWindowPlacement);
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

        var lines = await _meetings.ListLinesAsync(id);
        _lines.ItemsSource = lines.Select(line => new LineItem(line)).ToList();
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
            await _meetings.AddLineAsync(id, parsed.Value.Marker, parsed.Value.Text, DateTimeOffset.Now);
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
            await _meetings.UpdateLineAsync(item.Line.Id, parsed.Value.Marker, parsed.Value.Text);
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
            await _meetings.DeleteLineAsync(item.Line.Id);
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
        try
        {
            await _meetings.UpdateSessionAsync(
                id,
                _titleBox.Text.Trim(),
                _participantsBox.Text.Trim(),
                CurrentKind(),
                MeetingStatus.Closed,
                DateTimeOffset.Now);
            _ended = true;

            await OfferMarkdownExportAsync(id);

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
        }
    }

    private async Task OfferMarkdownExportAsync(Guid sessionId)
    {
        var settings = await _settings.LoadAsync();
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
            var lines = await _meetings.ListLinesAsync(sessionId);
            var session = await _meetings.GetSessionAsync(sessionId)
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

    private async void OnClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_closeArmed)
        {
            return;
        }

        e.Cancel = true;

        try
        {
            await _settingsStore.SetAsync(AppSettingKeys.MeetingWindowPlacement, FormatPlacement());
        }
        catch (Exception exception)
        {
            ErrorLog.Log("MeetingCaptureWindow.SavePlacement", exception);
        }

        if (_sessionId is { } id && !_ended)
        {
            try
            {
                await _meetings.UpdateSessionAsync(
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

        _closeArmed = true;
        Close();
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
