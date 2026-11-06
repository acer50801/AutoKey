namespace KeyAssist.Core;

public enum FieldKind { Key, Text, Choice, Position, Point, Toggle }
public sealed record Choice(string Id, string Label);
public sealed record FieldSpec(string Id, string Label, FieldKind Kind, string Default = "",
    IReadOnlyList<Choice>? Choices = null, bool Required = false);
public sealed record FeatureSpec(string Id, string Title, string PageId, IReadOnlyList<FieldSpec> Fields, bool Toggle = false);

public static class FeatureRequirements
{
    public static IReadOnlyList<Choice> Pages { get; } = [
        new("overview", "總覽"), new("input", "滑鼠與按鍵"), new("detection", "藥劑與偵測"),
        new("skills", "技能與地雷"), new("inventory", "背包與倉庫"), new("crafting", "裝備製作"),
        new("coordinates", "座標與配置")
    ];

    public static IReadOnlyList<Choice> CraftingModes { get; } = [
        new("F30", "改造石洗詞綴"), new("F31", "混沌石／重鑄＋點金"), new("F32", "機會石轉傳奇"),
        new("F33", "洗洞數"), new("F34", "洗連線"), new("F35", "洗顏色")
    ];

    public static IReadOnlyList<Choice> Materials { get; } = [
        new("alteration", "改造石"), new("augmentation", "增幅石"), new("regal", "富豪石"),
        new("scour", "重鑄石"), new("transmutation", "蛻變石"), new("chaos", "混沌石"),
        new("alchemy", "點金石"), new("chance", "機會石"), new("jeweller", "工匠石"),
        new("fusing", "鏈結石"), new("chromatic", "幻色石")
    ];

    public static IReadOnlyList<FeatureSpec> Features { get; } = [
        new("F25", "全域暫停、停止與工具管理", "overview",
            [Key("F25.pause", "暫停全部快捷鍵"), Key("F25.stop", "停止全部快捷鍵")]),
        new("F05", "滑鼠連點", "input",
            [Trigger("F05"), Key("F05.key"), Select("F05.button", "滑鼠按鈕", new("left", "左鍵"), new("right", "右鍵")),
                Text("F05.delay", "間隔（毫秒）", "100")], true),
        new("F01", "返回角色", "input", [Key("F01.key")], true),
        new("F03", "使用傳送卷軸", "input", [Key("F03.key"), Pos("F03.scroll", "傳送卷軸", true)], true),
        new("F04", "返回藏身處", "input", [Key("F04.key")], true),
        new("F28", "高級功能群組總開關", "detection", [], true),
        new("F07", "藥劑防呆與讀秒循環", "detection", [Trigger("F07"), Key("F07.key"), .. FlaskFields("F07")], true),
        new("F08", "使用技能時喝水", "detection",
            [Trigger("F08"), Key("F08.key"), Key("F08.skill", "監測的技能按鍵"), .. FlaskFields("F08")], true),
        new("F09", "生命、魔力與混傷偵測", "detection",
            [Point("F09.health", "角色頭上血條"), Point("F09.mana", "魔力球"),
                Point("F09.mixed", "混傷穿透後生命"), Point("F09.orb", "左下生命球")], true),
        new("F38", "藥劑使用結束時間偵測", "detection", [], true),
        new("F10", "技能連段", "skills",
            [Key("F10.key", "觸發技能按鍵"), Key("F10.second", "第 2 段技能"),
                Text("F10.delay2", "第 1 段延遲（毫秒）", "100"),
                Key("F10.third", "第 3 段技能", false), Text("F10.delay3", "第 2 段延遲（毫秒）", "100")], true),
        new("F11", "循環技能", "skills",
            [Trigger("F11"), Key("F11.key")], true),
        new("F13", "清包模式與顏色基準", "inventory",
            [Key("F13.key"), Point("F13.empty", "空格顏色偵測點")], true),
        new("F14", "五點快速取物", "inventory", [Key("F14.key")], true),
        new("F29", "製作控制、限制與紀錄", "crafting",
            [Trigger("F29"), Key("F29.key"), Select("F29.mode", "本次製作模式", CraftingModes.ToArray()),
                Text("F29.limit", "材料使用上限", "100")], true),
        new("F36", "詞綴條件與比對規則", "crafting", []),
        new("F30", CraftingModes[0].Label, "crafting",
            [.. new[] { "scour", "transmutation", "augmentation", "regal" }
                .Select(id => new FieldSpec("F30." + id, "允許使用" + Materials.Single(m => m.Id == id).Label, FieldKind.Toggle))]),
        new("F31", CraftingModes[1].Label, "crafting",
            [Select("F31.route", "製作路線", new("chaos", "混沌石"), new("scour-alch", "重鑄＋點金"))]),
        new("F32", CraftingModes[2].Label, "crafting", []),
        new("F33", CraftingModes[3].Label, "crafting", [Text("F33.count", "目標洞數", "6")]),
        new("F34", CraftingModes[4].Label, "crafting", [Text("F34.count", "目標連線數", "6")]),
        new("F35", CraftingModes[5].Label, "crafting",
            [Text("F35.red", "紅洞", "0"), Text("F35.green", "綠洞", "0"), Text("F35.blue", "藍洞", "0")]),
        new("F37", "物品、材料與工藝台定位", "crafting",
            [Select("F37.location", "定位方式", new("normal", "一般背包"), new("currency", "通貨頁"), new("bench", "工藝台")),
                Pos("F37.item", "一般物品位置"), Pos("F37.currencyTop", "通貨頁左上角"), Pos("F37.currencyBottom", "通貨頁右下角"),
                Pos("F37.benchItem", "工藝台物品位置"), Pos("F37.benchButton", "工藝台按鈕位置"),
                .. Materials.Select(material => Pos("F37." + material.Id, material.Label + "定位"))]),
        new("F20", "背包定位", "coordinates",
            [Pos("F20.top", "背包左上角", true), Pos("F20.bottom", "背包右下角", true)]),
        new("F21", "取物定位（5 個點位）", "coordinates",
            [.. Enumerable.Range(1, 5).SelectMany(i => new[]
                { Text($"F21.{i}.name", $"取物 {i} 名稱"), Pos($"F21.{i}", $"取物 {i}", true) })]),
        new("F22", "顏色偵測定位", "coordinates", []),
        new("F24", "原有文字模式與場景判斷", "coordinates",
            [Point("F24.scene", "場景偵測點"), Key("F24.copy", "複製物品資訊按鍵")])
    ];

    public static IReadOnlyDictionary<string, FieldSpec> Fields { get; } =
        Features.SelectMany(feature => feature.Fields).ToDictionary(field => field.Id);

    private static FieldSpec Key(string id, string label = "觸發快捷鍵", bool required = true) =>
        new(id, label, FieldKind.Key, Required: required);
    private static FieldSpec Text(string id, string label, string value = "") => new(id, label, FieldKind.Text, value);
    private static FieldSpec Select(string id, string label, params Choice[] choices) =>
        new(id, label, FieldKind.Choice, choices[0].Id, choices);
    private static FieldSpec Pos(string id, string label, bool required = false) =>
        new(id, label, FieldKind.Position, Required: required);
    private static FieldSpec Point(string id, string label) => new(id, label, FieldKind.Point, Required: true);

    private static FieldSpec Trigger(string id) =>
        Select(id + ".trigger", "啟動方式", new("key", "快捷鍵觸發"), new("enabled", "開啟功能後執行"));

    private static IEnumerable<FieldSpec> FlaskFields(string id) => Enumerable.Range(1, 5).SelectMany(i => new[]
    {
        new FieldSpec($"{id}.{i}.use", $"選用藥劑 {i}", FieldKind.Toggle, i == 1 ? "true" : "false"),
        Key($"{id}.{i}.key", $"藥劑 {i} 輸出鍵", false),
        Text($"{id}.{i}.duration", $"藥劑 {i} 持續時間（秒）", "4")
    });
}

public static class ReadinessEvaluator
{
    public static IReadOnlyList<ReadinessCard> Evaluate(SettingsSnapshot settings)
    {
        var cards = new List<ReadinessCard>();
        foreach (var feature in FeatureRequirements.Features)
        {
            if (feature.Id is "F28" or "F36" or "F38")
                continue;
            var issues = new List<ReadinessIssue>();
            foreach (var field in feature.Fields.Where(field => field.Required))
            {
                switch (field.Kind)
                {
                    case FieldKind.Key when field.Id != feature.Id + ".key" || settings.Get(feature.Id + ".trigger") != "enabled":
                        RequireKey(settings.Get(field.Id), field.Label, field.Id, issues);
                        break;
                    case FieldKind.Position:
                        CheckPosition(settings, field.Id, field.Label, settings.Get(field.Id + ".x"), settings.Get(field.Id + ".y"), issues);
                        break;
                    case FieldKind.Point:
                        CheckPoint(settings, Guid.TryParse(settings.Get(field.Id), out var id) ? id : null, field.Label, field.Id, issues);
                        break;
                }
            }

            switch (feature.Id)
            {
                case "F07" or "F08":
                    var slotCount = 0;
                    for (var i = 1; i <= 5; i++)
                    {
                        var slot = $"{feature.Id}.{i}";
                        var use = FeatureRequirements.Fields[slot + ".use"];
                        if (settings.Get(use.Id, use.Default) != "true")
                            continue;
                        slotCount++;
                        RequireKey(settings.Get(slot + ".key"), $"藥劑 {i} 輸出鍵", slot + ".key", issues);
                        var duration = FeatureRequirements.Fields[slot + ".duration"];
                        if (!RuleEvaluator.TryNumber(settings.Get(duration.Id, duration.Default), out var seconds) || seconds <= 0)
                            issues.Add(new($"藥劑 {i} 持續時間須大於 0。", duration.Id));
                    }
                    if (slotCount == 0)
                        issues.Add(new("請選用至少一槽藥劑。", feature.Id + ".1.use"));
                    break;
                case "F11":
                    if (settings.Cycle.Count == 0)
                        issues.Add(new("尚未加入循環技能", "F11.add"));
                    for (var i = 0; i < settings.Cycle.Count; i++)
                        RequireKey(settings.Cycle[i].Key, $"循環 {i + 1} 按鍵", $"F11.step.{i}", issues);
                    break;
                case "F13":
                case "F14":
                    var positions = FeatureRequirements.Features.Single(f => f.Id == (feature.Id == "F13" ? "F20" : "F21")).Fields;
                    foreach (var field in positions.Where(field => field.Kind == FieldKind.Position))
                        CheckPosition(settings, field.Id, field.Label, settings.Get(field.Id + ".x"), settings.Get(field.Id + ".y"), issues);
                    if (feature.Id == "F13")
                        CheckBagGeometry(settings, issues);
                    break;
                case "F20":
                    CheckBagGeometry(settings, issues);
                    break;
                case "F22":
                    if (settings.Points.Count == 0)
                        issues.Add(new("尚未新增偵測點", "F22.add"));
                    foreach (var point in settings.Points)
                        CheckPoint(settings, point.Id, point.Name, "F22.add", issues);
                    break;
                case "F29":
                case "F37":
                    var mode = settings.Get("F29.mode", "F30");
                    CheckCrafting(settings, mode, issues);
                    if (feature.Id == "F29")
                        CheckCraftingNumbers(settings, mode, issues);
                    break;
                case "F30" or "F31" or "F32" or "F33" or "F34" or "F35":
                    CheckCrafting(settings, feature.Id, issues);
                    CheckCraftingNumbers(settings, feature.Id, issues);
                    break;
            }
            cards.Add(new(feature.Id, feature.Title, feature.PageId, !settings.Disabled.Contains(feature.Id), issues));
        }
        for (var i = 0; i < settings.Flasks.Count; i++)
        {
            var slot = settings.Flasks[i];
            var id = $"F38.{i + 1}";
            var issues = new List<ReadinessIssue>();
            CheckPoint(settings, slot.PointId, "", id + ".point", issues);
            if (slot.Refill)
                RequireKey(slot.Key, "輸出鍵", id + ".key", issues);
            cards.Add(new(id, $"藥劑 {i + 1} 效果偵測", "detection", slot.Enabled && !settings.Disabled.Contains("F38"), issues));
        }
        return cards.OrderBy(card => card.IsConfigured).ToArray();
    }

    private static void RequireKey(string value, string label, string target, List<ReadinessIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new("缺少" + label, target));
    }

    private static void CheckPoint(SettingsSnapshot settings, Guid? id, string label, string target, List<ReadinessIssue> issues)
    {
        var point = settings.Points.FirstOrDefault(point => point.Id == id);
        if (point is null)
        {
            var prefix = label.Length > 0 ? label + "：" : "";
            issues.Add(new(prefix + (id.HasValue ? "引用的偵測點已刪除" : "尚未選擇偵測點"), target));
            return;
        }
        var position = $"point.{point.Id}";
        CheckPosition(settings, position, point.Name, point.X, point.Y, issues);
        if (point.Color.Length != 7 || point.Color[0] != '#' || !point.Color.AsSpan(1).ToString().All(Uri.IsHexDigit))
            issues.Add(new($"{point.Name}缺少有效顏色（#RRGGBB）", position + ".color"));
    }

    private static void CheckPosition(SettingsSnapshot settings, string id, string label, string x, string y, List<ReadinessIssue> issues)
    {
        var calibration = settings.Calibration;
        var missingX = CheckAxis(x, calibration.Width, "X", id + ".x");
        var missingY = CheckAxis(y, calibration.Height, "Y", id + ".y");
        if (!missingX && !missingY && (!calibration.IsValid || settings.ConfirmedPositions.GetValueOrDefault(id) != calibration))
            issues.Add(new($"{label}座標需確認目前內容區尺寸與 DPI", "calibration.confirm"));

        bool CheckAxis(string value, int limit, string axis, string target)
        {
            if (int.TryParse(value, out var number) && number >= 0 && (limit <= 0 || number < limit))
                return false;
            var message = string.IsNullOrWhiteSpace(value) ? $"缺少{label} {axis}" : $"{label} {axis} 超出範圍或格式錯誤";
            issues.Add(new(message, target));
            return true;
        }
    }

    private static void CheckCrafting(SettingsSnapshot settings, string mode, List<ReadinessIssue> issues)
    {
        var location = settings.Get("F37.location", "normal");
        if (location == "bench")
        {
            issues.Add(new("工藝台定位是否適用此模式待驗證", "F37.location"));
            Position("benchItem");
            Position("benchButton");
            return;
        }
        if (location == "currency")
        {
            Position("currencyTop");
            Position("currencyBottom");
        }
        Position("item");
        string[] materials = mode switch
        {
            "F30" => ["alteration", .. new[] { "scour", "transmutation", "augmentation", "regal" }
                .Where(material => settings.Get("F30." + material) == "true")],
            "F31" when settings.Get("F31.route") == "scour-alch" => ["scour", "alchemy"],
            "F31" => ["chaos"],
            "F32" => ["scour", "chance"],
            "F33" => ["jeweller"],
            "F34" => ["fusing"],
            "F35" => ["chromatic"],
            _ => []
        };
        if (materials.Length == 0)
            issues.Add(new("請選擇可辨識的製作模式", "F29.mode"));
        foreach (var material in materials)
            Position(material);

        void Position(string name)
        {
            var id = "F37." + name;
            CheckPosition(settings, id, FeatureRequirements.Fields[id].Label, settings.Get(id + ".x"), settings.Get(id + ".y"), issues);
        }
    }

    private static void CheckBagGeometry(SettingsSnapshot settings, List<ReadinessIssue> issues)
    {
        foreach (var (axis, relation) in new[] { ("x", "右方"), ("y", "下方") })
        {
            if (int.TryParse(settings.Get("F20.top." + axis), out var top) && top >= 0 &&
                int.TryParse(settings.Get("F20.bottom." + axis), out var bottom) && bottom >= 0 && bottom <= top)
                issues.Add(new($"背包右下角須位在左上角的{relation}。", "F20.bottom." + axis));
        }
    }

    private static void CheckCraftingNumbers(SettingsSnapshot settings, string mode, List<ReadinessIssue> issues)
    {
        Check("F29.limit", 1, int.MaxValue);
        if (mode is "F33" or "F34")
            Check(mode + ".count", 1, 6);
        if (mode == "F35")
        {
            var colors = new[] { "F35.red", "F35.green", "F35.blue" }.Select(id => Check(id, 0, 6)).ToArray();
            if (colors.All(count => count.HasValue) && (colors.Sum() is < 1 or > 6))
                issues.Add(new("三種目標顏色的洞數合計須為 1 至 6。", "F35.red"));
        }

        int? Check(string id, int min, int max)
        {
            var field = FeatureRequirements.Fields[id];
            if (int.TryParse(settings.Get(id, field.Default), out var value) && value >= min && value <= max)
                return value;
            issues.Add(new(max == int.MaxValue ? $"{field.Label}須為正整數。" : $"{field.Label}須為 {min} 至 {max}。", id));
            return null;
        }
    }
}
