using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobloxAccountManager.Core.Services;

/// <summary>應用程式設定（JSON）。檔案位置：%LOCALAPPDATA%\RobloxAccountManagerV3\config.json，可用環境變數 RAM_V3_DATA 改位置。</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "zh";
    public double AccountJoinDelay { get; set; } = 8.0;
    public string LastPlaceId { get; set; } = "";

    // ---- 啟動 ----
    public bool DirectLaunch { get; set; } = true;
    public string RobloxExePath { get; set; } = "";
    public bool MultiRobloxOnStartup { get; set; } = true;

    // ---- 資源限制預設值（0 = 不限制）----
    public int DefaultCpuPercent { get; set; }
    public int DefaultMemoryMb { get; set; }
    public bool AutoApplyLimits { get; set; } = true;

    // ---- 視窗大小 ----
    public bool WindowApplyOnLaunch { get; set; }
    public int WindowWidth { get; set; } = 800;
    public int WindowHeight { get; set; } = 600;
    public bool WindowTile { get; set; } = true;

    // ---- 其他 ----
    public int FpsCap { get; set; }
    public bool AntiAfkEnabled { get; set; }
    public int AntiAfkIntervalSeconds { get; set; } = 120;
    public string TeleportControllerUrl { get; set; } = "http://127.0.0.1:8765/config";

    // ---- 內建 Web API（與 V2 相同：port 7963，可選密碼）----
    public bool WebApiEnabled { get; set; }
    public int WebApiPort { get; set; } = 7963;
    public string WebApiPassword { get; set; } = "";
    /// <summary>允許區網裝置（手機等）連到網頁控制面板；必須同時設定密碼才會生效。</summary>
    public bool WebApiAllowLan { get; set; }

    // ---- Nexus（WebSocket，port 7964）----
    public bool NexusEnabled { get; set; }
    public int NexusPort { get; set; } = 7964;
    public string NexusPassword { get; set; } = "";

    // ---- 行程監控（RobloxWatcher）----
    public bool WatcherEnabled { get; set; }
    public bool WatcherCloseIfMemoryLow { get; set; }
    public int WatcherMemoryLowMb { get; set; } = 200;

    // ---- 效能（MultiRoblox-RAM 的 Performance 設定）----
    public bool LowerPriority { get; set; } = true;          // 2 個以上實例時自動降為 Below Normal
    public bool BlockCrashHandler { get; set; }              // 結束 RobloxCrashHandler.exe（多開省資源）
    public bool AutoTrimEnabled { get; set; }                // 定時釋放記憶體
    public int AutoTrimMinutes { get; set; } = 10;
    public string Renderer { get; set; } = "Default";        // Default / D3D11 / OpenGL / Vulkan
    public int QualityLevel { get; set; }                    // 0 = 不覆寫，1~21
    public string Channel { get; set; } = "";                // Roblox channel（空白 = 預設）
    public bool RelaunchOnDisconnect { get; set; }           // 斷線 / 當掉時自動重新加入同一個遊戲

    // ---- 追蹤（定時截圖送 Discord webhook）----
    public bool TrackingEnabled { get; set; }
    public string TrackingWebhookUrl { get; set; } = "";
    public int TrackingIntervalMinutes { get; set; } = 5;
    /// <summary>每個用戶名的擷取區域，格式 "x,y,w,h"（相對視窗左上角的像素）；沒有設定 = 整個視窗。</summary>
    public Dictionary<string, List<string>> TrackingRegions { get; set; } = new();

    // ---- 外觀 / 介面 ----
    public string Theme { get; set; } = "System";
    public bool ClickSounds { get; set; }
    public int ClickVolume { get; set; } = 50;
    public string ClickSoundFile { get; set; } = "";
    public bool HideToTray { get; set; }
    public string AccountLayout { get; set; } = "List";      // List / Tiles

    /// <summary>勾選要一起啟動的帳號（用戶名）。</summary>
    public List<string> CheckedUsernames { get; set; } = new();

    // ------------------------------------------------------------------ //

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly object Gate = new();
    private static AppSettings? _current;

    public static string DataDir
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("RAM_V3_DATA");
            var dir = !string.IsNullOrWhiteSpace(overridden)
                ? overridden
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobloxAccountManagerV3");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string FilePath => Path.Combine(DataDir, "config.json");

    public static AppSettings Current
    {
        get
        {
            lock (Gate) return _current ??= Load();
        }
    }

    /// <summary>測試用：重新從磁碟讀取。</summary>
    public static void Reload()
    {
        lock (Gate) _current = Load();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch
        {
            // 設定檔壞掉就退回預設值
        }
        return new AppSettings();
    }

    public void Save()
    {
        lock (Gate)
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
