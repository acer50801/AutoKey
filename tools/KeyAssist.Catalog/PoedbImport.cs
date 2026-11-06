using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using KeyAssist.Core;

namespace KeyAssist.Catalog;

public static partial class PoedbImport
{
    private const string Root = "https://poedb.tw/tw/";
    private sealed record Row(IElement? Description, SourceKind Source, string? OriginalId = null, string? Tier = null);
    private sealed record Candidate(string Template, SourceKind Source, bool CanEvaluate, AffixEvidence Evidence);

    public static CatalogSnapshot Build(IReadOnlyDictionary<string, string> pages, string version)
    {
        var parser = new HtmlParser();
        var coverage = new List<SourceCoverage>();
        var candidates = new List<Candidate>();
        var links = ReadIndex();
        Import("Modifiers_list", "All 前後綴", AllRows);
        Import("Rings", "戒指一般詞綴", document => ItemRows(document, parser), suffix: "#ModifiersCalc");
        Import("Crafting_Bench", "工藝台", BenchRows);
        Import("Rings", "戒指頁展示物品的固定詞綴", document => ImplicitRows(document, "div.implicitMod"), required: false);
        Import("Ruby_Ring", "紅玉戒指固定詞綴", document => ImplicitRows(document, ".Stats div.implicitMod"));
        foreach (var (page, label) in links.Where(link => link.Key != "Rings"))
            Import(page, label, document => ItemRows(document, parser), required: false, suffix: "#ModifiersCalc");

        var entries = candidates.GroupBy(row => (row.Source, Key: AffixText.Key(row.Template)))
            .Select(group =>
            {
                var row = group.First();
                var evidence = group.Select(candidate => candidate.Evidence).Distinct().ToArray();
                return new AffixEntry(AffixText.Id(row.Template, row.Source), row.Template, row.Source,
                    AffixText.Metrics(row.Template), group.Any(candidate => candidate.CanEvaluate), evidence);
            }).OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
        return new(version, coverage.All(source => !source.Required || source.Included > 0), entries, [.. coverage]);

        Dictionary<string, string> ReadIndex()
        {
            var categories = new Dictionary<string, string>();
            if (pages.TryGetValue("Modifiers", out var html))
            {
                foreach (var link in parser.ParseDocument(html).QuerySelectorAll("a[href$='#ModifiersCalc']"))
                {
                    var label = link.TextContent.Trim();
                    var href = link.GetAttribute("href");
                    if (label.Length == 0 || !Uri.TryCreate(new Uri(Root), href, out var url) ||
                        url.Host != "poedb.tw" || !url.AbsolutePath.StartsWith("/tw/", StringComparison.Ordinal))
                        continue;
                    var page = Uri.UnescapeDataString(url.AbsolutePath[4..]);
                    if (page.Length > 0 && !page.Contains('/') && !page.Contains('\\'))
                        categories.TryAdd(page, label);
                }
            }
            coverage.Add(new("詞綴索引（分類連結）", Root + "Modifiers", categories.Count, 0,
                categories.Count > 0 ? "已辨識分類連結" : "缺少來源檔或分類格式變動"));
            return categories;
        }

        void Import(string page, string category, Func<IDocument, IEnumerable<Row>> extract, bool required = true, string suffix = "")
        {
            var url = Root + Uri.EscapeDataString(page) + suffix;
            if (!pages.TryGetValue(page, out var html))
            {
                coverage.Add(new(category, url, 0, 0, required ? "缺少來源檔" : "本次未提供此分類的 HTML", required));
                return;
            }
            var included = 0;
            var rejected = 0;
            var searchOnly = 0;
            try
            {
                foreach (var row in extract(parser.ParseDocument(html)))
                {
                    var normalized = row.Description is null ? null : Normalize(row.Description);
                    if (normalized is null)
                    {
                        rejected++;
                        continue;
                    }
                    var (template, canEvaluate, original) = normalized.Value;
                    if (!canEvaluate)
                        searchOnly++;
                    included++;
                    var tier = string.IsNullOrWhiteSpace(row.Tier) ? original : $"{row.Tier} · {original}";
                    candidates.Add(new(template, row.Source, canEvaluate, new(category, url, row.OriginalId, tier)));
                }
            }
            catch (JsonException)
            {
                rejected++;
            }
            var status = included == 0 ? "找不到可辨識的詞綴，可能為拒絕頁或來源結構變動"
                : $"已匯入 {included} 筆來源列；{searchOnly} 筆僅供搜尋" +
                  (rejected > 0 ? "，另有無法解析或未辨識類型的列" : "");
            coverage.Add(new(category, url, included, rejected, status, required));
        }
    }

    private static IEnumerable<Row> AllRows(IDocument document)
    {
        foreach (var table in Tables(document, ["Level", "Pre/Suf", "Name", "Tags"]))
        {
            foreach (var tr in table.QuerySelectorAll("tbody tr"))
            {
                var cells = tr.Children;
                if (cells.Length < 4 || cells[1].TextContent.Trim() is not ("前綴" or "後綴"))
                {
                    yield return new(null, SourceKind.Other);
                    continue;
                }
                yield return new(cells[2], SourceKind.Explicit, HoverId(cells[2]),
                    $"{cells[1].TextContent.Trim()} · 需求等級 {cells[0].TextContent.Trim()}");
            }
        }
    }

    private static IEnumerable<Row> BenchRows(IDocument document)
    {
        foreach (var table in Tables(document, ["詞綴", "Require", "ItemClasses", "解鎖"]))
        {
            foreach (var tr in table.QuerySelectorAll("tbody tr"))
            {
                var cell = tr.FirstElementChild;
                yield return new(cell?.QuerySelector(".explicitMod"), SourceKind.Crafted,
                    cell is null ? null : HoverId(cell));
            }
        }
    }

    private static IEnumerable<Row> ImplicitRows(IDocument document, string selector) =>
        document.QuerySelectorAll(selector).Select(element =>
            new Row(element, SourceKind.Implicit, HoverId(element)));

    private static IEnumerable<Row> ItemRows(IDocument document, HtmlParser parser)
    {
        var script = document.QuerySelectorAll("script").Select(element => element.TextContent)
            .FirstOrDefault(text => text.Contains("new ModsView(", StringComparison.Ordinal));
        if (script is null)
            yield break;
        var start = script.IndexOf("new ModsView(", StringComparison.Ordinal) + "new ModsView(".Length;
        using var json = ReadScriptObject(script[start..]);
        if (json.RootElement.ValueKind != JsonValueKind.Object ||
            !json.RootElement.TryGetProperty("normal", out var list) || list.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var mod in list.EnumerateArray())
        {
            var kind = Property(mod, "ModGenerationTypeID");
            var text = Property(mod, "str");
            if (kind is not ("1" or "2") || text is null)
            {
                yield return new(null, SourceKind.Other);
                continue;
            }
            var tier = Property(mod, "Tier") is { Length: > 0 } number ? "T" + number : "";
            var description = parser.ParseDocument("<body>" + text).Body;
            var detail = $"{(kind == "1" ? "前綴" : "後綴")} {tier} {Property(mod, "Name")} · 需求等級 {Property(mod, "Level")}";
            yield return new(description, SourceKind.Explicit, IdFromHover(Property(mod, "hover")), detail);
        }
    }

    private static JsonDocument ReadScriptObject(string text)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text));
        return JsonDocument.ParseValue(ref reader);
    }

    private static string? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        ? value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null } : null;

    private static IEnumerable<IElement> Tables(IDocument document, string[] headers) =>
        document.QuerySelectorAll("table").Where(table =>
            table.QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim()).SequenceEqual(headers));

    private static string? HoverId(IElement element) =>
        IdFromHover(element.GetAttribute("data-hover")) ??
        element.QuerySelectorAll("[data-hover]").Select(node => IdFromHover(node.GetAttribute("data-hover"))).FirstOrDefault(id => id is not null);

    private static string? IdFromHover(string? hover)
    {
        const string marker = "/Poe_Data_Mods_hover/";
        if (hover is null)
            return null;
        var start = hover.IndexOf(marker, StringComparison.Ordinal);
        return start < 0 ? null : hover[(start + marker.Length)..];
    }

    private static (string Template, bool CanEvaluate, string Original)? Normalize(IElement description)
    {
        var clone = (IElement)description.Clone(true);
        foreach (var node in clone.QuerySelectorAll("script, style, .fas, .reminder"))
            node.Remove();
        foreach (var line in clone.QuerySelectorAll("br"))
            line.Replace(clone.Owner!.CreateTextNode(" ／ "));
        var original = Spaces().Replace(clone.TextContent, " ").Trim();
        if (original.Length == 0 || original.Contains("{{", StringComparison.Ordinal))
            return null;
        var canEvaluate = true;
        foreach (var roll in clone.QuerySelectorAll(".mod-value"))
        {
            var text = roll.TextContent.Trim();
            if (!Roll().IsMatch(text))
                canEvaluate = false;
            roll.TextContent = text.StartsWith('+') ? "+#" : "#";
        }
        var template = Spaces().Replace(clone.TextContent, " ").Trim();
        return (template, canEvaluate && !template.Contains('／') && !template.Any(char.IsDigit) && Chinese().IsMatch(template), original);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"[\u3400-\u9FFF]")]
    private static partial Regex Chinese();
    [GeneratedRegex(@"^[+−-]?(?:\d+(?:\.\d+)?|\([-−]?\d+(?:\.\d+)?\s*[-–—~]\s*[-−]?\d+(?:\.\d+)?\))$")]
    private static partial Regex Roll();
}
