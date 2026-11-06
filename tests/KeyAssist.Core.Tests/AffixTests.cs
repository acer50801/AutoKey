using KeyAssist.Core;

namespace KeyAssist.Core.Tests;

[TestClass]
public class AffixTests
{
    internal static readonly CatalogSnapshot Catalog = new("test", true,
        [
            Entry("fire", "+#% 火焰抗性", SourceKind.Explicit, "%"),
            Entry("fixed", "+#% 火焰抗性", SourceKind.Implicit, "%"),
            Entry("pen", "傷害穿透 #% 火焰抗性", SourceKind.Explicit, "%"),
            Entry("life", "+# 最大生命", SourceKind.Explicit, "值"),
            Entry("mana", "+# 最大魔力", SourceKind.Explicit, "值"),
            Entry("es", "+# 最大能量護盾", SourceKind.Explicit, "值"),
            Entry("damage", "攻擊附加 # 至 # 火焰傷害", SourceKind.Explicit, "傷害下限", "傷害上限"),
            Entry("freeze", "免疫冰凍", SourceKind.Explicit)
        ],
        [new("All", "https://poedb.tw/tw/Modifiers_list", 8, 0, "已匯入")]);

    private static AffixEntry Entry(string id, string text, SourceKind source, params string[] metrics) =>
        new(id, text, source, metrics, true, [new("All", "https://poedb.tw/tw/Modifiers_list", "id-" + id, "測試")]);

    internal static string Item(params string[] mods) =>
        "物品種類: 戒指\n稀有度: 稀有\n測試之環\n紅玉戒指\n--------\n物品等級: 85\n--------\n" + string.Join('\n', mods);

    [TestMethod]
    public void FireSearchRetainsImplicitAndExplicitCandidates()
    {
        var results = new AffixIndex(Catalog).Search("火焰　抗性");
        CollectionAssert.AreEquivalent(new[] { "fire", "fixed", "pen" }, results.Select(result => result.Id).ToArray());
        Assert.IsEmpty(new AffixIndex(Catalog).Search("查無此詞綴"));
    }

    [TestMethod]
    public void IncompleteUpdateKeepsPreviousCatalog()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new CatalogStore(Path.Combine(folder.FullName, "catalog.json"));
            Assert.IsNull(store.Replace(Catalog));
            Assert.IsNotNull(store.Replace(Catalog with { Version = "broken", IsComplete = false }));
            Assert.AreEqual("test", store.Load(Catalog with { Version = "fallback" }).Snapshot.Version);
        }
        finally { folder.Delete(recursive: true); }
    }

    [TestMethod]
    [DataRow("30", "", 29, Verdict.Failed)]
    [DataRow("30", "", 30, Verdict.Passed)]
    [DataRow("", "45", 45, Verdict.Passed)]
    [DataRow("", "45", 46, Verdict.Failed)]
    [DataRow("30", "45", 29, Verdict.Failed)]
    [DataRow("30", "45", 30, Verdict.Passed)]
    [DataRow("30", "45", 45, Verdict.Passed)]
    [DataRow("30", "45", 46, Verdict.Failed)]
    [DataRow("46", "45", 45, Verdict.Invalid)]
    [DataRow("", "", 0, Verdict.Passed)]
    public void InclusiveAndIndependentBounds(string min, string max, int roll, Verdict expected)
    {
        var result = Evaluate(Item($"+{roll}% 火焰抗性"), Group(GroupKind.All, new("fire") { Min = min, Max = max }));
        Assert.AreEqual(expected, result.Verdict);
    }

    [TestMethod]
    public void FourGroupsCompose()
    {
        var score = Group(GroupKind.Weighted, new("fire"), new RuleRow("life") { Weight = "0.5" });
        score.Min = "70";
        RuleGroup[] groups =
        [
            Group(GroupKind.All, new("fire") { Min = "30", Max = "45" }),
            Group(GroupKind.None, new("mana")),
            Group(GroupKind.Count, new RuleRow("life") { Min = "60" }, new RuleRow("es") { Min = "30" }),
            score
        ];
        var item = Item("+35% 火焰抗性", "+80 最大生命");
        var result = Evaluate(item, groups);
        Assert.AreEqual(Verdict.Passed, result.Verdict);
        Assert.AreEqual(75m, result.Groups[3].Score);
        Assert.AreEqual(Verdict.Failed, Evaluate(Item("+35% 火焰抗性", "+80 最大生命", "+40 最大魔力"), groups).Verdict);
        score.Min = "76";
        Assert.AreEqual(Verdict.Failed, Evaluate(item, groups).Verdict);
    }

    [TestMethod]
    public void SourcesAndMetricSelectionUseActualRolls()
    {
        Assert.AreEqual(Verdict.Failed, Evaluate(Item("+35% 火焰抗性 (implicit)"), Group(GroupKind.All, new("fire"))).Verdict);
        Assert.AreEqual(Verdict.Passed, Evaluate(Item("攻擊附加 12(10-13) 至 40(35-45) 火焰傷害"),
            Group(GroupKind.All, new("damage") { Metric = 1, Min = "40" })).Verdict);
        Assert.AreEqual(Verdict.Invalid, Evaluate(Item("免疫冰凍"), Group(GroupKind.All, new("freeze") { Min = "1" })).Verdict);
    }

    [TestMethod]
    public void KnownAbsenceIsZeroButAmbiguousInputNeverPasses()
    {
        var group = Group(GroupKind.Weighted, new("mana"));
        group.Min = "0";
        var absent = Evaluate(Item("+35% 火焰抗性"), group);
        Assert.AreEqual(Verdict.Passed, absent.Verdict);
        Assert.AreEqual(0m, absent.Groups[0].Score);
        Assert.AreEqual(Verdict.Indeterminate, Evaluate("+35% 火焰抗性", group).Verdict);
        Assert.AreEqual(Verdict.Indeterminate, Evaluate(Item("+?? 最大魔力"), group).Verdict);
        Assert.AreEqual(Verdict.Invalid, Evaluate(Item("+35% 火焰抗性"),
            Group(GroupKind.Count, new("fire"), new RuleRow("fire") { Min = "31" })).Verdict);
    }

    [TestMethod]
    public void MissingSavedCandidateCannotSilentlyPass()
    {
        var result = Evaluate(Item("+35% 火焰抗性"), Group(GroupKind.None, new("deleted-id")));
        Assert.AreEqual(Verdict.Invalid, result.Verdict);
    }

    [TestMethod]
    public void DetailedHybridDoesNotCountAsTwoStandaloneAffixes()
    {
        var catalog = Catalog with
        {
            Entries = [.. Catalog.Entries,
                Entry("avoid", "#% 機率避免來自擊中的火焰傷害", SourceKind.Explicit, "%"),
                Entry("hybrid", "+#% 火焰抗性 ／ #% 機率避免來自擊中的火焰傷害", SourceKind.Explicit, "%", "%")
                    with { CanEvaluate = false }]
        };
        var group = Group(GroupKind.Count, new("fire"), new RuleRow("avoid"));
        group.Count = "2";
        var rules = new RuleSet { Groups = [group] };
        const string fire = "+35(30-40)% 火焰抗性";
        const string avoid = "20(15-25)% 機率避免來自擊中的火焰傷害";
        var hybrid = Item("{ 後綴詞綴「複合詞綴」 }", fire, avoid);
        Assert.AreEqual(Verdict.Failed, RuleEvaluator.Evaluate(rules, ItemAffixReader.Read(hybrid, catalog), catalog).Verdict);
        var standalone = Item("{ 後綴詞綴「抗性」 }", fire, "{ 後綴詞綴「避免傷害」 }", avoid);
        Assert.AreEqual(Verdict.Passed, RuleEvaluator.Evaluate(rules, ItemAffixReader.Read(standalone, catalog), catalog).Verdict);
        Assert.AreEqual(Verdict.Indeterminate,
            RuleEvaluator.Evaluate(rules, ItemAffixReader.Read(Item(fire, avoid), catalog), catalog).Verdict);
    }

    [TestMethod]
    public void UpdateLosingPreviouslyImportedCoverageKeepsLastIndex()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new CatalogStore(Path.Combine(folder.FullName, "catalog.json"));
            var boots = new SourceCoverage("鞋子", "https://poedb.tw/tw/Boots", 10, 0, "已匯入", Required: false);
            var expanded = Catalog with { Coverage = [.. Catalog.Coverage, boots] };
            Assert.IsNull(store.Replace(expanded));
            var contracted = Catalog with { Version = "contracted", Coverage = [.. Catalog.Coverage, boots with { Included = 0 }] };
            Assert.IsNotNull(store.Replace(contracted));
            Assert.AreEqual("test", store.Load(Catalog with { Version = "fallback" }).Snapshot.Version);
        }
        finally { folder.Delete(recursive: true); }
    }

    private static RuleGroup Group(GroupKind kind, RuleRow first, params RuleRow[] others) =>
        new() { Kind = kind, Rows = [first, .. others] };

    private static RuleResult Evaluate(string text, params RuleGroup[] groups) =>
        RuleEvaluator.Evaluate(new() { Groups = [.. groups] }, ItemAffixReader.Read(text, Catalog), Catalog);
}
