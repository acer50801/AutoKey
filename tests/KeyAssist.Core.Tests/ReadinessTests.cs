using KeyAssist.Core;

namespace KeyAssist.Core.Tests;

[TestClass]
public class ReadinessTests
{
    private static ReadinessCard Card(SettingsSnapshot settings, string id) =>
        ReadinessEvaluator.Evaluate(settings).SingleOrDefault(card => card.FeatureId == id)
        ?? throw new AssertFailedException($"缺少設定檢查卡 {id}");

    [TestMethod]
    public void ScrollNeedsBothCoordinates()
    {
        var settings = new SettingsSnapshot { Calibration = new(1920, 1080, 96) };
        settings.Values["F03.key"] = "F3";
        settings.Values["F03.scroll.x"] = "0";
        settings.ConfirmedPositions["F03.scroll"] = settings.Calibration;
        var missing = Card(settings, "F03");
        Assert.IsFalse(missing.IsConfigured);
        Assert.AreEqual("F03.scroll.y", missing.TargetFieldId);
        settings.Values["F03.scroll.y"] = "0";
        Assert.IsTrue(Card(settings, "F03").IsConfigured);
        settings.Calibration = new(1920, 1080, 120);
        Assert.IsFalse(Card(settings, "F03").IsConfigured);
    }

    [TestMethod]
    public void ClickNeedsNoPositionAndDisabledStatusIsSeparate()
    {
        var settings = new SettingsSnapshot();
        settings.Values["F05.key"] = "F5";
        settings.Disabled.Add("F05");
        Assert.IsTrue(Card(settings, "F05").IsConfigured);
        Assert.IsFalse(Card(settings, "F05").IsEnabled);
    }

    [TestMethod]
    public void PointReferenceSurvivesReorderAndReportsInvalidColorOrDeletion()
    {
        var settings = new SettingsSnapshot { Calibration = new(1920, 1080, 96) };
        var id = Guid.NewGuid();
        settings.Points.Add(new(id, "藥劑偵測", "2", "3", "#FF4500"));
        settings.Points.Add(new(Guid.NewGuid(), "第二個點位"));
        settings.ConfirmedPositions[$"point.{id}"] = settings.Calibration;
        settings.Flasks[0] = new(true, false, "", id);
        settings.Points.Reverse();
        Assert.IsTrue(Card(settings, "F38.1").IsConfigured);
        settings.Points[1] = settings.Points[1] with { Color = "" };
        Assert.AreEqual($"point.{id}.color", Card(settings, "F38.1").TargetFieldId);
        settings.Points.RemoveAt(1);
        Assert.AreEqual("F38.1.point", Card(settings, "F38.1").TargetFieldId);
    }

    [TestMethod]
    public void OnlySelectedRecipePositionsAreRequired()
    {
        var settings = new SettingsSnapshot();
        settings.Values["F29.mode"] = "F31";
        Assert.IsTrue(Card(settings, "F29").Issues.Any(issue => issue.TargetFieldId == "F37.chaos.x"));
        settings.Values["F31.route"] = "scour-alch";
        var issues = Card(settings, "F29").Issues.Select(issue => issue.TargetFieldId).ToList();
        CollectionAssert.Contains(issues, "F37.scour.x");
        CollectionAssert.Contains(issues, "F37.alchemy.x");
        CollectionAssert.DoesNotContain(issues, "F37.chaos.x");
    }

    [TestMethod]
    public void MissingCardsAreFirst()
    {
        var settings = new SettingsSnapshot();
        settings.Values["F05.key"] = "F5";
        var cards = ReadinessEvaluator.Evaluate(settings);
        Assert.IsGreaterThan(1, cards.Count);
        Assert.IsFalse(cards[0].IsConfigured);
        Assert.IsTrue(cards[^1].IsConfigured);
    }

    [TestMethod]
    public void InvalidCraftTargetsAndReversedBagCornersAreReported()
    {
        var settings = new SettingsSnapshot { Calibration = new(1920, 1080, 96) };
        settings.Values["F29.limit"] = "0";
        settings.Values["F33.count"] = "7";
        settings.Values["F20.top.x"] = "70";
        settings.Values["F20.top.y"] = "80";
        settings.Values["F20.bottom.x"] = "60";
        settings.Values["F20.bottom.y"] = "120";
        settings.ConfirmedPositions["F20.top"] = settings.Calibration;
        settings.ConfirmedPositions["F20.bottom"] = settings.Calibration;
        Assert.IsTrue(Card(settings, "F29").Issues.Any(issue => issue.TargetFieldId == "F29.limit"));
        Assert.IsTrue(Card(settings, "F33").Issues.Any(issue => issue.TargetFieldId == "F33.count"));
        Assert.AreEqual("F20.bottom.x", Card(settings, "F20").TargetFieldId);
    }

    [TestMethod]
    public void TriggerRouteAndSelectedFlaskSlotsChangeRequiredKeys()
    {
        var settings = new SettingsSnapshot();
        settings.Values["F05.trigger"] = "enabled";
        Assert.IsTrue(Card(settings, "F05").IsConfigured);
        settings.Values["F07.trigger"] = "enabled";
        settings.Values["F07.1.use"] = "false";
        settings.Values["F07.3.use"] = "true";
        Assert.AreEqual("F07.3.key", Card(settings, "F07").TargetFieldId);
        settings.Values["F07.3.key"] = "3";
        Assert.IsTrue(Card(settings, "F07").IsConfigured);
    }
}
