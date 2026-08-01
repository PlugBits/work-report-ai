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

    private static readonly TimeSpan DialogSuppressionWindow = TimeSpan.FromSeconds(30);

    private const string SingleInstanceMutexName = "WorkLogAI.App.SingleInstance";
    private const string SingleInstanceSampleMutexName = "WorkLogAI.App.SingleInstance.Sample";

    private Forms.NotifyIcon? _trayIcon;
    private GlobalHotKey? _hotKey;
    private GlobalHotKey? _meetingHotKey;
    private AppServices? _services;
    private DispatcherTimer? _reminderTimer;
    private int _collectionRunning;
    private bool _sampleMode;
    private DateTime _lastUnhandledDialogShownAt = DateTime.MinValue;
    private Mutex? _singleInstanceMutex;
    private bool _singleInstanceMutexOwned;

    /// <summary>
    /// Set from <see cref="Microsoft.Win32.SystemEvents.SessionSwitch"/> (which may fire off
    /// the UI thread) on a Windows unlock; consumed and reset by the very next reminder tick
    /// regardless of whether that tick ends up firing a reminder. 0/1 rather than bool so the
    /// cross-thread read/reset can use <see cref="Interlocked"/>.
    /// </summary>
    private int _unlockPending;

    /// <summary>Idle duration observed on the previous reminder tick; null before the first tick.</summary>
    private TimeSpan? _previousIdleDuration;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _sampleMode = e.Args.Any(
            value => string.Equals(value, "--sample-data", StringComparison.OrdinalIgnoreCase));

        var mutexName = _sampleMode ? SingleInstanceSampleMutexName : SingleInstanceMutexName;
        _singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        _singleInstanceMutexOwned = createdNew;
        if (!createdNew)
        {
            MessageBox.Show(
                "WorkLog AI は既に起動しています。タスクトレイのアイコンをご確認ください。",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
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
                EnableMeetingHotKey();
            }

            StartReminderTimer();
            SubscribeSessionSwitch();
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
        UnsubscribeSessionSwitch();
        _reminderTimer?.Stop();
        _hotKey?.Dispose();
        _meetingHotKey?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        if (_singleInstanceMutex is not null)
        {
            try
            {
                if (_singleInstanceMutexOwned)
                {
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutexOwned = false;
                }
            }
            catch
            {
                // Abandoned-mutex or already-released edge cases must never crash exit.
            }
            finally
            {
                try
                {
                    _singleInstanceMutex.Dispose();
                }
                catch
                {
                    // Never crash exit on disposal failure.
                }
                _singleInstanceMutex = null;
            }
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            ErrorLog.Log("App.DispatcherUnhandled", e.Exception);
            e.Handled = true;

            var now = DateTime.UtcNow;
            if (now - _lastUnhandledDialogShownAt >= DialogSuppressionWindow)
            {
                _lastUnhandledDialogShownAt = now;
                MessageBox.Show(
                    "予期しないエラーが発生しました。操作をやり直してください。\n詳細はログに記録されました。",
                    "WorkLog AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch
        {
            // Handling an unhandled exception must never itself throw.
        }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            ErrorLog.Log(
                "App.DomainUnhandled",
                e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        }
        catch
        {
            // Logging must never throw here.
        }

        try
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
        }
        catch
        {
            // Best-effort cleanup only; the process is already going down.
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            ErrorLog.Log("App.UnobservedTask", e.Exception);
            e.SetObserved();
        }
        catch
        {
            // Handling an unobserved task exception must never itself throw.
        }
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
        menu.Items.Add("クイック入力 (Ctrl+Alt+W)", null, (_, _) => ShowQuickCapture());
        menu.Items.Add("議事録を開始 (Ctrl+Alt+M)", null, (_, _) => ShowMeetingMode());
        menu.Items.Add("週報候補を生成…", null, async (_, _) => await GenerateCandidatesAsync());
        menu.Items.Add("月次まとめを出力…", null, async (_, _) => await ExportMonthlySummaryAsync());
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

    /// <summary>
    /// One of the reminder's two break-return signals (the other is idle recovery, computed
    /// per-tick in <see cref="CheckReminderAsync"/>). <see cref="Microsoft.Win32.SystemEvents"/>
    /// invokes handlers on its own background thread, not the UI thread, hence the
    /// interlocked flag rather than touching UI-thread state directly.
    /// </summary>
    private void SubscribeSessionSwitch()
    {
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch (Exception exception)
        {
            ErrorLog.Log("App.Startup", exception);
        }
    }

    private void UnsubscribeSessionSwitch()
    {
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        catch
        {
            // SystemEvents holds static handlers; failing to unsubscribe must never crash exit.
        }
    }

    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        try
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
            {
                Interlocked.Exchange(ref _unlockPending, 1);
            }
        }
        catch (Exception exception)
        {
            ErrorLog.Log("App.SessionSwitch", exception);
        }
    }

    /// <summary>
    /// Break-return signal for reminder acceleration: either the user just unlocked the
    /// session, or they were idle (no keyboard/mouse input) for at least 10 minutes and have
    /// now been active for under a minute. This is the ONLY smart trigger — there is
    /// deliberately no git-commit or other repository-activity detection.
    /// </summary>
    private bool ComputeReturnedFromBreak()
    {
        var unlockPending = Interlocked.Exchange(ref _unlockPending, 0) == 1;

        var previousIdle = _previousIdleDuration;
        var currentIdle = TimeSpan.Zero;
        try
        {
            currentIdle = IdleTimeProvider.GetIdleDuration();
        }
        catch (Exception exception)
        {
            ErrorLog.Log("App.IdleDetection", exception);
        }
        _previousIdleDuration = currentIdle;

        var idleRecovery = previousIdle is { } previous
            && previous >= TimeSpan.FromMinutes(10)
            && currentIdle < TimeSpan.FromMinutes(1);

        return idleRecovery || unlockPending;
    }

    private async Task CheckReminderAsync()
    {
        if (_services is null || _trayIcon is null)
        {
            return;
        }

        try
        {
            await CheckDailyBackupAsync();

            // Computed unconditionally (and before anything can early-return) so the
            // once-per-tick unlock flag is always consumed, even on a tick where the
            // reminder itself does not end up firing.
            var returnedFromBreak = ComputeReturnedFromBreak();

            var settings = await _services.Settings.LoadAsync();
            var slotTimes = settings.ReminderTimes;
            var slotLastShownDates = new DateOnly?[slotTimes.Count];
            for (var i = 0; i < slotTimes.Count; i++)
            {
                var value = await _services.SettingsStore.GetAsync(
                    AppSettingKeys.ReminderSlotLastShownDate(i));
                slotLastShownDates[i] = DateOnly.TryParseExact(value, "yyyy-MM-dd", out var parsed)
                    ? parsed
                    : (DateOnly?)null;
            }

            var now = DateTime.Now;
            var todayStart = DateTime.Today;
            var tomorrowStart = todayStart.AddDays(1);
            var notes = await _services.Notes.ListAsync(
                new DateTimeOffset(todayStart),
                new DateTimeOffset(tomorrowStart));
            var noteTimestamps = notes.Select(note => note.CreatedAt).ToArray();

            var slotToFire = SlotReminderPlanner.SlotToFire(new SlotReminderPlanInput(
                settings.ReminderEnabled,
                slotTimes,
                now,
                slotLastShownDates,
                noteTimestamps,
                settings.ReminderSmartEnabled,
                returnedFromBreak));

            if (slotToFire is not { } slotIndex)
            {
                return;
            }

            await _services.SettingsStore.SetAsync(
                AppSettingKeys.ReminderSlotLastShownDate(slotIndex),
                DateOnly.FromDateTime(now).ToString("yyyy-MM-dd"));

            var message = slotIndex == 0
                ? "午前の業務メモがまだありません。Ctrl+Alt+W で1行記録しましょう。"
                : $"{slotTimes[slotIndex - 1]:HH:mm}以降の業務メモがありません。Ctrl+Alt+W で1行記録しましょう。";

            _trayIcon.ShowBalloonTip(10000, "WorkLog AI", message, Forms.ToolTipIcon.Info);
        }
        catch (Exception exception)
        {
            ErrorLog.Log("App.ReminderTick", exception);
        }
    }

    /// <summary>
    /// Runs at most once per calendar day from the reminder timer tick, so a tray
    /// instance that stays alive for days without restarting still gets a chance at
    /// the weekly database backup (which otherwise only ran at startup).
    /// <see cref="DatabaseBackupService.RunIfNeeded"/> remains idempotent per week;
    /// this only widens how often it gets invoked.
    /// </summary>
    private async Task CheckDailyBackupAsync()
    {
        if (_services is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var lastCheckedValue = await _services.SettingsStore.GetAsync(
            AppSettingKeys.BackupLastCheckedDate);
        var lastChecked = DateOnly.TryParseExact(lastCheckedValue, "yyyy-MM-dd", out var parsed)
            ? parsed
            : (DateOnly?)null;

        if (lastChecked == today)
        {
            return;
        }

        await _services.SettingsStore.SetAsync(
            AppSettingKeys.BackupLastCheckedDate,
            today.ToString("yyyy-MM-dd"));

        _services.RunDatabaseBackupIfDue();
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

    private void EnableMeetingHotKey()
    {
        if (_meetingHotKey is not null)
        {
            return;
        }

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

    private void DisableMeetingHotKey()
    {
        if (_meetingHotKey is null)
        {
            return;
        }

        _meetingHotKey.Dispose();
        _meetingHotKey = null;
    }

    public void ApplyMeetingHotKeySetting(bool enabled)
    {
        var currentlyEnabled = _meetingHotKey is not null;
        if (enabled == currentlyEnabled)
        {
            return;
        }

        if (enabled)
        {
            EnableMeetingHotKey();
        }
        else
        {
            DisableMeetingHotKey();
        }
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

        ProgressStatusWindow? progress = null;
        try
        {
            var settings = await _services.Settings.LoadAsync();
            var picker = new WeekPickerWindow(DateOnly.FromDateTime(DateTime.Today), settings.WeekStartsOn);
            if (picker.ShowDialog() != true || picker.SelectedRange is not { } range)
            {
                return;
            }

            progress = new ProgressStatusWindow();
            progress.SetStatus("ローカル収集中…");
            progress.Show();

            var collection = await _services.CollectLocalSourcesAsync(range);
            if (collection.Errors.Count > 0)
            {
                ErrorLog.Log("App.Collection", string.Join("; ", collection.Errors));
            }

            progress?.SetStatus("送信内容を準備中…");
            var preview = await _services.GetGenerationPreviewAsync(range);
            if (!preview.HasCredential)
            {
                progress?.Close();
                progress = null;
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
                progress?.Close();
                progress = null;
                MessageBox.Show("送信できる今週の根拠イベントがありません。", "WorkLog AI");
                return;
            }
            if (preview.PreviewEnabled)
            {
                var breakdown = string.Join(
                    ", ",
                    preview.SourceCounts.Select(item => $"{item.Key}:{item.Value}"));
                progress?.Hide();
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
                    progress?.Close();
                    progress = null;
                    return;
                }
                progress?.Show();
            }

            progress?.SetStatus("AI候補を生成中…");
            var generation = await _services.GenerateAiCandidatesAsync(range);
            if (!generation.Succeeded)
            {
                progress?.Close();
                progress = null;
                MessageBox.Show(
                    generation.Error,
                    "WorkLog AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            progress?.Close();
            progress = null;
            var banner =
                $"AI候補 {generation.Candidates.Count}件を生成しました（送信イベント {generation.SentEventCount}件、" +
                $"収集警告 {collection.Errors.Count}件、切り詰め{(generation.InputTruncated ? "あり" : "なし")}）。" +
                "不要な行は採用チェックを外してください。";
            if (generation.DeselectedLocalCount > 0)
            {
                banner += $"元メモ由来の行 {generation.DeselectedLocalCount}件の採用を外しました(AI候補に置換)。";
            }
            new CandidateWindow(_services, range, banner).Show();
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
            progress?.Close();
            Interlocked.Exchange(ref _collectionRunning, 0);
        }
    }

    private async Task ExportMonthlySummaryAsync()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            var picker = new MonthPickerWindow(DateOnly.FromDateTime(DateTime.Today));
            if (picker.ShowDialog() != true || picker.SelectedMonth is not { } month)
            {
                return;
            }

            var settings = await _services.Settings.LoadAsync();
            var firstDay = new DateOnly(month.Year, month.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);
            var candidates = await _services.Candidates.ListSelectedByDateRangeAsync(firstDay, lastDay);
            if (candidates.Count == 0)
            {
                MessageBox.Show("対象月に採用済みの行がありません。", "WorkLog AI");
                return;
            }

            var rows = new CandidateReportMapper().MapSelected(candidates);
            var path = await _services.Exporter.ExportMonthAsync(
                month.Year,
                month.Month,
                rows,
                settings.ExcelOutputDirectory,
                new ReportIdentity(settings.CompanyName, settings.EmployeeName, settings.ReportTitle));
            ExportResultPrompt.OfferToOpen(path);
        }
        catch (Exception exception)
        {
            ErrorLog.Log("App.ExportMonthlySummary", exception);
            MessageBox.Show(
                $"月次まとめを出力できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
