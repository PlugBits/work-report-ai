using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkLogAI.Core;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace WorkLogAI.App;

public sealed class WeekPickerWindow : Window
{
    private readonly ComboBox _combo = new();
    private readonly IReadOnlyList<WeekOption> _options;

    public WeekPickerWindow(DateOnly today, DayOfWeek weekStartsOn)
    {
        _options = WeekOptionBuilder.Build(today, weekStartsOn, 8);
        Title = "候補を生成 - WorkLog AI";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "対象週を選択してください。",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _combo.ItemsSource = _options.Select(DescribeOption).ToList();
        _combo.SelectedIndex = 0;
        _combo.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(_combo);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var ok = new Button { Content = "OK", IsDefault = true, Padding = new Thickness(18, 6, 18, 6) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => _combo.Focus();
    }

    public WeekRange? SelectedRange { get; private set; }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_combo.IsDropDownOpen)
        {
            e.Handled = true;
            Accept();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void Accept()
    {
        SelectedRange = _options[_combo.SelectedIndex].Range;
        DialogResult = true;
    }

    private static string DescribeOption(WeekOption option) => option.Kind switch
    {
        WeekOptionKind.ThisWeek => $"今週 {FormatRange(option.Range)}",
        WeekOptionKind.LastWeek => $"先週 {FormatRange(option.Range)}",
        _ => FormatRange(option.Range)
    };

    private static string FormatRange(WeekRange range) =>
        $"{range.Start:yyyy/MM/dd}〜{range.End:MM/dd}";
}
