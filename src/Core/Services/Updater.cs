using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace RobloxAccountManager.Core.Services;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string AssetUrl, string AssetName, long Size, string? Sha256);

/// <summary>
/// 自動更新：查 GitHub Releases 最新版 → 下載 zip → 驗證 → 由獨立腳本在程式結束後覆蓋檔案並重新啟動。
/// 只信任 https://github.com/{Repo}/releases/download/ 底下的檔案；Release 有附 SHA-256 時一定驗證。
/// 帳號與設定存在 %LOCALAPPDATA%，不在程式資料夾，更新不會動到。
/// </summary>
public static class Updater
{
    public const string Repo = "engnyg/roblox-account-manager";
    public const string ExeName = "RobloxAccountManagerV3.exe";
    public const string AssetPrefix = "RobloxAccountManagerV3";

    /// <summary>測試用：替換 HTTP 處理器。</summary>
    public static HttpMessageHandler? HandlerOverride { get; set; }

    private static HttpClient NewClient(TimeSpan timeout)
    {
        var http = HandlerOverride is null ? new HttpClient() : new HttpClient(HandlerOverride, disposeHandler: false);
        http.Timeout = timeout;
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RobloxAccountManagerV3", CurrentVersion.ToString(3)));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static Version CurrentVersion
    {
        get
        {
            // 版本號寫在 App 專案（<Version>），Core 的組件版本只是預設值
            var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return v is null ? new Version(0, 0, 0) : new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
        }
    }

    /// <summary>"v3.1.0" / "3.1.0-beta" → 3.1.0。</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var s = tag.Trim().TrimStart('v', 'V');
        int dash = s.IndexOfAny(['-', '+']);
        if (dash >= 0) s = s[..dash];
        if (!Version.TryParse(s, out var v) || v.Major < 0) return false;
        version = new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
        return true;
    }

    public static bool IsTrustedAssetUrl(string? url) =>
        url is not null && url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);

    /// <summary>有比 current 新的正式版就回傳，否則 null（含：沒有 Release、沒有合格的 zip、網路失敗）。</summary>
    public static async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        try
        {
            using var http = NewClient(TimeSpan.FromSeconds(15));
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
            return Parse(JsonNode.Parse(json), current);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static UpdateInfo? Parse(JsonNode? release, Version current)
    {
        if (release is null || release["draft"]?.GetValue<bool>() == true || release["prerelease"]?.GetValue<bool>() == true) return null;
        var tag = release["tag_name"]?.ToString();
        if (!TryParseVersion(tag, out var version) || version <= current) return null;
        foreach (var a in release["assets"] as JsonArray ?? [])
        {
            var name = a?["name"]?.ToString() ?? "";
            var url = a?["browser_download_url"]?.ToString();
            if (!name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsTrustedAssetUrl(url)) continue;
            string? sha = null;
            var digest = a?["digest"]?.ToString();
            if (digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) sha = digest[7..].Trim().ToLowerInvariant();
            long size = long.TryParse(a?["size"]?.ToString(), out var sz) ? sz : 0;
            return new UpdateInfo(version, tag!, release["body"]?.ToString() ?? "", url!, name, size, sha);
        }
        return null;
    }

    /// <summary>下載到暫存資料夾並驗證（SHA-256、zip 內有主程式）。回傳解壓後的資料夾。</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsTrustedAssetUrl(info.AssetUrl)) throw new InvalidOperationException("更新檔來源不受信任");
        var work = Path.Combine(Path.GetTempPath(), "RAMv3_update_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            var zip = Path.Combine(work, "update.zip");
            using (var http = NewClient(TimeSpan.FromMinutes(10)))
            using (var resp = await http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? info.Size;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(zip);
                var buf = new byte[81920]; long done = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
            if (info.Sha256 is not null)
            {
                await using var fs = File.OpenRead(zip);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
                if (actual != info.Sha256) throw new InvalidDataException("更新檔的 SHA-256 與 Release 不符，已放棄");
            }
            var extracted = Path.Combine(work, "files");
            ZipFile.ExtractToDirectory(zip, extracted);   // 內建會擋掉 ../ 的路徑
            // zip 可能多包一層資料夾
            var root = File.Exists(Path.Combine(extracted, ExeName)) ? extracted
                : Directory.GetDirectories(extracted).FirstOrDefault(d => File.Exists(Path.Combine(d, ExeName)))
                  ?? throw new InvalidDataException("更新檔裡找不到 " + ExeName);
            File.Delete(zip);
            return root;
        }
        catch
        {
            try { Directory.Delete(work, true); } catch { }
            throw;
        }
    }

    /// <summary>產生安裝腳本內容（獨立出來方便測試）。</summary>
    public static string BuildScript(string sourceDir, string installDir, int pid, string exeName = ExeName)
    {
        foreach (var p in new[] { sourceDir, installDir })
            if (p.IndexOfAny(['%', '^', '!', '"', '&', '|', '<', '>']) >= 0)
                throw new InvalidOperationException("安裝路徑含有特殊字元，無法自動更新：" + p);
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal");
        sb.AppendLine("set /a tries=0");
        sb.AppendLine(":wait");
        sb.AppendLine($"tasklist /FI \"PID eq {pid}\" /FI \"IMAGENAME eq {exeName}\" /NH 2>nul | find /I \"{exeName}\" >nul");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  set /a tries+=1");
        sb.AppendLine("  if %tries% GEQ 60 goto restart");
        sb.AppendLine("  timeout /t 1 /nobreak >nul");
        sb.AppendLine("  goto wait");
        sb.AppendLine(")");
        sb.AppendLine($"robocopy \"{sourceDir}\" \"{installDir}\" /E /IS /IT /R:10 /W:1 /NFL /NDL /NJH /NJS >nul");
        sb.AppendLine(":restart");
        sb.AppendLine($"start \"\" \"{Path.Combine(installDir, exeName)}\"");
        sb.AppendLine($"rmdir /S /Q \"{Path.GetDirectoryName(sourceDir.TrimEnd('\\'))}\" >nul 2>&1");
        sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
        return sb.ToString();
    }

    /// <summary>啟動安裝腳本；呼叫後主程式必須立刻結束。</summary>
    public static void LaunchInstaller(string sourceDir, string installDir, int pid)
    {
        var script = Path.Combine(Path.GetTempPath(), "RAMv3_update_" + Guid.NewGuid().ToString("N")[..8] + ".cmd");
        File.WriteAllText(script, BuildScript(sourceDir, installDir.TrimEnd('\\'), pid), Encoding.Default);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }

    /// <summary>開發環境（bin\Debug 等）不自動更新，免得覆蓋到編譯輸出。</summary>
    public static bool IsInstalledBuild(string baseDir) =>
        !baseDir.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) &&
        !baseDir.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase) &&
        Environment.GetEnvironmentVariable("RAM_V3_NO_UPDATE") != "1";
}
