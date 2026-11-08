using System.Diagnostics;

namespace RobloxAccountManager.Core.Native;

public enum MultiRobloxState
{
    /// <summary>未啟用。</summary>
    Off,
    /// <summary>已啟用，但 mutex 目前被別的行程（通常是執行中的 Roblox）擁有。</summary>
    Waiting,
    /// <summary>已擁有 mutex，多開生效。</summary>
    Active,
}

/// <summary>
/// 多開 Roblox。新版 Roblox 的單例機制有兩個物件：
///  - ROBLOX_singletonMutex：新實例啟動時嘗試取得；取不到就不會去動舊視窗。必須「真正擁有」它。
///  - ROBLOX_singletonEvent：新實例用它通知舊實例關閉。處理方式：啟動前從舊實例內關閉它的 handle
///    （<see cref="SingletonGuard"/>），再用同名 Mutex 佔住這個名稱，之後的實例就建不出這個 event。
///
/// mutex 的擁有權屬於「執行緒」，所以由一條常駐背景執行緒負責。
/// 注意：CreateMutexW 對已存在的 mutex 會忽略 initialOwner，因此一律用 WaitForSingleObject 判斷是否真的取得
/// （含 WAIT_ABANDONED：前一個擁有者結束時留下的 mutex）。
///
/// event 名稱佔位與啟動前清除 handle 的做法參考 MultiRoblox-RAM
/// （Required Notice: Copyright (c) 2026 PookiePepelsss，PolyForm Noncommercial 1.0.0；見 THIRD_PARTY_NOTICES.md）。
/// </summary>
public static class MultiRoblox
{
    // 可由測試改成替身名稱，避免動到真正的 Roblox。
    public static string MutexName { get; set; } = SingletonGuard.SingletonMutexName;
    public static string EventName { get; set; } = SingletonGuard.SingletonEventName;
    public static string TargetProcessName { get; set; } = SingletonGuard.RobloxProcessName;

    private sealed class Keeper
    {
        private readonly string _mutexName;
        private readonly string _eventName;
        private readonly Thread _thread;
        private volatile bool _halt;
        public volatile bool Owned;
        public volatile bool Squatted;
        public bool Alive => _thread.IsAlive;

        public Keeper(string mutexName, string eventName)
        {
            _mutexName = mutexName;
            _eventName = eventName;
            _thread = new Thread(Run) { IsBackground = true, Name = "multi-roblox-mutex" };
        }

        public void Start() => _thread.Start();

        public void Stop()
        {
            _halt = true;
            _thread.Join(2000);
        }

        private void Run()
        {
            var handle = NativeMethods.CreateMutexW(IntPtr.Zero, false, _mutexName);
            if (handle == IntPtr.Zero) return;
            var squat = IntPtr.Zero;
            try
            {
                while (!_halt)
                {
                    if (!Owned)
                    {
                        var r = NativeMethods.WaitForSingleObject(handle, 200);
                        if (r == NativeMethods.WAIT_OBJECT_0 || r == NativeMethods.WAIT_ABANDONED) Owned = true;
                        else if (r != NativeMethods.WAIT_TIMEOUT) break;
                    }
                    else
                    {
                        Thread.Sleep(200);
                    }

                    // 佔住 event 名稱：只要名稱還被「Event」物件佔用（舊 Roblox 還握著），
                    // 建立同名 Mutex 會失敗；等 handle 被關閉後才會成功。
                    if (squat == IntPtr.Zero)
                    {
                        squat = NativeMethods.CreateMutexW(IntPtr.Zero, false, _eventName);
                        if (squat != IntPtr.Zero) Squatted = true;
                    }
                }
            }
            finally
            {
                if (Owned) NativeMethods.ReleaseMutex(handle);
                Owned = false;
                NativeMethods.CloseHandle(handle);
                if (squat != IntPtr.Zero) NativeMethods.CloseHandle(squat);
                Squatted = false;
            }
        }
    }

    private static Keeper? _keeper;
    private static readonly object Gate = new();
    private static readonly HashSet<int> Tracked = new();

    /// <summary>啟用多開。回傳 true 表示已真正擁有 mutex；false 表示尚未擁有（背景會持續嘗試）。</summary>
    public static bool Enable(TimeSpan? wait = null)
    {
        Keeper k;
        lock (Gate)
        {
            if (_keeper is null || !_keeper.Alive)
            {
                _keeper = new Keeper(MutexName, EventName);
                _keeper.Start();
            }
            k = _keeper;
        }
        var deadline = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(1));
        while (DateTime.UtcNow < deadline)
        {
            if (k.Owned) return true;
            if (!k.Alive) return false;
            Thread.Sleep(20);
        }
        return k.Owned;
    }

    public static void Disable()
    {
        Keeper? k;
        lock (Gate) { k = _keeper; _keeper = null; }
        k?.Stop();
    }

    public static MultiRobloxState State
    {
        get
        {
            var k = _keeper;
            if (k is null || !k.Alive) return MultiRobloxState.Off;
            return k.Owned ? MultiRobloxState.Active : MultiRobloxState.Waiting;
        }
    }

    public static bool IsEventSquatted => _keeper is { Alive: true, Squatted: true };

    /// <summary>記錄由本程式啟動的 Roblox pid；啟動前清除 handle 時會跳過它們。</summary>
    public static void TrackPid(int pid)
    {
        lock (Gate) Tracked.Add(pid);
    }

    private static int[] LiveTracked()
    {
        lock (Gate)
        {
            Tracked.RemoveWhere(p =>
            {
                try { using var pr = Process.GetProcessById(p); return pr.HasExited; }
                catch (ArgumentException) { return true; }
            });
            return Tracked.ToArray();
        }
    }

    /// <summary>
    /// 每次啟動 Roblox「之前一刻」呼叫：關閉執行中（非本程式追蹤）Roblox 的 singleton event handle，
    /// 並等 event 名稱被佔住。多開未啟用時什麼都不做。回傳關閉的 handle 數量。
    /// </summary>
    public static int PrepareForLaunch(TimeSpan? squatWait = null)
    {
        if (_keeper is not { Alive: true }) return 0;
        int closed;
        try { closed = SingletonGuard.CloseSingletonEventHandles(LiveTracked(), TargetProcessName, EventName); }
        catch { closed = 0; }

        var deadline = DateTime.UtcNow + (squatWait ?? TimeSpan.FromSeconds(1.5));
        while (DateTime.UtcNow < deadline && !IsEventSquatted) Thread.Sleep(50);
        return closed;
    }
}
