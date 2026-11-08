using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;

namespace RobloxAccountManager.Core.Services;

public readonly record struct ApiResult(bool Ok, string Message, string? NewCookie = null);

public sealed record Presence(long UserId, int Type, long PlaceId, string GameId);

/// <summary>帳號工具：PIN、隱私、改密碼/信箱、登出其他裝置、封鎖、顯示名稱、好友、玩家搜尋。移植自 V2 core/account.py。</summary>
public static class AccountApi
{
    public const string AuthApi = "https://auth.roblox.com";
    public const string UsersApi = "https://users.roblox.com";
    public const string FriendsApi = "https://friends.roblox.com";
    public const string AccountSettingsApi = "https://accountsettings.roblox.com";
    public const string PresenceApi = "https://presence.roblox.com";

    public static readonly IReadOnlyList<string> FollowPrivacy = ["All", "Followers", "Following", "Friends", "NoOne"];

    // PIN 解鎖到期時間（以 cookie 為鍵；PIN 狀態屬於工作階段，不寫入磁碟）
    private static readonly ConcurrentDictionary<string, DateTime> PinUnlocked = new();

    private sealed record Raw(int Status, string Body, string? NewCookie);

    /// <summary>帶 CSRF 的請求；遇到 403 且回傳新的 x-csrf-token 時自動重試一次。</summary>
    private static async Task<Raw> SendAsync(
        Account acc, HttpMethod method, string url, Func<HttpContent?>? content = null,
        string referer = "https://www.roblox.com/", CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(acc.CsrfToken))
        {
            var (ok, t) = await RobloxApi.GetCsrfTokenAsync(acc, ct);
            if (!ok) return new Raw(0, $"取得 CSRF token 失敗: {t}", null);
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var http = RobloxApi.CreateClient();
            using var req = RobloxApi.Build(method, url, acc);
            req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", acc.CsrfToken);
            req.Headers.TryAddWithoutValidation("Referer", referer);
            if (content is not null) req.Content = content();
            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if ((int)resp.StatusCode == 403 && attempt == 0 &&
                resp.Headers.TryGetValues("x-csrf-token", out var v) && v.FirstOrDefault() is { Length: > 0 } fresh)
            {
                acc.CsrfToken = fresh;
                continue;
            }
            return new Raw((int)resp.StatusCode, body, ExtractCookie(resp));
        }
        return new Raw(0, "CSRF retry failed", null);
    }

    private static string? ExtractCookie(HttpResponseMessage resp)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var c in cookies)
        {
            const string key = ".ROBLOSECURITY=";
            var i = c.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) continue;
            var rest = c[(i + key.Length)..];
            var end = rest.IndexOf(';');
            var val = end < 0 ? rest : rest[..end];
            if (val.Length > 0) return val;
        }
        return null;
    }

    private static string FirstError(string body, string fallback)
    {
        try { return (JsonNode.Parse(body)?["errors"] as JsonArray)?.FirstOrDefault()?["message"]?.ToString() ?? fallback; }
        catch { return fallback; }
    }

    // ------------------------------------------------------------------ //
    // PIN
    // ------------------------------------------------------------------ //

    public static async Task<bool> CheckPinAsync(Account acc, CancellationToken ct = default)
    {
        if (PinUnlocked.TryGetValue(acc.SecurityToken, out var until) && DateTime.Now < until) return true;
        var r = await SendAsync(acc, HttpMethod.Get, $"{AuthApi}/v1/account/pin/", ct: ct);
        if (r.Status != 200) return false;
        var info = JsonNode.Parse(r.Body);
        if (info?["isEnabled"]?.GetValue<bool>() != true) return true;
        return (info["unlockedUntil"]?.GetValue<long>() ?? 0) > 0;
    }

    public static async Task<bool> UnlockPinAsync(Account acc, string pin, CancellationToken ct = default)
    {
        if (pin.Length != 4) return false;
        if (await CheckPinAsync(acc, ct)) return true;
        var r = await SendAsync(acc, HttpMethod.Post, $"{AuthApi}/v1/account/pin/unlock",
            () => new FormUrlEncodedContent(new Dictionary<string, string> { ["pin"] = pin }), ct: ct);
        if (r.Status != 200) return false;
        var info = JsonNode.Parse(r.Body);
        var secs = info?["unlockedUntil"]?.GetValue<long>() ?? 0;
        if (info?["isEnabled"]?.GetValue<bool>() == true && secs > 0)
        {
            PinUnlocked[acc.SecurityToken] = DateTime.Now.AddSeconds(secs);
            return true;
        }
        return false;
    }

    private static async Task<ApiResult?> RequirePinAsync(Account acc, CancellationToken ct) =>
        await CheckPinAsync(acc, ct) ? null : new ApiResult(false, "PIN 已鎖定，請先解鎖");

    // ------------------------------------------------------------------ //
    // 隱私 / 密碼 / 信箱 / 登出
    // ------------------------------------------------------------------ //

    public static async Task<ApiResult> SetFollowPrivacyAsync(Account acc, int privacy, CancellationToken ct = default)
    {
        if (await RequirePinAsync(acc, ct) is { } locked) return locked;
        var value = FollowPrivacy[Math.Clamp(privacy, 0, FollowPrivacy.Count - 1)];
        var r = await SendAsync(acc, HttpMethod.Post, $"{RobloxApi.RobloxBase}/account/settings/follow-me-privacy",
            () => new FormUrlEncodedContent(new Dictionary<string, string> { ["FollowMePrivacy"] = value }),
            "https://www.roblox.com/my/account", ct);
        return new ApiResult(r.Status == 200, r.Status == 200 ? $"追蹤隱私已設為 {value}" : $"失敗 {r.Status}");
    }

    public static async Task<ApiResult> ChangePasswordAsync(Account acc, string current, string @new, CancellationToken ct = default)
    {
        if (await RequirePinAsync(acc, ct) is { } locked) return locked;
        var r = await SendAsync(acc, HttpMethod.Post, $"{AuthApi}/v2/user/passwords/change",
            () => JsonContent.Create(new { currentPassword = current, newPassword = @new }), ct: ct);
        if (r.Status != 200) return new ApiResult(false, FirstError(r.Body, $"失敗 {r.Status}"));
        acc.Password = @new;
        if (r.NewCookie is not null) acc.SecurityToken = r.NewCookie;
        return new ApiResult(true, "密碼已變更", r.NewCookie);
    }

    public static async Task<ApiResult> ChangeEmailAsync(Account acc, string password, string newEmail, CancellationToken ct = default)
    {
        if (await RequirePinAsync(acc, ct) is { } locked) return locked;
        var r = await SendAsync(acc, HttpMethod.Post, $"{AccountSettingsApi}/v1/email",
            () => JsonContent.Create(new { password, emailAddress = newEmail }), ct: ct);
        if (r.Status != 200) return new ApiResult(false, FirstError(r.Body, $"失敗 {r.Status}"));
        acc.Email = newEmail;
        return new ApiResult(true, "信箱已變更");
    }

    public static async Task<ApiResult> LogoutOtherSessionsAsync(Account acc, CancellationToken ct = default)
    {
        if (await RequirePinAsync(acc, ct) is { } locked) return locked;
        var r = await SendAsync(acc, HttpMethod.Post,
            $"{RobloxApi.RobloxBase}/authentication/signoutfromallsessionsandreauthenticate",
            () => new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), ct: ct);
        if (r.Status != 200) return new ApiResult(false, $"失敗 {r.Status}");
        if (r.NewCookie is not null) acc.SecurityToken = r.NewCookie;
        return new ApiResult(true, "已登出其他所有裝置", r.NewCookie);
    }

    public static async Task<ApiResult> SetDisplayNameAsync(Account acc, string displayName, CancellationToken ct = default)
    {
        if (acc.UserId == 0) return new ApiResult(false, "此帳號沒有 UserId，請先重新整理帳號資訊");
        var r = await SendAsync(acc, new HttpMethod("PATCH"), $"{UsersApi}/v1/users/{acc.UserId}/display-names",
            () => JsonContent.Create(new { newDisplayName = displayName }), ct: ct);
        return r.Status == 200
            ? new ApiResult(true, "顯示名稱已設定")
            : new ApiResult(false, FirstError(r.Body, $"失敗 {r.Status}"));
    }

    // ------------------------------------------------------------------ //
    // 封鎖 / 好友
    // ------------------------------------------------------------------ //

    public static async Task<(bool Ok, List<long> UserIds)> GetBlockedUsersAsync(Account acc, CancellationToken ct = default)
    {
        if (!await CheckPinAsync(acc, ct)) return (false, []);
        var r = await SendAsync(acc, HttpMethod.Get, $"{AccountSettingsApi}/v1/users/get-detailed-blocked-users", ct: ct);
        if (r.Status != 200) return (false, []);
        var ids = (JsonNode.Parse(r.Body)?["blockedUsers"] as JsonArray ?? [])
            .Select(u => u?["userId"]?.GetValue<long>() ?? 0).Where(x => x > 0).ToList();
        return (true, ids);
    }

    public static async Task<ApiResult> BlockUserAsync(Account acc, long userId, bool unblock = false, CancellationToken ct = default)
    {
        if (await RequirePinAsync(acc, ct) is { } locked) return locked;
        var action = unblock ? "unblock" : "block";
        var r = await SendAsync(acc, HttpMethod.Post, $"{AccountSettingsApi}/v1/users/{userId}/{action}", ct: ct);
        return r.Status == 200
            ? new ApiResult(true, unblock ? "已解除封鎖" : "已封鎖")
            : new ApiResult(false, $"失敗 {r.Status}");
    }

    /// <summary>已封鎖就解除、否則封鎖。</summary>
    public static async Task<ApiResult> ToggleBlockAsync(Account acc, string username, CancellationToken ct = default)
    {
        var uid = await GetUserIdAsync(username, ct);
        if (uid is null) return new ApiResult(false, $"找不到用戶 {username}");
        var (ok, blocked) = await GetBlockedUsersAsync(acc, ct);
        if (!ok) return new ApiResult(false, "無法取得封鎖清單（PIN 可能已鎖定）");
        return await BlockUserAsync(acc, uid.Value, unblock: blocked.Contains(uid.Value), ct);
    }

    public static async Task<ApiResult> UnblockEveryoneAsync(Account acc, CancellationToken ct = default)
    {
        var (ok, blocked) = await GetBlockedUsersAsync(acc, ct);
        if (!ok) return new ApiResult(false, "無法取得封鎖清單（PIN 可能已鎖定）");
        int done = 0;
        foreach (var id in blocked)
            if ((await BlockUserAsync(acc, id, unblock: true, ct)).Ok) done++;
        return new ApiResult(done == blocked.Count, $"已解除 {done}/{blocked.Count} 位");
    }

    public static async Task<ApiResult> SendFriendRequestAsync(Account acc, long targetUserId, CancellationToken ct = default)
    {
        var r = await SendAsync(acc, HttpMethod.Post, $"{FriendsApi}/v1/users/{targetUserId}/request-friendship", ct: ct);
        return r.Status == 200 ? new ApiResult(true, "已送出好友邀請") : new ApiResult(false, FirstError(r.Body, $"失敗 {r.Status}"));
    }

    /// <summary>
    /// 把私人伺服器分享連結（roblox.com/share?code=…&amp;type=Server）解析成 (placeId, 真正的 privateServerLinkCode)。
    /// 使用 apis.roblox.com/sharelinks/v1/resolve-link。
    /// </summary>
    public static async Task<(bool Ok, long PlaceId, string LinkCode, string Message)> ResolveShareLinkAsync(
        Account acc, string shareCode, CancellationToken ct = default)
    {
        var r = await SendAsync(acc, HttpMethod.Post, "https://apis.roblox.com/sharelinks/v1/resolve-link",
            () => JsonContent.Create(new { linkId = shareCode, linkType = "Server" }), ct: ct);
        if (r.Status != 200) return (false, 0, "", FirstError(r.Body, $"HTTP {r.Status}"));
        try
        {
            var data = JsonNode.Parse(r.Body)?["privateServerInviteData"];
            var place = data?["placeId"] is { } p && long.TryParse(p.ToString(), out var pl) ? pl : 0;
            var code = data?["linkCode"]?.ToString() ?? "";
            if (place > 0 && code.Length > 0) return (true, place, code, "OK");
            var status = data?["status"]?.ToString() ?? JsonNode.Parse(r.Body)?["status"]?.ToString() ?? "";
            return (false, 0, "", $"分享連結無效或已過期 {status}".Trim());
        }
        catch (Exception e) { return (false, 0, "", e.Message); }
    }

    // ------------------------------------------------------------------ //
    // 用戶 / 在線狀態 / 玩家搜尋
    // ------------------------------------------------------------------ //

    public static async Task<long?> GetUserIdAsync(string username, CancellationToken ct = default)
    {
        try
        {
            using var http = RobloxApi.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{UsersApi}/v1/usernames/users")
            {
                Content = JsonContent.Create(new { usernames = new[] { username }, excludeBannedUsers = false }),
            };
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var id = (JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))?["data"] as JsonArray)?.FirstOrDefault()?["id"];
            return id?.GetValue<long>();
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    public static async Task<List<Presence>> GetPresenceAsync(Account acc, IEnumerable<long> userIds, CancellationToken ct = default)
    {
        var result = new List<Presence>();
        var r = await SendAsync(acc, HttpMethod.Post, $"{PresenceApi}/v1/presence/users",
            () => JsonContent.Create(new { userIds = userIds.ToArray() }), ct: ct);
        if (r.Status != 200) return result;
        foreach (var p in JsonNode.Parse(r.Body)?["userPresences"] as JsonArray ?? [])
        {
            if (p is null) continue;
            result.Add(new Presence(
                p["userId"]?.GetValue<long>() ?? 0,
                p["userPresenceType"]?.GetValue<int>() ?? 0,
                p["placeId"] is { } pl && long.TryParse(pl.ToString(), out var pid) ? pid : 0,
                p["gameId"]?.ToString() ?? ""));
        }
        return result;
    }

    /// <summary>找出目標玩家目前所在伺服器的 Job ID（必須與 placeId 相同；用指定帳號的身分查詢在線狀態）。</summary>
    public static async Task<(bool Ok, string Message, long PlaceId, string JobId)> FindPlayerServerAsync(
        Account asker, string targetUsername, CancellationToken ct = default)
    {
        var uid = await GetUserIdAsync(targetUsername, ct);
        if (uid is null) return (false, $"找不到用戶 {targetUsername}", 0, "");
        var p = (await GetPresenceAsync(asker, [uid.Value], ct)).FirstOrDefault();
        if (p is null) return (false, "查詢在線狀態失敗", 0, "");
        if (p.Type != 2 || p.PlaceId == 0) return (false, "對方目前不在遊戲中（或隱私設定不允許查看）", 0, "");
        if (p.GameId.Length == 0) return (false, "對方在遊戲中但沒有公開 Job ID", p.PlaceId, "");
        return (true, "OK", p.PlaceId, p.GameId);
    }
}
