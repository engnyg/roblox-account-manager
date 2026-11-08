using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxAccountManager.Core.Native;

/// <summary>行程微調：釋放記憶體（trim working set）、CPU 優先權、「多開時自動降低優先權」。</summary>
public static partial class ProcessTuning
{
    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(IntPtr hProcess);

    /// <summary>把行程的 working set 換出（釋放實體記憶體；需要時 Windows 會自動換回）。</summary>
    public static bool TrimMemory(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return EmptyWorkingSet(p.Handle);
        }
        catch { return false; }
    }

    public static int TrimMemory(IEnumerable<int> pids) => pids.Count(TrimMemory);

    public static bool SetPriority(int pid, ProcessPriorityClass priority)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = priority;
            return true;
        }
        catch { return false; }
    }

    public static ProcessPriorityClass? GetPriority(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.PriorityClass; }
        catch { return null; }
    }

    /// <summary>
    /// 「多開時降低優先權」：同時有 2 個以上實例 → 全部設為 BelowNormal；只剩 1 個 → 還原 Normal。
    /// 回傳實際設定成功的數量。
    /// </summary>
    public static int ApplyLowerPriority(IReadOnlyCollection<int> pids, bool enabled)
    {
        if (!enabled) return 0;
        var target = pids.Count >= 2 ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
        int ok = 0;
        foreach (var pid in pids)
        {
            if (GetPriority(pid) is { } cur && cur != target && SetPriority(pid, target)) ok++;
        }
        return ok;
    }

    public static readonly (string Name, ProcessPriorityClass Value)[] PriorityChoices =
    [
        ("Realtime", ProcessPriorityClass.RealTime),
        ("High", ProcessPriorityClass.High),
        ("Above Normal", ProcessPriorityClass.AboveNormal),
        ("Normal", ProcessPriorityClass.Normal),
        ("Below Normal", ProcessPriorityClass.BelowNormal),
        ("Low", ProcessPriorityClass.Idle),
    ];
}
