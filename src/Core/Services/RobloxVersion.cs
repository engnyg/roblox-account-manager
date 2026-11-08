using System.Text.Json.Nodes;

namespace RobloxAccountManager.Core.Services;

/// <summary>從 Roblox 官方 clientsettingscdn 取得目前 Windows 玩家端的版本。</summary>
public static class RobloxVersion
{
    public static async Task<string?> GetLatestAsync(string channel = "", CancellationToken ct = default)
    {
        try
        {
            var url = "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer" +
                      (channel.Length > 0 ? $"/channel/{Uri.EscapeDataString(channel)}" : "");
            using var http = RobloxApi.CreateClient();
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var o = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
            return o?["clientVersionUpload"]?.ToString() ?? o?["version"]?.ToString();
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
}
