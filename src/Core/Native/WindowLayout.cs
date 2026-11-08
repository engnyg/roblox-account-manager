using System.Diagnostics;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Core.Native;

public readonly record struct WindowSettings(bool ApplyOnLaunch, int Width, int Height, bool Tile);

/// <summary>
/// Roblox 視窗大小 / 排列：啟動後等視窗出現，調成指定大小；可選擇排成格狀
/// （每個實例佔一格，視窗關閉後空出的格子給下一個實例）。Roblox 若在全螢幕（F11）就不吃外部調整。
/// </summary>
public static class WindowLayout
{
    public const int MinSize = 200;
    public const int MaxSize = 8000;

    private const int SW_RESTORE = 9;
    private const uint SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const uint SPI_GETWORKAREA = 0x0030;

    public static (int W, int H) ClampSize(int w, int h) =>
        (Math.Clamp(w, MinSize, MaxSize), Math.Clamp(h, MinSize, MaxSize));

    public static WindowSettings Load()
    {
        var s = AppSettings.Current;
        var (w, h) = ClampSize(s.WindowWidth, s.WindowHeight);
        return new WindowSettings(s.WindowApplyOnLaunch, w, h, s.WindowTile);
    }

    public static void Save(WindowSettings ws)
    {
        var s = AppSettings.Current;
        s.WindowApplyOnLaunch = ws.ApplyOnLaunch;
        s.WindowWidth = ws.Width;
        s.WindowHeight = ws.Height;
        s.WindowTile = ws.Tile;
        s.Save();
    }

    /// <summary>找出 pid 的主視窗（可見的頂層視窗；優先 Roblox 的 WINDOWSCLIENT 類別）。</summary>
    public static IntPtr FindWindow(int pid)
    {
        var found = new List<(int Prio, IntPtr Hwnd)>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var p);
            if (p != pid || !NativeMethods.IsWindowVisible(hwnd)) return true;
            NativeMethods.GetWindowRect(hwnd, out var rc);
            if (rc.Width < 100 || rc.Height < 100) return true;
            var buf = new char[64];
            int n = NativeMethods.GetClassNameW(hwnd, buf, buf.Length);
            var cls = new string(buf, 0, Math.Max(0, n));
            int prio = cls == "WINDOWSCLIENT" ? 2 : (NativeMethods.GetWindowTextLengthW(hwnd) > 0 ? 1 : 0);
            found.Add((prio, hwnd));
            return true;
        }, IntPtr.Zero);
        return found.Count == 0 ? IntPtr.Zero : found.OrderByDescending(f => f.Prio).First().Hwnd;
    }

    public static (int L, int T, int W, int H) GetRect(IntPtr hwnd)
    {
        NativeMethods.GetWindowRect(hwnd, out var rc);
        return (rc.Left, rc.Top, rc.Width, rc.Height);
    }

    private static NativeMethods.RECT WorkArea()
    {
        return NativeMethods.SystemParametersInfoW(SPI_GETWORKAREA, 0, out var rc, 0)
            ? rc
            : new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
    }

    public static (int X, int Y) SlotPosition(int slot, int width, int height)
    {
        var wa = WorkArea();
        int cols = Math.Max(1, wa.Width / width);
        int rows = Math.Max(1, wa.Height / height);
        int perScreen = cols * rows;
        int s = slot % perScreen;
        int shift = (slot / perScreen) * 32; // 超過一個螢幕後稍微錯開疊上去
        return (wa.Left + (s % cols) * width + shift, wa.Top + (s / cols) * height + shift);
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<int, int> Slots = new();

    private static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static int AllocSlot(int pid)
    {
        lock (Gate)
        {
            foreach (var p in Slots.Keys.ToList()) if (!Alive(p)) Slots.Remove(p);
            if (Slots.TryGetValue(pid, out var existing)) return existing;
            var used = new HashSet<int>(Slots.Values);
            int n = 0;
            while (used.Contains(n)) n++;
            Slots[pid] = n;
            return n;
        }
    }

    /// <summary>對 pid 的視窗套用大小（tile 時一併移到格子位置）。找不到視窗回傳 false。</summary>
    public static bool Apply(int pid, int width, int height, bool tile, int? slot = null)
    {
        (width, height) = ClampSize(width, height);
        var hwnd = FindWindow(pid);
        if (hwnd == IntPtr.Zero) return false;
        if (NativeMethods.IsZoomed(hwnd)) NativeMethods.ShowWindow(hwnd, SW_RESTORE);
        uint flags = SWP_NOZORDER | SWP_NOACTIVATE;
        int x = 0, y = 0;
        if (tile) (x, y) = SlotPosition(slot ?? AllocSlot(pid), width, height);
        else flags |= SWP_NOMOVE;
        return NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, flags);
    }

    /// <summary>背景等視窗出現後套用設定；Roblox 載入完成後常會自己再調一次大小，所以稍後再套兩次。</summary>
    public static void ScheduleLayout(int pid, WindowSettings? settings = null, TimeSpan? timeout = null)
    {
        var ws = settings ?? Load();
        if (!ws.ApplyOnLaunch) return;
        var limit = timeout ?? TimeSpan.FromSeconds(90);
        _ = Task.Run(async () =>
        {
            int? slot = ws.Tile ? AllocSlot(pid) : null;
            var deadline = DateTime.UtcNow + limit;
            bool applied = false;
            while (DateTime.UtcNow < deadline && Alive(pid))
            {
                if (Apply(pid, ws.Width, ws.Height, ws.Tile, slot)) { applied = true; break; }
                await Task.Delay(500);
            }
            if (!applied) return;
            foreach (var delay in new[] { 3000, 6000 })
            {
                await Task.Delay(delay);
                if (!Alive(pid)) return;
                Apply(pid, ws.Width, ws.Height, ws.Tile, slot);
            }
        });
    }

    /// <summary>對一批實例套用（重新依序編格子）。回傳 (成功數, 總數)。</summary>
    public static (int Ok, int Total) ArrangeAll(IEnumerable<int> pids, WindowSettings ws)
    {
        var list = pids.OrderBy(p => p).ToList();
        lock (Gate) Slots.Clear();
        int ok = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (Apply(list[i], ws.Width, ws.Height, ws.Tile, ws.Tile ? i : null))
            {
                ok++;
                if (ws.Tile) lock (Gate) Slots[list[i]] = i;
            }
        }
        return (ok, list.Count);
    }
}
