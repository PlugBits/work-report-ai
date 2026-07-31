using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkLogAI.App;

/// <summary>
/// Simple modal prompt for editing a single quick note's text, opened by
/// double-clicking a non-deleted row in <see cref="HistoryWindow"/>. Mirrors
/// <see cref="MeetingLineEditWindow"/>'s minimal single-field dialog style.
/// </summary>
public sealed class QuickNoteEditWindow : Window
{
    private readonly TextBox _input = new();

    public QuickNoteEditWindow(string text)
    {
        AppTheme.Apply(this);
        Title = "記録を編集 - WorkLog AI";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;

        var root = new StackPanel { Margin = new Thickness(16) };

        _input.Text = text;
        _input.Margin = new Thickness(0, 0, 0, 12);
        _input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Accept();
            }
        };
        root.Children.Add(_input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(14, 6, 14, 6) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    public string ResultText { get; private set; } = string.Empty;

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(_input.Text))
        {
            MessageBox.Show(this, "内容を入力してください。", "WorkLog AI");
            return;
        }

        ResultText = _input.Text;
        DialogResult = true;
    }
}
