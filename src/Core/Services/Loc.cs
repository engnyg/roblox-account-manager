namespace RobloxAccountManager.Core.Services;

/// <summary>簡單的多語系字串表。鍵值就是英文原文；找不到翻譯就顯示英文。</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, string> Zh = new()
    {
        ["Roblox Account Manager V3"] = "Roblox 帳號管理器 V3",
        ["File"] = "檔案", ["Accounts"] = "帳號", ["Game"] = "遊戲", ["Tools"] = "工具",
        ["Import Cookies..."] = "匯入 Cookie...", ["Import V2 Accounts..."] = "匯入 V2 帳號...",
        ["Export Cookies"] = "匯出 Cookie", ["Exit"] = "結束",
        ["Check All Visible"] = "勾選目前顯示的帳號", ["Uncheck All"] = "取消全部勾選",
        ["Refresh Account Info"] = "重新整理帳號資訊",
        ["Multi-Roblox"] = "多開 Roblox", ["FPS Cap..."] = "FPS 上限...", ["Anti-AFK"] = "防掛機",
        ["Mixer"] = "混音台（實例管理）", ["Settings"] = "設定",
        ["Search:"] = "搜尋：", ["Group:"] = "群組：", ["All Groups"] = "所有群組",
        ["Launch Group"] = "啟動群組", ["Launch Checked"] = "啟動已勾選",
        ["Place ID:"] = "Place ID：", ["Job ID:"] = "Job ID：", ["Join"] = "加入",
        ["Launch"] = "啟動", ["Username"] = "用戶名", ["Alias"] = "別名", ["Group"] = "群組",
        ["Description"] = "描述", ["Last Use"] = "最後使用", ["Valid"] = "有效",
        ["Join Server"] = "加入伺服器", ["Check Selected"] = "勾選選取的帳號",
        ["Uncheck Selected"] = "取消勾選選取的帳號", ["Set Alias"] = "設定別名",
        ["Set Group"] = "設定群組", ["Set Description"] = "設定描述",
        ["Copy Username"] = "複製用戶名", ["Copy Cookie"] = "複製 Cookie", ["Remove"] = "移除",
        ["Ready"] = "就緒", ["accounts"] = "個帳號", ["Loaded"] = "已載入",
        ["No accounts checked"] = "沒有勾選任何帳號", ["No accounts in this group"] = "此群組沒有帳號",
        ["A launch is already in progress"] = "已有啟動作業進行中",
        ["Enter a valid Place ID"] = "請輸入有效的 Place ID", ["No account selected"] = "尚未選取帳號",
        ["Launching"] = "啟動中", ["Group launched"] = "已啟動",
        ["Multi-Roblox enabled"] = "多開已啟用",
        ["Multi-Roblox waiting: another Roblox owns the mutex; close it and it will take over"] =
            "多開等待中：有 Roblox 持有 mutex，關閉它後會自動接手",
        ["Multi-Roblox disabled"] = "多開已停用",
        // Mixer
        ["PID"] = "PID", ["Memory (MB)"] = "記憶體 (MB)", ["CPU %"] = "CPU %", ["Limit"] = "限制",
        ["Account"] = "帳號", ["Unknown"] = "未知",
        ["CPU limit:"] = "CPU 上限：", ["RAM limit:"] = "RAM 上限：", ["Off"] = "不限制",
        ["Apply to Selected"] = "套用到選取", ["Apply to All"] = "套用到全部", ["Clear Limits"] = "清除限制",
        ["Auto-apply these limits to newly launched instances"] = "新啟動的實例自動套用這組限制",
        ["CPU limit is a percentage of the whole machine (all cores). RAM limit caps the working set (min 256 MB); Roblox pages out instead of crashing, but may stutter."] =
            "CPU 上限是佔整台電腦（所有核心）的百分比。RAM 上限是限制工作集（最小 256 MB）：Roblox 會把多的頁面換出到分頁檔而不會崩潰，但可能變卡。",
        ["Window size:"] = "視窗大小：", ["Tile in a grid"] = "自動排成格狀", ["Apply on launch"] = "啟動時套用",
        ["Apply to Running Windows"] = "套用到執行中的視窗", ["Kill All Roblox"] = "結束所有 Roblox",
        ["This will close every running Roblox window. Continue?"] = "這會關閉所有執行中的 Roblox 視窗，要繼續嗎？",
        ["Select at least one instance first"] = "請先選取至少一個實例",
        ["No running Roblox instances"] = "沒有執行中的 Roblox",
        ["Some windows could not be resized (not created yet, or in fullscreen mode)."] =
            "部分視窗無法調整（尚未建立，或處於全螢幕模式）。",
        ["Close"] = "關閉", ["Cleared"] = "已清除", ["FPS Cap:"] = "FPS 上限：",
        // Settings
        ["Language:"] = "語言：", ["Join delay (seconds):"] = "批次啟動間隔（秒）：",
        ["Launch Roblox directly (skip bootstrapper)"] = "直接啟動 Roblox（略過 bootstrapper）",
        ["RobloxPlayerBeta.exe path (blank = auto):"] = "RobloxPlayerBeta.exe 路徑（空白 = 自動）：",
        ["Enable Multi-Roblox on startup"] = "啟動時啟用多開", ["OK"] = "確定", ["Cancel"] = "取消",
        ["Teleport-Controller URL:"] = "Teleport-Controller 位址：",
    };

    public static string Language { get; set; } = "zh";

    public static string T(string key)
        => Language != "zh" ? key : Zh.TryGetValue(key, out var v) ? v : ZhExtra.TryGetValue(key, out var v2) ? v2 : key;
}
