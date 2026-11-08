using System.Diagnostics;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Core.Native;

/// <summary>
/// 直接啟動 RobloxPlayerBeta.exe（繞過 roblox-player: 協定處理器 / Bootstrapper）。
/// 好處：避開 Froststrap / Bloxstrap 的確認視窗，並能拿到真正的 pid（追蹤、資源限制、視窗排列）。
/// 啟動前會先呼叫 <see cref="MultiRoblox.PrepareForLaunch"/>。
/// </summary>
public static class RobloxLauncher
{
    public const string ExeFileName = "RobloxPlayerBeta.exe";
    private static readonly string[] LocalRoots = ["Roblox", "Froststrap", "Bloxstrap", "Voidstrap", "Fishstrap"];

    /// <summary>測試用：覆寫找到的 exe。</summary>
    public static Func<string?>? FindExeOverride { get; set; }

    private static string? RunningRobloxExe()
    {
        foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
        {
            using (p)
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                }
                catch { /* 沒有權限讀取就跳過 */ }
            }
        }
        return null;
    }

    /// <summary>目前執行中的 Roblox 實例數（FPS 等旗標只對「之後才啟動」的實例生效，介面用它提醒使用者）。</summary>
    public static int RunningInstanceCount()
    {
        try
        {
            var procs = Process.GetProcessesByName("RobloxPlayerBeta");
            foreach (var p in procs) p.Dispose();
            return procs.Length;
        }
        catch { return 0; }
    }

    private static IEnumerable<string> InstalledCandidates()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var bases = new List<string>();
        if (!string.IsNullOrEmpty(local))
            bases.AddRange(LocalRoots.Select(r => Path.Combine(local, r, "Versions")));
        foreach (var pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") })
            if (!string.IsNullOrEmpty(pf)) bases.Add(Path.Combine(pf, "Roblox", "Versions"));

        foreach (var b in bases.Where(Directory.Exists))
            foreach (var dir in Directory.EnumerateDirectories(b))
            {
                var exe = Path.Combine(dir, ExeFileName);
                if (File.Exists(exe)) yield return exe;
            }
    }

    /// <summary>
    /// 找出 RobloxPlayerBeta.exe：設定指定的路徑 → 執行中 Roblox 所用的 exe → 各安裝位置「最近修改」的版本。
    /// </summary>
    public static string? FindRobloxExe()
    {
        if (FindExeOverride is not null) return FindExeOverride();

        var custom = AppSettings.Current.RobloxExePath.Trim().Trim('"');
        if (custom.Length > 0 && File.Exists(custom)) return custom;

        var running = RunningRobloxExe();
        if (running is not null) return running;

        return InstalledCandidates().OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    /// <summary>
    /// 啟動 Roblox 並載入 roblox-player: URI。回傳 (pid 或 null, 方式)。
    /// pid 為 null 表示改用了協定處理器，無法取得 pid。
    /// </summary>
    public static (int? Pid, string How) Launch(string launcherUri)
    {
        var settings = AppSettings.Current;
        var exe = settings.DirectLaunch ? FindRobloxExe() : null;

        MultiRoblox.PrepareForLaunch();

        // Roblox 只在啟動時讀 ClientAppSettings.json：先把 FPS 上限等旗標寫到這個 exe 旁邊（失敗不擋啟動）
        if (exe is not null) { try { FastFlags.ApplySaved(settings, exe); } catch { } }

        if (exe is null)
        {
            Process.Start(new ProcessStartInfo(launcherUri) { UseShellExecute = true });
            return (null, "protocol");
        }

        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(launcherUri);
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("無法啟動 Roblox");
        int pid = proc.Id;
        MultiRoblox.TrackPid(pid);

        _ = Task.Run(() => { try { ResourceLimits.ApplyDefaults(pid); } catch { } });
        try { WindowLayout.ScheduleLayout(pid); } catch { }
        return (pid, "direct");
    }
}
