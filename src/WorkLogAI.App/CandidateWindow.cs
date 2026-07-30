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
    private List<CandidateEditor> _items = [];
    private bool _lowConfidenceOnly;

    public CandidateWindow(AppServices services, WeekRange range)
    {
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
        actions.Children.Add(ActionButton("Excel出力", ExportAsync));
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

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
            new CandidateEditor(candidate, DescribeEvidence(candidate.SourceEventIds, byId))).ToList();
        RenderCards();
    }

    private void RenderCards()
    {
        _cards.Children.Clear();
        var visible = _lowConfidenceOnly
            ? _items.Where(item => item.Confidence < .6)
            : _items;
        foreach (var item in visible)
        {
            _cards.Children.Add(CreateCard(item));
        }
        if (!_cards.Children.Cast<UIElement>().Any())
        {
            _cards.Children.Add(new TextBlock
            {
                Text = "表示する候補がありません。",
                Margin = new Thickness(8)
            });
        }
    }

    private static Border CreateCard(CandidateEditor item)
    {
        var panel = new StackPanel();
        panel.Children.Add(BoundCheckBox("採用", item, nameof(item.Selected)));
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
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
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

    private async void AddManualRowAsync(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var workDate = _range.Contains(today) ? today : _range.End;
        var localTime = workDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var occurredAt = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
        var sourceEvent = SourceEventFactory.Create(
            occurredAt,
            SourceTypes.Manual,
            "レビュー画面で手動追加",
            "ユーザーが明示的に追加した週報行",
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
        _items.Add(new CandidateEditor(candidate, DescribeEvidence([sourceEvent.Id],
            new Dictionary<Guid, SourceEvent> { [sourceEvent.Id] = sourceEvent })));
        _lowConfidenceOnly = false;
        RenderCards();
    }

    private async void SaveAsync(object sender, RoutedEventArgs e)
    {
        if (await PersistAsync())
        {
            MessageBox.Show(this, "レビュー内容を保存しました。", "WorkLog AI");
        }
    }

    private async void ExportAsync(object sender, RoutedEventArgs e)
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
        var settings = await _services.Settings.LoadAsync();
        var path = await _services.Exporter.ExportAsync(
            _range,
            rows,
            settings.ExcelOutputDirectory,
            new ReportIdentity(settings.CompanyName, settings.EmployeeName));
        MessageBox.Show(this, $"Excelを出力しました。\n{path}", "WorkLog AI");
    }

    private async Task<bool> PersistAsync()
    {
        if (!TryBuildCandidates(out var candidates))
        {
            return false;
        }
        await _services.Candidates.SaveReviewAsync(_range.Start, candidates);
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

    private static string DescribeEvidence(
        IReadOnlyList<Guid> ids,
        IReadOnlyDictionary<Guid, SourceEvent> events) =>
        string.Join(
            "\n",
            ids.Select(id => events.TryGetValue(id, out var source)
                ? $"{source.OccurredAt:yyyy/MM/dd HH:mm} [{source.SourceType}] " +
                  $"{SafeTextSanitizer.Sanitize(source.Title, 300)} — " +
                  $"{SafeTextSanitizer.Sanitize(source.Evidence, 800)}"
                : $"[{id:D}] 根拠詳細なし"));

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
        private bool _selected;

        public CandidateEditor(ReportCandidate candidate, string evidenceDescription)
        {
            Candidate = candidate;
            _workDateText = candidate.WorkDate.ToString("yyyy-MM-dd");
            _workItem = candidate.WorkItem;
            _activity = candidate.Activity;
            _resultOrNext = candidate.ResultOrNext;
            _status = candidate.Status;
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
        public bool Selected { get => _selected; set => Set(ref _selected, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ReportCandidate ToCandidate(DateOnly date) => Candidate with
        {
            WorkDate = date,
            WorkItem = WorkItem.Trim(),
            Activity = Activity.Trim(),
            ResultOrNext = ResultOrNext.Trim(),
            Status = Status,
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
