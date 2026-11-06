using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyAssist.Core;

namespace KeyAssist.Desktop;

public partial class MainWindow
{
    private static readonly Choice[] GroupKinds =
    [
        new(nameof(GroupKind.All), "必須命中"), new(nameof(GroupKind.None), "排除"),
        new(nameof(GroupKind.Count), "至少命中 N 條"), new(nameof(GroupKind.Weighted), "加權總和")
    ];

    private string itemText = "";

    private void BuildRules(Panel body)
    {
        var entries = catalog.Entries.ToDictionary(entry => entry.Id);
        body.Children.Add(Text($"本機詞綴索引 {catalog.Version} · {catalog.Entries.Length:N0} 個候選。所有啟用的群組須通過。", Muted));
        var coverage = new Expander { Header = "來源覆蓋與缺口", Margin = new Thickness(0, 3, 0, 8) };
        var coverageText = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
        foreach (var source in catalog.Coverage)
            coverageText.Children.Add(Text($"{source.Category} · 納入 {source.Included:N0} · 略過 {source.Rejected:N0} · {source.Status}\n{source.Url}", Muted));
        coverage.Content = coverageText;
        body.Children.Add(coverage);
        var groupList = new StackPanel();
        var resultPanel = new StackPanel();
        body.Children.Add(Button("＋ 新增群組", () =>
        {
            draft.Rules.Groups.Add(new());
            Edited();
            RenderGroups();
        }, "group.add"));
        body.Children.Add(groupList);
        var legacy = new Expander { Header = "舊條件原文", Margin = new Thickness(0, 3, 0, 10), IsExpanded = draft.Rules.LegacyText.Length > 0 };
        var legacyText = Input(draft.Rules.LegacyText, text => { draft.Rules.LegacyText = text; Changed(); });
        legacyText.AcceptsReturn = true;
        legacyText.TextWrapping = TextWrapping.Wrap;
        legacy.Content = legacyText;
        body.Children.Add(legacy);
        body.Children.Add(Heading("離線物品文字比對", 16));
        body.Children.Add(Text("貼上繁中物品文字，保留稀有度、物品等級及分隔線。遇到可能屬於同一混合詞綴的文字時，請改貼含詞綴標頭的詳細版本。", Muted));
        var item = Input(itemText, text => itemText = text, "item.text");
        item.AcceptsReturn = true;
        item.TextWrapping = TextWrapping.Wrap;
        item.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        item.Height = 105;
        item.Margin = new Thickness(0, 0, 0, 7);
        body.Children.Add(item);
        Identify(resultPanel, "item.result");
        item.TextChanged += (_, _) => resultPanel.Children.Clear();
        body.Children.Add(Button("比對物品文字", Evaluate, "item.evaluate"));
        body.Children.Add(resultPanel);
        RenderGroups();

        void Edited()
        {
            Changed();
            resultPanel.Children.Clear();
        }

        void RenderGroups()
        {
            groupList.Children.Clear();
            for (var i = 0; i < draft.Rules.Groups.Count; i++)
            {
                var group = draft.Rules.Groups[i];
                BuildGroup(Card(groupList, $"群組 {i + 1}"), group, i);
            }
        }

        void BuildGroup(Panel panel, RuleGroup group, int groupIndex)
        {
            var options = Wrap();
            var kindSelector = Select(GroupKinds, group.Kind.ToString(), value =>
            {
                group.Kind = Enum.Parse<GroupKind>(value);
                Edited();
                RenderGroups();
            });
            AutomationName(kindSelector, "群組類型", "group.kind", groupIndex);
            Labelled(options, "類型", kindSelector);
            options.Children.Add(Check("啟用群組", group.Enabled, enabled => { group.Enabled = enabled; Edited(); }));
            options.Children.Add(Button("刪除群組", () =>
            {
                draft.Rules.Groups.Remove(group);
                Edited();
                RenderGroups();
            }));
            panel.Children.Add(options);
            var limits = Wrap();
            if (group.Kind == GroupKind.Count)
                Labelled(limits, "至少命中條數", Input(group.Count, value => { group.Count = value; Edited(); }), 110);
            if (group.Kind == GroupKind.Weighted)
            {
                var minimum = Input(group.Min, value => { group.Min = value; Edited(); });
                AutomationName(minimum, "最低分", "group.min", groupIndex);
                Labelled(limits, "最低分", minimum, 110);
                Labelled(limits, "最高分", Input(group.Max, value => { group.Max = value; Edited(); }), 110);
            }
            panel.Children.Add(limits);
            var query = new TextBox { Margin = new Thickness(0, 3, 0, 5) };
            AutomationName(query, "搜尋詞綴", "affix.query", groupIndex);
            panel.Children.Add(Text("搜尋詞綴（例如火焰）", Muted));
            panel.Children.Add(query);
            var candidates = new ListBox
            {
                DisplayMemberPath = nameof(AffixEntry.Display), MaxHeight = 145,
                Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed
            };
            AutomationName(candidates, "詞綴候選", "affix.candidates", groupIndex);
            panel.Children.Add(candidates);
            var candidateNote = Text("", Muted);
            panel.Children.Add(candidateNote);
            var rows = new StackPanel();
            var add = Button("加入選取詞綴", AddSelected);
            AutomationName(add, "加入選取詞綴", "affix.add", groupIndex);
            add.IsEnabled = false;
            panel.Children.Add(add);
            panel.Children.Add(rows);
            RenderRows();

            query.TextChanged += (_, _) =>
            {
                var results = index.Search(query.Text);
                candidateNote.Text = results.Count == 0 && !string.IsNullOrWhiteSpace(query.Text) ? "沒有相符的候選。" : "";
                candidates.ItemsSource = results;
                candidates.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                candidates.SelectedIndex = results.Count > 0 ? 0 : -1;
            };
            candidates.SelectionChanged += (_, _) =>
            {
                var entry = candidates.SelectedItem as AffixEntry;
                add.IsEnabled = entry is not null;
                if (entry is null)
                    return;
                var categories = string.Join("、", entry.Evidence.Select(evidence => evidence.Category).Distinct());
                candidateNote.Text = (entry.CanEvaluate ? "" : "僅供搜尋。 ") +
                    (categories.Length > 0 ? $"來源 {categories} · {entry.Evidence.Length} 筆資料" : "");
            };
            query.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Down or Key.Up && candidates.Items.Count > 0)
                {
                    candidates.SelectedIndex = Math.Clamp(candidates.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, candidates.Items.Count - 1);
                    candidates.ScrollIntoView(candidates.SelectedItem);
                    e.Handled = true;
                }
                if (e.Key == Key.Enter)
                {
                    AddSelected();
                    e.Handled = true;
                }
            };
            candidates.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    AddSelected();
                    e.Handled = true;
                }
            };
            candidates.MouseDoubleClick += (_, _) => AddSelected();

            void AddSelected()
            {
                if (candidates.SelectedItem is not AffixEntry entry)
                    return;
                if (group.Rows.Any(row => row.AffixId == entry.Id))
                {
                    candidateNote.Text = "本群組已加入此詞綴。";
                    return;
                }
                group.Rows.Add(new(entry.Id));
                query.Clear();
                Edited();
                RenderRows();
            }

            void RenderRows()
            {
                rows.Children.Clear();
                for (var i = 0; i < group.Rows.Count; i++)
                    BuildRow(group.Rows[i], i);
            }

            void BuildRow(RuleRow row, int rowIndex)
            {
                entries.TryGetValue(row.AffixId, out var entry);
                var card = Card(rows, entry?.Display ?? "詞綴待修");
                if (entry is null || !entry.CanEvaluate)
                    card.Children.Add(Text(entry is null ? $"此詞綴已不在索引。識別碼 {row.AffixId}" : "此來源或格式尚待核對，暫無法比對。", Muted));
                if (entry is { CanEvaluate: true })
                {
                    var numbers = Wrap();
                    if (entry.Metrics.Length > 1)
                    {
                        Choice[] metrics = [new("", "選擇數值"), .. entry.Metrics.Select((name, n) => new Choice(n.ToString(), name))];
                        Labelled(numbers, "比對欄位", Select(metrics, row.Metric?.ToString() ?? "", value =>
                        {
                            row.Metric = int.TryParse(value, out var metric) ? metric : null;
                            Edited();
                        }));
                    }
                    if (entry.Metrics.Length > 0)
                    {
                        var suffix = groupIndex == 0 && rowIndex == 0 ? "" : $".{groupIndex}.{rowIndex}";
                        Labelled(numbers, "最小值", Input(row.Min, value => { row.Min = value; Edited(); }, "rule.min" + suffix), 91);
                        Labelled(numbers, "最大值", Input(row.Max, value => { row.Max = value; Edited(); }, "rule.max" + suffix), 91);
                        if (group.Kind == GroupKind.Weighted)
                            Labelled(numbers, "權重", Input(row.Weight, value => { row.Weight = value; Edited(); }, "rule.weight" + suffix), 91);
                    }
                    else
                        card.Children.Add(Text("此詞綴只做有無判斷。", Muted));
                    card.Children.Add(numbers);
                }
                card.Children.Add(Button("刪除條件", () =>
                {
                    group.Rows.Remove(row);
                    Edited();
                    RenderRows();
                }));
            }
        }

        void Evaluate()
        {
            var read = ItemAffixReader.Read(itemText, catalog);
            var result = RuleEvaluator.Evaluate(draft.Rules, read, catalog);
            resultPanel.Children.Clear();
            resultPanel.Children.Add(Heading(Overall(result.Verdict), 18, Paint(result.Verdict == Verdict.Passed ? "#237453" : "#AB3B49")));
            resultPanel.Children.Add(Text(result.Reason));
            if (read.Warning is not null && read.Warning != result.Reason)
                resultPanel.Children.Add(Text(read.Warning, Muted));
            var activeGroups = draft.Rules.Groups.Select((group, i) => (group, i)).Where(pair => pair.group.Enabled).ToArray();
            for (var i = 0; i < result.Groups.Count; i++)
            {
                var group = result.Groups[i];
                var name = GroupKinds.Single(kind => kind.Id == group.Kind.ToString()).Label;
                resultPanel.Children.Add(Heading($"群組 {activeGroups[i].i + 1} · {name} · {Overall(group.Verdict)}", 15));
                resultPanel.Children.Add(Text($"命中 {group.Hits} 條" + (group.Score is { } score ? $" · {score:0.############################} 分" : "") + $" · {group.Reason}"));
                foreach (var row in group.Rows)
                    resultPanel.Children.Add(Text($"{entries.GetValueOrDefault(row.AffixId)?.Display ?? row.AffixId} · {row.Reason}" +
                        (row.Value is { } roll ? $"（{roll}）" : ""), Muted));
            }
            resultPanel.BringIntoView();
        }
    }

    private static void AutomationName(FrameworkElement element, string label, string id, int group)
    {
        Identify(element, group == 0 ? id : $"{id}.{group}");
        System.Windows.Automation.AutomationProperties.SetName(element, label);
    }

    private static string Overall(Verdict verdict) => verdict switch
    {
        Verdict.Passed => "達標", Verdict.Failed => "未達標", Verdict.Invalid => "條件待修", _ => "無法判定"
    };
}
