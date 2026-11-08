using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxAccountManager.Core.Native;

/// <summary>
/// 關閉執行中 Roblox 的 ROBLOX_singletonEvent handle，讓新實例不會把舊實例擠掉。
///
/// 新版 Roblox 啟動時會透過具名 event 通知「舊的」實例關閉
/// （日誌：Systray: Received close main window request）。要多開，除了持有
/// ROBLOX_singletonMutex 之外，還得讓舊實例「聽不到」這個 event：
///   1. 從外部關閉舊實例裡指向該 event 的 handle（本類別）。
///   2. 之後用同名 Mutex 佔住這個名稱，新實例就建立不了 event（見 <see cref="MultiRoblox"/>）。
///
/// ----------------------------------------------------------------------------
/// Derived from MultiRoblox-RAM (RobloxNative.cs: HandleCloser / RunMutex)
/// Required Notice: Copyright (c) 2026 PookiePepelsss
/// (https://github.com/PookiePepelsss/MultiRoblox-RAM)
/// Licensed under the PolyForm Noncommercial License 1.0.0
/// (https://polyformproject.org/licenses/noncommercial/1.0.0).
/// 僅供非商業用途；詳見專案根目錄 THIRD_PARTY_NOTICES.md。
/// 改良：先用 ObjectTypeIndex 只挑出 Event 類型的 handle 才查名稱，
/// 避免對 named pipe 等物件呼叫 NtQueryObject 造成卡死。
/// ----------------------------------------------------------------------------
/// </summary>
public static unsafe class SingletonGuard
{
    public const string SingletonEventName = "ROBLOX_singletonEvent";
    public const string SingletonMutexName = "ROBLOX_singletonMutex";
    public const string RobloxProcessName = "RobloxPlayerBeta";

    private const int SystemExtendedHandleInformation = 64;
    private const uint StatusInfoLengthMismatch = 0xC0000004;
    private const int ObjectNameInformation = 1;

    // SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX（x64 共 40 bytes）
    [StructLayout(LayoutKind.Sequential)]
    private struct HandleEntry
    {
        public IntPtr Object;
        public nuint UniqueProcessId;
        public nuint HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    private static IntPtr Snapshot(out int count)
    {
        uint size = 1u << 20;
        while (true)
        {
            var buf = Marshal.AllocHGlobal((nint)size);
            var status = (uint)NativeMethods.NtQuerySystemInformation(SystemExtendedHandleInformation, buf, size, out var needed);
            if (status == 0)
            {
                count = (int)*(nint*)buf;
                return buf;
            }
            Marshal.FreeHGlobal(buf);
            if (status != StatusInfoLengthMismatch)
                throw new InvalidOperationException($"NtQuerySystemInformation failed: 0x{status:X}");
            size = Math.Max(size * 2, needed + (1u << 16));
        }
    }

    private static string QueryName(IntPtr handle)
    {
        const int cap = 2048;
        var buf = Marshal.AllocHGlobal(cap);
        try
        {
            if (NativeMethods.NtQueryObject(handle, ObjectNameInformation, buf, cap, out _) < 0) return "";
            var length = *(ushort*)buf;
            if (length == 0) return "";
            var ptr = *(IntPtr*)((byte*)buf + (IntPtr.Size == 8 ? 8 : 4));
            return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUni(ptr, length / 2) ?? "";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>
    /// 關閉所有執行中 Roblox 內名稱含 <paramref name="nameSubstring"/> 的 Event handle，回傳關閉的數量。
    /// </summary>
    /// <param name="exemptPids">由本程式啟動並追蹤中的實例，跳過不處理。</param>
    /// <param name="processName">不含 .exe 的行程名稱；可替換，方便用替身行程測試。</param>
    public static int CloseSingletonEventHandles(
        IEnumerable<int>? exemptPids = null,
        string processName = RobloxProcessName,
        string nameSubstring = SingletonEventName)
    {
        var exempt = new HashSet<int>(exemptPids ?? Array.Empty<int>());
        var targets = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            using (p) if (!exempt.Contains(p.Id)) targets.Add(p.Id);
        }
        if (targets.Count == 0) return 0;

        // 建一個匿名 event，從 handle 表找出 Event 類型的 ObjectTypeIndex，之後只對該類型查名稱。
        var probe = NativeMethods.CreateEventW(IntPtr.Zero, true, false, null);
        if (probe == IntPtr.Zero) return 0;

        IntPtr table = IntPtr.Zero;
        int closed = 0;
        var processHandles = new Dictionary<int, IntPtr>();
        try
        {
            table = Snapshot(out var count);
            var entries = (HandleEntry*)((byte*)table + IntPtr.Size * 2);
            int myPid = Environment.ProcessId;

            int eventType = -1;
            for (int i = 0; i < count; i++)
            {
                if ((int)entries[i].UniqueProcessId == myPid && (nint)entries[i].HandleValue == probe)
                {
                    eventType = entries[i].ObjectTypeIndex;
                    break;
                }
            }
            NativeMethods.CloseHandle(probe);
            probe = IntPtr.Zero;
            if (eventType < 0) return 0;

            var self = NativeMethods.GetCurrentProcess();
            for (int i = 0; i < count; i++)
            {
                ref var e = ref entries[i];
                int pid = (int)e.UniqueProcessId;
                if (e.ObjectTypeIndex != eventType || !targets.Contains(pid)) continue;

                if (!processHandles.TryGetValue(pid, out var src))
                {
                    src = NativeMethods.OpenProcess(NativeMethods.PROCESS_DUP_HANDLE, false, (uint)pid);
                    processHandles[pid] = src;
                }
                if (src == IntPtr.Zero) continue;

                if (!NativeMethods.DuplicateHandle(src, (nint)e.HandleValue, self, out var dup, 0, false,
                        NativeMethods.DUPLICATE_SAME_ACCESS)) continue;
                string name;
                try { name = QueryName(dup); }
                finally { NativeMethods.CloseHandle(dup); }

                if (name.Contains(nameSubstring, StringComparison.Ordinal))
                {
                    // 目標為 NULL + DUPLICATE_CLOSE_SOURCE = 只從來源行程關閉該 handle
                    if (NativeMethods.DuplicateHandleClose(src, (nint)e.HandleValue, IntPtr.Zero, IntPtr.Zero, 0, false,
                            NativeMethods.DUPLICATE_CLOSE_SOURCE))
                        closed++;
                }
            }
        }
        finally
        {
            if (probe != IntPtr.Zero) NativeMethods.CloseHandle(probe);
            foreach (var h in processHandles.Values) if (h != IntPtr.Zero) NativeMethods.CloseHandle(h);
            if (table != IntPtr.Zero) Marshal.FreeHGlobal(table);
        }
        return closed;
    }
}
