using System.Text.Json.Nodes;

namespace RobloxAccountManager.Core.Services;

public sealed record ServerInfo(string Id, int Playing, int MaxPlayers, double Ping, double Fps)
{
    public string Occupancy => $"{Playing}/{MaxPlayers}";
}

public sealed record ChartGame(string Name, long PlaceId, long UniverseId, long Playing);

public sealed record ChartSort(string Name, List<ChartGame> Games);

/// <summary>主視窗「目的地」卡片用的遊戲資訊。</summary>
public sealed record PlaceInfo(long PlaceId, long UniverseId, string Name, long Playing, string IconUrl);

/// <summary>
/// 伺服器清單（公開伺服器）與遊戲探索排行榜。
/// 排行榜用的是 Roblox 未公開文件的內部端點，欄位可能隨時變動，所以做防禦性解析並保留舊版 API 當 fallback。
/// </summary>
public static class GameDiscovery
{
    private const string GamesApi = "https://games.roblox.com";
    private const string ExploreApi = "https://apis.roblox.com/explore-api/v1";

    private static async Task<JsonNode?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var http = RobloxApi.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    // ------------------------------------------------------------------ //
    // 伺服器清單
    // ------------------------------------------------------------------ //

    public static ServerInfo? ParseServer(JsonNode? n)
    {
        var id = n?["id"]?.ToString();
        if (string.IsNullOrEmpty(id)) return null;
        return new ServerInfo(id, ToInt(n!["playing"]), ToInt(n["maxPlayers"]), ToDouble(n["ping"]), ToDouble(n["fps"]));
    }

    /// <summary>取得公開伺服器（最多 maxPages 頁，每頁 100 台）。</summary>
    public static async Task<List<ServerInfo>> FetchServersAsync(long placeId, int maxPages = 5, CancellationToken ct = default)
    {
        var all = new List<ServerInfo>();
        string cursor = "";
        for (int i = 0; i < maxPages; i++)
        {
            var url = $"{GamesApi}/v1/games/{placeId}/servers/Public?sortOrder=Asc&limit=100" +
                      (cursor.Length > 0 ? $"&cursor={Uri.EscapeDataString(cursor)}" : "");
            var json = await GetJsonAsync(url, ct);
            if (json?["data"] is not JsonArray data) break;
            foreach (var s in data)
                if (ParseServer(s) is { } info) all.Add(info);
            cursor = json["nextPageCursor"]?.ToString() ?? "";
            if (cursor.Length == 0) break;
        }
        return all;
    }

    /// <summary>取得遊戲名稱（用來在伺服器清單視窗顯示）。</summary>
    public static async Task<string?> GetPlaceNameAsync(long placeId, CancellationToken ct = default)
        => (await GetPlaceInfoAsync(placeId, ct))?.Name;

    private static readonly Dictionary<long, PlaceInfo> PlaceCache = new();

    /// <summary>
    /// 用不需要登入的公開 API 取得遊戲資訊：place → universe → 名稱 / 人數，另外查遊戲圖示。
    /// （multiget-place-details 需要 cookie，未登入會回 401，所以不用它。）成功的結果會快取。
    /// </summary>
    public static async Task<PlaceInfo?> GetPlaceInfoAsync(long placeId, CancellationToken ct = default)
    {
        if (placeId <= 0) return null;
        lock (PlaceCache) if (PlaceCache.TryGetValue(placeId, out var cached)) return cached;

        var uni = await GetJsonAsync($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct);
        long universe = ToLong(uni?["universeId"]);
        if (universe <= 0) return null;

        var gamesTask = GetJsonAsync($"{GamesApi}/v1/games?universeIds={universe}", ct);
        var iconTask = GetJsonAsync($"https://thumbnails.roblox.com/v1/places/gameicons?placeIds={placeId}&returnPolicy=PlaceHolder&size=150x150&format=Png&isCircular=false", ct);
        await Task.WhenAll(gamesTask, iconTask);

        var game = (gamesTask.Result?["data"] as JsonArray)?.FirstOrDefault();
        var name = game?["name"]?.ToString() ?? "";
        if (name.Length == 0) return null;
        var iconNode = (iconTask.Result?["data"] as JsonArray)?.FirstOrDefault();
        var icon = iconNode?["state"]?.ToString() == "Completed" ? iconNode["imageUrl"]?.ToString() ?? "" : "";

        var info = new PlaceInfo(placeId, universe, name, ToLong(game!["playing"]), icon);
        lock (PlaceCache) PlaceCache[placeId] = info;
        return info;
    }

    /// <summary>測試用：清除遊戲資訊快取。</summary>
    internal static void ClearPlaceCache() { lock (PlaceCache) PlaceCache.Clear(); }

    // ------------------------------------------------------------------ //
    // 排行榜
    // ------------------------------------------------------------------ //

    /// <summary>把不同 API 版本的欄位名稱統一。</summary>
    public static ChartGame NormalizeGame(JsonNode raw)
    {
        long universe = ToLong(raw["universeId"]) is var u and > 0 ? u : ToLong(raw["id"]);
        long place = FirstPositive(ToLong(raw["rootPlaceId"]), ToLong(raw["placeId"]), ToLong(raw["gameId"]), universe);
        string name = FirstNonEmpty(raw["name"], raw["gameName"], raw["title"]);
        long playing = FirstPositive(ToLong(raw["playerCount"]), ToLong(raw["playing"]), ToLong(raw["totalPlayerCount"]));
        return new ChartGame(name, place, universe, playing);
    }

    public static async Task<List<ChartSort>> FetchChartsAsync(int maxSorts = 8, int gamesPerSort = 20, CancellationToken ct = default)
    {
        var session = Guid.NewGuid().ToString();
        var result = new List<ChartSort>();

        var sorts = (await GetJsonAsync($"{ExploreApi}/get-sorts?sessionId={session}", ct))?["sorts"] as JsonArray;
        if (sorts is { Count: > 0 })
        {
            foreach (var s in sorts.Take(maxSorts))
            {
                var sortId = FirstNonEmpty(s?["sortId"], s?["id"]);
                var sortName = FirstNonEmpty(s?["sortDisplayName"], s?["displayName"], sortId);
                if (sortId.Length == 0) continue;
                var content = await GetJsonAsync($"{ExploreApi}/get-sort-content?sessionId={session}&sortId={Uri.EscapeDataString(sortId)}", ct);
                var games = ToGames(content?["games"] as JsonArray, gamesPerSort);
                if (games.Count > 0) result.Add(new ChartSort(sortName, games));
            }
            if (result.Count > 0) return result;
        }

        // Fallback：舊版（已淘汰但可能仍可用）
        var legacy = (await GetJsonAsync($"{GamesApi}/v1/games/sorts?gameSortsContext=GamesDefaultSorts", ct))?["sorts"] as JsonArray;
        if (legacy is null) return result;
        foreach (var s in legacy.Take(maxSorts))
        {
            var token = FirstNonEmpty(s?["token"], s?["sortId"]);
            var sortName = FirstNonEmpty(s?["sortDisplayName"], s?["name"], token);
            if (token.Length == 0) continue;
            var content = await GetJsonAsync($"{GamesApi}/v1/games/list?sortToken={Uri.EscapeDataString(token)}&limit={gamesPerSort}", ct);
            var games = ToGames(content?["games"] as JsonArray, gamesPerSort);
            if (games.Count > 0) result.Add(new ChartSort(sortName, games));
        }
        return result;
    }

    private static List<ChartGame> ToGames(JsonArray? arr, int limit) =>
        (arr ?? []).Where(g => g is not null).Take(limit).Select(g => NormalizeGame(g!)).Where(g => g.PlaceId > 0).ToList();

    // ------------------------------------------------------------------ //

    private static long ToLong(JsonNode? n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<double>(out var d)) return (long)d;
            if (v.TryGetValue<string>(out var s) && long.TryParse(s, out var p)) return p;
        }
        return 0;
    }

    private static int ToInt(JsonNode? n) => (int)ToLong(n);

    private static double ToDouble(JsonNode? n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<long>(out var l)) return l;
        }
        return 0;
    }

    private static long FirstPositive(params long[] values) => values.FirstOrDefault(x => x > 0);

    private static string FirstNonEmpty(params object?[] nodes) =>
        nodes.Select(n => n?.ToString() ?? "").FirstOrDefault(s => s.Length > 0) ?? "";
}
