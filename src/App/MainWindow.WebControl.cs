using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.App;

/// <summary>網頁控制面板（WebApiServer）呼叫的操作；全部切回 UI 執行緒，行為跟在主視窗上按一樣。</summary>
public partial class MainWindow : IWebControl
{
    private List<Account> FindAccounts(IReadOnlyList<string> usernames) =>
        usernames.Count == 0
            ? _accounts.Where(a => a.Checked).ToList()
            : _accounts.Where(a => usernames.Contains(a.Username, StringComparer.OrdinalIgnoreCase)).ToList();

    Task<WebState> IWebControl.GetStateAsync() => Dispatcher.InvokeAsync(() =>
    {
        var accounts = _accounts.Select(a => new WebAccount(
            a.Username, a.Alias, a.Group, a.UserId, a.AvatarUrl, a.Checked, a.Pid, a.TargetText, a.Valid, a.Subtitle)).ToList();
        int.TryParse(RunningText.Text, out var running);
        var log = AppLog.Snapshot();
        return new WebState(accounts, running, PlaceBox.Text.Trim(), JobBox.Text.Trim(), _launching,
            log.Skip(Math.Max(0, log.Count - 40)).ToList(), ThemeManager.WebColors(), VersionText.Text);
    }).Task;

    Task<(bool Ok, string Message)> IWebControl.LaunchAsync(IReadOnlyList<string> usernames, string place, string job) =>
        Dispatcher.InvokeAsync(async () =>
        {
            var list = FindAccounts(usernames);
            if (list.Count == 0) return (false, usernames.Count == 0 ? "還沒有勾選任何帳號" : "找不到這些帳號");
            if (_launching) return (false, "已經有一批帳號正在啟動，請等它結束");

            LaunchTarget? forced = null;
            if (job.Length > 0 && PrivateServer.ParseUrl(job) is (var psPlace, { } code))
            {
                forced = new LaunchTarget(TargetKind.Private, psPlace ?? (long.TryParse(place, out var pp) ? pp : null), LinkCode: code);
            }
            else if (place.Length > 0 || job.Length > 0)
            {
                long? typed = long.TryParse(place, out var t) && t > 0 ? t : null;
                var (p, j) = await JoinTarget.NormalizeAsync(typed, job);
                if (p is null) return (false, "Place ID 不正確，或連結裡找不到 Place ID");
                forced = j.Length > 0 ? new LaunchTarget(TargetKind.Job, p, j) : new LaunchTarget(TargetKind.Place, p);
                // 讓桌面上的目的地也同步顯示
                PlaceBox.Text = p.Value.ToString();
                JobBox.Text = j;
            }
            else if (list.Any(a => AccountTargets.Get(a).Kind == TargetKind.None)
                     && !long.TryParse(PlaceBox.Text.Trim(), out _) && JobBox.Text.Trim().Length == 0)
            {
                return (false, "沒有目標：請輸入 Place ID，或在帳號上設定專屬目標");
            }

            AppLog.Info($"[網頁控制] 啟動 {list.Count} 個帳號" + (forced is null ? "" : " → " + AccountTargets.Describe(forced)));
            _ = LaunchBatchAsync(list, forced);
            return (true, $"開始啟動 {list.Count} 個帳號");
        }).Task.Unwrap();

    Task<int> IWebControl.KillAsync(IReadOnlyList<string> usernames) => Dispatcher.InvokeAsync(async () =>
    {
        var list = FindAccounts(usernames);
        int n = KillAccounts(list);
        AppLog.Info($"[網頁控制] 結束 {n} 個實例");
        await Task.Delay(400);
        await RefreshRunningAsync();
        return n;
    }).Task.Unwrap();

    Task IWebControl.SetCheckedAsync(IReadOnlyList<string> usernames, bool value) =>
        Dispatcher.InvokeAsync(() => SetChecked(FindAccounts(usernames), value)).Task;
}
