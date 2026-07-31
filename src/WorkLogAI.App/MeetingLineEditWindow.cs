using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using WorkLogAI.Core;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkLogAI.App;

/// <summary>
/// Simple modal prompt for editing a single confirmed meeting line. The caller
/// passes in the raw text with the marker prefix restored (e.g. "@buy milk") and
/// re-parses <see cref="ResultText"/> with <see cref="MeetingLineParser"/> after the
/// dialog closes with OK.
/// </summary>
public sealed class MeetingLineEditWindow : Window
{
    private readonly TextBox _input = new();

    public MeetingLineEditWindow(string rawText)
    {
        Title = "行を編集 - WorkLog AI";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "行頭 @ = 宿題 / 行頭 ! = 決定",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 6)
        });

        _input.Text = rawText;
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
