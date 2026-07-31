using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace WorkLogAI.App;

/// <summary>
/// Mandatory line-level consent dialog shown before any meeting content is sent to
/// the AI formatting endpoint. Every line is listed with a checkbox, all checked by
/// default; unchecked lines are excluded from the outbound payload only — SQLite is
/// never modified here (see MeetingFormatPayloadBuilder). There is no setting that
/// bypasses this dialog — it is constructed directly in the AI整形 flow, with no
/// alternate code path around it.
/// </summary>
public sealed class MeetingSendPreviewWindow : Window
{
    private readonly MeetingSession _session;
    private readonly string _model;
    private readonly List<(MeetingLine Line, CheckBox Box)> _items;
    private readonly TextBlock _header = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
    };

    public MeetingSendPreviewWindow(MeetingSession session, IReadOnlyList<MeetingLine> lines, string model)
    {
        AppTheme.Apply(this);
        _session = session;
        _model = model;

        Title = "送信前プレビュー - WorkLog AI";
        Width = 480;
        Height = 560;
        MinWidth = 360;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        ResizeMode = ResizeMode.CanResize;

        var root = new DockPanel { Margin = new Thickness(16) };

        DockPanel.SetDock(_header, Dock.Top);
        root.Children.Add(_header);

        var note = new TextBlock
        {
            Text = "チェックを外した行は送信されません。",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var ok = new Button { Content = "送信", IsDefault = true, Padding = new Thickness(14, 6, 14, 6) };
        ok.Style = (Style)System.Windows.Application.Current.FindResource("AccentButton");
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var linesPanel = new StackPanel();
        _items = lines
            .OrderBy(line => line.LineNo)
            .Select(line =>
            {
                var box = new CheckBox
                {
                    Content = MeetingLineFormatter.FormatWithTime(line),
                    IsChecked = true,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                box.Checked += (_, _) => RefreshHeader();
                box.Unchecked += (_, _) => RefreshHeader();
                return (line, box);
            })
            .ToList();
        foreach (var (_, box) in _items)
        {
            linesPanel.Children.Add(box);
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = linesPanel
        };
        root.Children.Add(scroll);

        Content = root;
        RefreshHeader();
    }

    /// <summary>Lines the user left checked, in original order. Only populated
    /// meaningfully after the dialog closes with <see cref="Window.DialogResult"/> true.</summary>
    public IReadOnlyList<MeetingLine> IncludedLines =>
        _items.Where(item => item.Box.IsChecked == true).Select(item => item.Line).ToArray();

    private void RefreshHeader()
    {
        var included = IncludedLines;
        var payload = MeetingFormatPayloadBuilder.Build(_session, included);
        _header.Text =
            $"モデル: {_model}\n" +
            $"行数: {included.Count}/{_items.Count}\n" +
            $"送信サイズ（概算）: {payload.Utf8ByteCount / 1024.0:0.0} KB";
    }
}
