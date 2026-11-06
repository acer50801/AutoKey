using KeyAssist.Catalog;
using KeyAssist.Core;

namespace KeyAssist.Core.Tests;

[TestClass]
public class CatalogImportTests
{
    private static readonly Dictionary<string, string> Pages = new()
    {
        ["Modifiers"] = """<a href="/tw/Rings#ModifiersCalc">戒指</a><a href="/tw/Boots#ModifiersCalc">鞋子</a>""",
        ["Modifiers_list"] = """
            <table><thead><tr><th>Level</th><th>Pre/Suf</th><th>Name</th><th>Tags</th></tr></thead><tbody>
            <tr><td>1</td><td>後綴</td><td><span class="mod-value">+(8—12)</span>% 火焰抗性</td><td></td></tr>
            <tr><td>8</td><td>後綴</td><td><span class="mod-value">+(13—17)</span>% 火焰抗性</td><td></td></tr>
            <tr><td>26</td><td>前綴</td><td>傷害穿透 <span class="mod-value">4</span>% 火焰抗性</td><td></td></tr>
            <tr><td>26</td><td>意外類型</td><td>其他詞綴</td><td></td></tr>
            </tbody></table>
            """,
        ["Rings"] = """
            <script>$(function() { new ModsView({"normal":[{"Tier":"3","Name":"烈焰之","Level":"12","ModGenerationTypeID":"2","str":"<span class='mod-value'>+20</span>% 火焰抗性","hover":"https://cdn.poedb.tw/cache1/tw/Poe_Data_Mods_hover/id-fire"}]}); });</script>
            <div class="Stats"><div class="implicitMod"><span class="mod-value">+(20—30)</span>% 火焰抗性</div></div>
            """,
        ["Ruby_Ring"] = """<div class="Stats"><div class="implicitMod"><span class="mod-value">+(20—30)</span>% 火焰抗性</div></div>""",
        ["Crafting_Bench"] = """
            <table><thead><tr><th>詞綴</th><th>Require</th><th>ItemClasses</th><th>解鎖</th></tr></thead>
            <tbody><tr><td><span class="explicitMod"><span class="mod-value">+(16—20)</span>% 火焰抗性</span></td><td>蛻變石</td><td>戒指</td><td>Default</td></tr></tbody></table>
            """
    };

    [TestMethod]
    public void ImportGroupsTiersAndReportsRejectedRowsAndUnfetchedCategories()
    {
        var snapshot = PoedbImport.Build(Pages, "2026-11-06");
        Assert.IsTrue(snapshot.IsComplete);
        var matches = new AffixIndex(snapshot).Search("火焰");
        Assert.HasCount(4, matches);
        var normal = matches.Single(entry => entry.Template == "+#% 火焰抗性" && entry.Source == SourceKind.Explicit);
        Assert.HasCount(3, normal.Evidence);
        Assert.IsTrue(normal.Evidence.Any(evidence => evidence.OriginalId == "id-fire" && evidence.TierText!.Contains("T3", StringComparison.Ordinal)));
        Assert.AreEqual(1, snapshot.Coverage.Single(source => source.Category == "All 前後綴").Rejected);
        Assert.IsTrue(snapshot.Coverage.Any(source => source.Category == "鞋子" && source.Included == 0));
    }

    [TestMethod]
    public void BadSourceCannotReplacePriorIndex()
    {
        var missing = new Dictionary<string, string>(Pages) { ["Rings"] = "<h1>403</h1>" };
        var snapshot = PoedbImport.Build(missing, "bad");
        Assert.IsFalse(snapshot.IsComplete);
        Assert.IsTrue(snapshot.Coverage.Any(source => source.Category == "戒指一般詞綴" && source.Included == 0));
    }
}
