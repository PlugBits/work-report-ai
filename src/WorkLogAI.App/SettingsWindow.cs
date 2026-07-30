using System.Windows;
using System.Windows.Controls;
using WorkLogAI.Core;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkLogAI.App;

public sealed class SettingsWindow : Window
{
    private readonly AppSettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly IStartupRegistrar _startupRegistrar;
    private readonly bool _sampleMode;
    private readonly TextBox _company = new();
    private readonly TextBox _employee = new();
    private readonly ComboBox _weekStart = new();
    private readonly TextBox _outputDirectory = new();
    private readonly TextBox _repositories = MultiLineTextBox();
    private readonly TextBox _codexFolder = new();
    private readonly TextBox _recentFolders = MultiLineTextBox();
    private readonly TextBox _model = new();
    private readonly CheckBox _preview = new() { Content = "送信前にプレビューを表示" };
    private readonly PasswordBox _apiKey = new();
    private readonly CheckBox _removeApiKey = new() { Content = "保存済みAPIキーを削除" };
    private readonly TextBlock _credentialStatus = new();
    private readonly CheckBox _reminderEnabled = new() { Content = "平日夕方にメモ0件をリマインド" };
    private readonly TextBox _reminderTime = new();
    private readonly CheckBox _autoStart = new() { Content = "Windowsログイン時に自動起動する" };

    public SettingsWindow(
        AppSettingsService settings,
        ICredentialStore credentials,
        IStartupRegistrar startupRegistrar,
        bool sampleMode = false)
    {
        _settings = settings;
        _credentials = credentials;
        _startupRegistrar = startupRegistrar;
        _sampleMode = sampleMode;
        Title = "設定 - WorkLog AI";
        Width = 560;
        Height = 840;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 17; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddRow(grid, 0, "会社名", _company);
        AddRow(grid, 1, "氏名", _employee);
        _weekStart.ItemsSource = Enum.GetValues<DayOfWeek>();
        AddRow(grid, 2, "週の開始曜日", _weekStart);
        AddRow(grid, 3, "Excel保存先", _outputDirectory);
        AddRow(grid, 4, "ホットキー", new TextBlock
        {
            Text = "Ctrl + Alt + W",
            Margin = new Thickness(4, 8, 4, 8)
        });
        AddRow(grid, 5, "ローカルGit\n(1行1パス)", _repositories);
        AddRow(grid, 6, "Codexセッション", _codexFolder);
        AddRow(grid, 7, "更新ファイル対象\n(1行1パス)", _recentFolders);
        AddRow(grid, 8, "収集範囲", new TextBlock
        {
            Text = "設定したローカルフォルダーのみ。ファイル本文・diff・コマンド引数は収集しません。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 8)
        });
        AddRow(grid, 9, "OpenAIモデル", _model);
        AddRow(grid, 10, "送信前確認", _preview);
        AddRow(grid, 11, "OpenAI APIキー", _apiKey);
        AddRow(grid, 12, "キー状態", new StackPanel
        {
            Children = { _credentialStatus, _removeApiKey }
        });
        AddRow(grid, 13, "秘密情報", new TextBlock
        {
            Text = "APIキーはWindows Credential Managerだけに保存します。既存キーは表示しません。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 8)
        });
        AddRow(grid, 14, "メモ0件リマインド", _reminderEnabled);
        AddRow(grid, 15, "リマインド時刻(HH:mm)", _reminderTime);
        _autoStart.IsEnabled = !_sampleMode;
        AddRow(grid, 16, "自動起動", _autoStart);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var save = new Button { Content = "保存", IsDefault = true, Padding = new Thickness(18, 6, 18, 6) };
        save.Click += SaveAsync;
        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 17);
        Grid.SetColumnSpan(buttons, 2);

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(buttons);
        Content = new ScrollViewer
        {
            Content = grid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Loaded += LoadAsync;
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement value)
    {
        var labelControl = new TextBlock
        {
            Text = label,
            Margin = new Thickness(4, 8, 12, 8),
            VerticalAlignment = VerticalAlignment.Center
        };
        value.Margin = new Thickness(4, 6, 4, 6);
        Grid.SetRow(labelControl, row);
        Grid.SetColumn(labelControl, 0);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(labelControl);
        grid.Children.Add(value);
    }

    private async void LoadAsync(object sender, RoutedEventArgs e)
    {
        var values = await _settings.LoadAsync();
        _company.Text = values.CompanyName;
        _employee.Text = values.EmployeeName;
        _weekStart.SelectedItem = values.WeekStartsOn;
        _outputDirectory.Text = values.ExcelOutputDirectory;
        _repositories.Text = string.Join(Environment.NewLine, values.LocalRepositoryPaths);
        _codexFolder.Text = values.CodexSessionFolder ?? string.Empty;
        _recentFolders.Text = string.Join(Environment.NewLine, values.RecentFileFolders);
        _model.Text = values.OpenAiModel;
        _preview.IsChecked = values.SendPreviewEnabled;
        _reminderEnabled.IsChecked = values.ReminderEnabled;
        _reminderTime.Text = values.ReminderTime.ToString("HH:mm");
        try
        {
            _credentialStatus.Text = string.IsNullOrWhiteSpace(
                await _credentials.GetAsync(CredentialTargets.OpenAiApiKey))
                ? "未設定"
                : "設定済み";
        }
        catch
        {
            _credentialStatus.Text = "Credential Managerを利用できません";
        }

        try
        {
            _autoStart.IsChecked = _startupRegistrar.IsEnabled();
        }
        catch
        {
            _autoStart.IsChecked = false;
        }
        _autoStart.IsEnabled = !_sampleMode;
    }

    private async void SaveAsync(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_company.Text)
            || string.IsNullOrWhiteSpace(_employee.Text)
            || string.IsNullOrWhiteSpace(_outputDirectory.Text)
            || string.IsNullOrWhiteSpace(_model.Text)
            || _weekStart.SelectedItem is not DayOfWeek weekStartsOn)
        {
            MessageBox.Show(this, "すべての項目を入力してください。", "WorkLog AI");
            return;
        }

        if (!TimeOnly.TryParseExact(_reminderTime.Text.Trim(), "HH:mm", out var reminderTime))
        {
            MessageBox.Show(this, "リマインド時刻はHH:mm形式で入力してください。", "WorkLog AI");
            return;
        }

        try
        {
            await _settings.SaveAsync(new AppSettingsSnapshot(
                _company.Text.Trim(),
                _employee.Text.Trim(),
                weekStartsOn,
                _outputDirectory.Text.Trim(),
                ParsePaths(_repositories.Text),
                string.IsNullOrWhiteSpace(_codexFolder.Text) ? null : _codexFolder.Text.Trim(),
                ParsePaths(_recentFolders.Text),
                _model.Text.Trim(),
                _preview.IsChecked == true,
                _reminderEnabled.IsChecked == true,
                reminderTime));
            if (_removeApiKey.IsChecked == true)
            {
                await _credentials.DeleteAsync(CredentialTargets.OpenAiApiKey);
            }
            else if (!string.IsNullOrWhiteSpace(_apiKey.Password))
            {
                await _credentials.SetAsync(CredentialTargets.OpenAiApiKey, _apiKey.Password);
                _apiKey.Clear();
            }
        }
        catch
        {
            _apiKey.Clear();
            MessageBox.Show(
                this,
                "設定を保存できませんでした。Credential Managerが利用可能か確認してください。",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (!_sampleMode)
        {
            try
            {
                if (_autoStart.IsChecked == true)
                {
                    _startupRegistrar.Enable();
                }
                else
                {
                    _startupRegistrar.Disable();
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "WorkLog AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        DialogResult = true;
    }

    private static TextBox MultiLineTextBox() => new()
    {
        AcceptsReturn = true,
        Height = 58,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private static IReadOnlyList<string> ParsePaths(string value) =>
        value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
