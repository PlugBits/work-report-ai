using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorkLogAI.Core;
using Button = System.Windows.Controls.Button;

namespace WorkLogAI.App;

/// <summary>
/// Read-only WYSIWYG preview of exactly what the weekly Excel export will contain,
/// shown before the export actually runs. Replaces the previous bare blank-weekday
/// confirmation <see cref="MessageBox"/> with a full per-day rendering built from
/// the same <see cref="DailyReportGrouper"/> grouping the exporter itself uses, plus
/// (when any selected weekday is empty) the same warning text shown inline instead
/// of in a separate dialog. Modeled on <see cref="WeekPickerWindow"/>'s dialog
/// style, but larger and resizable to fit a full week of content.
/// </summary>
public sealed class ExportPreviewWindow : Window
{
    public ExportPreviewWindow(
        IReadOnlyList<ReportRow> rows,
        WeekRange range,
        IReadOnlyList<string> blankWeekdayLabels)
    {
        AppTheme.Apply(this);
        Title = $"Excel出力プレビュー {range.Start:yyyy/MM/dd}〜{range.End:yyyy/MM/dd} - WorkLog AI";
        Width = 700;
        Height = 600;
        MinWidth = 520;
        MinHeight = 360;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(16) };

        var header = new TextBlock
        {
            Text = "この内容でExcelに出力されます",
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        if (blankWeekdayLabels.Count > 0)
        {
            var warning = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF7, 0xED)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xBA, 0x74)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock
                {
                    Text = $"空白の平日があります: {string.Join(", ", blankWeekdayLabels)}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x34, 0x12))
                }
            };
            DockPanel.SetDock(warning, Dock.Top);
            root.Children.Add(warning);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var export = new Button
        {
            Content = "出力",
            Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        export.Style = (Style)System.Windows.Application.Current.FindResource("AccentButton");
        export.Click += (_, _) => DialogResult = true;
        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            Padding = new Thickness(18, 6, 18, 6)
        };
        buttons.Children.Add(export);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var groupedDays = DailyReportGrouper.Group(rows);
        foreach (var day in groupedDays)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"{day.Date:yyyy/MM/dd} ({CandidateWindow.JapaneseDayOfWeek(day.Date.DayOfWeek)})",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 12, 0, 4)
            });
            foreach (var item in day.Items)
            {
                body.Children.Add(new TextBlock
                {
                    Text = $"{DailyReportGrouper.CircledNumber(item.Number)} {item.WorkItem} — {item.Activity}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2)
                });
                if (!string.IsNullOrWhiteSpace(item.ResultOrNext))
                {
                    body.Children.Add(new TextBlock
                    {
                        Text = $"→ {item.ResultOrNext}",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brushes.DimGray,
                        Margin = new Thickness(20, 0, 0, 4)
                    });
                }
            }
        }

        if (groupedDays.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "出力される行がありません。",
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 12, 0, 0)
            });
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = body
        };
        root.Children.Add(scroll);

        Content = root;
    }
}
