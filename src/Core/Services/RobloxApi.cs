using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;

namespace RobloxAccountManager.Core.Services;

/// <summary>Roblox Web API（驗證、auth ticket、帳號資訊、gamejoin 預檢）。</summary>
public static class RobloxApi
{
    public const string AuthApi = "https://auth.roblox.com";
    public const string RobloxBase = "https://www.roblox.com";
    private const string Referer = "https://www.roblox.com/games/4924922222/Brookhaven-RP";

    // gamejoin API 對「瀏覽器 UA」一律回 status=12 / "Unable to join Game 311"（連有效的公開伺服器也是），
    // 只有用 Roblox 客戶端自己的 UA 才會回真實結果。
    internal const string RobloxClientUa = "Roblox/WinInet";

    /// <summary>測試用：替換 HTTP 處理器（攔截請求）。</summary>
    public static HttpMessageHandler? HandlerOverride { get; set; }

    internal static HttpClient CreateClient()
    {
        var handler = HandlerOverride ?? new HttpClientHandler
        {
            UseCookies = false,           // 我們自己帶 .ROBLOSECURITY
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        return new HttpClient(handler, disposeHandler: HandlerOverride is null) { Timeout = TimeSpan.FromSeconds(15) };
    }

    internal static HttpRequestMessage Build(HttpMethod method, string url, Account acc, string userAgent = "Mozilla/5.0")
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={acc.SecurityToken}");
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        return req;
    }

    public static async Task<(bool Ok, string Value)> GetCsrfTokenAsync(Account acc, CancellationToken ct = default)
    {
        try
        {
            using var http = CreateClient();
            using var req = Build(HttpMethod.Post, $"{AuthApi}/v1/authentication-ticket/", acc);
            req.Headers.TryAddWithoutValidation("Referer", Referer);
            req.Content = new StringContent("");
            using var resp = await http.SendAsync(req, ct);
            if ((int)resp.StatusCode != 403)
                return (false, $"[{(int)resp.StatusCode}] {await resp.Content.ReadAsStringAsync(ct)}");
            if (resp.Headers.TryGetValues("x-csrf-token", out var vals) && vals.FirstOrDefault() is { Length: > 0 } token)
            {
                acc.CsrfToken = token;
                acc.LastUse = DateTime.Now;
                return (true, token);
            }
            return (false, "");
        }
        catch (Exception e) { return (false, e.Message); }
    }

    public static async Task<(bool Ok, string Value)> GetAuthTicketAsync(Account acc, CancellationToken ct = default)
    {
        var (ok, token) = await GetCsrfTokenAsync(acc, ct);
        if (!ok) return (false, $"CSRF failed: {token}");
        try
        {
            using var http = CreateClient();
            using var req = Build(HttpMethod.Post, $"{AuthApi}/v1/authentication-ticket/", acc);
            req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", token);
            req.Headers.TryAddWithoutValidation("Referer", Referer);
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var resp = await http.SendAsync(req, ct);
            if (resp.Headers.TryGetValues("rbx-authentication-ticket", out var vals) && vals.FirstOrDefault() is { Length: > 0 } ticket)
                return (true, ticket);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (false, $"[{(int)resp.StatusCode}] no ticket header. body={body[..Math.Min(200, body.Length)]}");
        }
        catch (Exception e) { return (false, e.Message); }
    }

    /// <summary>用 cookie 取得用戶名與 UserId。</summary>
    public static async Task<bool> FetchInfoAsync(Account acc, CancellationToken ct = default)
    {
        try
        {
            using var http = CreateClient();
            using var req = Build(HttpMethod.Get, $"{RobloxBase}/my/account/json", acc);
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return false;
            var obj = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct)) as JsonObject;
            var name = obj?["Name"]?.GetValue<string>() ?? "";
            var id = obj?["UserId"]?.GetValue<long>() ?? 0;
            if (name.Length == 0 || id == 0) return false;
            acc.Username = name;
            acc.UserId = id;
            acc.Valid = true;
            acc.LastUse = DateTime.Now;
            return true;
        }
        catch { return false; }
    }

    // gamejoin status：0/1/3 = 準備中（重試）、2 = 可加入、4 = 遊戲已停用、5 = 找不到該伺服器
    // （已關閉，或 Job ID 與 Place ID 不匹配）、6 = 已滿。其餘代表 API 本身拒絕或行為改變，不足以斷定 Job ID 失效。
    private static readonly HashSet<int> RetryStatuses = [0, 1, 3];

    private static string DefinitiveFailure(int status, long placeId) => status switch
    {
        4 => "遊戲目前已停用",
        5 => $"找不到此伺服器（已關閉，或這個 Job ID 不屬於 Place ID {placeId}）",
        6 => "伺服器已滿",
        _ => "",
    };

    /// <summary>
    /// 透過 gamejoin API 驗證 Job ID 目前是否仍可加入。
    /// Definitive=true 表示失敗是確定的（伺服器不存在 / 已滿…），呼叫端應擋下；
    /// false 表示預檢無法斷定（API 拒絕、網路錯誤…），應照常嘗試啟動。
    /// </summary>
    public static async Task<(bool Ok, string Message, bool Definitive)> ValidateJobIdAsync(
        Account acc, long placeId, string jobId, string tracker, CancellationToken ct = default,
        TimeSpan? retryDelay = null)
    {
        var (ok, csrf) = await GetCsrfTokenAsync(acc, ct);
        if (!ok) return (false, $"取得 CSRF token 失敗: {csrf}", false);

        try
        {
            int status = -1;
            string message = "";
            for (int attempt = 0; attempt < 4; attempt++)
            {
                using var http = CreateClient();
                using var req = Build(HttpMethod.Post, "https://gamejoin.roblox.com/v1/join-game-instance", acc, RobloxClientUa);
                req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
                req.Headers.TryAddWithoutValidation("Origin", RobloxBase);
                req.Headers.TryAddWithoutValidation("Referer", $"{RobloxBase}/games/{placeId}");
                req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                req.Content = JsonContent.Create(new Dictionary<string, object?>
                {
                    ["placeId"] = placeId,
                    ["gameId"] = jobId,
                    ["isPlayTogetherGame"] = false,
                    ["isTeleport"] = false,
                    ["isPartyLeader"] = false,
                    ["gamerTag"] = "",
                    ["browserTrackerId"] = long.TryParse(tracker, out var t) ? t : 0,
                    // 每次嘗試都要用獨立的 GUID（不可用 Job ID，否則會被當成重複請求）
                    ["gameJoinAttemptId"] = Guid.NewGuid().ToString(),
                });
                using var resp = await http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    return (false, $"HTTP {(int)resp.StatusCode}: {body[..Math.Min(200, body.Length)]}", false);

                var data = JsonNode.Parse(body) as JsonObject;
                status = data?["status"]?.GetValue<int>() ?? -1;
                message = data?["message"]?.ToString() ?? "";
                if (status == 2) return (true, "OK", false);
                if (RetryStatuses.Contains(status))
                {
                    await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(1), ct);
                    continue;
                }
                break;
            }

            var reason = DefinitiveFailure(status, placeId);
            if (reason.Length > 0) return (false, $"無法加入此 Job ID（status={status}）：{reason}", true);
            return (false, $"status={status} {message}".Trim(), false);
        }
        catch (Exception e) { return (false, e.Message, false); }
    }
}
