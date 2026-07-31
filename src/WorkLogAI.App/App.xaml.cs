using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;

namespace WorkLogAI.App;

public partial class App : System.Windows.Application
{
    private const int QuickCaptureHotKeyId = 0x5741;
    private const int MeetingHotKeyId = 0x574D;

    private Forms.NotifyIcon? _trayIcon;
    private GlobalHotKey? _hotKey;
    private GlobalHotKey? _meetingHotKey;
    private AppServices? _services;
    private DispatcherTimer? _reminderTimer;
    private int _collectionRunning;
    private bool _sampleMode;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _sampleMode = e.Args.Any(
                value => string.Equals(value, "--sample-data", StringComparison.OrdinalIgnoreCase));
            _services = new AppServices(_sampleMode);
            await _services.InitializeAsync();
            CreateTrayIcon(_sampleMode);
            _hotKey = new GlobalHotKey(Key.W, QuickCaptureHotKeyId, ShowQuickCapture);

            if (!_hotKey.Register())
            {
                MessageBox.Show(
                    "Ctrl+Alt+W を登録できませんでした。別のアプリで使用されている可能性があります。",
                    "WorkLog AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var startupSettings = await _services.Settings.LoadAsync();
            if (startupSettings.MeetingHotkeyEnabled)
            {
                _meetingHotKey = new GlobalHotKey(Key.M, MeetingHotKeyId, ShowMeetingMode);
                if (!_meetingHotKey.Register())
                {
                    MessageBox.Show(
                        "Ctrl+Alt+M を登録できませんでした。別のアプリで使用されている可能性があります。",
                        "WorkLog AI",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            StartReminderTimer();
        }
        catch (Exception exception)
        {
            ErrorLog.Log("App.Startup", exception);
            MessageBox.Show(
                $"起動に失敗しました。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _reminderTimer?.Stop();
        _hotKey?.Dispose();
        _meetingHotKey?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        base.OnExit(e);
    }

    private void CreateTrayIcon(bool sampleMode)
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = sampleMode ? "WorkLog AI (サンプルデータ)" : "WorkLog AI",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("クイック入力", null, (_, _) => ShowQuickCapture());
        menu.Items.Add("議事録を開始", null, (_, _) => ShowMeetingMode());
        menu.Items.Add("候補を生成…", null, async (_, _) => await GenerateCandidatesAsync());
        menu.Items.Add("今週の記録を見る", null, (_, _) => ShowHistory());
        menu.Items.Add("設定", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowQuickCapture();
        _trayIcon.BalloonTipClicked += (_, _) => ShowQuickCapture();
    }

    private void StartReminderTimer()
    {
        _reminderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _reminderTimer.Tick += async (_, _) => await CheckReminderAsync();
        _reminderTimer.Start();
    }

    private async Task CheckReminderAsync()
    {
        if (_services is null || _trayIcon is null)
        {
            return;
        }

        try
        {
            var settings = await _services.Settings.LoadAsync();
            var lastShownValue = await _services.SettingsStore.GetAsync(
                AppSettingKeys.ReminderLastShownDate);
            var lastShown = DateOnly.TryParseExact(lastShownValue, "yyyy-MM-dd", out var parsed)
                ? parsed
                : (DateOnly?)null;

            var now = DateTime.Now;
            var todayStart = DateTime.Today;
            var tomorrowStart = todayStart.AddDays(1);
            var notes = await _services.Notes.ListAsync(
                new DateTimeOffset(todayStart),
                new DateTimeOffset(tomorrowStart));

            var shouldRemind = ReminderPlanner.ShouldRemind(new ReminderPlanInput(
                settings.ReminderEnabled,
                settings.ReminderTime,
                now,
                notes.Count,
                lastShown));

            if (!shouldRemind)
            {
                return;
            }

            await _services.SettingsStore.SetAsync(
                AppSettingKeys.ReminderLastShownDate,
                DateOnly.FromDateTime(now).ToString("yyyy-MM-dd"));

            _trayIcon.ShowBalloonTip(
                10000,
                "WorkLog AI",
                "今日の業務メモがまだ0件です。Ctrl+Alt+W で1行記録しましょう。",
                Forms.ToolTipIcon.Info);
        }
        catch (Exception exception)
        {
            ErrorLog.Log("App.ReminderTick", exception);
        }
    }

    private void ShowQuickCapture()
    {
        if (_services is null)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            var window = new QuickCaptureWindow(_services.Notes);
            window.Show();
            window.Activate();
        });
    }

    private void ShowMeetingMode() => _ = ShowMeetingModeAsync();

    private async Task ShowMeetingModeAsync()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var drafts = await _services.Meetings.ListSessionsAsync(MeetingStatus.Draft);
            MeetingSession? session = null;
            if (drafts.Count > 0)
            {
                var chooser = new MeetingSessionChooserWindow(drafts);
                if (chooser.ShowDialog() != true)
                {
                    return;
                }

                session = chooser.SelectedSession;
            }

            Dispatcher.Invoke(() =>
            {
                var window = new MeetingCaptureWindow(_services, session);
                window.Show();
                window.Activate();
            });
        }
        catch (Exception exception)
        {
            ErrorLog.Log("App.MeetingMode", exception);
            MessageBox.Show(
                $"議事録モードを開始できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task GenerateCandidatesAsync()
    {
        if (_services is null || Interlocked.Exchange(ref _collectionRunning, 1) == 1)
        {
            if (_collectionRunning == 1)
            {
                MessageBox.Show("ローカル収集は実行中です。", "WorkLog AI");
            }
            return;
        }

        try
        {
            var settings = await _services.Settings.LoadAsync();
            var picker = new WeekPickerWindow(DateOnly.FromDateTime(DateTime.Today), settings.WeekStartsOn);
            if (picker.ShowDialog() != true || picker.SelectedRange is not { } range)
            {
                return;
            }

            var collection = await _services.CollectLocalSourcesAsync(range);
            if (collection.Errors.Count > 0)
            {
                ErrorLog.Log("App.Collection", string.Join("; ", collection.Errors));
            }
            var preview = await _services.GetGenerationPreviewAsync(range);
            if (!preview.HasCredential)
            {
                MessageBox.Show(
                    "OpenAI APIキーが設定されていません。設定画面でCredential Managerへ保存してください。\n" +
                    $"ローカル収集は完了しました（新規 {collection.InsertedEvents}件）。",
                    "WorkLog AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            if (preview.AvailableEvents == 0)
            {
                MessageBox.Show("送信できる今週の根拠イベントがありません。", "WorkLog AI");
                return;
            }
            if (preview.PreviewEnabled)
            {
                var breakdown = string.Join(
                    ", ",
                    preview.SourceCounts.Select(item => $"{item.Key}:{item.Value}"));
                var answer = MessageBox.Show(
                    $"OpenAIへ送信しますか？\nモデル: {preview.Model}\n" +
                    $"対象週: {range.Start:yyyy/MM/dd}〜{range.End:yyyy/MM/dd}\n" +
                    $"送信イベント: {preview.SentEvents}/{preview.AvailableEvents}\n" +
                    $"内訳: {breakdown}\n" +
                    $"切り詰め: {(preview.Truncated ? "あり" : "なし")}\n\n" +
                    "APIキー、sourceRef、diff、ソース本文、function outputは送信しません。",
                    "送信前プレビュー",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            var generation = await _services.GenerateAiCandidatesAsync(range);
            if (!generation.Succeeded)
            {
                MessageBox.Show(
                    generation.Error,
                    "WorkLog AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            MessageBox.Show(
                $"候補生成が完了しました。\nAI候補: {generation.Candidates.Count}件\n" +
                $"送信イベント: {generation.SentEventCount}件\n" +
                $"切り詰め: {(generation.InputTruncated ? "あり" : "なし")}\n" +
                $"ローカル収集警告: {collection.Errors.Count}件",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            new CandidateWindow(_services, range).Show();
        }
        catch (Exception exception)
        {
            ErrorLog.Log("App.GenerateCandidates", exception);
            MessageBox.Show(
                $"ローカル収集に失敗しました。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _collectionRunning, 0);
        }
    }

    private void ShowHistory()
    {
        if (_services is null)
        {
            return;
        }

        new HistoryWindow(_services).Show();
    }

    private void ShowSettings()
    {
        if (_services is null)
        {
            return;
        }

        new SettingsWindow(
            _services.Settings,
            _services.Credentials,
            _services.StartupRegistrar,
            _services.CreateGraphAuth,
            _sampleMode).ShowDialog();
    }
}
