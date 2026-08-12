using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace WorkLogAI.App;

/// <summary>
/// Self-drawn topmost reminder toast. Replaces
/// <see cref="System.Windows.Forms.NotifyIcon.ShowBalloonTip"/>, whose toasts
/// Windows silently reroutes to the notification center — never actually shown to
/// the user — whenever the OS per-app "banner" setting is off. Drawing our own
/// window sidesteps that setting entirely.
/// <see cref="ShowActivated"/> is false so the toast never steals focus from
/// whatever the user is doing.
/// </summary>
public sealed class ReminderToastWindow : Window
{
    private const double ScreenMargin = 16;
    private static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(60);

    private static ReminderToastWindow? _current;

    private readonly DispatcherTimer _autoCloseTimer;
    private bool _closing;

    private ReminderToastWindow(string message, Action onRecordClick)
    {
        AppTheme.Apply(this);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var title = new TextBlock
        {
            Text = "WorkLog AI",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var body = new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var recordButton = new Button
        {
            Content = "今すぐ記録",
            Padding = new Thickness(14, 6, 14, 6)
        };
        recordButton.Style = (Style)System.Windows.Application.Current.FindResource("AccentButton");
        recordButton.Click += (_, _) =>
        {
            if (_closing)
            {
                return;
            }

            Close();
            onRecordClick();
        };

        var closeButton = new Button
        {
            Content = "閉じる",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        closeButton.Click += (_, _) =>
        {
            if (!_closing)
            {
                Close();
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(recordButton);
        buttons.Children.Add(closeButton);

        var content = new StackPanel();
        content.Children.Add(title);
        content.Children.Add(body);
        content.Children.Add(buttons);

        Content = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = content
        };

        Loaded += (_, _) => PositionBottomRight();

        _autoCloseTimer = new DispatcherTimer { Interval = AutoCloseDelay };
        _autoCloseTimer.Tick += (_, _) =>
        {
            if (!_closing)
            {
                Close();
            }
        };
    }

    /// <summary>
    /// Closes any previously shown toast and shows a new one. There is only ever
    /// one reminder toast on screen at a time.
    /// </summary>
    public static void ShowReminder(string message, Action onRecordClick)
    {
        _current?.Close();

        var toast = new ReminderToastWindow(message, onRecordClick);
        _current = toast;
        toast.Show();
        toast._autoCloseTimer.Start();
        System.Media.SystemSounds.Asterisk.Play();
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - ScreenMargin;
        Top = workArea.Bottom - ActualHeight - ScreenMargin;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        _autoCloseTimer.Stop();
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        base.OnClosing(e);
    }
}
