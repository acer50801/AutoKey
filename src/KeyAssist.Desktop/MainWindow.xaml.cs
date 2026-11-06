using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeyAssist.Core;

namespace KeyAssist.Desktop;

public partial class MainWindow : Window
{
    private readonly SettingsStore store;
    private readonly SettingsSnapshot draft;
    private readonly CatalogSnapshot catalog;
    private readonly AffixIndex index;
    private readonly Dictionary<string, FrameworkElement> targets = [];
    private readonly Dictionary<string, Button> navigation = [];
    private StackPanel? readinessPanel;
    private StackPanel? shortcutsPanel;
    private string currentPage = "overview";
    private bool dirty;

    private static readonly Brush Ink = Paint("#19283B");
    private static readonly Brush Muted = Paint("#405D72");
    private static readonly Brush Line = Paint("#CAD8E3");

    public MainWindow(SettingsStore store, CatalogSnapshot catalog, string? catalogError = null)
    {
        this.store = store;
        this.catalog = catalog;
        index = new(catalog);
        var loaded = store.Load();
        draft = loaded.Settings;
        InitializeComponent();
        Identify(SaveButton, "settings.save");
        SaveButton.Click += (_, _) => Save();
        Closing += OnClosing;
        foreach (var page in FeatureRequirements.Pages)
        {
            var button = Button(page.Label, () => Navigate(page.Id), "nav." + page.Id);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 0, 0, 7);
            Navigation.Children.Add(button);
            navigation.Add(page.Id, button);
        }
        Navigate(navigation.ContainsKey(draft.SelectedPage) ? draft.SelectedPage : "overview");
        StatusText.Text = loaded.Error is { } error ? "原有設定讀取失敗。 " + error :
            catalogError is not null ? "詞綴索引讀取失敗。 " + catalogError : "離線設定模式 · 可手動編輯與儲存設定";
    }

    private void Navigate(string page)
    {
        currentPage = page;
        PageContent.Children.Clear();
        targets.Clear();
        readinessPanel = shortcutsPanel = null;
        PageScroll.ScrollToTop();
        foreach (var (id, button) in navigation)
        {
            button.Background = Paint(id == page ? "#D6F1EA" : "#F3F7FA");
            button.FontWeight = id == page ? FontWeights.SemiBold : FontWeights.Normal;
        }
        PageContent.Children.Add(Heading(page == "overview" ? "設定檢查" : FeatureRequirements.Pages.Single(p => p.Id == page).Label, 26));
        if (page == "overview")
            BuildOverview();
        if (page == "coordinates")
            BuildCalibration();
        foreach (var feature in FeatureRequirements.Features.Where(feature => feature.PageId == page))
            BuildFeature(feature);
    }

    private void BuildOverview()
    {
        PageContent.Children.Add(Text("紅框表示有設定缺漏，綠框表示設定齊全。點卡片可前往對應欄位。", Muted));
        var jump = Button("前往快捷鍵一覽", () => OpenSetting("F26"));
        jump.HorizontalAlignment = HorizontalAlignment.Left;
        PageContent.Children.Add(jump);
        readinessPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 12) };
        PageContent.Children.Add(readinessPanel);
        var summary = Card(PageContent, "快捷鍵一覽", "F26");
        shortcutsPanel = new StackPanel();
        summary.Children.Add(shortcutsPanel);
        RefreshOverview();
    }

    private void RefreshOverview()
    {
        if (readinessPanel is null || shortcutsPanel is null)
            return;
        readinessPanel.Children.Clear();
        var cards = ReadinessEvaluator.Evaluate(draft);
        var missing = cards.Count(card => !card.IsConfigured);
        readinessPanel.Children.Add(Heading($"{missing} 項未設定 · {cards.Count - missing} 項已設定", 16));
        var overviewCards = Wrap();
        overviewCards.SizeChanged += (_, args) =>
        {
            if (args.WidthChanged)
                overviewCards.ItemWidth = Math.Floor(args.NewSize.Width / (args.NewSize.Width >= 710 ? 2 : 1));
        };
        readinessPanel.Children.Add(overviewCards);
        foreach (var card in cards)
        {
            var status = card.IsConfigured ? "已設定" : "未設定";
            if (!card.IsEnabled)
                status += " · 功能停用";
            var color = Paint(card.IsConfigured ? "#237453" : "#AB3B49");
            var content = new StackPanel();
            content.Children.Add(Heading($"{status}  |  {card.Title}", 15, color));
            content.Children.Add(Text(card.IsConfigured ? "目前所需設定已齊全" :
                string.Join("、", card.Issues.Take(3).Select(issue => issue.Message)) +
                (card.Issues.Count > 3 ? $"等 {card.Issues.Count} 項" : ""), Muted));
            var button = Button(content, () => OpenSetting(card.TargetFieldId), "readiness." + card.FeatureId);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(11, 6, 11, 7);
            button.Margin = new Thickness(0, 0, 7, 7);
            button.BorderThickness = new Thickness(2);
            button.BorderBrush = color;
            button.Background = Paint(card.IsConfigured ? "#F3FBF7" : "#FFF6F6");
            AutomationProperties.SetName(button, status + " " + card.Title);
            overviewCards.Children.Add(button);
        }
        shortcutsPanel.Children.Clear();
        shortcutsPanel.Children.Add(Text("選取快捷鍵可前往編輯。", Muted));
        var keys = FeatureRequirements.Features.SelectMany(feature => feature.Fields
            .Where(field => field.Kind == FieldKind.Key)
            .Select(field => (field.Id, Name: $"{feature.Title} · {field.Label}", Value: draft.Get(field.Id))))
            .Concat(draft.Flasks.Select((slot, i) => ($"F38.{i + 1}.key", $"藥劑 {i + 1} · 補喝輸出鍵", slot.Key)))
            .Concat(draft.Cycle.Select((step, i) => ($"F11.step.{i}", $"循環技能 {i + 1}", step.Key)));
        foreach (var (id, title, value) in keys)
            shortcutsPanel.Children.Add(Button($"{title}    {(!string.IsNullOrWhiteSpace(value) ? value : "未綁定")}", () => OpenSetting(id)));
    }

    private void OpenSetting(string id)
    {
        var feature = id.Split('.')[0];
        var page = feature is "point" or "calibration" ? "coordinates" :
            FeatureRequirements.Features.FirstOrDefault(item => item.Id == feature)?.PageId ?? currentPage;
        if (page != currentPage)
            Navigate(page);
        if (!targets.TryGetValue(id, out var target) && !targets.TryGetValue(feature, out target))
            return;
        target.BringIntoView();
        target.Focus();
        if (target is TextBox box && !box.IsReadOnly)
            box.SelectAll();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (target.IsVisible)
                target.BringIntoView();
        });
    }

    private void BuildFeature(FeatureSpec feature)
    {
        var body = Card(PageContent, feature.Title, feature.Id);
        if (feature.Toggle)
            body.Children.Add(Check("啟用功能", !draft.Disabled.Contains(feature.Id), enabled =>
            {
                if (enabled)
                    draft.Disabled.Remove(feature.Id);
                else
                    draft.Disabled.Add(feature.Id);
                Changed();
            }, feature.Id + ".enabled"));
        var fields = Wrap();
        foreach (var field in feature.Fields)
            AddField(fields, field);
        if (fields.Children.Count > 0)
            body.Children.Add(fields);
        switch (feature.Id)
        {
            case "F25":
                body.Children.Add(Text("遊戲執行引擎尚未接入。此處可保存暫停及停止的按鍵設定。", Muted));
                break;
            case "F38":
                BuildFlasks(body);
                break;
            case "F11":
                BuildCycle(body);
                break;
            case "F13":
                BuildInventory(body);
                break;
            case "F22":
                BuildPoints(body);
                break;
            case "F29":
                body.Children.Add(Text("設定檢查會核對定位、材料上限及模式欄位。詞綴規則請在下方用物品文字單獨驗證。", Muted));
                break;
            case "F30":
            case "F31":
                body.Children.Add(Text("共用上方的詞綴條件與比對規則。", Muted));
                break;
            case "F36":
                BuildRules(body);
                break;
        }
    }

    private void AddField(Panel panel, FieldSpec field)
    {
        var value = draft.Get(field.Id, field.Default);
        void Set(string newValue)
        {
            draft.Values[field.Id] = newValue;
            Changed();
        }

        switch (field.Kind)
        {
            case FieldKind.Key: Labelled(panel, field.Label, Hotkey(value, Set, field.Id)); break;
            case FieldKind.Text: Labelled(panel, field.Label, Input(value, Set, field.Id)); break;
            case FieldKind.Choice: Labelled(panel, field.Label, Select(field.Choices!, value, Set, field.Id)); break;
            case FieldKind.Point: Labelled(panel, field.Label, PointSelect(value, Set, field.Id)); break;
            case FieldKind.Toggle: panel.Children.Add(Check(field.Label, value == "true", enabled => Set(enabled ? "true" : "false"), field.Id)); break;
            case FieldKind.Position:
                foreach (var axis in new[] { "x", "y" })
                {
                    var id = $"{field.Id}.{axis}";
                    Labelled(panel, $"{field.Label} {axis.ToUpperInvariant()}", Input(draft.Get(id), text =>
                    {
                        draft.Values[id] = text;
                        ConfirmEditedPosition(field.Id, draft.Get(field.Id + ".x"), draft.Get(field.Id + ".y"));
                        Changed();
                    }, id));
                }
                break;
        }
    }

    private void Changed()
    {
        dirty = true;
        SaveButton.IsEnabled = true;
        StatusText.Text = "有尚未儲存的變更。";
        if (currentPage == "overview")
            RefreshOverview();
    }

    private bool Save()
    {
        draft.SelectedPage = currentPage;
        if (store.Save(draft) is { } error)
        {
            StatusText.Text = "儲存失敗。 " + error;
            return false;
        }
        dirty = false;
        SaveButton.IsEnabled = false;
        StatusText.Text = "設定已儲存。";
        return true;
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (!IsVisible || !dirty)
            return;
        var action = MessageBox.Show(this, "設定有變更。離開前要儲存嗎？", Title, MessageBoxButton.YesNoCancel);
        args.Cancel = action == MessageBoxResult.Cancel || (action == MessageBoxResult.Yes && !Save());
    }

    private StackPanel Card(Panel panel, string title, string? id = null)
    {
        var body = new StackPanel();
        body.Children.Add(Heading(title, 19));
        var border = new Border
        {
            BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Background = Brushes.White, Padding = new Thickness(13), Margin = new Thickness(0, 0, 0, 10), Child = body
        };
        if (id is not null)
            Register(border, id);
        panel.Children.Add(border);
        return body;
    }

    private T Register<T>(T element, string? id) where T : FrameworkElement
    {
        if (id is not null)
        {
            targets[id] = element;
            Identify(element, id);
        }
        return element;
    }

    private static void Identify(DependencyObject element, string id) => AutomationProperties.SetAutomationId(element, id);

    private Button Button(object label, Action click, string? id = null)
    {
        var button = new Button { Content = label };
        if (id is not null)
            Identify(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    private TextBox Input(string value, Action<string> change, string? id = null)
    {
        var box = Register(new TextBox { Text = value }, id);
        box.TextChanged += (_, _) => change(box.Text);
        return box;
    }

    private TextBox Hotkey(string value, Action<string> change, string id)
    {
        var box = Input(value, change, id);
        box.IsReadOnly = true;
        box.ToolTip = "聚焦後按下快捷鍵。Delete 或 Backspace 清除，Esc 取消，Tab 移至下一欄。";
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
                return;
            e.Handled = true;
            if (key is Key.Escape or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
                return;
            if ((key is Key.Delete or Key.Back) && Keyboard.Modifiers == ModifierKeys.None)
            {
                box.Text = "";
                return;
            }
            var tokens = new List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) tokens.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) tokens.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) tokens.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) tokens.Add("Win");
            tokens.Add(key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
            box.Text = string.Join("+", tokens);
        };
        return box;
    }

    private ComboBox Select(IReadOnlyList<Choice> choices, string value, Action<string> change, string? id = null)
    {
        var combo = Register(new ComboBox
        {
            ItemsSource = choices, DisplayMemberPath = nameof(Choice.Label), SelectedValuePath = nameof(Choice.Id), SelectedValue = value
        }, id);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedValue is string selected)
                change(selected);
        };
        return combo;
    }

    private ComboBox PointSelect(string value, Action<string> change, string id)
    {
        var choices = new List<Choice> { new("", "未選擇") };
        choices.AddRange(draft.Points.Select(point => new Choice(point.Id.ToString(), point.Name)));
        if (value.Length > 0 && choices.All(choice => choice.Id != value))
            choices.Add(new(value, "原點位已刪除"));
        return Select(choices, value, change, id);
    }

    private CheckBox Check(string label, bool value, Action<bool> change, string? id = null)
    {
        var box = Register(new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 5, 14, 9) }, id);
        box.Checked += (_, _) => change(true);
        box.Unchecked += (_, _) => change(false);
        return box;
    }

    private static void Labelled(Panel parent, string label, Control input, double width = 163)
    {
        var pair = new StackPanel { Width = width, Margin = new Thickness(0, 0, 12, 9) };
        var text = Text(label, Muted);
        text.Margin = new Thickness(0, 0, 0, 3);
        AutomationProperties.SetLabeledBy(input, text);
        pair.Children.Add(text);
        pair.Children.Add(input);
        parent.Children.Add(pair);
    }

    private static WrapPanel Wrap() => new() { Margin = new Thickness(0, 3, 0, 0) };

    private static TextBlock Text(string text, Brush? color = null) => new()
    {
        Text = text, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5)
    };

    private static TextBlock Heading(string text, double size, Brush? color = null)
    {
        var heading = Text(text, color);
        heading.FontSize = size;
        heading.FontWeight = FontWeights.SemiBold;
        return heading;
    }

    private static Brush Paint(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }
}
