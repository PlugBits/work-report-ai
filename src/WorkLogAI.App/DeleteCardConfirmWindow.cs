using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;

namespace WorkLogAI.App;

/// <summary>
/// The two ways a review-window card can be deleted, chosen via
/// <see cref="DeleteCardConfirmWindow"/>.
/// </summary>
public enum DeleteCardChoice
{
    Cancel,
    RowOnly,
    RowAndSource
}

/// <summary>
/// Small modal prompt offering the row-only vs. row-and-source-data choice for
/// deleting a review card. Mirrors <see cref="MeetingLineEditWindow"/>'s minimal
/// dialog style.
/// </summary>
public sealed class DeleteCardConfirmWindow : Window
{
    public DeleteCardConfirmWindow()
    {
        AppTheme.Apply(this);
        Title = "行を削除 - WorkLog AI";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "この行を削除しますか？",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "行のみ削除: 次回の収集で復活することがあります。\n"
                + "元データごと削除: 今後の収集からも除外され復活しません。",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var rowOnly = new Button { Content = "行のみ削除", Padding = new Thickness(10, 6, 10, 6) };
        rowOnly.Click += (_, _) =>
        {
            Choice = DeleteCardChoice.RowOnly;
            DialogResult = true;
        };
        var rowAndSource = new Button
        {
            Content = "元データごと削除",
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
        };
        rowAndSource.Click += (_, _) =>
        {
            Choice = DeleteCardChoice.RowAndSource;
            DialogResult = true;
        };
        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(8, 0, 0, 0)
        };
        buttons.Children.Add(rowOnly);
        buttons.Children.Add(rowAndSource);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
    }

    public DeleteCardChoice Choice { get; private set; } = DeleteCardChoice.Cancel;
}
