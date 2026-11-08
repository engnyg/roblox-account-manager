using System.Diagnostics;
using System.Windows.Forms;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Tests;

/// <summary>替身行程輔助：用 PowerShell 當「假 Roblox」，絕不碰真正的 Roblox。</summary>
internal static class Stand
{
    public static Process Ps(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);
        var p = Process.Start(psi)!;
        var line = p.StandardOutput.ReadLine();
        Assert.Equal("ready", line);
        return p;
    }

    public static Process HoldEvent(string name) =>
        Ps($"$e = New-Object System.Threading.EventWaitHandle($false,'ManualReset','{name}'); 'ready'; Start-Sleep 120");

    public static Process Busy() => Ps("'ready'; while($true){}");

    public static Process AllocMemory(int mb) =>
        Ps($"$b = New-Object byte[] ({mb}MB); for($i=0;$i -lt $b.Length;$i+=4096){{$b[$i]=1}}; 'ready'; while($true){{ for($i=0;$i -lt $b.Length;$i+=4096){{$b[$i]=2}}; Start-Sleep -Milliseconds 50 }}");

    public static double CpuPercentOfMachine(Process p, TimeSpan window)
    {
        p.Refresh();
        var t0 = p.TotalProcessorTime; var w0 = DateTime.UtcNow;
        Thread.Sleep(window);
        p.Refresh();
        var t1 = p.TotalProcessorTime; var w1 = DateTime.UtcNow;
        return (t1 - t0).TotalSeconds / (w1 - w0).TotalSeconds / Environment.ProcessorCount * 100.0;
    }

    public static bool NameFree(string name)
    {
        var h = NativeMethodsProbe.CreateMutex(name);
        if (h != IntPtr.Zero) { NativeMethodsProbe.Close(h); return true; }
        return false;
    }
}

internal static class NativeMethodsProbe
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutexW(IntPtr a, bool owner, string name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEventW(IntPtr a, bool manual, bool initial, string name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    public static IntPtr CreateMutex(string name) => CreateMutexW(IntPtr.Zero, false, name);
    public static IntPtr CreateEvent(string name) => CreateEventW(IntPtr.Zero, true, false, name);
    public static void Close(IntPtr h) => CloseHandle(h);
    public static int LastError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();
}

[Collection("native")]
public class SingletonGuardTests
{
    [Fact]
    public void Closes_event_handle_in_stand_in_and_leaves_it_alive()
    {
        var evt = "TESTEVT_" + Guid.NewGuid().ToString("N");
        using var p = Stand.HoldEvent(evt);
        try
        {
            Assert.False(Stand.NameFree(evt));   // Event 還在 → 同名 Mutex 建不出來
            var n = SingletonGuard.CloseSingletonEventHandles(null, "powershell", evt);
            Assert.Equal(1, n);
            Assert.True(Stand.NameFree(evt));    // handle 被關 → 名稱釋放
            Assert.False(p.HasExited);
        }
        finally { p.Kill(); }
    }

    [Fact]
    public void Exempt_pids_are_skipped()
    {
        var evt = "TESTEVT_" + Guid.NewGuid().ToString("N");
        using var p = Stand.HoldEvent(evt);
        try
        {
            Assert.Equal(0, SingletonGuard.CloseSingletonEventHandles(new[] { p.Id }, "powershell", evt));
            Assert.False(Stand.NameFree(evt));
        }
        finally { p.Kill(); }
    }

    [Fact]
    public void No_matching_process_returns_zero()
        => Assert.Equal(0, SingletonGuard.CloseSingletonEventHandles(null, "definitely_not_running_xyz", "whatever"));
}

[Collection("native")]
public class MultiRobloxTests
{
    [Fact]
    public void Owns_mutex_and_squats_event_name_after_prepare()
    {
        var id = Guid.NewGuid().ToString("N");
        MultiRoblox.MutexName = "TESTMTX_" + id;
        MultiRoblox.EventName = "TESTEVT_" + id;
        MultiRoblox.TargetProcessName = "powershell";
        using var p = Stand.HoldEvent(MultiRoblox.EventName);
        try
        {
            Assert.True(MultiRoblox.Enable(TimeSpan.FromSeconds(2)));
            Assert.Equal(MultiRobloxState.Active, MultiRoblox.State);
            Assert.False(MultiRoblox.IsEventSquatted);          // 替身還握著 event，佔不到名稱

            var closed = MultiRoblox.PrepareForLaunch(TimeSpan.FromSeconds(3));
            Assert.Equal(1, closed);
            Assert.True(MultiRoblox.IsEventSquatted);

            // 新「Roblox」想建立同名 event → 被擋（ERROR_INVALID_HANDLE = 6）
            var h = NativeMethodsProbe.CreateEvent(MultiRoblox.EventName);
            Assert.Equal(IntPtr.Zero, h);
            Assert.Equal(6, NativeMethodsProbe.LastError());
            Assert.False(p.HasExited);

            // 另一個「新 Roblox」想取得 mutex → 被擋（WAIT_TIMEOUT），所以不會去關舊視窗
            var m = NativeMethodsProbe.CreateMutex(MultiRoblox.MutexName);
            Assert.NotEqual(IntPtr.Zero, m);
            Assert.Equal(NativeMethods_WaitTimeout, WaitOnce(m));
            NativeMethodsProbe.Close(m);
        }
        finally
        {
            MultiRoblox.Disable();
            p.Kill();
            MultiRoblox.MutexName = SingletonGuard.SingletonMutexName;
            MultiRoblox.EventName = SingletonGuard.SingletonEventName;
            MultiRoblox.TargetProcessName = SingletonGuard.RobloxProcessName;
        }
        Assert.Equal(MultiRobloxState.Off, MultiRoblox.State);
    }

    private const uint NativeMethods_WaitTimeout = 0x102;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr h, uint ms);
    private static uint WaitOnce(IntPtr h) => WaitForSingleObject(h, 300);

    [Fact]
    public void Waits_while_another_process_owns_mutex_then_takes_over()
    {
        var id = Guid.NewGuid().ToString("N");
        MultiRoblox.MutexName = "TESTMTX_" + id;
        MultiRoblox.EventName = "TESTEVT_" + id;
        // 另一個行程持有 mutex（initial owner），3 秒後結束
        using var owner = Stand.Ps($"$m = New-Object System.Threading.Mutex($true,'{MultiRoblox.MutexName}'); 'ready'; Start-Sleep 3");
        try
        {
            Assert.False(MultiRoblox.Enable(TimeSpan.FromMilliseconds(500)));
            Assert.Equal(MultiRobloxState.Waiting, MultiRoblox.State);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && MultiRoblox.State != MultiRobloxState.Active) Thread.Sleep(100);
            Assert.Equal(MultiRobloxState.Active, MultiRoblox.State);   // 擁有者結束 → WAIT_ABANDONED → 接手
        }
        finally
        {
            MultiRoblox.Disable();
            if (!owner.HasExited) owner.Kill();
            MultiRoblox.MutexName = SingletonGuard.SingletonMutexName;
            MultiRoblox.EventName = SingletonGuard.SingletonEventName;
        }
    }
}

[Collection("native")]
public class ResourceLimitTests
{
    [Fact]
    public void Clamp_enforces_bounds()
    {
        Assert.Equal(new Limits(100, 256), ResourceLimits.Clamp(150, 10));
        Assert.Equal(new Limits(0, 0), ResourceLimits.Clamp(-5, 0));
        Assert.Equal(new Limits(0, ResourceLimits.MaxMemoryMb), ResourceLimits.Clamp(0, int.MaxValue));
    }

    [Fact]
    public void Cpu_cap_throttles_and_clear_restores()
    {
        using var p = Stand.Busy();
        try
        {
            Thread.Sleep(1500);
            var free = Stand.CpuPercentOfMachine(p, TimeSpan.FromSeconds(2));
            Assert.True(free > 100.0 / Environment.ProcessorCount * 0.5, $"uncapped={free:F1}");

            var cap = Math.Max(1, (int)Math.Round(100.0 / Environment.ProcessorCount / 3));   // 約單核的 1/3
            ResourceLimits.Apply(p.Id, new Limits(cap, 0));
            Assert.Equal(new Limits(cap, 0), ResourceLimits.GetApplied(p.Id));
            var capped = Stand.CpuPercentOfMachine(p, TimeSpan.FromSeconds(3));
            Assert.True(capped <= cap + 1.5 && capped < free * 0.7, $"capped={capped:F1} cap={cap} free={free:F1}");

            ResourceLimits.Clear(p.Id);
            Assert.False(ResourceLimits.GetApplied(p.Id).Active);
            var after = Stand.CpuPercentOfMachine(p, TimeSpan.FromSeconds(2));
            Assert.True(after > capped * 1.5, $"after={after:F1} capped={capped:F1}");
        }
        finally { p.Kill(); }
    }

    [Fact]
    public void Ram_cap_trims_working_set_without_killing()
    {
        using var p = Stand.AllocMemory(600);
        try
        {
            Thread.Sleep(1000);
            p.Refresh();
            var before = p.WorkingSet64 / 1048576.0;
            Assert.True(before > 500, $"before={before:F0}");

            ResourceLimits.Apply(p.Id, new Limits(0, 300));
            Thread.Sleep(3000);
            p.Refresh();
            var capped = p.WorkingSet64 / 1048576.0;
            Assert.True(capped <= 360, $"capped={capped:F0}");
            Assert.False(p.HasExited);

            ResourceLimits.Clear(p.Id);
            Thread.Sleep(3000);
            p.Refresh();
            Assert.True(p.WorkingSet64 / 1048576.0 > capped * 1.5);
        }
        finally { p.Kill(); }
    }

    [Fact]
    public void Missing_pid_throws_and_dead_process_state_is_pruned()
    {
        Assert.Throws<LimitException>(() => ResourceLimits.Apply(99999999, new Limits(10, 0)));

        using var p = Stand.Busy();
        ResourceLimits.Apply(p.Id, new Limits(10, 512));
        Assert.True(ResourceLimits.GetApplied(p.Id).Active);
        p.Kill(); p.WaitForExit();
        Assert.False(ResourceLimits.GetApplied(p.Id).Active);
    }
}

[Collection("native")]
public class WindowLayoutTests
{
    private static (Form Form, Thread Thread) ShowForm(int x, int y)
    {
        Form? form = null;
        var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            form = new Form { Text = "stub", StartPosition = FormStartPosition.Manual, Left = x, Top = y, Width = 400, Height = 300, ShowInTaskbar = false };
            form.Shown += (_, _) => ready.Set();
            Application.Run(form);
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(ready.Wait(5000));
        return (form!, t);
    }

    [Fact]
    public void Resizes_own_window_and_tiles_slots()
    {
        var (form, thread) = ShowForm(100, 100);
        try
        {
            int pid = Environment.ProcessId;
            var hwnd = WindowLayout.FindWindow(pid);
            Assert.NotEqual(IntPtr.Zero, hwnd);

            Assert.True(WindowLayout.Apply(pid, 700, 500, tile: false));
            var (l, t, w, h) = WindowLayout.GetRect(hwnd);
            Assert.Equal((700, 500), (w, h));
            Assert.Equal((100, 100), (l, t));   // 不移動

            Assert.True(WindowLayout.Apply(pid, 600, 400, tile: true, slot: 0));
            var r0 = WindowLayout.GetRect(hwnd);
            var pos0 = WindowLayout.SlotPosition(0, 600, 400);
            Assert.Equal((pos0.X, pos0.Y, 600, 400), r0);

            Assert.True(WindowLayout.Apply(pid, 600, 400, tile: true, slot: 1));
            var r1 = WindowLayout.GetRect(hwnd);
            var pos1 = WindowLayout.SlotPosition(1, 600, 400);
            Assert.Equal((pos1.X, pos1.Y), (r1.L, r1.T));
            Assert.NotEqual((r0.L, r0.T), (r1.L, r1.T));
        }
        finally { form.Invoke(form.Close); thread.Join(2000); }
    }

    [Fact]
    public void Clamp_and_missing_window()
    {
        Assert.Equal((200, 8000), WindowLayout.ClampSize(10, 99999));
        Assert.False(WindowLayout.Apply(99999999, 600, 400, true));
    }

    [Fact]
    public void Slot_positions_wrap_within_work_area()
    {
        var a = WindowLayout.SlotPosition(0, 600, 400);
        var b = WindowLayout.SlotPosition(1, 600, 400);
        Assert.Equal(600, b.X - a.X);
        Assert.Equal(a.Y, b.Y);
    }
}

[Collection("native")]
public class LauncherTests
{
    [Fact]
    public void Direct_launch_returns_pid_tracks_it_and_applies_default_limits()
    {
        // 替身 exe：一個跑 30 秒的 .cmd，參數（URI）會被當作 %1 忽略
        var dir = Path.Combine(Path.GetTempPath(), "ramv3_launch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var stub = Path.Combine(dir, "FakeRoblox.cmd");
        File.WriteAllText(stub, "@echo off\r\nping -n 30 127.0.0.1 >nul\r\n");
        var data = Path.Combine(dir, "data");
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("RAM_V3_DATA", data);
        AppSettings.Reload();
        AppSettings.Current.DirectLaunch = true;
        AppSettings.Current.DefaultCpuPercent = 7;
        AppSettings.Current.DefaultMemoryMb = 0;
        AppSettings.Current.AutoApplyLimits = true;
        RobloxLauncher.FindExeOverride = () => stub;
        int? pid = null;
        try
        {
            var (p, how) = RobloxLauncher.Launch("roblox-player:1+launchmode:play+browsertrackerid:123");
            pid = p;
            Assert.Equal("direct", how);
            Assert.NotNull(pid);

            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && !ResourceLimits.GetApplied(pid!.Value).Active) Thread.Sleep(100);
            Assert.Equal(new Limits(7, 0), ResourceLimits.GetApplied(pid!.Value));
        }
        finally
        {
            RobloxLauncher.FindExeOverride = null;
            if (pid is not null) try { Process.GetProcessById(pid.Value).Kill(true); } catch { }
            Environment.SetEnvironmentVariable("RAM_V3_DATA", null);
            AppSettings.Reload();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

[CollectionDefinition("native", DisableParallelization = true)]
public class NativeCollection;
