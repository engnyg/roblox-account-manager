using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Core.Native;

/// <summary>
/// 行程監控：定期檢查 Roblox 行程，記憶體過低（通常代表卡在載入畫面或已當掉）的實例自動結束。
/// 剛啟動 30 秒內的實例不處理。對應 V2 manager/roblox_watcher.py。
/// </summary>
public sealed class RobloxWatcher : IDisposable
{
    public bool CloseIfMemoryLow { get; set; }
    public int MemoryLowMb { get; set; } = 200;
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>測試用：替換列出行程 / 讀取記憶體 / 結束行程。</summary>
    public Func<List<RobloxInstance>> ListOverride { get; set; } = RobloxProcesses.List;
    public Func<int, double> MemoryOverride { get; set; } = RobloxProcesses.MemoryMb;
    public Action<int> KillOverride { get; set; } = pid =>
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); p.Kill(); } catch { }
    };

    public event Action<int, string>? Killed;

    private Timer? _timer;
    private int _busy;

    public static RobloxWatcher FromSettings(AppSettings s) => new()
    {
        CloseIfMemoryLow = s.WatcherCloseIfMemoryLow,
        MemoryLowMb = Math.Max(1, s.WatcherMemoryLowMb),
    };

    /// <summary>找出該被結束的 PID 與原因。</summary>
    public List<(int Pid, string Reason)> Evaluate(DateTime now)
    {
        var result = new List<(int, string)>();
        foreach (var inst in ListOverride())
        {
            if (inst.StartTime is { } st && now - st < GracePeriod) continue; // 剛啟動
            if (inst.StartTime is null) continue;                              // 不知道啟動時間就不冒險
            if (CloseIfMemoryLow)
            {
                var mb = MemoryOverride(inst.Pid);
                if (mb > 0 && mb < MemoryLowMb) result.Add((inst.Pid, $"記憶體過低 ({mb:F0} MB < {MemoryLowMb} MB)"));
            }
        }
        return result;
    }

    public int Tick(DateTime? now = null)
    {
        var victims = Evaluate(now ?? DateTime.Now);
        foreach (var (pid, reason) in victims)
        {
            KillOverride(pid);
            Killed?.Invoke(pid, reason);
        }
        return victims.Count;
    }

    public void Start(TimeSpan? interval = null)
    {
        Stop();
        var iv = interval ?? TimeSpan.FromSeconds(5);
        _timer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try { Tick(); } catch { } finally { Interlocked.Exchange(ref _busy, 0); }
        }, null, iv, iv);
    }

    public void Stop() { _timer?.Dispose(); _timer = null; }

    public void Dispose() => Stop();
}
