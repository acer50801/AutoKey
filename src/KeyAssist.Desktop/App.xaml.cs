using System.IO;
using System.Windows;
using KeyAssist.Core;

namespace KeyAssist.Desktop;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(false, @"Local\KeyAssist.v8");
        try { ownsInstance = instance.WaitOne(0); }
        catch (AbandonedMutexException) { ownsInstance = true; }
        if (!ownsInstance)
        {
            MessageBox.Show("按鍵輔助 v8 已開啟。請回到原本的視窗繼續編輯。", "按鍵輔助 v8");
            Shutdown();
            return;
        }
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyAssist", "v8");
        var fallback = new CatalogSnapshot("未安裝詞綴快照", false, [], []);
        var bundled = CatalogStore.LoadBundled(Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json"), fallback);
        var catalog = new CatalogStore(Path.Combine(directory, "catalog.json")).Load(bundled.Snapshot);
        MainWindow = new MainWindow(new SettingsStore(Path.Combine(directory, "settings.json")), catalog.Snapshot,
            catalog.Error ?? bundled.Error);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsInstance)
            instance!.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
