using System.Windows;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.Core.Services.Loc;

namespace RobloxAccountManager.App;

/// <summary>自動更新：啟動時（可在設定關閉）與「工具 → 檢查更新」查 GitHub Releases。</summary>
public partial class MainWindow
{
    private bool _updating;

    private void CheckUpdate_Click(object s, RoutedEventArgs e) => _ = CheckForUpdateAsync(manual: true);

    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_updating) return;
        var baseDir = AppContext.BaseDirectory;
        if (!Updater.IsInstalledBuild(baseDir))
        {
            if (manual) MessageBox.Show(this, T("This is a development build; updates are disabled."), T("Check for Updates..."), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (manual) Status(T("Checking for updates…"));
        var info = await Updater.CheckAsync(Updater.CurrentVersion);
        if (info is null)
        {
            if (manual)
            {
                Status("");
                MessageBox.Show(this, T("You are on the latest version.") + $" (v{Updater.CurrentVersion.ToString(3)})", T("Check for Updates..."), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        var notes = info.Notes.Length > 600 ? info.Notes[..600] + "…" : info.Notes;
        var text = $"{T("A new version is available:")} v{info.Version.ToString(3)}  ({T("current")} v{Updater.CurrentVersion.ToString(3)})\n\n"
                   + (notes.Length > 0 ? notes + "\n\n" : "")
                   + T("Update now? The app will restart; your accounts and settings are kept.");
        if (MessageBox.Show(this, text, T("Check for Updates..."), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _updating = true;
        try
        {
            var progress = new Progress<double>(p => Status($"{T("Downloading update…")} {(int)(p * 100)}%"));
            var dir = await Updater.DownloadAsync(info, progress);
            AppLog.Info($"[更新] 已下載 v{info.Version.ToString(3)}，重新啟動以安裝");
            Updater.LaunchInstaller(dir, baseDir, Environment.ProcessId);
            _quitting = true;
            Close();
        }
        catch (Exception ex)
        {
            _updating = false;
            Status("");
            AppLog.Error("[更新] " + ex.Message);
            MessageBox.Show(this, T("Update failed:") + "\n" + ex.Message, T("Check for Updates..."), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
