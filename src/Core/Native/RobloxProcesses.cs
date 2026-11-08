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
                    var tracker = ParseCommandLine(cmd);
                    if (tracker is null) continue;
                    DateTime? start = null;
                    if (mo["CreationDate"] is string cd)
                    {
                        try { start = ManagementDateTimeConverter.ToDateTime(cd); } catch { }
                    }
                    result.Add(new RobloxInstance(Convert.ToInt32(mo["ProcessId"]), tracker, cmd ?? "", start));
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
