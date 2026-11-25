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
        // 若這個行程只是把請求交給常駐托盤行程就結束了，真正的遊戲在另一個 pid：找到後也套用限制與排版
        _ = Task.Run(() => FollowHandoff(pid, TrackerOfUri(launcherUri)));
        return (pid, "direct");
    }

    internal static string? TrackerOfUri(string uri)
    {
        var m = System.Text.RegularExpressions.Regex.Match(uri, @"browsertrackerid:(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>啟動的行程很快就結束（交給常駐行程了）時，等 tracker 相同的實例出現，對它套用限制與視窗排版。</summary>
    private static void FollowHandoff(int launchedPid, string? tracker)
    {
        if (string.IsNullOrEmpty(tracker)) return;
        try
        {
            // 先確認啟動的行程是不是很快就結束；沒結束就代表它自己就是遊戲，不用處理
            var until = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < until && IsRunning(launchedPid)) Thread.Sleep(500);
            if (IsRunning(launchedPid)) return;

            var give = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < give)
            {
                var inst = RobloxProcesses.List().FirstOrDefault(i => i.TrackerId == tracker);
                if (inst is not null)
                {
                    try { MultiRoblox.TrackPid(inst.Pid); } catch { }
                    try { ResourceLimits.ApplyDefaults(inst.Pid); } catch { }
                    try { WindowLayout.ScheduleLayout(inst.Pid); } catch { }
                    return;
                }
                Thread.Sleep(1000);
            }
        }
        catch { }
    }

    private static bool IsRunning(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }
}
