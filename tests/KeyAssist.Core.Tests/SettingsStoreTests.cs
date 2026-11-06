using KeyAssist.Core;

namespace KeyAssist.Core.Tests;

[TestClass]
public class SettingsStoreTests
{
    [TestMethod]
    public void FailedReplacementKeepsLastSnapshot()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(folder.FullName, "settings.json");
            var store = new SettingsStore(path);
            var settings = new SettingsSnapshot();
            settings.Values["F03.scroll.x"] = "11";
            settings.Rules.LegacyText = "原本的詞綴名稱與階級";
            Assert.IsNull(store.Save(settings));
            settings.Values["F03.scroll.x"] = "22";
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.IsNotNull(store.Save(settings));
            var restored = store.Load();
            Assert.IsNull(restored.Error);
            Assert.AreEqual("11", restored.Settings.Get("F03.scroll.x"));
            Assert.AreEqual("原本的詞綴名稱與階級", restored.Settings.Rules.LegacyText);
        }
        finally { folder.Delete(recursive: true); }
    }
}
