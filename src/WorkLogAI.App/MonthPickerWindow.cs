using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkLogAI.Core;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace WorkLogAI.App;

public sealed class MonthPickerWindow : Window
{
    private readonly ComboBox _combo = new();
    private readonly IReadOnlyList<MonthOption> _options;

    public MonthPickerWindow(DateOnly today)
    {
        AppTheme.Apply(this);
        _options = MonthOptionBuilder.Build(today, 12);
        Title = "月次まとめを出力 - WorkLog AI";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "対象月を選択してください。",
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
        ok.Style = (Style)System.Windows.Application.Current.FindResource("AccentButton");
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

    public MonthOption? SelectedMonth { get; private set; }

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
        SelectedMonth = _options[_combo.SelectedIndex];
        DialogResult = true;
    }

    private static string DescribeOption(MonthOption option) => $"{option.Year}年{option.Month}月";
}
