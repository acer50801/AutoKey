using System.Text;
using System.Text.RegularExpressions;

namespace KeyAssist.Core;

public static partial class ItemAffixReader
{
    private sealed record ParsedLine(string Text, string Template, decimal[]? Values, SourceKind? InlineSource);

    public static ItemReadResult Read(string text, CatalogSnapshot catalog)
    {
        var values = new Dictionary<string, decimal[]>();
        var ambiguous = new HashSet<string>();
        var lines = text.TrimStart('\uFEFF').ReplaceLineEndings("\n").Normalize(NormalizationForm.FormKC)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var level = Array.FindIndex(lines, line => Level().IsMatch(line));
        var rarity = Array.FindIndex(lines, line => line.StartsWith("稀有度:", StringComparison.Ordinal));
        if (level <= 0 || rarity < 0 || rarity >= level || lines[rarity].Split(':')[1].Trim() is not ("普通" or "魔法" or "稀有") ||
            !lines.Take(level).Contains("--------") || level + 1 >= lines.Length || lines[level + 1] != "--------")
            return new(values, ambiguous, false, "請貼上含稀有度、物品等級及分隔線的 PoE 1 繁中普通、魔法或稀有物品文字。");

        var byText = catalog.Entries.ToLookup(entry => AffixText.Key(entry.Template));
        var compounds = catalog.Entries.Select(entry => (entry.Source, Parts: AffixText.Key(entry.Template).Split('/')))
            .Where(entry => entry.Parts.Length > 1).OrderByDescending(entry => entry.Parts.Length)
            .ToLookup(entry => entry.Parts[0]);
        string? warning = null;
        SourceKind? sectionSource = null;
        string? header = null;
        var linesInBlock = new List<ParsedLine>();
        foreach (var line in lines.Skip(level + 2))
        {
            if (line == "--------")
            {
                FlushBlock();
                continue;
            }
            if (line is "已汙染" or "已污染" or "已複製" or "分裂" or "鏡像" or "未鑑定")
            {
                FlushBlock();
                if (line == "未鑑定")
                    warning ??= "物品尚未鑑定。";
                continue;
            }
            if (line.StartsWith('{'))
            {
                FlushBlock();
                header = line;
                sectionSource = HeaderSource(line);
                continue;
            }
            if (line.StartsWith('(') && line.EndsWith(')'))
                continue;
            linesInBlock.Add(Parse(line));
        }
        FlushBlock();
        if (lines[^1] == "--------")
            warning ??= "物品文字在分隔線結束，後續內容可能不完整。";
        return new(values, ambiguous, warning is null, warning);

        void FlushBlock()
        {
            if (header is not null)
            {
                if (linesInBlock.Count == 0)
                    warning ??= "詞綴標頭後缺少詞綴文字。";
                else if (sectionSource is not { } source || linesInBlock.Any(line => line.InlineSource is { } inline && inline != source))
                {
                    warning ??= $"無法確認詞綴來源：{header}";
                    foreach (var line in linesInBlock)
                        MarkAmbiguous(line.Template);
                }
                else
                    Match(linesInBlock, source);
            }
            else
            {
                for (var i = 0; i < linesInBlock.Count; i++)
                {
                    var line = linesInBlock[i];
                    var source = line.InlineSource ?? SourceKind.Explicit;
                    var possible = compounds[line.Template].FirstOrDefault(compound =>
                        compound.Source == source && compound.Parts.Length <= linesInBlock.Count - i &&
                        compound.Parts.Select((part, offset) => (part, line: linesInBlock[i + offset]))
                            .All(pair => pair.part == pair.line.Template && (pair.line.InlineSource ?? SourceKind.Explicit) == source));
                    if (possible.Parts is { } parts)
                    {
                        foreach (var part in parts)
                            MarkAmbiguous(part, source);
                        i += parts.Length - 1;
                    }
                    else
                        Match([line], source);
                }
            }
            linesInBlock.Clear();
            header = null;
            sectionSource = null;
        }

        void Match(IReadOnlyList<ParsedLine> block, SourceKind source)
        {
            var key = string.Join("/", block.Select(line => line.Template));
            var matching = byText[key].Where(entry => entry.Source == source).ToArray();
            var description = string.Join(" ／ ", block.Select(line => line.Text));
            if (matching.Length != 1)
            {
                warning ??= $"有詞綴未收錄或來源不明：{description}";
                MarkAmbiguous(key);
                return;
            }
            var entry = matching[0];
            if (!entry.CanEvaluate)
                return;
            var rolls = block.SelectMany(line => line.Values ?? []).ToArray();
            if (block.Any(line => line.Values is null) || entry.Metrics.Length != rolls.Length || !values.TryAdd(entry.Id, rolls))
            {
                ambiguous.Add(entry.Id);
            }
        }

        void MarkAmbiguous(string key, SourceKind? source = null)
        {
            foreach (var entry in byText[key].Where(entry => source is null || entry.Source == source))
                ambiguous.Add(entry.Id);
        }
    }

    private static ParsedLine Parse(string line)
    {
        var annotation = Annotation().Match(line);
        var source = annotation.Success ? AnnotationSource(annotation.Groups[1].Value) : null;
        var stat = annotation.Success ? line[..annotation.Index].Trim() : line;
        stat = RollRange().Replace(stat, "").Replace('−', '-');
        var numbers = Number().Matches(stat);
        var template = AffixText.Key(Number().Replace(stat, "#"));
        var rolls = new decimal[numbers.Count];
        for (var i = 0; i < numbers.Count; i++)
            if (!RuleEvaluator.TryNumber(numbers[i].Value, out rolls[i]))
                return new(line, template, null, source);
        return new(line, template, rolls, source);
    }

    private static SourceKind? HeaderSource(string line)
    {
        if (!line.EndsWith('}'))
            return null;
        var label = line.Trim('{', '}', ' ').Split('"', '—', '–')[0].Trim();
        if (label.Contains("固定", StringComparison.Ordinal) || label.StartsWith("Implicit", StringComparison.OrdinalIgnoreCase))
            return SourceKind.Implicit;
        if (label.Contains("工藝", StringComparison.Ordinal) || label.StartsWith("Crafted", StringComparison.OrdinalIgnoreCase))
            return SourceKind.Crafted;
        if (label.Contains("附魔", StringComparison.Ordinal) || label.StartsWith("Enchant", StringComparison.OrdinalIgnoreCase))
            return SourceKind.Enchant;
        if (label.Contains("前綴", StringComparison.Ordinal) || label.Contains("後綴", StringComparison.Ordinal) ||
            label.StartsWith("Prefix", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Suffix", StringComparison.OrdinalIgnoreCase))
            return SourceKind.Explicit;
        return null;
    }

    private static SourceKind? AnnotationSource(string label) => label.ToLowerInvariant() switch
    {
        "implicit" or "固定" or "固定詞綴" => SourceKind.Implicit,
        "crafted" or "工藝" or "工藝詞綴" => SourceKind.Crafted,
        "enchant" or "附魔" or "附魔詞綴" => SourceKind.Enchant,
        "fractured" or "破裂" or "破裂詞綴" or "veiled" or "隱匿" => SourceKind.Other,
        _ => null
    };

    [GeneratedRegex(@"^物品等級:\s*[0-9]+$")]
    private static partial Regex Level();
    [GeneratedRegex(@"\s*\((implicit|固定|固定詞綴|crafted|工藝|工藝詞綴|enchant|附魔|附魔詞綴|fractured|破裂|破裂詞綴|veiled|隱匿)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex Annotation();
    [GeneratedRegex(@"(?<=[0-9])\(\s*[-−]?[0-9]+(?:\.[0-9]+)?\s*[-—–~]\s*[-−]?[0-9]+(?:\.[0-9]+)?\s*\)")]
    private static partial Regex RollRange();
    [GeneratedRegex(@"-?[0-9]+(?:\.[0-9]+)?")]
    private static partial Regex Number();
}
