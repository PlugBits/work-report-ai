using System.Windows;
using System.Windows.Controls;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;
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
    private readonly TextBox _reportTitle = new();
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
    private readonly TextBox _graphClientId = new();
    private readonly TextBox _graphTenantId = new();
    private readonly CheckBox _graphMailEnabled = new() { Content = "送信済みメールを収集" };
    private readonly CheckBox _graphCalendarEnabled = new() { Content = "カレンダーを収集" };
    private readonly Button _graphSignIn = new() { Content = "Microsoftサインイン", Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _graphSignOut = new() { Content = "サインアウト", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _graphStatus = new() { Text = "未サインイン", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
    private readonly Func<string, string, GraphAuthService> _graphAuthFactory;
    private readonly TextBox _meetingOutputFolder = new();
    private readonly CheckBox _meetingIncludeRawLog = new() { Content = "生ログをMDに同梱" };
    private readonly CheckBox _meetingHotkeyEnabled = new() { Content = "Ctrl+Alt+M を有効化（再起動後に反映）" };

    public SettingsWindow(
        AppSettingsService settings,
        ICredentialStore credentials,
        IStartupRegistrar startupRegistrar,
        Func<string, string, GraphAuthService> graphAuthFactory,
        bool sampleMode = false)
    {
        _settings = settings;
        _credentials = credentials;
        _startupRegistrar = startupRegistrar;
        _graphAuthFactory = graphAuthFactory;
        _sampleMode = sampleMode;
        Title = "設定 - WorkLog AI";
        Width = 560;
        Height = 1130;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 26; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddRow(grid, 0, "会社名", _company);
        AddRow(grid, 1, "氏名", _employee);
        AddRow(grid, 2, "週報タイトル", _reportTitle);
        _weekStart.ItemsSource = Enum.GetValues<DayOfWeek>();
        AddRow(grid, 3, "週の開始曜日", _weekStart);
        AddRow(grid, 4, "Excel保存先", _outputDirectory);
        AddRow(grid, 5, "ホットキー", new TextBlock
        {
            Text = "Ctrl + Alt + W",
            Margin = new Thickness(4, 8, 4, 8)
        });
        AddRow(grid, 6, "ローカルGit\n(1行1パス)", _repositories);
        AddRow(grid, 7, "Codexセッション", _codexFolder);
        AddRow(grid, 8, "更新ファイル対象\n(1行1パス)", _recentFolders);
        AddRow(grid, 9, "収集範囲", new TextBlock
        {
            Text = "設定したローカルフォルダーのみ。ファイル本文・diff・コマンド引数は収集しません。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 8)
        });
        AddRow(grid, 10, "OpenAIモデル", _model);
        AddRow(grid, 11, "送信前確認", _preview);
        AddRow(grid, 12, "OpenAI APIキー", _apiKey);
        AddRow(grid, 13, "キー状態", new StackPanel
        {
            Children = { _credentialStatus, _removeApiKey }
        });
        AddRow(grid, 14, "秘密情報", new TextBlock
        {
            Text = "APIキーはWindows Credential Managerだけに保存します。既存キーは表示しません。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 8)
        });
        AddRow(grid, 15, "メモ0件リマインド", _reminderEnabled);
        AddRow(grid, 16, "リマインド時刻(HH:mm)", _reminderTime);
        _autoStart.IsEnabled = !_sampleMode;
        AddRow(grid, 17, "自動起動", _autoStart);
        AddRow(grid, 18, "クライアントID", _graphClientId);
        AddRow(grid, 19, "テナントID", _graphTenantId);
        AddRow(grid, 20, "Outlookメール", _graphMailEnabled);
        AddRow(grid, 21, "Outlookカレンダー", _graphCalendarEnabled);
        _graphSignIn.Click += GraphSignInAsync;
        _graphSignOut.Click += GraphSignOutAsync;
        _graphClientId.TextChanged += (_, _) => UpdateGraphSignInEnabled();
        AddRow(grid, 22, "Microsoftサインイン", new StackPanel
        {
            Children =
            {
                _graphStatus,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { _graphSignIn, _graphSignOut }
                }
            }
        });
        AddRow(grid, 23, "議事録出力フォルダ", _meetingOutputFolder);
        AddRow(grid, 24, "議事録Markdown", _meetingIncludeRawLog);
        AddRow(grid, 25, "議事録ホットキー", _meetingHotkeyEnabled);

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
        Grid.SetRow(buttons, 26);
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
        _reportTitle.Text = values.ReportTitle;
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

        _graphClientId.Text = values.GraphClientId;
        _graphTenantId.Text = values.GraphTenantId;
        _graphMailEnabled.IsChecked = values.GraphMailEnabled;
        _graphCalendarEnabled.IsChecked = values.GraphCalendarEnabled;
        _meetingOutputFolder.Text = values.MeetingOutputFolder;
        _meetingIncludeRawLog.IsChecked = values.MeetingIncludeRawLog;
        _meetingHotkeyEnabled.IsChecked = values.MeetingHotkeyEnabled;
        UpdateGraphSignInEnabled();
        await RefreshGraphStatusAsync();
    }

    private void UpdateGraphSignInEnabled()
    {
        _graphSignIn.IsEnabled = !string.IsNullOrWhiteSpace(_graphClientId.Text);
    }

    private async Task RefreshGraphStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(_graphClientId.Text))
        {
            _graphStatus.Text = "未サインイン";
            return;
        }

        try
        {
            var auth = _graphAuthFactory(_graphClientId.Text.Trim(), _graphTenantId.Text.Trim());
            var user = await auth.GetSignedInUserAsync();
            _graphStatus.Text = string.IsNullOrWhiteSpace(user)
                ? "未サインイン"
                : $"サインイン中: {user}";
        }
        catch
        {
            _graphStatus.Text = "未サインイン";
        }
    }

    private async void GraphSignInAsync(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_graphClientId.Text))
        {
            return;
        }

        _graphSignIn.IsEnabled = false;
        try
        {
            var auth = _graphAuthFactory(_graphClientId.Text.Trim(), _graphTenantId.Text.Trim());
            var user = await auth.SignInAsync();
            _graphStatus.Text = string.IsNullOrWhiteSpace(user)
                ? "未サインイン"
                : $"サインイン中: {user}";
        }
        catch (Exception exception)
        {
            _graphStatus.Text = $"サインインに失敗しました: {exception.Message}";
        }
        finally
        {
            UpdateGraphSignInEnabled();
        }
    }

    private async void GraphSignOutAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            var auth = _graphAuthFactory(_graphClientId.Text.Trim(), _graphTenantId.Text.Trim());
            await auth.SignOutAsync();
        }
        catch
        {
            // Best-effort sign-out; fall through to reset the displayed status.
        }
        _graphStatus.Text = "未サインイン";
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
                reminderTime,
                _graphClientId.Text.Trim(),
                string.IsNullOrWhiteSpace(_graphTenantId.Text) ? "common" : _graphTenantId.Text.Trim(),
                _graphMailEnabled.IsChecked == true,
                _graphCalendarEnabled.IsChecked == true,
                _reportTitle.Text.Trim(),
                _meetingOutputFolder.Text.Trim(),
                _meetingIncludeRawLog.IsChecked == true,
                _meetingHotkeyEnabled.IsChecked == true));
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
        catch (Exception exception)
        {
            ErrorLog.Log("SettingsWindow.Save", exception);
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
                ErrorLog.Log("SettingsWindow.Save", exception);
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
