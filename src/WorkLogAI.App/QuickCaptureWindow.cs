using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorkLogAI.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkLogAI.App;

public sealed class QuickCaptureWindow : Window
{
    private readonly IQuickNoteRepository _notes;
    private readonly TextBox _input;
    private bool _saving;

    public QuickCaptureWindow(IQuickNoteRepository notes)
    {
        _notes = notes;
        Title = "WorkLog AI";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;

        _input = new TextBox
        {
            FontSize = 18,
            MinHeight = 40,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(8),
            VerticalContentAlignment = VerticalAlignment.Center,
            AcceptsReturn = false,
            MaxLines = 1
        };
        _input.PreviewKeyDown += OnPreviewKeyDown;
        Content = _input;
        Loaded += (_, _) =>
        {
            _input.Focus();
            Keyboard.Focus(_input);
        };
        Deactivated += (_, _) =>
        {
            if (!_saving)
            {
                Close();
            }
        };
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (e.Key != Key.Enter || _saving)
        {
            return;
        }

        e.Handled = true;
        var stayOpen = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var text = _input.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        _saving = true;
        try
        {
            await _notes.CreateAsync(text);
            if (stayOpen)
            {
                _input.Clear();
                _input.Focus();
            }
            else
            {
                Close();
            }

            SaveToast.ShowFor(this, TimeSpan.FromMilliseconds(800));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"保存できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _saving = false;
        }
    }
}

internal sealed class SaveToast : Window
{
    private SaveToast(Window relativeTo)
    {
        Width = 150;
        Height = 42;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 40, 40, 40)),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = "保存しました",
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var relativeWidth = relativeTo.ActualWidth;
        var relativeHeight = relativeTo.ActualHeight;
        if (relativeWidth is <= 0 or double.NaN || relativeHeight is <= 0 or double.NaN)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + (workArea.Height - Height) / 2;
            return;
        }

        Left = relativeTo.Left + (relativeWidth - Width) / 2;
        Top = relativeTo.Top + relativeHeight + 6;
    }

    public static void ShowFor(Window relativeTo, TimeSpan duration)
    {
        var toast = new SaveToast(relativeTo);
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            toast.Close();
        };
        toast.Show();
        timer.Start();
    }
}
