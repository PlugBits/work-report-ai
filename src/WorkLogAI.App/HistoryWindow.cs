using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;
using Button = System.Windows.Controls.Button;

namespace WorkLogAI.App;

public sealed class HistoryWindow : Window
{
    private readonly AppServices _services;
    private readonly DataGrid _grid;
    private readonly TextBlock _weekLabel;
    private DateOnly _anchor = DateOnly.FromDateTime(DateTime.Today);
    private AppSettingsSnapshot? _settings;

    public HistoryWindow(AppServices services)
    {
        AppTheme.Apply(this);
        _services = services;
        Title = "今週の記録 - WorkLog AI";
        Width = 900;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { Margin = new Thickness(12) };
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        controls.Children.Add(Button("前週", async (_, _) =>
        {
            _anchor = _anchor.AddDays(-7);
            await RefreshAsync();
        }));
        controls.Children.Add(Button("次週", async (_, _) =>
        {
            _anchor = _anchor.AddDays(7);
            await RefreshAsync();
        }));
        _weekLabel = new TextBlock
        {
            Margin = new Thickness(14, 7, 14, 0),
            FontWeight = FontWeights.Bold
        };
        controls.Children.Add(_weekLabel);
        controls.Children.Add(Button("削除", DeleteSelectedAsync));
        controls.Children.Add(Button("再開", ReopenSelectedAsync));
        controls.Children.Add(Button("この週の候補レビュー", ShowCandidates));
        var exportButton = Button("Excel出力", ExportAsync);
        exportButton.Style = (Style)System.Windows.Application.Current.FindResource("AccentButton");
        controls.Children.Add(exportButton);
        DockPanel.SetDock(controls, Dock.Top);
        root.Children.Add(controls);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "日時",
            Binding = new Binding(nameof(HistoryItem.CreatedAt)) { StringFormat = "yyyy/MM/dd HH:mm" },
            Width = 145
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "記録",
            Binding = new Binding(nameof(HistoryItem.Text)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "状態",
            Binding = new Binding(nameof(HistoryItem.State)),
            Width = 90
        });
        root.Children.Add(_grid);
        Content = root;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        button.Click += handler;
        return button;
    }

    private async Task RefreshAsync()
    {
        _settings = await _services.Settings.LoadAsync();
        var range = new WeekRangeCalculator(_settings.WeekStartsOn).GetWeekRange(_anchor);
        var timestamps = LocalWeekClock.ToTimestampRange(range);
        var notes = await _services.Notes.ListAsync(
            timestamps.From,
            timestamps.To,
            includeDeleted: true);
        _grid.ItemsSource = notes.Select(note => new HistoryItem(note)).ToList();
        _weekLabel.Text = $"{range.Start:yyyy/MM/dd} 〜 {range.End:yyyy/MM/dd}";
    }

    private async void DeleteSelectedAsync(object sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not HistoryItem item || item.Note.DeletedAt is not null)
        {
            return;
        }

        await _services.Notes.SoftDeleteAsync(item.Note.Id);
        await RefreshAsync();
    }

    private async void ReopenSelectedAsync(object sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not HistoryItem item || item.Note.DeletedAt is null)
        {
            return;
        }

        await _services.Notes.ReopenAsync(item.Note.Id);
        await RefreshAsync();
    }

    private async void ExportAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings ??= await _services.Settings.LoadAsync();
            var range = new WeekRangeCalculator(_settings.WeekStartsOn).GetWeekRange(_anchor);
            var timestamps = LocalWeekClock.ToTimestampRange(range);
            var notes = await _services.Notes.ListAsync(timestamps.From, timestamps.To);
            var mapper = new ManualReportMapper();
            var path = await _services.Exporter.ExportAsync(
                range,
                notes.Select(mapper.Map),
                _settings.ExcelOutputDirectory,
                new ReportIdentity(_settings.CompanyName, _settings.EmployeeName, _settings.ReportTitle));
            ExportResultPrompt.OfferToOpen(this, path);
        }
        catch (Exception exception)
        {
            ErrorLog.Log("HistoryWindow.Export", exception);
            MessageBox.Show(
                this,
                $"Excelを出力できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowCandidates(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        var range = new WeekRangeCalculator(_settings.WeekStartsOn).GetWeekRange(_anchor);
        new CandidateWindow(_services, range).Show();
    }

    private sealed record HistoryItem(QuickNote Note)
    {
        public DateTimeOffset CreatedAt => Note.CreatedAt;

        public string Text => Note.Text;

        public string State => Note.DeletedAt is null ? "有効" : "削除済み";
    }
}
