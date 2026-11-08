using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;

namespace RobloxAccountManager.Core.Services;

public readonly record struct JoinResult(bool Ok, string Message, int? Pid = null);

/// <summary>組出 roblox-player: URI 並啟動 Roblox。</summary>
public static class GameLauncher
{
    private static readonly Random Rng = new();

    public static string NewBrowserTracker() =>
        Rng.Next(100000, 175001).ToString() + Rng.Next(100000, 900001).ToString();

    public static string BuildPlaceLauncherUrl(string tracker, long placeOrUserId, string jobId, bool followUser = false)
    {
        const string baseUrl = "https://assetgame.roblox.com/game/PlaceLauncher.ashx";
        if (followUser) return $"{baseUrl}?request=RequestFollowUser&userId={placeOrUserId}";
        if (jobId.Length > 0)
            return $"{baseUrl}?request=RequestGameJob&browserTrackerId={tracker}&placeId={placeOrUserId}&gameId={jobId}&isPlayTogetherGame=false";
        return $"{baseUrl}?request=RequestGame&browserTrackerId={tracker}&placeId={placeOrUserId}&isPlayTogetherGame=false";
    }

    public static string BuildLauncherUri(string ticket, long launchTimeMs, string placeLauncherUrl, string tracker, string? channel = null) =>
        "roblox-player:1+launchmode:play" +
        $"+gameinfo:{ticket}" +
        $"+launchtime:{launchTimeMs}" +
        $"+placelauncherurl:{Uri.EscapeDataString(placeLauncherUrl)}" +
        $"+browsertrackerid:{tracker}" +
        $"+robloxLocale:en_us+gameLocale:en_us+channel:{channel ?? AppSettings.Current.Channel}+LaunchExp:InApp";

    /// <summary>依照帳號的目標（遊戲 / Job / 私人伺服器 / 追蹤玩家）啟動；沒有目標時用 fallback。</summary>
    public static async Task<JoinResult> LaunchTargetAsync(
        Account account, LaunchTarget target, long fallbackPlaceId, string fallbackJobId = "", CancellationToken ct = default)
    {
        switch (target.Kind)
        {
            case TargetKind.Follow:
                return await JoinServerAsync(account, target.FollowUserId, "", followUser: true, ct);
            case TargetKind.Private:
            {
                var place = target.PlaceId;
                var code = target.LinkCode;
                if (place is null)
                {
                    // 分享短連結：先用 sharelinks API 解析成 placeId + 真正的 linkCode
                    var (ok, p, c, _) = await AccountApi.ResolveShareLinkAsync(account, target.LinkCode, ct);
                    if (ok) { place = p; code = c; }
                }
                return await JoinPrivateServerAsync(account, place, code, ct);
            }
            case TargetKind.Job:
                return await JoinServerAsync(account, target.PlaceId ?? fallbackPlaceId, target.JobId, false, ct);
            case TargetKind.Place:
                return await JoinServerAsync(account, target.PlaceId!.Value, "", false, ct);
            default:
                return await JoinServerAsync(account, fallbackPlaceId, fallbackJobId, false, ct);
        }
    }

    /// <summary>多開沒有真正生效時回傳警語（這次啟動會把執行中的 Roblox 關掉），生效時回傳空字串。</summary>
    public static string MultiRobloxWarning() => MultiRoblox.State switch
    {
        MultiRobloxState.Active => "",
        MultiRobloxState.Waiting => "（⚠ 多開未生效：目前有 Roblox 持有 mutex，這次啟動可能會關閉它；請先關閉所有 Roblox 再試）",
        _ => "（⚠ 多開已停用，這次啟動會關閉執行中的 Roblox）",
    };

    /// <summary>
    /// 啟動 Roblox 並加入伺服器。jobId 可以是單純的 Job ID，也可以是整條加入連結；會自動解析並校正 Place ID。
    /// </summary>
    public static async Task<JoinResult> JoinServerAsync(
        Account account, long placeId, string jobId = "", bool followUser = false, CancellationToken ct = default)
    {
        if (jobId.Length > 0 && !followUser)
        {
            var (p, j) = await JoinTarget.NormalizeAsync(placeId, jobId, ct);
            if (p is null) return new JoinResult(false, "Place ID 無效");
            placeId = p.Value;
            jobId = j;
        }

        var (ok, ticket) = await RobloxApi.GetAuthTicketAsync(account, ct);
        if (!ok) return new JoinResult(false, $"取得 auth ticket 失敗: {ticket}");

        if (string.IsNullOrEmpty(account.BrowserTrackerId)) account.BrowserTrackerId = NewBrowserTracker();
        var tracker = account.BrowserTrackerId;
        var launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var note = MultiRobloxWarning();
        if (jobId.Length > 0 && !followUser)
        {
            var (valid, msg, definitive) = await RobloxApi.ValidateJobIdAsync(account, placeId, jobId, tracker, ct);
            if (!valid)
            {
                if (definitive) return new JoinResult(false, msg);
                note += $"（預檢無法確認：{msg}，仍已嘗試啟動）"; // 預檢無法斷定 → 照常啟動，只是提醒
            }
        }

        try
        {
            var url = BuildPlaceLauncherUrl(tracker, placeId, jobId, followUser);
            var uri = BuildLauncherUri(ticket, launchTime, url, tracker);
            // 啟動前準備含短暫等待與 handle 掃描，放到背景執行緒避免卡住 UI
            var (pid, _) = await Task.Run(() => RobloxLauncher.Launch(uri), ct);
            account.LastUse = DateTime.Now;
            return new JoinResult(true, "Success" + note, pid);
        }
        catch (Exception e)
        {
            return new JoinResult(false, e.Message);
        }
    }

    /// <summary>
    /// 透過私人伺服器連結代碼（或分享短連結的 share code）啟動 Roblox 並加入。placeId 為 null 代表分享短連結格式。
    /// </summary>
    public static async Task<JoinResult> JoinPrivateServerAsync(
        Account account, long? placeId, string linkCode, CancellationToken ct = default)
    {
        var (ok, ticket) = await RobloxApi.GetAuthTicketAsync(account, ct);
        if (!ok) return new JoinResult(false, $"取得 auth ticket 失敗: {ticket}");

        if (string.IsNullOrEmpty(account.BrowserTrackerId)) account.BrowserTrackerId = NewBrowserTracker();
        var tracker = account.BrowserTrackerId;
        var launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            var accessCode = linkCode; // share code：直接當 accessCode
            if (placeId is not null)
            {
                // 舊版格式：從遊戲頁面 HTML 取 UUID accessCode，取不到就退回 linkCode
                var (okAc, ac) = await PrivateServer.FetchAccessCodeAsync(account, placeId.Value, linkCode, ct);
                if (okAc) accessCode = ac;
            }
            var url = PrivateServer.BuildPlaceLauncherUrl(tracker, placeId, accessCode, linkCode);
            var uri = BuildLauncherUri(ticket, launchTime, url, tracker);
            var (pid, _) = await Task.Run(() => RobloxLauncher.Launch(uri), ct);
            account.LastUse = DateTime.Now;
            return new JoinResult(true, "Success" + MultiRobloxWarning(), pid);
        }
        catch (Exception e)
        {
            return new JoinResult(false, e.Message);
        }
    }

    /// <summary>批次啟動：逐一加入，間隔 delay。progress(done,total) 在每個帳號後回報。</summary>
    public static async Task BatchJoinAsync(
        IReadOnlyList<Account> accounts, long placeId, string jobId, TimeSpan delay,
        Action<int, int, Account, JoinResult>? progress = null, CancellationToken ct = default)
    {
        for (int i = 0; i < accounts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var r = await JoinServerAsync(accounts[i], placeId, jobId, false, ct);
            progress?.Invoke(i + 1, accounts.Count, accounts[i], r);
            if (i < accounts.Count - 1) await Task.Delay(delay, ct);
        }
    }
}
