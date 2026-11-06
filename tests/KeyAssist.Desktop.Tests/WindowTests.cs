using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeyAssist.Core;
using KeyAssist.Desktop;

[assembly: DoNotParallelize]

namespace KeyAssist.Desktop.Tests;

[TestClass]
public class WindowTests
{
    private static readonly CatalogSnapshot Catalog = new("驗收範例", false,
        [Entry("fire", "+#% 火焰抗性"), Entry("life", "+# 最大生命"), Entry("mana", "+# 最大魔力"), Entry("es", "+# 最大能量護盾")], []);

    private static AffixEntry Entry(string id, string template) => new(id, template, SourceKind.Explicit, ["數值"], true, []);

    [STATestMethod]
    public void BundledCatalogFindsFireResistanceAcrossSourcesAndPenetration()
    {
        var catalog = CatalogStore.LoadBundled(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json"), Catalog).Snapshot;
        var window = new MainWindow(new SettingsStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")), catalog);
        try
        {
            Click(Find<Button>(window, "nav.crafting"));
            Click(Find<Button>(window, "group.add"));
            var search = Find<TextBox>(window, "affix.query");
            search.Text = "火焰";
            var candidates = Find<ListBox>(window, "affix.candidates").Items.Cast<AffixEntry>().ToArray();
            foreach (var source in new[] { SourceKind.Explicit, SourceKind.Implicit, SourceKind.Crafted })
                Assert.IsTrue(candidates.Any(entry => entry.Template == "+#% 火焰抗性" && entry.Source == source));
            Assert.IsTrue(candidates.Any(entry => entry.Template == "傷害穿透 #% 火焰抗性"));
            Capture(window, "04-affix-search", search);
        }
        finally { window.Close(); }
    }

    [STATestMethod]
    public void HomeCardTargetsMissingScrollYAndBecomesReady()
    {
        WithWindow(new() { Calibration = new(1920, 1080, 96), Values = { ["F03.key"] = "F3", ["F03.scroll.x"] = "100" } },
            (window, _) =>
            {
                var card = Find<Button>(window, "readiness.F03");
                Assert.IsTrue(TextOf(card).Contains("缺少傳送卷軸 Y", StringComparison.Ordinal));
                Capture(window, "01-overview");
                Click(card);
                Find<TextBox>(window, "F03.scroll.y").Text = "200";
                Click(Find<Button>(window, "nav.overview"));
                Assert.IsTrue(TextOf(Find<Button>(window, "readiness.F03")).Contains("已設定", StringComparison.Ordinal));
            });
    }

    [STATestMethod]
    public void DeletedFlaskPointShowsReasonAndHotkeyOnlyFeatureStaysReady()
    {
        var id = Guid.NewGuid();
        var settings = new SettingsSnapshot { Calibration = new(1920, 1080, 96) };
        settings.Values["F05.key"] = "F5";
        settings.Points.Add(new(id, "藥劑像素", "20", "40", "#73B594"));
        settings.ConfirmedPositions[$"point.{id}"] = settings.Calibration;
        settings.Flasks[1] = new(true, true, "2", id);
        WithWindow(settings, (window, _) =>
        {
            StringAssert.Contains(TextOf(Find<Button>(window, "readiness.F38.2")), "已設定");
            Click(Find<Button>(window, "nav.coordinates"));
            Click(Find<Button>(window, $"point.{id}.delete"));
            Click(Find<Button>(window, "nav.overview"));
            var missing = Find<Button>(window, "readiness.F38.2");
            StringAssert.Contains(TextOf(missing), "引用的偵測點已刪除");
            StringAssert.Contains(TextOf(Find<Button>(window, "readiness.F05")), "已設定");
            Click(missing);
            Assert.AreEqual(id.ToString(), Find<ComboBox>(window, "F38.2.point").SelectedValue);
        });
    }

    [STATestMethod]
    public void BackpackOnlyHasTwoCornersAndShortcutSummaryHasNoShowWindow()
    {
        WithWindow(new(), (window, _) =>
        {
            var summary = Find<FrameworkElement>(window, "F26");
            Assert.IsFalse(TextOf(summary).Contains("顯示主視窗", StringComparison.Ordinal));
            Click(Find<Button>(window, "nav.coordinates"));
            var bag = Find<FrameworkElement>(window, "F20");
            CollectionAssert.AreEquivalent(new[] { "F20.top.x", "F20.top.y", "F20.bottom.x", "F20.bottom.y" },
                Elements(bag).OfType<TextBox>().Select(AutomationProperties.GetAutomationId).ToArray());
            window.Width = window.MinWidth;
            Capture(window, "03-narrow");
        });
    }

    [STATestMethod]
    public void CraftingEditsEvaluateAndPersistInNormalWindow()
    {
        WithWindow(new() { Rules = new() { LegacyText = "舊詞綴階級" } }, (window, store) =>
        {
            Click(Find<Button>(window, "nav.crafting"));
            var editor = Find<FrameworkElement>(window, "F36");
            var alteration = Find<FrameworkElement>(window, "F30");
            Assert.AreSame(alteration, ((Panel)editor.Parent).Children[((Panel)editor.Parent).Children.IndexOf(editor) + 1]);
            AddGroup(GroupKind.All, 0);
            AddAffix("火焰", 0);
            Find<TextBox>(window, "rule.min").Text = "30";
            Find<TextBox>(window, "rule.max").Text = "45";
            AddGroup(GroupKind.None, 1);
            AddAffix("最大魔力", 1);
            AddGroup(GroupKind.Count, 2);
            AddAffix("最大生命", 2);
            AddAffix("最大能量護盾", 2);
            AddGroup(GroupKind.Weighted, 3);
            AddAffix("火焰", 3);
            AddAffix("最大生命", 3);
            Find<TextBox>(window, "rule.weight.3.1").Text = "0.5";
            Find<TextBox>(window, "group.min.3").Text = "70";
            Find<TextBox>(window, "item.text").Text =
                "稀有度: 稀有\n測試戒指\n--------\n物品等級: 85\n--------\n+35% 火焰抗性\n+80 最大生命";
            Click(Find<Button>(window, "item.evaluate"));
            var results = Find<FrameworkElement>(window, "item.result");
            StringAssert.Contains(TextOf(results), "所有啟用群組均通過");
            StringAssert.Contains(TextOf(results), "75 分");
            Capture(window, "02-crafting", results);
            Click(Find<Button>(window, "settings.save"));
            var saved = store.Load().Settings;
            Assert.HasCount(4, saved.Rules.Groups);
            Assert.AreEqual("30", saved.Rules.Groups[0].Rows.Single().Min);
            Assert.AreEqual("0.5", saved.Rules.Groups[3].Rows[1].Weight);
            Assert.AreEqual("舊詞綴階級", saved.Rules.LegacyText);
            var reopened = new MainWindow(store, Catalog);
            try { Assert.AreEqual("45", Find<TextBox>(reopened, "rule.max").Text); }
            finally { reopened.Close(); }

            void AddGroup(GroupKind kind, int index)
            {
                Click(Find<Button>(window, "group.add"));
                Find<ComboBox>(window, GroupId("group.kind", index)).SelectedValue = kind.ToString();
            }

            void AddAffix(string text, int index)
            {
                Find<TextBox>(window, GroupId("affix.query", index)).Text = text;
                Find<ListBox>(window, GroupId("affix.candidates", index)).SelectedIndex = 0;
                Click(Find<Button>(window, GroupId("affix.add", index)));
            }
        });
    }

    private static string GroupId(string id, int index) => index == 0 ? id : $"{id}.{index}";

    private static void Capture(Window window, string name, FrameworkElement? target = null)
    {
        var folder = Environment.GetEnvironmentVariable("KEYASSIST_QA_DIR");
        if (string.IsNullOrWhiteSpace(folder))
            return;
        Directory.CreateDirectory(folder);
        window.Show();
        try
        {
            window.UpdateLayout();
            target?.BringIntoView();
            window.UpdateLayout();
            var visual = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(folder, name + ".png"));
            encoder.Save(output);
        }
        finally { window.Hide(); }
    }

    private static void WithWindow(SettingsSnapshot settings, Action<MainWindow, SettingsStore> test)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var store = new SettingsStore(Path.Combine(directory.FullName, "settings.json"));
            Assert.IsNull(store.Save(settings));
            var window = new MainWindow(store, Catalog);
            try { test(window, store); }
            finally { window.Close(); }
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Elements(root).OfType<T>().SingleOrDefault(element => AutomationProperties.GetAutomationId(element) == id)
        ?? throw new AssertFailedException($"找不到介面控制項 {id}");

    private static string TextOf(DependencyObject root) =>
        string.Join(" ", Elements(root).OfType<TextBlock>().Select(element => element.Text));

    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var element in Elements(child))
                yield return element;
    }
}
