using RobloxAccountManager.Core.Models;

namespace RobloxAccountManager.Core.Services;

/// <summary>
/// 斷線自動重開：記住每個由我們啟動的實例（帳號 + 目標），行程不見了（當掉 / 斷線被踢）就重新加入同一個目標。
/// 有 crash-loop 保護：同一帳號 10 分鐘內最多重開 3 次，超過就放棄並回報。
/// 使用者主動結束的實例要先呼叫 <see cref="Forget"/>，才不會被重開。
/// </summary>
public sealed class RelaunchSupervisor : IDisposable
{
    private sealed record Tracked(Account Account, LaunchTarget Target, int Pid, DateTime Started);

    private readonly Dictionary<string, Tracked> _byUser = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<DateTime>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private Timer? _timer;
    private int _busy;

    public int MaxRelaunches { get; set; } = 3;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(10);
    /// <summary>剛啟動多久內不判定死亡（pid 在啟動初期可能換手）。</summary>
    public TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>測試用：判斷行程是否還活著 / 實際重開。</summary>
    public Func<int, bool> IsAlive { get; set; } = pid =>
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    };
    public Func<Account, LaunchTarget, Task<JoinResult>> Relauncher { get; set; } = (_, _) => Task.FromResult(new JoinResult(false, "no launcher"));

    public event Action<Account, JoinResult>? Relaunched;
    public event Action<Account, string>? GaveUp;

    public void Register(Account acc, LaunchTarget target, int? pid)
    {
        if (pid is null || target.Kind == TargetKind.None) return;
        lock (_gate) _byUser[acc.Username] = new Tracked(acc, target, pid.Value, DateTime.Now);
    }

    public void Forget(string username) { lock (_gate) _byUser.Remove(username); }
    public void ForgetPid(int pid)
    {
        lock (_gate)
            foreach (var k in _byUser.Where(kv => kv.Value.Pid == pid).Select(kv => kv.Key).ToList()) _byUser.Remove(k);
    }
    public void ForgetAll() { lock (_gate) _byUser.Clear(); }

    public int TrackedCount { get { lock (_gate) return _byUser.Count; } }

    /// <summary>檢查一次，回傳重開的帳號數。</summary>
    public async Task<int> TickAsync(DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        List<Tracked> dead;
        lock (_gate)
            dead = _byUser.Values.Where(x => t - x.Started >= Grace && !IsAlive(x.Pid)).ToList();

        int n = 0;
        foreach (var d in dead)
        {
            string user = d.Account.Username;
            bool allowed;
            lock (_gate)
            {
                var h = _history.TryGetValue(user, out var l) ? l : _history[user] = new();
                h.RemoveAll(x => t - x > Window);
                allowed = h.Count < MaxRelaunches;
                if (allowed) h.Add(t);
                else _byUser.Remove(user);
            }
            if (!allowed)
            {
                GaveUp?.Invoke(d.Account, $"{Window.TotalMinutes:F0} 分鐘內已重開 {MaxRelaunches} 次，放棄自動重開");
                continue;
            }

            var r = await Relauncher(d.Account, d.Target);
            lock (_gate)
            {
                if (r.Ok && r.Pid is int pid) _byUser[user] = new Tracked(d.Account, d.Target, pid, DateTime.Now);
                else if (r.Ok) _byUser.Remove(user); // 沒有 pid 就無法再追蹤
            }
            Relaunched?.Invoke(d.Account, r);
            n++;
        }
        return n;
    }

    public void Start(TimeSpan? interval = null)
    {
        Stop();
        var iv = interval ?? TimeSpan.FromSeconds(3);
        _timer = new Timer(async _ =>
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try { await TickAsync(); } catch { } finally { Interlocked.Exchange(ref _busy, 0); }
        }, null, iv, iv);
    }

    public void Stop() { _timer?.Dispose(); _timer = null; }
    public void Dispose() => Stop();
}
