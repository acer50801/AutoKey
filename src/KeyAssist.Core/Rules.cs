using System.Globalization;

namespace KeyAssist.Core;

public sealed record RuleSet
{
    public List<RuleGroup> Groups { get; init; } = [];
    public string LegacyText { get; set; } = "";
}

public enum GroupKind { All, None, Count, Weighted }
public sealed class RuleGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public GroupKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public string Count { get; set; } = "1";
    public string Min { get; set; } = "";
    public string Max { get; set; } = "";
    public List<RuleRow> Rows { get; init; } = [];
}

public sealed class RuleRow(string affixId)
{
    public string AffixId { get; init; } = affixId;
    public int? Metric { get; set; }
    public string Min { get; set; } = "";
    public string Max { get; set; } = "";
    public string Weight { get; set; } = "1";
}

public enum Verdict { Passed, Failed, Indeterminate, Invalid }
public sealed record ItemReadResult(IReadOnlyDictionary<string, decimal[]> Values, IReadOnlySet<string> Ambiguous,
    bool Complete, string? Warning);
public sealed record RowResult(string AffixId, Verdict Verdict, decimal? Value, string Reason);
public sealed record GroupResult(GroupKind Kind, Verdict Verdict, int Hits, decimal? Score, string Reason, IReadOnlyList<RowResult> Rows);
public sealed record RuleResult(Verdict Verdict, string Reason, IReadOnlyList<GroupResult> Groups);

public static class RuleEvaluator
{
    public static RuleResult Evaluate(RuleSet rules, ItemReadResult item, CatalogSnapshot catalog)
    {
        var active = rules.Groups.Where(group => group.Enabled).ToArray();
        if (active.Length == 0)
            return new(Verdict.Invalid, "請啟用至少一個群組。", []);
        var entries = catalog.Entries.ToDictionary(entry => entry.Id);
        var groups = active.Select(group => EvaluateGroup(group, item, entries)).ToArray();
        var verdict = groups.Any(group => group.Verdict == Verdict.Invalid) ? Verdict.Invalid
            : groups.Any(group => group.Verdict == Verdict.Failed) ? Verdict.Failed
            : !item.Complete || groups.Any(group => group.Verdict == Verdict.Indeterminate) ? Verdict.Indeterminate
            : Verdict.Passed;
        return new(verdict, verdict switch
        {
            Verdict.Passed => "所有啟用群組均通過。",
            Verdict.Failed => "有群組未通過。",
            Verdict.Invalid => "請修正群組或詞綴條件。",
            _ => item.Warning ?? "物品文字有無法判定的詞綴。"
        }, groups);
    }

    private static GroupResult EvaluateGroup(RuleGroup group, ItemReadResult item, Dictionary<string, AffixEntry> entries)
    {
        var rows = new List<RowResult>();
        var hits = 0;
        GroupResult Result(Verdict status, string reason, decimal? score = null) =>
            new(group.Kind, status, hits, score, reason, rows);

        if (group.Rows.Count == 0)
            return Result(Verdict.Invalid, "群組尚未加入條件。");
        if (group.Rows.Select(row => row.AffixId).Distinct().Count() != group.Rows.Count)
            return Result(Verdict.Invalid, "同一群組含有重複詞綴。");

        var count = 0;
        if (group.Kind == GroupKind.Count &&
            (!int.TryParse(group.Count, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1 || count > group.Rows.Count))
            return Result(Verdict.Invalid, "命中門檻須介於 1 和條件數之間。");

        decimal? min = null, max = null;
        var weighted = group.Kind == GroupKind.Weighted;
        if (weighted && (!Bounds(group.Min, group.Max, out min, out max) || (min is null && max is null)))
            return Result(Verdict.Invalid, "請輸入有效的分數上下限，至少一側。");

        decimal score = 0;
        foreach (var row in group.Rows)
        {
            var result = EvaluateRow(row, item, entries, weighted, out var weight);
            rows.Add(result);
            if (result.Verdict != Verdict.Passed)
                continue;
            hits++;
            if (weighted)
            {
                try { score += result.Value!.Value * weight; }
                catch (OverflowException) { return Result(Verdict.Invalid, "加權分數超出可表示範圍。"); }
            }
        }

        if (rows.Any(row => row.Verdict == Verdict.Invalid))
            return Result(Verdict.Invalid, "有條件需要修正。");
        var unknown = rows.Count(row => row.Verdict == Verdict.Indeterminate);
        var missed = rows.Count - hits - unknown;
        return group.Kind switch
        {
            GroupKind.All when missed > 0 => Result(Verdict.Failed, "有必須命中的條件未命中。"),
            GroupKind.None when hits > 0 => Result(Verdict.Failed, "命中了排除條件。"),
            GroupKind.Count when hits + unknown < count => Result(Verdict.Failed, $"命中不足 {count} 條。"),
            _ when unknown > 0 => Result(Verdict.Indeterminate, "有條件無法判定。"),
            GroupKind.Weighted => InRange(score, min, max)
                ? Result(Verdict.Passed, "加權分數在範圍內。", score)
                : Result(Verdict.Failed, "加權分數超出範圍。", score),
            _ => Result(Verdict.Passed, "群組通過。")
        };
    }

    private static RowResult EvaluateRow(RuleRow row, ItemReadResult item, Dictionary<string, AffixEntry> entries,
        bool weighted, out decimal weight)
    {
        RowResult Result(Verdict verdict, string reason, decimal? value = null) => new(row.AffixId, verdict, value, reason);
        weight = 0;
        if (!entries.TryGetValue(row.AffixId, out var entry))
            return Result(Verdict.Invalid, "詞綴已不在目前索引，請重新選擇。");
        if (!entry.CanEvaluate)
            return Result(Verdict.Invalid, "此候選的文字格式或來源待驗證。");
        if (!Bounds(row.Min, row.Max, out var min, out var max))
            return Result(Verdict.Invalid, "上下限格式錯誤或最小值大於最大值。");
        var numeric = weighted || min.HasValue || max.HasValue;
        var metric = row.Metric ?? (entry.Metrics.Length == 1 ? 0 : -1);
        if (numeric && (metric < 0 || metric >= entry.Metrics.Length))
            return Result(Verdict.Invalid, "請選擇可比較的數值欄位。");
        if (weighted && !TryNumber(row.Weight, out weight))
            return Result(Verdict.Invalid, "請輸入有效的權重。");
        if (item.Ambiguous.Contains(entry.Id))
            return Result(Verdict.Indeterminate, "詞綴來源或數值無法確定。");
        if (!item.Values.TryGetValue(entry.Id, out var rolls))
            return item.Complete
                ? Result(Verdict.Failed, weighted ? "物品無此詞綴，計為 0 分。" : "物品無此詞綴。")
                : Result(Verdict.Indeterminate, "文字不完整，無法確認是否存在。");
        if (!numeric)
            return Result(Verdict.Passed, "存在指定詞綴。");
        if (metric >= rolls.Length)
            return Result(Verdict.Indeterminate, "未能讀取所選的數值。");
        var roll = rolls[metric];
        return InRange(roll, min, max)
            ? Result(Verdict.Passed, "符合數值條件。", roll)
            : Result(Verdict.Failed, weighted ? "數值超出條件範圍，計為 0 分。" : "數值超出條件範圍。", roll);
    }

    internal static bool TryNumber(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out value);

    private static bool Bounds(string low, string high, out decimal? min, out decimal? max)
    {
        min = max = null;
        if (!Parse(low, out min) || !Parse(high, out max))
            return false;
        return min is null || max is null || min <= max;

        static bool Parse(string text, out decimal? limit)
        {
            limit = null;
            if (string.IsNullOrWhiteSpace(text))
                return true;
            if (!TryNumber(text, out var value))
                return false;
            limit = value;
            return true;
        }
    }

    private static bool InRange(decimal value, decimal? min, decimal? max) =>
        (min is null || value >= min) && (max is null || value <= max);
}
