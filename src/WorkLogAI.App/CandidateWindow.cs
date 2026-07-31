using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WorkLogAI.Core;
using WorkLogAI.Infrastructure;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace WorkLogAI.App;

public sealed class CandidateWindow : Window
{
    private readonly AppServices _services;
    private readonly WeekRange _range;
    private readonly StackPanel _cards = new();
    private readonly StackPanel _coverageBar = new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 0, 0, 10)
    };
    private readonly List<Guid> _pendingDeletedReviewManualEventIds = [];
    // "元データごと削除" queues: the backing events are permanently removed and their
    // source refs suppressed so future collection never revives them; matching quick
    // notes are soft-deleted too, so the history view matches. All flushed together
    // in PersistAsync after SaveReviewAsync succeeds.
    private readonly List<Guid> _pendingSuppressedSourceEventIds = [];
    private readonly List<string> _pendingSuppressedSourceRefs = [];
    private readonly List<Guid> _pendingSoftDeletedQuickNoteIds = [];
    private readonly StackPanel _notesList = new();
    private List<CandidateEditor> _items = [];
    private IReadOnlyList<QuickNote> _weekNotes = [];
    private bool _lowConfidenceOnly;
    private DateOnly? _dayFilter;

    public CandidateWindow(AppServices services, WeekRange range, string? statusBanner = null)
    {
        AppTheme.Apply(this);
        _services = services;
        _range = range;
        Title = $"週次レビュー {range.Start:yyyy/MM/dd}〜{range.End:yyyy/MM/dd}";
        Width = 1_050;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { Margin = new Thickness(12) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        actions.Children.Add(ActionButton("全採用", (_, _) =>
        {
            foreach (var item in _items) item.Selected = true;
        }));
        actions.Children.Add(ActionButton("低確信度だけ表示", (_, _) =>
        {
            _lowConfidenceOnly = !_lowConfidenceOnly;
            RenderCards();
        }));
        actions.Children.Add(ActionButton("重複候補を統合", MergeCandidates));
        actions.Children.Add(ActionButton("1行追加", AddManualRowAsync));
        actions.Children.Add(ActionButton("編集を保存", SaveAsync));
        var exportButton = ActionButton("Excel出力", ExportAsync);
        exportButton.Style = (Style)System.Windows.Application.Current.FindResource("AccentButton");
        actions.Children.Add(exportButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        if (!string.IsNullOrWhiteSpace(statusBanner))
        {
            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock
                {
                    Text = statusBanner,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            DockPanel.SetDock(banner, Dock.Top);
            root.Children.Add(banner);
        }

        DockPanel.SetDock(_coverageBar, Dock.Top);
        root.Children.Add(_coverageBar);

        var notesSidebar = new DockPanel
        {
            Width = 280,
            Margin = new Thickness(12, 0, 0, 0)
        };
        var notesHeading = new TextBlock
        {
            Text = "今週のクイック入力",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(notesHeading, Dock.Top);
        notesSidebar.Children.Add(notesHeading);
        var notesScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _notesList
        };
        notesSidebar.Children.Add(notesScroll);
        DockPanel.SetDock(notesSidebar, Dock.Right);
        root.Children.Add(notesSidebar);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _cards
        };
        root.Children.Add(scroll);
        Content = root;
        Loaded += LoadAsync;
    }

    private async void LoadAsync(object sender, RoutedEventArgs e)
    {
        var candidates = await _services.Candidates.ListAsync(_range.Start);
        var evidenceIds = candidates.SelectMany(item => item.SourceEventIds).Distinct().ToArray();
        var sourceEvents = await _services.SourceEvents.GetByIdsAsync(evidenceIds);
        var byId = sourceEvents.ToDictionary(item => item.Id);
        _items = candidates.Select(candidate =>
            new CandidateEditor(candidate, EvidenceFormatter.Describe(candidate.SourceEventIds, byId))).ToList();

        var bounds = WeekRangeTimeBounds.Local(_range);
        _weekNotes = await _services.Notes.ListAsync(bounds.From, bounds.To);

        RenderCards();
    }

    private void RenderCards()
    {
        _cards.Children.Clear();
        IEnumerable<CandidateEditor> visible = _items;
        if (_lowConfidenceOnly)
        {
            visible = visible.Where(item => item.Confidence < .6);
        }
        if (_dayFilter is { } filterDate)
        {
            visible = visible.Where(item =>
                DateOnly.TryParse(item.WorkDateText, out var date) && date == filterDate);
        }
        var visibleItems = visible.ToList();
        foreach (var item in visibleItems)
        {
            _cards.Children.Add(CreateCard(item));
        }
        if (visibleItems.Count == 0)
        {
            _cards.Children.Add(_dayFilter is { } emptyFilterDate
                ? CreateEmptyDayFilterMessage(emptyFilterDate)
                : new TextBlock { Text = "表示する候補がありません。", Margin = new Thickness(8) });
        }
        RenderCoverageBar();
        RenderNotesSidebar();
    }

    private FrameworkElement CreateEmptyDayFilterMessage(DateOnly date)
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = $"{date:M/d} の候補はありません。" });
        var addButton = ActionButton("この日に1行追加", (_, _) => _ = AddManualRowAsync(date));
        addButton.Margin = new Thickness(0, 8, 0, 0);
        addButton.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(addButton);
        return panel;
    }

    private void RenderCoverageBar()
    {
        _coverageBar.Children.Clear();
        _coverageBar.Children.Add(new TextBlock
        {
            Text = "記入状況:",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        _coverageBar.Children.Add(CreateAllChip());

        var candidateDates = _items
            .Select(item => DateOnly.TryParse(item.WorkDateText, out var date) ? (DateOnly?)date : null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value);
        var coverage = WeekCoverageCalculator.Calculate(_range, candidateDates);
        foreach (var day in coverage)
        {
            _coverageBar.Children.Add(CreateCoverageDayElement(day));
        }
    }

    private Button CreateAllChip()
    {
        var button = new Button
        {
            Content = "全て",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        button.Click += (_, _) =>
        {
            _dayFilter = null;
            RenderCards();
        };
        ApplyChipState(button, _dayFilter is null);
        return button;
    }

    private FrameworkElement CreateCoverageDayElement(DayCoverage day)
    {
        var label = new TextBlock
        {
            Text = $"{JapaneseDayOfWeek(day.DayOfWeek)} {day.Date:M/d}\n{day.CandidateCount}件",
            TextAlignment = TextAlignment.Center
        };

        var button = new Button
        {
            Content = label,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Background = day.HasCandidates
                ? Brushes.WhiteSmoke
                : day.IsWeekday
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xCD, 0xD2))
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0)),
            BorderBrush = day.HasCandidates
                ? Brushes.LightGray
                : day.IsWeekday ? Brushes.IndianRed : Brushes.SandyBrown,
            Foreground = Brushes.Black,
            ToolTip = day.HasCandidates
                ? "クリックしてこの日の候補だけを表示します。"
                : day.IsWeekday
                    ? "この日の候補がありません。クリックして絞り込みます。"
                    : "この日の候補はありません（週末）。クリックして絞り込みます。"
        };
        button.Click += (_, _) =>
        {
            _dayFilter = _dayFilter == day.Date ? null : day.Date;
            RenderCards();
        };
        ApplyChipState(button, _dayFilter == day.Date);
        return button;
    }

    private static void ApplyChipState(Button button, bool isActive)
    {
        if (!isActive)
        {
            return;
        }
        button.BorderThickness = new Thickness(2);
        button.FontWeight = FontWeights.Bold;
    }

    private void RenderNotesSidebar()
    {
        _notesList.Children.Clear();
        IEnumerable<QuickNote> notes = _weekNotes;
        if (_dayFilter is { } filterDate)
        {
            notes = notes.Where(note =>
                DateOnly.FromDateTime(note.CreatedAt.ToLocalTime().DateTime) == filterDate);
        }
        var notesToShow = notes.OrderBy(note => note.CreatedAt).ToList();

        if (notesToShow.Count == 0)
        {
            _notesList.Children.Add(new TextBlock
            {
                Text = "この週のクイック入力はありません。",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var note in notesToShow)
        {
            var localTime = note.CreatedAt.ToLocalTime();
            _notesList.Children.Add(new TextBlock
            {
                Text = $"{localTime:MM/dd}({JapaneseDayOfWeek(localTime.DayOfWeek)}) {localTime:HH:mm}  {note.Text}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }
    }

    private static string JapaneseDayOfWeek(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "月",
        DayOfWeek.Tuesday => "火",
        DayOfWeek.Wednesday => "水",
        DayOfWeek.Thursday => "木",
        DayOfWeek.Friday => "金",
        DayOfWeek.Saturday => "土",
        DayOfWeek.Sunday => "日",
        _ => "?"
    };

    private Border CreateCard(CandidateEditor item)
    {
        var panel = new StackPanel();
        var headerRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        var selected = BoundCheckBox("採用", item, nameof(item.Selected));
        selected.Margin = new Thickness(0);
        DockPanel.SetDock(selected, Dock.Left);
        headerRow.Children.Add(selected);
        var deleteButton = new Button
        {
            Content = "削除",
            Padding = new Thickness(9, 3, 9, 3),
            Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
        };
        deleteButton.Click += (_, _) => _ = DeleteCardAsync(item);
        DockPanel.SetDock(deleteButton, Dock.Right);
        headerRow.Children.Add(deleteButton);
        panel.Children.Add(headerRow);
        panel.Children.Add(Field("日付 (yyyy-MM-dd)", BoundTextBox(item, nameof(item.WorkDateText))));
        panel.Children.Add(Field("業務項目", BoundTextBox(item, nameof(item.WorkItem))));
        panel.Children.Add(Field("活動内容", BoundTextBox(item, nameof(item.Activity), true)));
        panel.Children.Add(Field("結果・決定事項／今後の課題", BoundTextBox(item, nameof(item.ResultOrNext), true)));
        var status = new ComboBox
        {
            ItemsSource = new[] { "completed", "ongoing", "pending" },
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        status.SetBinding(
            ComboBox.SelectedItemProperty,
            new Binding(nameof(item.Status))
            {
                Source = item,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        panel.Children.Add(Field("状態", status));
        panel.Children.Add(new TextBlock
        {
            Text = $"AI確信度: {item.Confidence:P0}",
            Margin = new Thickness(0, 5, 0, 5)
        });
        if (!string.IsNullOrWhiteSpace(item.ConfirmationQuestion))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"確認: {item.ConfirmationQuestion}",
                Foreground = Brushes.DarkOrange,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 5)
            });
        }
        panel.Children.Add(new TextBlock
        {
            Text = $"根拠:\n{item.EvidenceDescription}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0)
        });

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = panel
        };
    }

    private void MergeCandidates(object sender, RoutedEventArgs e)
    {
        if (!TryBuildCandidates(out var candidates))
        {
            return;
        }
        var merged = new CandidateMergeService().Merge(candidates);
        _items = merged.Select(item =>
        {
            var evidence = _items
                .Where(previous => previous.SourceEventIds.Intersect(item.SourceEventIds).Any())
                .Select(previous => previous.EvidenceDescription)
                .Distinct();
            return new CandidateEditor(item with { Edited = true }, string.Join("\n", evidence));
        }).ToList();
        RenderCards();
    }

    private async void AddManualRowAsync(object sender, RoutedEventArgs e) => await AddManualRowAsync((DateOnly?)null);

    private async Task AddManualRowAsync(DateOnly? presetDate)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var workDate = presetDate ?? (_range.Contains(today) ? today : _range.End);
        var localTime = workDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var occurredAt = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
        var sourceEvent = SourceEventFactory.Create(
            occurredAt,
            SourceTypes.Manual,
            "レビュー画面で手動追加",
            "この行はレビュー画面で手動追加されました",
            "ユーザーによる手動追加",
            $"review-manual:{Guid.NewGuid():D}",
            1);
        await _services.SourceEvents.InsertIfNewAsync(sourceEvent);
        var candidate = new ReportCandidate(
            Guid.NewGuid(),
            _range.Start,
            workDate,
            "",
            "",
            "",
            "pending",
            1,
            true,
            true,
            [sourceEvent.Id],
            false,
            null,
            CandidateOrigins.Manual);
        _items.Add(new CandidateEditor(candidate, EvidenceFormatter.Describe([sourceEvent.Id],
            new Dictionary<Guid, SourceEvent> { [sourceEvent.Id] = sourceEvent })));
        _lowConfidenceOnly = false;
        RenderCards();
    }

    private async Task DeleteCardAsync(CandidateEditor item)
    {
        var isManual = item.Candidate.Origin == CandidateOrigins.Manual;
        if (isManual)
        {
            // Manual rows added via "1行追加" already delete fully today (their
            // backing review-manual: event is never re-collectable), so a simple
            // confirm is enough — no need for the row-only vs. row-and-source choice.
            var answer = MessageBox.Show(
                this,
                "この行を削除しますか？",
                "WorkLog AI",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            _items.Remove(item);

            if (item.SourceEventIds.Count > 0)
            {
                var backingEvents = await _services.SourceEvents.GetByIdsAsync(item.SourceEventIds);
                foreach (var sourceEvent in backingEvents)
                {
                    if (sourceEvent.SourceRef.StartsWith("review-manual:", StringComparison.Ordinal))
                    {
                        _pendingDeletedReviewManualEventIds.Add(sourceEvent.Id);
                    }
                }
            }

            RenderCards();
            return;
        }

        var dialog = new DeleteCardConfirmWindow { Owner = this };
        dialog.ShowDialog();
        if (dialog.Choice == DeleteCardChoice.Cancel)
        {
            return;
        }

        _items.Remove(item);

        if (dialog.Choice == DeleteCardChoice.RowAndSource && item.SourceEventIds.Count > 0)
        {
            var backingEvents = await _services.SourceEvents.GetByIdsAsync(item.SourceEventIds);
            foreach (var sourceEvent in backingEvents)
            {
                _pendingSuppressedSourceEventIds.Add(sourceEvent.Id);
                if (!string.IsNullOrWhiteSpace(sourceEvent.SourceRef))
                {
                    _pendingSuppressedSourceRefs.Add(sourceEvent.SourceRef);
                }
                if (sourceEvent.SourceRef.StartsWith("quick-note:", StringComparison.Ordinal))
                {
                    var idText = sourceEvent.SourceRef["quick-note:".Length..];
                    if (Guid.TryParse(idText, out var noteId))
                    {
                        _pendingSoftDeletedQuickNoteIds.Add(noteId);
                    }
                }
            }
        }

        RenderCards();
    }

    private async void SaveAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await PersistAsync())
            {
                MessageBox.Show(this, "レビュー内容を保存しました。", "WorkLog AI");
            }
        }
        catch (Exception exception)
        {
            ErrorLog.Log("CandidateWindow.Save", exception);
            MessageBox.Show(
                this,
                $"保存できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await PersistAsync() || !TryBuildCandidates(out var candidates))
            {
                return;
            }
            var rows = new CandidateReportMapper().MapSelected(candidates);
            if (rows.Count == 0)
            {
                MessageBox.Show(this, "採用された候補がありません。", "WorkLog AI");
                return;
            }
            if (!ConfirmBlankWeekdays())
            {
                return;
            }

            var settings = await _services.Settings.LoadAsync();
            var path = await _services.Exporter.ExportAsync(
                _range,
                rows,
                settings.ExcelOutputDirectory,
                new ReportIdentity(settings.CompanyName, settings.EmployeeName, settings.ReportTitle));
            ExportResultPrompt.OfferToOpen(this, path);
        }
        catch (Exception exception)
        {
            ErrorLog.Log("CandidateWindow.Export", exception);
            MessageBox.Show(
                this,
                $"Excelを出力できませんでした。\n{exception.Message}",
                "WorkLog AI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool ConfirmBlankWeekdays()
    {
        var selectedDates = _items
            .Where(item => item.Selected)
            .Select(item => DateOnly.TryParse(item.WorkDateText, out var date) ? (DateOnly?)date : null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value);
        var blankWeekdays = WeekCoverageCalculator.Calculate(_range, selectedDates)
            .Where(day => day.IsWeekday && !day.HasCandidates)
            .ToList();
        if (blankWeekdays.Count == 0)
        {
            return true;
        }

        var days = string.Join(", ", blankWeekdays.Select(day => JapaneseDayOfWeek(day.DayOfWeek)));
        var answer = MessageBox.Show(
            this,
            $"空白の平日があります: {days}\nこのままExcelを出力しますか？",
            "WorkLog AI",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }

    private async Task<bool> PersistAsync()
    {
        if (!TryBuildCandidates(out var candidates))
        {
            return false;
        }
        await _services.Candidates.SaveReviewAsync(_range.Start, candidates);
        if (_pendingDeletedReviewManualEventIds.Count > 0)
        {
            try
            {
                await _services.SourceEvents.DeleteByIdsAsync(_pendingDeletedReviewManualEventIds);
            }
            catch (Exception exception)
            {
                ErrorLog.Log("CandidateWindow.DeleteManualEvents", exception);
            }
            finally
            {
                _pendingDeletedReviewManualEventIds.Clear();
            }
        }
        if (_pendingSuppressedSourceEventIds.Count > 0 || _pendingSuppressedSourceRefs.Count > 0)
        {
            try
            {
                await _services.SourceEvents.DeleteByIdsAsync(_pendingSuppressedSourceEventIds);
                await _services.SourceEvents.SuppressSourceRefsAsync(
                    _pendingSuppressedSourceRefs,
                    DateTimeOffset.Now);
                foreach (var noteId in _pendingSoftDeletedQuickNoteIds)
                {
                    await _services.Notes.SoftDeleteAsync(noteId);
                }
            }
            catch (Exception exception)
            {
                ErrorLog.Log("CandidateWindow.SuppressSources", exception);
            }
            finally
            {
                _pendingSuppressedSourceEventIds.Clear();
                _pendingSuppressedSourceRefs.Clear();
                _pendingSoftDeletedQuickNoteIds.Clear();
            }
        }
        _items = candidates.Select((item, index) =>
            new CandidateEditor(item, _items[index].EvidenceDescription)).ToList();
        return true;
    }

    private bool TryBuildCandidates(out IReadOnlyList<ReportCandidate> candidates)
    {
        var result = new List<ReportCandidate>();
        foreach (var item in _items)
        {
            if (!DateOnly.TryParse(item.WorkDateText, out var date) || !_range.Contains(date)
                || string.IsNullOrWhiteSpace(item.WorkItem)
                || string.IsNullOrWhiteSpace(item.Activity)
                || item.SourceEventIds.Count == 0
                || item.Status is not ("completed" or "ongoing" or "pending"))
            {
                MessageBox.Show(
                    this,
                    "日付、業務項目、活動内容、状態を確認してください。日付は対象週内である必要があります。",
                    "WorkLog AI");
                candidates = [];
                return false;
            }
            result.Add(item.ToCandidate(date));
        }
        candidates = result;
        return true;
    }

    private static Button ActionButton(string text, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(9, 5, 9, 5),
            Margin = new Thickness(0, 0, 6, 0)
        };
        button.Click += handler;
        return button;
    }

    private static CheckBox BoundCheckBox(string text, object source, string property)
    {
        var control = new CheckBox { Content = text, Margin = new Thickness(0, 0, 0, 6) };
        control.SetBinding(
            CheckBox.IsCheckedProperty,
            new Binding(property)
            {
                Source = source,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        return control;
    }

    private static TextBox BoundTextBox(object source, string property, bool multiline = false)
    {
        var control = new TextBox
        {
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 54 : 25,
            VerticalScrollBarVisibility = multiline
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled
        };
        control.SetBinding(
            TextBox.TextProperty,
            new Binding(property)
            {
                Source = source,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        return control;
    }

    private static FrameworkElement Field(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(control);
        return panel;
    }

    private sealed class CandidateEditor : INotifyPropertyChanged
    {
        private string _workDateText;
        private string _workItem;
        private string _activity;
        private string _resultOrNext;
        private string _status;
        private string _category;
        private bool _selected;

        public CandidateEditor(ReportCandidate candidate, string evidenceDescription)
        {
            Candidate = candidate;
            _workDateText = candidate.WorkDate.ToString("yyyy-MM-dd");
            _workItem = candidate.WorkItem;
            _activity = candidate.Activity;
            _resultOrNext = candidate.ResultOrNext;
            _status = candidate.Status;
            _category = candidate.Category;
            _selected = candidate.Selected;
            EvidenceDescription = evidenceDescription;
        }

        public ReportCandidate Candidate { get; }
        public IReadOnlyList<Guid> SourceEventIds => Candidate.SourceEventIds;
        public double Confidence => Candidate.Confidence;
        public string? ConfirmationQuestion => Candidate.ConfirmationQuestion;
        public string EvidenceDescription { get; }

        public string WorkDateText { get => _workDateText; set => Set(ref _workDateText, value); }
        public string WorkItem { get => _workItem; set => Set(ref _workItem, value); }
        public string Activity { get => _activity; set => Set(ref _activity, value); }
        public string ResultOrNext { get => _resultOrNext; set => Set(ref _resultOrNext, value); }
        public string Status { get => _status; set => Set(ref _status, value); }
        public string Category { get => _category; set => Set(ref _category, value); }
        public bool Selected { get => _selected; set => Set(ref _selected, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ReportCandidate ToCandidate(DateOnly date) => Candidate with
        {
            WorkDate = date,
            WorkItem = WorkItem.Trim(),
            Activity = Activity.Trim(),
            ResultOrNext = ResultOrNext.Trim(),
            Status = Status,
            Category = Category,
            Selected = Selected,
            Edited = true
        };

        private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}
