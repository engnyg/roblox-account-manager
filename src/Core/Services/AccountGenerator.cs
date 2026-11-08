using System.Net.Http.Json;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;

namespace RobloxAccountManager.Core.Services;

public enum GeneratorProvider { Unknown, BloxGen, Altgen }

/// <summary>一次生成的結果：成功取得的帳號，以及過程中遇到的錯誤（遇到錯誤就會停止，已取得的帳號仍會保留）。</summary>
public sealed record GenerateResult(List<Account> Accounts, string? Error);

/// <summary>
/// 向 BloxGen / Altgen 購買並取得 Roblox 帳號。移植自 MultiRoblox-RAM 的 Generator：
///  • BloxGen：POST https://core.bloxgen.net/api/generate，API key 放在 JSON 的 apiKey，一次一個帳號（key 以 "BLOX-" 開頭）。
///  • Altgen ：POST https://api.altgen.me/api/v1/generate，API key 放在 Authorization: Bearer，可一次要最多 100 個（key 以 "MG_" 開頭）。
/// API key 只會送到對應的服務，不會寫進錯誤訊息。
/// </summary>
public static class AccountGenerator
{
    public const string BloxGenUrl = "https://core.bloxgen.net/api/generate";
    public const string AltgenUrl = "https://api.altgen.me/api/v1/generate";
    public const int MaxCount = 100;

    /// <summary>BloxGen 的帳號種類（API 要的是這些原始字串，不能自己改寫）。</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> BloxGenTypes =
    [
        ("alt", "Alt"),
        ("+30 days old", "+30 days old"),
        ("+1 year old", "+1 year old"),
        ("5+ years old", "5+ years old"),
        ("dump", "Dump"),
    ];

    /// <summary>由 API key 的開頭判斷是哪一家。</summary>
    public static GeneratorProvider Detect(string? apiKey)
    {
        var k = (apiKey ?? "").Trim();
        if (k.StartsWith("BLOX-", StringComparison.Ordinal)) return GeneratorProvider.BloxGen;
        if (k.StartsWith("MG_", StringComparison.Ordinal)) return GeneratorProvider.Altgen;
        return GeneratorProvider.Unknown;
    }

    /// <summary>
    /// 生成 <paramref name="count"/> 個帳號。每取得一個帳號就立刻呼叫 <paramref name="onAccount"/>，
    /// 讓呼叫端馬上存檔（買到的帳號不能因為後面失敗或程式當掉而遺失）。
    /// </summary>
    public static async Task<GenerateResult> GenerateAsync(
        string apiKey, int count, string bloxGenType = "alt",
        Func<Account, Task>? onAccount = null, CancellationToken ct = default)
    {
        var key = (apiKey ?? "").Trim();
        count = Math.Clamp(count, 1, MaxCount);
        var provider = Detect(key);
        var accounts = new List<Account>();
        string? error = null;

        async Task Accept(Account a)
        {
            accounts.Add(a);
            if (onAccount is not null) await onAccount(a);
        }

        try
        {
            switch (provider)
            {
                case GeneratorProvider.BloxGen:
                    for (int i = 0; i < count && error is null; i++)
                    {
                        var (acc, err) = await BloxGenOneAsync(key, bloxGenType, ct);
                        if (acc is null) error = err;
                        else await Accept(acc);
                    }
                    break;

                case GeneratorProvider.Altgen:
                    var (list, e) = await AltgenAsync(key, count, ct);
                    foreach (var a in list) await Accept(a);
                    error = e;
                    break;

                default:
                    error = "Enter a valid API key (BloxGen: BLOX-... or Altgen: MG_...)";
                    break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { error = ex.Message; }

        return new GenerateResult(accounts, error);
    }

    // ------------------------------------------------------------------ //
    // BloxGen
    // ------------------------------------------------------------------ //

    private static async Task<(Account? Account, string? Error)> BloxGenOneAsync(string apiKey, string type, CancellationToken ct)
    {
        using var http = NewClient();
        using var resp = await http.PostAsJsonAsync(BloxGenUrl, new { apiKey, type = string.IsNullOrWhiteSpace(type) ? "alt" : type }, ct);
        var root = ParseJson(await resp.Content.ReadAsStringAsync(ct));
        if (root is null) return (null, $"HTTP {(int)resp.StatusCode}");
        if (root["success"]?.GetValue<bool>() != true)
            return (null, ErrorText(root) ?? $"HTTP {(int)resp.StatusCode}");
        return ToAccount(root["data"], "BloxGen") is { } acc ? (acc, null) : (null, "No account returned");
    }

    // ------------------------------------------------------------------ //
    // Altgen
    // ------------------------------------------------------------------ //

    private static async Task<(List<Account> Accounts, string? Error)> AltgenAsync(string apiKey, int quantity, CancellationToken ct)
    {
        using var http = NewClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, AltgenUrl)
        {
            Content = JsonContent.Create(new { type = "ROBLOX_NORMAL", quantity }),
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        using var resp = await http.SendAsync(req, ct);
        var root = ParseJson(await resp.Content.ReadAsStringAsync(ct));
        if (root is null) return ([], $"HTTP {(int)resp.StatusCode}");
        if (root["success"]?.GetValue<bool>() != true)
            return ([], ErrorText(root) ?? $"HTTP {(int)resp.StatusCode}");

        var list = new List<Account>();
        foreach (var item in root["data"]?["accounts"] as JsonArray ?? [])
            if (ToAccount(item, "Altgen") is { } acc) list.Add(acc);
        return (list, list.Count == 0 ? "No account returned" : null);
    }

    // ------------------------------------------------------------------ //
    // 共用
    // ------------------------------------------------------------------ //

    private static HttpClient NewClient()
    {
        var http = RobloxApi.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(60);   // 生成帳號比一般 API 慢
        return http;
    }

    private static JsonNode? ParseJson(string body)
    {
        try { return JsonNode.Parse(body) is JsonObject o ? o : null; }
        catch { return null; }
    }

    /// <summary>兩家的錯誤格式不同：message / error 可能是字串，Altgen 的 error 是 { message }。</summary>
    private static string? ErrorText(JsonNode root)
    {
        foreach (var n in new[] { root["message"], (root["error"] as JsonObject)?["message"], root["error"] })
        {
            if (n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static Account? ToAccount(JsonNode? n, string source)
    {
        var cookie = n?["cookie"]?.ToString().Trim() ?? "";
        if (cookie.Length == 0) return null;
        return new Account
        {
            SecurityToken = cookie,
            Username = n?["username"]?.ToString().Trim() ?? "",
            Password = n?["password"]?.ToString() ?? "",
            Group = "Generated",
            Description = source,
        };
    }
}
