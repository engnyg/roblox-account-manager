using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RobloxAccountManager.Core.Services;

/// <summary>解析「Job ID」欄位的輸入，並用 Teleport-Controller 的 bot 回報校正 Place ID。</summary>
public static partial class JoinTarget
{
    private const string Uuid = "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";

    [GeneratedRegex(@"[?&]placeId=(\d+)", RegexOptions.IgnoreCase)] private static partial Regex PlaceQuery();
    [GeneratedRegex(@"/games/(\d+)")] private static partial Regex PlacePath();
    [GeneratedRegex("(?:gameInstanceId|jobId|gameId)=(" + Uuid + ")", RegexOptions.IgnoreCase)] private static partial Regex JobParam();
    [GeneratedRegex(Uuid, RegexOptions.IgnoreCase)] private static partial Regex AnyUuid();

    /// <summary>測試用：替換查詢 bot 的函式。</summary>
    public static Func<string, CancellationToken, Task<long?>>? BotLookupOverride { get; set; }

    /// <summary>
    /// 解析 Job ID 欄位。除了單純的 Job ID（UUID），也接受整條加入連結：
    /// roblox://experiences/start?placeId=1&amp;gameInstanceId=UUID、
    /// https://www.roblox.com/games/start?placeId=1&amp;gameInstanceId=UUID、
    /// https://www.roblox.com/games/1/Name?gameInstanceId=UUID。
    /// 連結裡有 placeId 時一併回傳，避免 Job ID 搭配到錯的 Place ID。
    /// </summary>
    public static (long? PlaceId, string JobId) Parse(string? text)
    {
        var raw = (text ?? "").Trim().Trim('"', '\'', '`', '<', '>').Trim();
        if (raw.Length == 0) return (null, "");

        long? place = null;
        var m = PlaceQuery().Match(raw);
        if (!m.Success) m = PlacePath().Match(raw);
        if (m.Success) place = long.Parse(m.Groups[1].Value);

        m = JobParam().Match(raw);
        if (m.Success) return (place, m.Groups[1].Value.ToLowerInvariant());
        m = AnyUuid().Match(raw);
        if (m.Success) return (place, m.Value.ToLowerInvariant());

        // 不是 UUID 格式就原樣交給後續流程（讓 API 回報真正的錯誤）
        return (place, raw);
    }

    private static async Task<long?> LookupBotPlaceAsync(string jobId, CancellationToken ct)
    {
        if (BotLookupOverride is not null) return await BotLookupOverride(jobId, ct);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var body = await http.GetStringAsync(AppSettings.Current.TeleportControllerUrl, ct);
            if (JsonNode.Parse(body)?["bots"] is JsonArray bots)
                foreach (var bot in bots)
                {
                    var j = bot?["jobId"]?.ToString() ?? "";
                    if (string.Equals(j, jobId, StringComparison.OrdinalIgnoreCase) && bot?["placeId"] is { } p
                        && long.TryParse(p.ToString(), out var place) && place > 0)
                        return place;
                }
        }
        catch { /* 控制伺服器沒開或查不到就略過 */ }
        return null;
    }

    /// <summary>整理加入目標：解析貼上的連結 → 再用 bot 回報的 placeId 校正。</summary>
    public static async Task<(long? PlaceId, string JobId)> NormalizeAsync(long? placeId, string? jobText, CancellationToken ct = default)
    {
        var (linkPlace, jobId) = Parse(jobText);
        if (linkPlace is not null) placeId = linkPlace;
        if (jobId.Length > 0)
        {
            var botPlace = await LookupBotPlaceAsync(jobId, ct);
            if (botPlace is not null && botPlace != placeId) placeId = botPlace;
        }
        return (placeId, jobId);
    }
}
