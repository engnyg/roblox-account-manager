using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RobloxAccountManager.Core.Models;

namespace RobloxAccountManager.Core.Services;

/// <summary>
/// 私人伺服器（VIP）連結：解析網址、取得 accessCode、解析目前的 Job ID。
/// 移植自 V2 manager/game_launcher.py 的 private server 相關函式。
/// </summary>
public static partial class PrivateServer
{
    private const string Uuid = "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";
    private const string BrowserUa =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // 遊戲頁面內嵌 accessCode 的幾種寫法
    private static readonly Regex[] AccessCodePatterns =
    [
        new($@"joinPrivateGame\s*\(\s*\d+\s*,\s*['""]({Uuid})['""]", RegexOptions.IgnoreCase),
        new($@"""accessCode""\s*:\s*""({Uuid})""", RegexOptions.IgnoreCase),
        new($@"""privateServerAccessCode""\s*:\s*""({Uuid})""", RegexOptions.IgnoreCase),
        new($@"\baccessCode\s*:\s*['""]({Uuid})['""]", RegexOptions.IgnoreCase),
    ];

    private static readonly Regex[] PlacePatterns =
    [
        new(@"/games/(\d+)"),
        new(@"""placeId""\s*:\s*(\d+)"),
        new(@"""PlaceId""\s*:\s*(\d+)"),
        new(@"roblox\.com/games/(\d+)"),
    ];

    [GeneratedRegex(@"[?&]code=([a-fA-F0-9]+)")] private static partial Regex ShareCode();
    [GeneratedRegex(@"/games/(\d+)")] private static partial Regex GamesPath();
    [GeneratedRegex(@"[?&]privateServerLinkCode=([^\s&]+)")] private static partial Regex LinkCodeParam();

    /// <summary>
    /// 解析私人伺服器網址，回傳 (PlaceId, LinkCode)。
    /// 完整格式 /games/{placeId}/...?privateServerLinkCode={code} → (placeId, code)；
    /// 分享短連結 /share?code={code}&amp;type=Server → (null, code)；其餘 → (null, null)。
    /// </summary>
    public static (long? PlaceId, string? LinkCode) ParseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (null, null);
        var s = ShareCode().Match(url);
        if (s.Success && url.Contains("type=Server", StringComparison.Ordinal)) return (null, s.Groups[1].Value);

        var p = GamesPath().Match(url);
        var c = LinkCodeParam().Match(url);
        if (p.Success && c.Success) return (long.Parse(p.Groups[1].Value), c.Groups[1].Value);
        return (null, null);
    }

    // ------------------------------------------------------------------ //

    private static async Task<(int Status, string Body, string FinalUrl)> GetAsync(
        Account acc, string url, string ua, CancellationToken ct, bool browserHeaders = false)
    {
        using var http = RobloxApi.CreateClient();
        using var req = RobloxApi.Build(HttpMethod.Get, url, acc, ua);
        if (browserHeaders)
        {
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            req.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com/");
        }
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return ((int)resp.StatusCode, body, resp.RequestMessage?.RequestUri?.ToString() ?? url);
    }

    /// <summary>訪問遊戲頁面 HTML，抽出 UUID 格式的 accessCode。</summary>
    public static async Task<(bool Ok, string Value)> FetchAccessCodeAsync(
        Account acc, long placeId, string linkCode, CancellationToken ct = default)
    {
        try
        {
            var (status, body, _) = await GetAsync(acc,
                $"https://www.roblox.com/games/{placeId}?privateServerLinkCode={Uri.EscapeDataString(linkCode)}",
                "Mozilla/5.0", ct);
            if (status is < 200 or >= 300) return (false, $"HTTP {status}");
            foreach (var re in AccessCodePatterns)
            {
                var m = re.Match(body);
                if (m.Success) return (true, m.Groups[1].Value);
            }
            return (false, "頁面中找不到 accessCode（連結可能已失效或尚未開啟伺服器）");
        }
        catch (Exception e) { return (false, e.Message); }
    }

    /// <summary>不走 gamejoin：share code → placeId → universeId → 列出 Private 伺服器取第一個 Job ID。</summary>
    internal static async Task<(bool Ok, string Value)> ResolveViaServersApiAsync(
        Account acc, long? placeId, string linkCode, CancellationToken ct)
    {
        var resolved = placeId;
        if (resolved is null)
        {
            try
            {
                var (_, body, finalUrl) = await GetAsync(acc,
                    $"https://www.roblox.com/share?code={Uri.EscapeDataString(linkCode)}&type=Server", BrowserUa, ct, browserHeaders: true);
                foreach (var target in new[] { finalUrl, body })
                {
                    foreach (var re in PlacePatterns)
                    {
                        var m = re.Match(target);
                        if (m.Success) { resolved = long.Parse(m.Groups[1].Value); break; }
                    }
                    if (resolved is not null) break;
                }
                if (resolved is null) return (false, "share code 無法解析為 place ID（請確認連結有效）");
            }
            catch (Exception e) { return (false, $"share code 解析失敗: {e.Message}"); }
        }

        long universeId;
        try
        {
            var (status, body, _) = await GetAsync(acc,
                $"https://apis.roblox.com/universes/v1/places/{resolved}/universe", "Mozilla/5.0", ct);
            if (status is < 200 or >= 300) return (false, $"universe ID 查詢失敗: HTTP {status}");
            universeId = JsonNode.Parse(body)?["universeId"]?.GetValue<long>() ?? 0;
            if (universeId == 0) return (false, "無法取得 universeId");
        }
        catch (Exception e) { return (false, $"universe ID 查詢失敗: {e.Message}"); }

        try
        {
            var (status, body, _) = await GetAsync(acc,
                $"https://games.roblox.com/v1/games/{universeId}/servers/Private?sortOrder=Asc&limit=100", "Mozilla/5.0", ct);
            if (status is < 200 or >= 300) return (false, $"伺服器列表查詢失敗: HTTP {status}");
            if (JsonNode.Parse(body)?["data"] is not JsonArray { Count: > 0 } servers)
                return (false, "目前沒有運行中的私人伺服器（伺服器可能已停止）");
            var id = servers[0]?["id"]?.ToString();
            return string.IsNullOrEmpty(id) ? (false, "伺服器列表中找不到 Job ID") : (true, id);
        }
        catch (Exception e) { return (false, $"伺服器列表查詢失敗: {e.Message}"); }
    }

    private static readonly HashSet<int> RetryStatuses = [0, 1, 3];
    private static readonly HashSet<int> ErrorStatuses = [4, 5, 6, 7, 9, 10, 11, 12];

    /// <summary>
    /// 取得私人伺服器目前的 Job ID（需已登入帳號）。先試 servers listing API，失敗才 fallback 到 gamejoin。
    /// </summary>
    public static async Task<(bool Ok, string Value)> ResolveJobIdAsync(
        Account acc, long? placeId, string linkCode, TimeSpan? timeout = null, TimeSpan? pollDelay = null,
        CancellationToken ct = default)
    {
        var (ok0, job0) = await ResolveViaServersApiAsync(acc, placeId, linkCode, ct);
        if (ok0) return (true, job0);

        var (okCsrf, csrf) = await RobloxApi.GetCsrfTokenAsync(acc, ct);
        if (!okCsrf) return (false, $"取得 CSRF token 失敗: {csrf}");

        var body = new Dictionary<string, object?>
        {
            ["isPlayTogetherGame"] = false, ["isTeleport"] = false, ["gamerTag"] = "",
        };
        bool usedFallback = false;
        if (placeId is null) body["shareCode"] = linkCode;
        else
        {
            var (okAc, ac) = await FetchAccessCodeAsync(acc, placeId.Value, linkCode, ct);
            if (okAc) { body["placeId"] = placeId.Value; body["accessCode"] = ac; }
            else { body["shareCode"] = linkCode; usedFallback = true; }
        }

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                using var http = RobloxApi.CreateClient();
                using var req = RobloxApi.Build(HttpMethod.Post, "https://gamejoin.roblox.com/v1/join-private-game", acc, BrowserUa);
                req.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
                req.Headers.TryAddWithoutValidation("Origin", RobloxApi.RobloxBase);
                req.Headers.TryAddWithoutValidation("Referer", $"{RobloxApi.RobloxBase}/games/{placeId}");
                req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                req.Content = JsonContent.Create(body);
                using var resp = await http.SendAsync(req, ct);
                var text = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) return (false, $"HTTP {(int)resp.StatusCode}: {text[..Math.Min(300, text.Length)]}");

                var data = JsonNode.Parse(text) as JsonObject;
                int status = data?["status"]?.GetValue<int>() ?? -1;
                if (status == 2)
                {
                    var job = data?["jobId"]?.ToString();
                    if (string.IsNullOrEmpty(job))
                    {
                        var js = data?["joinScript"];
                        JsonNode? node = js is JsonValue v && v.TryGetValue<string>(out var s) ? TryParse(s) : js;
                        job = node?["GameId"]?.ToString() ?? node?["gameId"]?.ToString();
                    }
                    return string.IsNullOrEmpty(job) ? (false, $"狀態=2 但無 Job ID，完整回應: {text}") : (true, job);
                }
                if (ErrorStatuses.Contains(status))
                {
                    var msg = data?["message"]?.ToString() ?? "";
                    if (status == 12)
                    {
                        if (usedFallback) return (false, $"無法解析私人伺服器連結（頁面格式可能已更新），請改用直接加入按鈕。原始訊息：{msg}");
                        if (data?["statusData"] is not null) return (false, $"帳號無法加入此遊戲（年齡限制或存取限制）。原始訊息：{msg}");
                        return (false, $"無法加入此私人伺服器（連結可能已失效，或遊戲設定限制加入）。原始訊息：{msg}");
                    }
                    return (false, $"錯誤 status={status}: {msg}");
                }
                if (!RetryStatuses.Contains(status)) return (false, $"未知 status={status}: {data?["message"]}");
                await Task.Delay(pollDelay ?? TimeSpan.FromSeconds(2), ct);
            }
            return (false, "逾時：無法取得 Job ID");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { return (false, e.Message); }
    }

    private static JsonNode? TryParse(string s)
    {
        try { return JsonNode.Parse(s); } catch { return null; }
    }

    /// <summary>組出 RequestPrivateGame 的 PlaceLauncher 網址。placeId 為 null 代表分享短連結。</summary>
    public static string BuildPlaceLauncherUrl(string tracker, long? placeId, string accessCode, string linkCode)
    {
        const string baseUrl = "https://assetgame.roblox.com/game/PlaceLauncher.ashx";
        var place = placeId is null ? "" : $"&placeId={placeId}";
        return $"{baseUrl}?request=RequestPrivateGame&browserTrackerId={tracker}{place}" +
               $"&accessCode={accessCode}&linkCode={linkCode}&isPlayTogetherGame=false";
    }
}
