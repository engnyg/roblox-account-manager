namespace RobloxAccountManager.Core.Native;

/// <summary>
/// 防掛機：定期對所有 Roblox 視窗 PostMessage 一次 Space 的 down/up（不搶焦點），
/// 避免帳號因閒置太久被踢出遊戲。
/// </summary>
public sealed class AntiAfk : IDisposable
{
    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    private static readonly IntPtr VkSpace = 0x20;

    private Timer? _timer;
    public TimeSpan Interval { get; }

    public AntiAfk(TimeSpan interval) => Interval = interval;

    public bool Running => _timer is not null;

    public void Start()
    {
        if (_timer is not null) return;
        _timer = new Timer(_ => Tick(), null, Interval, Interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public static int Tick()
    {
        int sent = 0;
        foreach (var inst in RobloxProcesses.List())
        {
            var hwnd = WindowLayout.FindWindow(inst.Pid);
            if (hwnd == IntPtr.Zero) continue;
            NativeMethods.PostMessageW(hwnd, WM_KEYDOWN, VkSpace, IntPtr.Zero);
            NativeMethods.PostMessageW(hwnd, WM_KEYUP, VkSpace, (IntPtr)0x00C00001);
            sent++;
        }
        return sent;
    }

    public void Dispose() => Stop();
}
