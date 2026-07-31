using System.Windows;
using System.Windows.Controls;
using WorkLogAI.Core;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;

namespace WorkLogAI.App;

/// <summary>
/// Shown when 議事録を開始 is triggered while draft meeting sessions already exist.
/// Lets the user resume one of them or start a fresh session.
/// </summary>
public sealed class MeetingSessionChooserWindow : Window
{
    private readonly ListBox _list = new() { DisplayMemberPath = nameof(DraftItem.Label) };

    public MeetingSessionChooserWindow(IReadOnlyList<MeetingSession> drafts)
    {
        AppTheme.Apply(this);
        Title = "議事録を開始 - WorkLog AI";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "下書きの議事録があります。再開するか、新規作成してください。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _list.ItemsSource = drafts.Select(session => new DraftItem(session)).ToList();
        _list.SelectedIndex = drafts.Count > 0 ? 0 : -1;
        _list.MaxHeight = 260;
        _list.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(_list);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var resume = new Button { Content = "再開", IsDefault = true, Padding = new Thickness(14, 6, 14, 6) };
        resume.Click += (_, _) =>
        {
            if (_list.SelectedItem is not DraftItem item)
            {
                return;
            }

            SelectedSession = item.Session;
            DialogResult = true;
        };
        var createNew = new Button
        {
            Content = "新規作成",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        createNew.Click += (_, _) =>
        {
            SelectedSession = null;
            DialogResult = true;
        };
        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        buttons.Children.Add(resume);
        buttons.Children.Add(createNew);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
    }

    /// <summary>Null means "start a fresh session"; non-null means "resume this one".</summary>
    public MeetingSession? SelectedSession { get; private set; }

    private sealed record DraftItem(MeetingSession Session)
    {
        public string Label =>
            $"{Session.StartedAt:yyyy/MM/dd HH:mm} " +
            (string.IsNullOrWhiteSpace(Session.Title) ? "(無題)" : Session.Title);
    }
}
