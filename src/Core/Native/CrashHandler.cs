using System.Diagnostics;

namespace RobloxAccountManager.Core.Native;

/// <summary>
/// 「封鎖當機處理程式」：Roblox 當機時會啟動 RobloxCrashHandler.exe 收集並上傳當機資料，
/// 多開時它們會各自佔用記憶體與 CPU。這裡只結束行程名稱完全等於 RobloxCrashHandler 的程式，
/// 不改系統設定、不動登錄檔，關掉功能後 Roblox 的行為立刻恢復。
/// </summary>
public static class CrashHandler
{
    public const string ProcessName = "RobloxCrashHandler";

    /// <summary>結束所有 RobloxCrashHandler 行程，回傳成功結束的數量。</summary>
    public static int Kill(string processName = ProcessName)
    {
        int killed = 0;
        foreach (var p in Process.GetProcessesByName(processName))
        {
            using (p)
            {
                try { p.Kill(); killed++; } catch { /* 已結束或權限不足 */ }
            }
        }
        return killed;
    }
}
