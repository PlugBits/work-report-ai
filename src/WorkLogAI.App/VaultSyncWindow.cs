using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace WorkLogAI.App;

/// <summary>Which date range the tray's **Obsidianへ同期…** action should cover.</summary>
public enum VaultSyncRangeKind
{
    ThisWeek,
    LastWeek,
    Backfill
}

/// <summary>
/// Small dialog for the tray **Obsidianへ同期…** action, modeled on
/// <see cref="WeekPickerWindow"/>'s dumb-picker style: a range combo (今週/先週/
/// 全期間(バックフィル)) plus an "AIで固有名詞を抽出して辞書を更新する" checkbox,
/// disabled and unchecked with an explanatory note when no OpenAI API key is stored
/// (<paramref name="hasApiKey"/>). The caller resolves the range's concrete
/// <see cref="DateOnly"/> bounds and the extraction send-preview confirmation
/// afterward — this window only decides the user's two choices.
/// </summary>
public sealed class VaultSyncWindow : Window
{
    private static readonly (string Label, VaultSyncRangeKind Kind)[] Options =
    [
        ("今週", VaultSyncRangeKind.ThisWeek),
        ("先週", VaultSyncRangeKind.LastWeek),
        ("全期間(バックフィル)", VaultSyncRangeKind.Backfill)
    ];

    private readonly ComboBox _combo = new();
    private readonly CheckBox _extraction = new() { Content = "AIで固有名詞を抽出して辞書を更新する" };

    public VaultSyncWindow(bool hasApiKey)
    {
        AppTheme.Apply(this);
        Title = "Obsidianへ同期 - WorkLog AI";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "同期する期間を選択してください。",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _combo.ItemsSource = Options.Select(option => option.Label).ToList();
        _combo.SelectedIndex = 0;
        _combo.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(_combo);

        _extraction.IsChecked = hasApiKey;
        _extraction.IsEnabled = hasApiKey;
        _extraction.Margin = new Thickness(0, 0, 0, 4);
        root.Children.Add(_extraction);
        if (!hasApiKey)
        {
            root.Children.Add(new TextBlock
            {
                Text = "OpenAI APIキーが未設定のため無効です。",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
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

    public VaultSyncRangeKind SelectedRange { get; private set; }

    public bool RunExtraction { get; private set; }

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
        SelectedRange = Options[_combo.SelectedIndex].Kind;
        RunExtraction = _extraction.IsChecked == true;
        DialogResult = true;
    }
}
