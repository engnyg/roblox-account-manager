using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;

namespace RobloxAccountManager.Core.Services;

/// <summary>透過公開的 thumbnails API 取得帳號大頭貼網址（不需要 cookie）。</summary>
public static class AvatarService
{
    public const string Endpoint = "https://thumbnails.roblox.com/v1/users/avatar-headshot";
    private const int BatchSize = 100;

    /// <summary>批次查詢大頭貼網址；回傳「userId → 圖片網址」，審核中 / 失敗的不會出現在結果裡。</summary>
    public static async Task<Dictionary<long, string>> FetchHeadshotsAsync(IEnumerable<long> userIds, string size = "75x75", CancellationToken ct = default)
    {
        var result = new Dictionary<long, string>();
        var ids = userIds.Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0) return result;

        using var http = RobloxApi.CreateClient();
        foreach (var batch in ids.Chunk(BatchSize))
        {
            try
            {
                var url = $"{Endpoint}?userIds={string.Join(',', batch)}&size={size}&format=Png&isCircular=false";
                using var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) continue;
                if (JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct)) is not JsonObject root || root["data"] is not JsonArray data) continue;
                foreach (var item in data)
                {
                    if (item is null) continue;
                    var state = item["state"]?.GetValue<string>();
                    var image = item["imageUrl"]?.GetValue<string>();
                    long? id = item["targetId"]?.GetValue<long>();
                    if (id is not null && state == "Completed" && !string.IsNullOrEmpty(image)) result[id.Value] = image;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 網路錯誤：這一批沒有頭像，下次再試 */ }
        }
        return result;
    }

    /// <summary>替還沒有頭像的帳號補上 <see cref="Account.AvatarUrl"/>，回傳補上的數量。</summary>
    public static async Task<int> FillAsync(IEnumerable<Account> accounts, CancellationToken ct = default)
    {
        var need = accounts.Where(a => a.UserId > 0 && a.AvatarUrl.Length == 0).ToList();
        if (need.Count == 0) return 0;
        var map = await FetchHeadshotsAsync(need.Select(a => a.UserId), ct: ct);
        int n = 0;
        foreach (var a in need)
            if (map.TryGetValue(a.UserId, out var url)) { a.AvatarUrl = url; n++; }
        return n;
    }
}
