using System.Windows;
using System.Windows.Controls;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;

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
    private readonly Button _testApiKey = new()
    {
        Content = "APIキーをテスト",
        Padding = new Thickness(10, 4, 10, 4),
        Margin = new Thickness(0, 6, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left
    };
    private readonly TextBlock _testApiKeyResult = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly OpenAiKeyProbe _keyProbe = new();
    private readonly CheckBox _reminderEnabled = new() { Content = "平日にメモ0件をリマインド" };
    private readonly TextBox _reminderTimes = new();
    private readonly CheckBox _reminderSmartEnabled = new() { Content = "スマート通知（離席復帰で前倒し）" };
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
    private readonly CheckBox _meetingHotkeyEnabled = new() { Content = "Ctrl+Alt+M を有効化" };
    private readonly TextBox _vaultDailyNotesFolder = new();
    private readonly TextBox _vaultEntityLinkMinOccurrences = new();

    public SettingsWindow(
        AppSettingsService settings,
        ICredentialStore credentials,
        IStartupRegistrar startupRegistrar,
        Func<string, string, GraphAuthService> graphAuthFactory,
        bool sampleMode = false)
    {
        AppTheme.Apply(this);
        _settings = settings;
        _credentials = credentials;
        _startupRegistrar = startupRegistrar;
        _graphAuthFactory = graphAuthFactory;
        _sampleMode = sampleMode;
        Title = "設定 - WorkLog AI";
        Width = 560;
        Height = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // 「基本」: 会社名, 氏名, 週報タイトル, 週の開始曜日, Excel保存先, ホットキー(表示のみ),
        // 自動起動, メモ0件リマインド, 通知時刻(複数スロット), スマート通知
        var basicGrid = NewTabGrid(10);
        var basicRow = 0;
        AddRow(basicGrid, basicRow++, "会社名", _company);
        AddRow(basicGrid, basicRow++, "氏名", _employee);
        AddRow(basicGrid, basicRow++, "週報タイトル", _reportTitle);
        _weekStart.ItemsSource = Enum.GetValues<DayOfWeek>();
        AddRow(basicGrid, basicRow++, "週の開始曜日", _weekStart);
        AddRow(basicGrid, basicRow++, "Excel保存先", _outputDirectory);
        AddRow(basicGrid, basicRow++, "ホットキー", new TextBlock
        {
            Text = "Ctrl + Alt + W",
            Margin = new Thickness(4, 8, 4, 8)
        });
        _autoStart.IsEnabled = !_sampleMode;
        AddRow(basicGrid, basicRow++, "自動起動", _autoStart);
        AddRow(basicGrid, basicRow++, "メモ0件リマインド", _reminderEnabled);
        AddRow(basicGrid, basicRow++, "通知時刻 (HH:mm、カンマ区切り)", _reminderTimes);
        AddRow(basicGrid, basicRow++, "スマート通知", _reminderSmartEnabled);

        // 「収集」: ローカルGit, Codexセッション, 更新ファイル対象, 収集範囲の説明文
        var collectionGrid = NewTabGrid(4);
        var collectionRow = 0;
        AddRow(collectionGrid, collectionRow++, "ローカルGit\n(1行1パス)", _repositories);
        AddRow(collectionGrid, collectionRow++, "Codexセッション", _codexFolder);
        AddRow(collectionGrid, collectionRow++, "更新ファイル対象\n(1行1パス)", _recentFolders);
        AddRow(collectionGrid, collectionRow++, "収集範囲", new TextBlock
        {
            Text = "設定したローカルフォルダーのみ。ファイル本文・diff・コマンド引数は収集しません。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 8)
        });

        // 「AI」: OpenAIモデル, 送信前確認, OpenAI APIキー, キー状態(+テストボタン群), 秘密情報の説明文
        var aiGrid = NewTabGrid(5);
        var aiRow = 0;
        AddRow(aiGrid, aiRow++, "OpenAIモデル", _model);
        AddRow(aiGrid, aiRow++, "送信前確認", _preview);
        AddRow(aiGrid, aiRow++, "OpenAI APIキー", _apiKey);
        _testApiKey.Click += TestApiKeyAsync;
        AddRow(aiGrid, aiRow++, "キー状態", new StackPanel
        {
            Children = { _credentialStatus, _removeApiKey, _testApiKey, _testApiKeyResult }
        });
        AddRow(aiGrid, aiRow++, "秘密情報", new TextBlock
        {
            Text = "APIキーはWindows Credential Managerだけに保存します。既存キーは表示しません。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 8)
        });

        // 「Microsoft 365」: クライアントID, テナントID, Outlookメール, Outlookカレンダー,
        // Microsoftサインイン(状態+ボタン)
        var graphGrid = NewTabGrid(5);
        var graphRow = 0;
        AddRow(graphGrid, graphRow++, "クライアントID", _graphClientId);
        AddRow(graphGrid, graphRow++, "テナントID", _graphTenantId);
        AddRow(graphGrid, graphRow++, "Outlookメール", _graphMailEnabled);
        AddRow(graphGrid, graphRow++, "Outlookカレンダー", _graphCalendarEnabled);
        _graphSignIn.Click += GraphSignInAsync;
        _graphSignOut.Click += GraphSignOutAsync;
        _graphClientId.TextChanged += (_, _) => UpdateGraphSignInEnabled();
        AddRow(graphGrid, graphRow++, "Microsoftサインイン", new StackPanel
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

        // 「議事録/Obsidian」: 議事録出力フォルダ, 生ログをMDに同梱, 議事録ホットキー(Ctrl+Alt+M),
        // デイリーノート出力フォルダ, リンク化の最低出現回数
        var meetingGrid = NewTabGrid(5);
        var meetingRow = 0;
        AddRow(meetingGrid, meetingRow++, "議事録出力フォルダ", _meetingOutputFolder);
        AddRow(meetingGrid, meetingRow++, "議事録Markdown", _meetingIncludeRawLog);
        AddRow(meetingGrid, meetingRow++, "議事録ホットキー", _meetingHotkeyEnabled);
        AddRow(meetingGrid, meetingRow++, "デイリーノート出力フォルダ", _vaultDailyNotesFolder);
        AddRow(meetingGrid, meetingRow++, "リンク化の最低出現回数", _vaultEntityLinkMinOccurrences);

        var tabControl = new TabControl();
        tabControl.Items.Add(new TabItem { Header = "基本", Content = TabScrollHost(basicGrid) });
        tabControl.Items.Add(new TabItem { Header = "収集", Content = TabScrollHost(collectionGrid) });
        tabControl.Items.Add(new TabItem { Header = "AI", Content = TabScrollHost(aiGrid) });
        tabControl.Items.Add(new TabItem { Header = "Microsoft 365", Content = TabScrollHost(graphGrid) });
        tabControl.Items.Add(new TabItem { Header = "議事録/Obsidian", Content = TabScrollHost(meetingGrid) });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 12, 16, 16)
        };
        var save = new Button { Content = "保存", IsDefault = true, Padding = new Thickness(18, 6, 18, 6) };
        save.Style = (Style)System.Windows.Application.Current.FindResource("AccentButton");
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

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(tabControl);
        Content = root;
        Loaded += LoadAsync;
    }

    private static Grid NewTabGrid(int rowCount)
    {
        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < rowCount; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        return grid;
    }

    private static ScrollViewer TabScrollHost(Grid grid) => new()
    {
        Content = grid,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

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
        _reminderTimes.Text = string.Join(", ", values.ReminderTimes.Select(time => time.ToString("HH:mm")));
        _reminderSmartEnabled.IsChecked = values.ReminderSmartEnabled;
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
        _vaultDailyNotesFolder.Text = values.VaultDailyNotesFolder;
        _vaultEntityLinkMinOccurrences.Text = values.VaultEntityLinkMinOccurrences.ToString();
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

    private async void TestApiKeyAsync(object sender, RoutedEventArgs e)
    {
        _testApiKey.IsEnabled = false;
        _testApiKeyResult.Text = "確認中…";
        try
        {
            var key = !string.IsNullOrWhiteSpace(_apiKey.Password)
                ? _apiKey.Password
                : await _credentials.GetAsync(CredentialTargets.OpenAiApiKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                _testApiKeyResult.Text = "APIキーが未設定です。";
                return;
            }

            var status = await _keyProbe.ProbeAsync(key);
            _testApiKeyResult.Text = status switch
            {
                OpenAiKeyProbeStatus.Ok => "キーは有効です。",
                OpenAiKeyProbeStatus.Unauthorized => "キーが無効です(401)。",
                _ => "接続できませんでした。"
            };
        }
        catch
        {
            _testApiKeyResult.Text = "接続できませんでした。";
        }
        finally
        {
            _testApiKey.IsEnabled = true;
        }
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

        var reminderTimes = _reminderTimes.Text
            .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => TimeOnly.TryParseExact(part, "HH:mm", out var time) ? (TimeOnly?)time : null)
            .Where(time => time.HasValue)
            .Select(time => time!.Value)
            .Distinct()
            .OrderBy(time => time)
            .ToArray();

        if (reminderTimes.Length == 0)
        {
            MessageBox.Show(this, "通知時刻はHH:mm形式で1つ以上入力してください。", "WorkLog AI");
            return;
        }

        if (!int.TryParse(_vaultEntityLinkMinOccurrences.Text, out var vaultEntityLinkMinOccurrences)
            || vaultEntityLinkMinOccurrences < 1)
        {
            MessageBox.Show(this, "リンク化の最低出現回数は1以上の整数で入力してください。", "WorkLog AI");
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
                reminderTimes,
                _reminderSmartEnabled.IsChecked == true,
                _graphClientId.Text.Trim(),
                string.IsNullOrWhiteSpace(_graphTenantId.Text) ? "common" : _graphTenantId.Text.Trim(),
                _graphMailEnabled.IsChecked == true,
                _graphCalendarEnabled.IsChecked == true,
                _reportTitle.Text.Trim(),
                _meetingOutputFolder.Text.Trim(),
                _meetingIncludeRawLog.IsChecked == true,
                _meetingHotkeyEnabled.IsChecked == true,
                _vaultDailyNotesFolder.Text.Trim(),
                vaultEntityLinkMinOccurrences));
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

        if (System.Windows.Application.Current is App app)
        {
            app.ApplyMeetingHotKeySetting(_meetingHotkeyEnabled.IsChecked == true);
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
