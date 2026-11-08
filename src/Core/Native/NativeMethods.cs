using System.Runtime.InteropServices;

namespace RobloxAccountManager.Core.Native;

internal static partial class NativeMethods
{
    // ---- 存取權 ----
    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint PROCESS_DUP_HANDLE = 0x0040;
    public const uint PROCESS_SET_QUOTA = 0x0100;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public const uint DUPLICATE_CLOSE_SOURCE = 0x1;
    public const uint DUPLICATE_SAME_ACCESS = 0x2;

    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint WAIT_ABANDONED = 0x00000080;
    public const uint WAIT_TIMEOUT = 0x00000102;

    // ---- kernel32 ----
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateMutexW(IntPtr attrs, [MarshalAs(UnmanagedType.Bool)] bool initialOwner, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseMutex(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateEventW(IntPtr attrs, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateHandle(IntPtr srcProcess, IntPtr srcHandle, IntPtr dstProcess,
        out IntPtr dstHandle, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "DuplicateHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateHandleClose(IntPtr srcProcess, IntPtr srcHandle, IntPtr dstProcess,
        IntPtr dstHandle, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateJobObjectW(IntPtr attrs, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWorkingSetSizeEx(IntPtr process, nuint min, nuint max, uint flags);

    // ---- ntdll ----
    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int infoClass, IntPtr buffer, uint length, out uint returnLength);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryObject(IntPtr handle, int infoClass, IntPtr buffer, uint length, out uint returnLength);

    // ---- user32 ----
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hwnd, char[] buffer, int max);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfoW(uint action, uint param, out RECT rect, uint winIni);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CpuRateControlInformation
    {
        public uint ControlFlags;
        public uint CpuRate;
    }
}
