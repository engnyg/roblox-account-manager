using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace RobloxAccountManager.Core.Native;

public sealed record RobloxInstance(int Pid, string TrackerId, string CommandLine, DateTime? StartTime);

public static partial class RobloxProcesses
{
    public const string ExeName = "RobloxPlayerBeta.exe";

    [GeneratedRegex(@"-b (\d+)")] private static partial Regex DashB();
    [GeneratedRegex(@"browsertrackerid:(\d+)", RegexOptions.IgnoreCase)] private static partial Regex UriTracker();

    /// <summary>
    /// 判斷是否為「主要」Roblox 遊戲行程，回傳 tracker id（可為空字串）；不是則回傳 null。
    /// 兩種形式：
    ///  - 經 bootstrapper / 協定處理器：`... -t &lt;ticket&gt; -j &lt;url&gt; -b &lt;tracker&gt; ...`
    ///  - 直接以 URI 啟動：`RobloxPlayerBeta.exe roblox-player:1+...+browsertrackerid:&lt;tracker&gt;+...`
    /// </summary>
    public static string? ParseCommandLine(string? cmdline)
    {
        if (string.IsNullOrEmpty(cmdline) || cmdline.StartsWith(@"\??\", StringComparison.Ordinal)) return null;
        if (cmdline.Contains("-t ") || cmdline.Contains("-j "))
        {
            var m = DashB().Match(cmdline);
            return m.Success ? m.Groups[1].Value : "";
        }
        if (cmdline.Contains("roblox-player:", StringComparison.Ordinal))
        {
            var m = UriTracker().Match(cmdline);
            return m.Success ? m.Groups[1].Value : "";
        }
        return null;
    }

    // ------------------------------------------------------------------ //
    // Roblox 0.746 的「常駐托盤行程」：
    // 遊戲結束後 Roblox 會留一個 `RobloxPlayerBeta.exe --launch-to-tray` 在背景；之後我們啟動的行程（命令列有 roblox-player:）
    // 只是中轉，把加入請求交給它就結束了，真正的遊戲跑在這個常駐行程裡，命令列看不出 tracker。
    // 這時改成：有視窗的 --launch-to-tray 行程算實例，tracker 從它的 Roblox 日誌讀（"web launch, websiteBTId is N"）。
    // ------------------------------------------------------------------ //

    [GeneratedRegex(@"websiteBTId is (\d+)")] private static partial Regex WebBtId();
    [GeneratedRegex(@"browserid=(\d+)")] private static partial Regex CookieBtId();
    [GeneratedRegex(@"_(\d{8}T\d{6})Z_Player_")] private static partial Regex LogStamp();

    /// <summary>Roblox 日誌資料夾；測試可替換。</summary>
    public static string? LogsDirOverride { get; set; }
    private static string LogsDir => LogsDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

    public static bool IsTrayHost(string? cmdline) =>
        !string.IsNullOrEmpty(cmdline) && cmdline.Contains("--launch-to-tray", StringComparison.Ordinal);

    /// <summary>從日誌文字取出最近一次加入用的 tracker（先找 web launch，再找 cookie 那行）。</summary>
    public static string? TrackerFromLogText(string text)
    {
        string? last = null;
        foreach (Match m in WebBtId().Matches(text)) last = m.Groups[1].Value;
        if (last is not null) return last;
        foreach (Match m in CookieBtId().Matches(text)) last = m.Groups[1].Value;
        return last;
    }

    private static readonly Dictionary<string, (DateTime At, long Len, string? Tracker)> LogCache = new();

    /// <summary>找出啟動時間最接近 start 的 Player 日誌（檔名時間是 UTC，通常比行程啟動晚 1~3 秒），讀出 tracker。</summary>
    public static string? TrackerFromHostLog(DateTime startLocal)
    {
        try
        {
            if (!Directory.Exists(LogsDir)) return null;
            var startUtc = startLocal.ToUniversalTime();
            string? best = null; double bestGap = double.MaxValue;
            foreach (var f in Directory.EnumerateFiles(LogsDir, "*_Player_*_last.log"))
            {
                var name = Path.GetFileName(f);
                if (name.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase)) continue;
                var m = LogStamp().Match(name);
                if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd'T'HHmmss", null,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var t)) continue;
                var gap = (t - startUtc).TotalSeconds;
                if (gap < -3 || gap > 20) continue;
                if (Math.Abs(gap) < bestGap) { best = f; bestGap = Math.Abs(gap); }
            }
            if (best is null) return null;

            var info = new FileInfo(best);
            lock (LogCache)
                if (LogCache.TryGetValue(best, out var c) && c.Len == info.Length && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(30)) return c.Tracker;

            using var fs = new FileStream(best, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string text;
            using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
            var tracker = TrackerFromLogText(text);
            lock (LogCache) LogCache[best] = (DateTime.UtcNow, info.Length, tracker);
            return tracker;
        }
        catch { return null; }
    }

    private static bool HasWindow(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.MainWindowHandle != IntPtr.Zero; }
        catch { return false; }
    }

    /// <summary>列出執行中的 Roblox 遊戲行程。</summary>
    public static List<RobloxInstance> List()
    {
        var result = new List<RobloxInstance>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine, CreationDate FROM Win32_Process WHERE Name = '" + ExeName + "'");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var cmd = mo["CommandLine"] as string;
                    var pid = Convert.ToInt32(mo["ProcessId"]);
                    var tracker = ParseCommandLine(cmd);
                    DateTime? start = null;
                    if (mo["CreationDate"] is string cd)
                    {
                        try { start = ManagementDateTimeConverter.ToDateTime(cd); } catch { }
                    }
                    // 常駐托盤行程：有視窗才代表真的在玩（閒置時沒有視窗）
                    if (tracker is null && IsTrayHost(cmd) && HasWindow(pid))
                        tracker = (start is { } st ? TrackerFromHostLog(st) : null) ?? "";
                    if (tracker is null) continue;
                    result.Add(new RobloxInstance(pid, tracker, cmd ?? "", start));
                }
            }
        }
        catch
        {
            // WMI 不可用時退回只列行程（沒有 tracker）
            foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
                using (p) result.Add(new RobloxInstance(p.Id, "", "", null));
        }
        return result;
    }

    public static double MemoryMb(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.WorkingSet64 / 1048576.0; }
        catch { return 0; }
    }

    /// <summary>結束所有 Roblox 行程，回傳數量。</summary>
    public static int KillAll()
    {
        int n = 0;
        foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            using (p)
            {
                try { p.Kill(); n++; } catch { }
            }
        }
        return n;
    }
}
