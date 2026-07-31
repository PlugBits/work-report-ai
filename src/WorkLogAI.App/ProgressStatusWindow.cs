using System.Windows;
using System.Windows.Controls;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace WorkLogAI.App;

/// <summary>
/// Tiny topmost, non-modal status window shown during candidate generation
/// (local collection → send preview prep → AI generation) so the user sees the app
/// is working rather than a frozen tray action. No cancel button — generation is
/// already non-reentrant (<c>App._collectionRunning</c>).
/// </summary>
public sealed class ProgressStatusWindow : Window
{
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public ProgressStatusWindow()
    {
        Title = "WorkLog AI";
        Width = 300;
        Height = 100;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 14,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(_status);
        root.Children.Add(progressBar);
        Content = root;
    }

    public void SetStatus(string text) => _status.Text = text;
}
