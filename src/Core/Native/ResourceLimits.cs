using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Core.Native;

/// <summary>CPU / RAM 限制。0 = 不限制。</summary>
public readonly record struct Limits(int CpuPercent = 0, int MemoryMb = 0)
{
    public bool Active => CpuPercent > 0 || MemoryMb > 0;

    public string Describe()
    {
        var parts = new List<string>();
        if (CpuPercent > 0) parts.Add($"CPU {CpuPercent}%");
        if (MemoryMb > 0) parts.Add($"RAM {MemoryMb} MB");
        return string.Join(" / ", parts);
    }
}

public sealed class LimitException(string message) : Exception(message);

/// <summary>
/// 每個 Roblox 實例的 CPU / RAM 限制。
/// CPU：行程放進 Job Object，啟用 CPU rate control 的 hard cap；數字是佔整台電腦（所有核心）的百分比。
/// RAM：SetProcessWorkingSetSizeEx 設定 working set「硬上限」。超過的頁面換出到分頁檔，
///      不會讓 Roblox 崩潰，但設太低會變卡（下限 256 MB）。這不是 commit limit（超過會讓配置失敗而崩潰）。
/// 清除：CPU 設回 100%、RAM 取消硬上限。
/// </summary>
public static class ResourceLimits
{
    public const int MinMemoryMb = 256;
    public const int MaxMemoryMb = 262144;

    private const int JobObjectCpuRateControlInformation = 15;
    private const uint CpuRateControlEnable = 0x1;
    private const uint CpuRateControlHardCap = 0x4;
    private const uint QuotaLimitsHardWsMinDisable = 0x2;
    private const uint QuotaLimitsHardWsMaxEnable = 0x4;
    private const uint QuotaLimitsHardWsMaxDisable = 0x8;

    private sealed class Entry
    {
        public DateTime StartTime;
        public IntPtr Job;
        public Limits Limits;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<int, Entry> State = new();

    public static Limits Clamp(int cpuPercent, int memoryMb)
    {
        var cpu = Math.Clamp(cpuPercent, 0, 100);
        var mem = memoryMb;
        if (mem > 0) mem = Math.Clamp(mem, MinMemoryMb, MaxMemoryMb);
        else mem = 0;
        return new Limits(cpu, mem);
    }

    private static LimitException Err(string what) =>
        new($"{what} 失敗（Win32 錯誤 {Marshal.GetLastWin32Error()}）");

    private static DateTime? StartTimeOf(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.StartTime; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (Win32Exception) { return null; }
    }

    private static void PruneDeadLocked()
    {
        foreach (var pid in State.Keys.ToList())
        {
            var e = State[pid];
            if (StartTimeOf(pid) != e.StartTime)
            {
                if (e.Job != IntPtr.Zero) NativeMethods.CloseHandle(e.Job);
                State.Remove(pid);
            }
        }
    }

    private static void SetCpuRate(IntPtr job, int percent)
    {
        // CpuRate 單位為「整體 CPU 的 1/100 個百分點」（1–10000）
        var info = new NativeMethods.CpuRateControlInformation
        {
            ControlFlags = CpuRateControlEnable | CpuRateControlHardCap,
            CpuRate = (uint)Math.Clamp(percent * 100, 1, 10000),
        };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.CpuRateControlInformation>());
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!NativeMethods.SetInformationJobObject(job, JobObjectCpuRateControlInformation, ptr,
                    (uint)Marshal.SizeOf<NativeMethods.CpuRateControlInformation>()))
                throw Err("設定 CPU 上限");
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static void SetRamCap(IntPtr process, int memoryMb)
    {
        bool ok;
        if (memoryMb > 0)
        {
            ok = NativeMethods.SetProcessWorkingSetSizeEx(process, 1024 * 1024, (nuint)memoryMb * 1024 * 1024,
                QuotaLimitsHardWsMinDisable | QuotaLimitsHardWsMaxEnable);
        }
        else
        {
            // 取消硬上限；軟上限放到實體記憶體大小，等同不限制
            var total = (nuint)Math.Min(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, long.MaxValue);
            ok = NativeMethods.SetProcessWorkingSetSizeEx(process, 1024 * 1024, total,
                QuotaLimitsHardWsMinDisable | QuotaLimitsHardWsMaxDisable);
        }
        if (!ok) throw Err("設定 RAM 上限");
    }

    /// <summary>對 pid 套用限制（<c>new Limits()</c> 等同清除）。失敗拋出 <see cref="LimitException"/>。</summary>
    public static Limits Apply(int pid, Limits limits)
    {
        var start = StartTimeOf(pid) ?? throw new LimitException($"行程 {pid} 不存在");
        var hProc = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_TERMINATE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            false, (uint)pid);
        if (hProc == IntPtr.Zero) throw Err($"開啟行程 {pid}");
        try
        {
            lock (Gate)
            {
                PruneDeadLocked();
                State.TryGetValue(pid, out var ent);
                var prev = ent?.Limits ?? new Limits();

                var job = ent?.Job ?? IntPtr.Zero;
                if (limits.CpuPercent > 0 || job != IntPtr.Zero)
                {
                    if (job == IntPtr.Zero)
                    {
                        job = NativeMethods.CreateJobObjectW(IntPtr.Zero, null);
                        if (job == IntPtr.Zero) throw Err("建立 Job Object");
                        if (!NativeMethods.AssignProcessToJobObject(job, hProc))
                        {
                            var ex = Err("把行程加入 Job Object");
                            NativeMethods.CloseHandle(job);
                            throw ex;
                        }
                    }
                    SetCpuRate(job, limits.CpuPercent > 0 ? limits.CpuPercent : 100);
                }

                if (limits.MemoryMb > 0 || prev.MemoryMb > 0) SetRamCap(hProc, limits.MemoryMb);

                if (job == IntPtr.Zero && !limits.Active) State.Remove(pid);
                else State[pid] = new Entry { StartTime = start, Job = job, Limits = limits };
            }
            return limits;
        }
        finally { NativeMethods.CloseHandle(hProc); }
    }

    public static void Clear(int pid) => Apply(pid, new Limits());

    public static Limits GetApplied(int pid)
    {
        lock (Gate)
        {
            if (State.TryGetValue(pid, out var e) && StartTimeOf(pid) == e.StartTime) return e.Limits;
        }
        return new Limits();
    }

    /// <summary>目前設定中的預設限制。</summary>
    public static Limits Defaults()
    {
        var s = AppSettings.Current;
        return Clamp(s.DefaultCpuPercent, s.DefaultMemoryMb);
    }

    /// <summary>新啟動的實例：若啟用自動套用且預設值不為空就套用。剛啟動的行程可能還沒就緒，會重試幾次。</summary>
    public static Limits? ApplyDefaults(int pid)
    {
        if (!AppSettings.Current.AutoApplyLimits) return null;
        var d = Defaults();
        if (!d.Active) return null;
        for (int i = 0; i < 5; i++)
        {
            try { return Apply(pid, d); }
            catch (LimitException) { Thread.Sleep(400); }
        }
        return null;
    }
}
