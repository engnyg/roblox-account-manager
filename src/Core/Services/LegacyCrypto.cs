using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;
using Sodium;

namespace RobloxAccountManager.Core.Services;

/// <summary>
/// 讀寫 V2（以及原版 Roblox Account Manager）的加密帳號檔 accounts.dat：
/// 64 位元組標頭 + 16 位元組 Argon2 salt + 24 位元組 nonce + SecretBox（XSalsa20-Poly1305）密文，
/// 金鑰由 Argon2id（libsodium MODERATE）從密碼衍生。
/// </summary>
public static class LegacyCrypto
{
    public static readonly byte[] Header = Encoding.ASCII.GetBytes(
        "Roblox Account Manager created by ic3w0lf22 @ github.com .......");

    private const int SaltBytes = 16;
    private const int NonceBytes = 24;

    public static bool HasHeader(byte[] data)
    {
        if (data.Length < Header.Length) return false;
        for (int i = 0; i < Header.Length; i++)
            if (data[i] != Header[i]) return false;
        return true;
    }

    public static bool HasHeader(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var buf = new byte[Header.Length];
            return f.Read(buf, 0, buf.Length) == buf.Length && HasHeader(buf);
        }
        catch { return false; }
    }

    private static byte[] DeriveKey(byte[] password, byte[] salt) =>
        PasswordHash.ArgonHashBinary(password, salt, PasswordHash.StrengthArgon.Moderate, 32, PasswordHash.ArgonAlgorithm.Argon_2ID13);

    public static byte[] Encrypt(string content, string password)
    {
        if (content.Length == 0) throw new ArgumentException("content must not be empty");
        var pwd = Encoding.UTF8.GetBytes(password);
        var salt = PasswordHash.ArgonGenerateSalt();
        var key = DeriveKey(pwd, salt);
        var nonce = SecretBox.GenerateNonce();
        var cipher = SecretBox.Create(Encoding.UTF8.GetBytes(content), nonce, key);
        using var ms = new MemoryStream();
        ms.Write(Header);
        ms.Write(salt);
        ms.Write(nonce);
        ms.Write(cipher);
        return ms.ToArray();
    }

    /// <summary>解密，密碼錯誤或檔案損毀時丟出 <see cref="CryptographicException"/>。</summary>
    public static string Decrypt(byte[] data, string password)
    {
        if (!HasHeader(data)) throw new System.Security.Cryptography.CryptographicException("不是 Roblox Account Manager 的加密檔（標頭不符）");
        int off = Header.Length;
        if (data.Length < off + SaltBytes + NonceBytes + 16)
            throw new System.Security.Cryptography.CryptographicException("檔案長度不足");
        var salt = data[off..(off + SaltBytes)];
        var nonce = data[(off + SaltBytes)..(off + SaltBytes + NonceBytes)];
        var cipher = data[(off + SaltBytes + NonceBytes)..];
        var key = DeriveKey(Encoding.UTF8.GetBytes(password), salt);
        try
        {
            return Encoding.UTF8.GetString(SecretBox.Open(cipher, nonce, key));
        }
        catch (Exception e) when (e is not System.Security.Cryptography.CryptographicException)
        {
            throw new System.Security.Cryptography.CryptographicException("密碼錯誤或檔案已損毀", e);
        }
    }

    /// <summary>
    /// 把解密後的 JSON 轉成帳號。同時接受 V2 的 snake_case 欄位與原版 RAM 的 PascalCase 欄位
    /// （SecurityToken / Username / UserID / Alias / Description / Password / Group / Fields / LastUse）。
    /// </summary>
    public static List<Account> ParseAccounts(string json)
    {
        var result = new List<Account>();
        if (JsonNode.Parse(json) is not JsonArray arr) return result;
        foreach (var n in arr)
        {
            if (n is not JsonObject o) continue;
            string S(params string[] keys) => keys.Select(k => Find(o, k)?.ToString()).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
            var token = S("security_token", "SecurityToken");
            if (token.Length == 0) continue;

            var acc = new Account
            {
                SecurityToken = token,
                Username = S("username", "Username"),
                Alias = S("alias", "Alias"),
                Description = S("description", "Description"),
                Password = S("password", "Password"),
                Email = S("email", "Email"),
                EmailPassword = S("email_password", "EmailPassword"),
                BrowserTrackerId = S("browser_tracker_id", "BrowserTrackerID"),
            };
            var group = S("group", "Group");
            if (group.Length > 0) acc.Group = group;
            if (long.TryParse(S("user_id", "UserID", "UserId"), out var uid)) acc.UserId = uid;
            if (DateTime.TryParse(S("last_use", "LastUse"), out var lu)) acc.LastUse = lu;
            if ((Find(o, "fields") ?? Find(o, "Fields")) is JsonObject fields)
                foreach (var kv in fields) acc.Fields[kv.Key] = kv.Value?.ToString() ?? "";
            result.Add(acc);
        }
        return result;
    }

    private static JsonNode? Find(JsonObject o, string key)
    {
        foreach (var kv in o)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    public static List<Account> ImportFile(string path, string password) =>
        ParseAccounts(Decrypt(File.ReadAllBytes(path), password));

    /// <summary>以 V2 / 原版相容的格式寫出（測試與備份用）。</summary>
    public static void ExportFile(string path, IEnumerable<Account> accounts, string password) =>
        File.WriteAllBytes(path, Encrypt(JsonSerializer.Serialize(accounts), password));
}
