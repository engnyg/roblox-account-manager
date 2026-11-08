using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;

namespace RobloxAccountManager.Core.Services;

/// <summary>
/// 帳號儲存。V3 用 Windows DPAPI（目前使用者）加密整份帳號 JSON，因此 cookie 不會以明文躺在磁碟上；
/// 同時可匯入 V2 的 accounts.dat.json、產生器的 cookies.json、以及每行一個的 .ROBLOSECURITY 字串。
/// 也可改用自訂密碼（AES-256-GCM）。V2 / 原版 RAM 的 Argon2 加密 accounts.dat 可用 <see cref="LegacyCrypto"/> 匯入。
/// </summary>
public static class AccountStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RobloxAccountManagerV3");
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppSettings.DataDir, "accounts.v3.dat");

    // ---- 加密模式 ----
    // 預設用 Windows DPAPI（綁定目前使用者）。也可改用自訂密碼：PBKDF2-SHA256(210000 次) 衍生金鑰 + AES-256-GCM。
    // 密碼檔以 "RAM3PW1" 開頭，所以載入時可以自動判斷是哪一種。
    private static readonly byte[] PwMagic = Encoding.ASCII.GetBytes("RAM3PW1");
    private const int Pbkdf2Iterations = 210_000;
    private static string? _password;

    /// <summary>目前是否使用自訂密碼加密。</summary>
    public static bool UsesPassword => _password is not null;

    /// <summary>磁碟上的帳號檔是否是密碼加密的（需要先 <see cref="Unlock"/>）。</summary>
    public static bool IsPasswordProtected()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            using var f = File.OpenRead(FilePath);
            var buf = new byte[PwMagic.Length];
            return f.Read(buf, 0, buf.Length) == buf.Length && buf.AsSpan().SequenceEqual(PwMagic);
        }
        catch { return false; }
    }

    /// <summary>用密碼解鎖。密碼正確回傳 true，之後的 Save 會沿用此密碼。</summary>
    public static bool Unlock(string password)
    {
        if (!File.Exists(FilePath)) { _password = password; return true; }
        try
        {
            DecryptWithPassword(File.ReadAllBytes(FilePath), password);
            _password = password;
            return true;
        }
        catch { return false; }
    }

    /// <summary>切換加密方式：傳入 null = 改回 DPAPI。呼叫端之後要再 Save 一次才會重新加密磁碟檔案。</summary>
    public static void SetPassword(string? password) => _password = string.IsNullOrEmpty(password) ? null : password;

    /// <summary>測試用：回到預設狀態。</summary>
    internal static void ResetProtection() => _password = null;

    /// <summary>刪除所有帳號資料（「清除資料」）。</summary>
    public static void ClearAll()
    {
        lock (Gate)
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        _password = null;
    }

    private static byte[] EncryptWithPassword(byte[] plain, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        return [.. PwMagic, .. salt, .. nonce, .. tag, .. cipher];
    }

    private static byte[] DecryptWithPassword(byte[] blob, string password)
    {
        int o = PwMagic.Length;
        if (blob.Length < o + 16 + 12 + 16 || !blob.AsSpan(0, o).SequenceEqual(PwMagic))
            throw new CryptographicException("不是密碼加密的帳號檔");
        var salt = blob[o..(o + 16)];
        var nonce = blob[(o + 16)..(o + 28)];
        var tag = blob[(o + 28)..(o + 44)];
        var cipher = blob[(o + 44)..];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    public static void Save(IEnumerable<Account> accounts)
    {
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(accounts, Json));
        var blob = _password is { } pw
            ? EncryptWithPassword(plain, pw)
            : ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        lock (Gate)
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, blob);
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    public static List<Account> Load()
    {
        if (!File.Exists(FilePath)) return new List<Account>();
        try
        {
            var blob = File.ReadAllBytes(FilePath);
            var plain = blob.AsSpan().StartsWith(PwMagic)
                ? (_password is { } pw ? DecryptWithPassword(blob, pw) : throw new CryptographicException("需要密碼"))
                : ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<Account>>(Encoding.UTF8.GetString(plain), Json) ?? new();
        }
        catch
        {
            return new List<Account>();
        }
    }

    // ------------------------------------------------------------------ //
    // 匯入 / 匯出
    // ------------------------------------------------------------------ //

    /// <summary>匯入 V2 純 JSON 帳號檔（accounts.dat.json）。</summary>
    public static List<Account> ImportV2Json(string path)
    {
        var list = JsonSerializer.Deserialize<List<Account>>(File.ReadAllText(path), Json) ?? new();
        return list.Where(a => a.SecurityToken.Length > 0).ToList();
    }

    /// <summary>匯入產生器輸出的 cookies.json。</summary>
    public static List<Account> ImportCookiesJson(string path)
    {
        var result = new List<Account>();
        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonArray arr) return result;
        foreach (var entry in arr)
        {
            var cookie = (entry?["cookies"] as JsonArray)?
                .FirstOrDefault(c => c?["name"]?.ToString() == ".ROBLOSECURITY")?["value"]?.ToString() ?? "";
            if (cookie.Length == 0) continue;
            result.Add(new Account
            {
                SecurityToken = cookie,
                Username = entry?["username"]?.ToString() ?? "",
                Password = entry?["password"]?.ToString() ?? "",
            });
        }
        return result;
    }

    /// <summary>自動判斷檔案類型匯入（V2 JSON / cookies.json）。</summary>
    public static List<Account> ImportFile(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path));
        if (node is JsonArray { Count: > 0 } arr && arr[0] is JsonObject first)
            return first.ContainsKey("cookies") ? ImportCookiesJson(path) : ImportV2Json(path);
        return new List<Account>();
    }

    /// <summary>每行一個 .ROBLOSECURITY 字串；逐一向 Roblox 查詢用戶名。</summary>
    public static async Task<(List<Account> Accounts, int Failed)> ImportCookieLinesAsync(
        IEnumerable<string> lines, CancellationToken ct = default)
    {
        var accounts = new List<Account>();
        int failed = 0;
        foreach (var raw in lines)
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            var acc = new Account { SecurityToken = token };
            if (await RobloxApi.FetchInfoAsync(acc, ct)) accounts.Add(acc);
            else failed++;
        }
        return (accounts, failed);
    }

    public static string ExportCookies(IEnumerable<Account> accounts) =>
        string.Join(Environment.NewLine, accounts.Where(a => a.SecurityToken.Length > 0).Select(a => a.SecurityToken));

    /// <summary>把匯入的帳號併入現有清單：同用戶名的更新 cookie，其餘新增。回傳 (新增數, 更新數)。</summary>
    public static (int Added, int Updated) Merge(IList<Account> existing, IEnumerable<Account> incoming)
    {
        int added = 0, updated = 0;
        foreach (var inc in incoming)
        {
            var match = inc.Username.Length == 0 ? null
                : existing.FirstOrDefault(a => string.Equals(a.Username, inc.Username, StringComparison.OrdinalIgnoreCase));
            if (match is null) { existing.Add(inc); added++; }
            else { match.SecurityToken = inc.SecurityToken; updated++; }
        }
        return (added, updated);
    }
}
