using System.Net;
using System.Net.Sockets;
using System.Windows.Threading;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.App;

/// <summary>
/// 背景服務的統一管理：斷線自動重開、行程監控、定時釋放記憶體、多開降優先權、定時截圖追蹤、內建 Web API、Nexus。
/// 依 <see cref="AppSettings"/> 啟動或停止；設定改變後呼叫 <see cref="Apply"/> 即可。
/// </summary>
public sealed class BackgroundServices : IDisposable
{
    private readonly Func<IReadOnlyList<Account>> _accounts;
    private readonly Func<Account?> _selected;
    private readonly DispatcherTimer _trimTimer = new();
    private readonly DispatcherTimer _priorityTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public RelaunchSupervisor Relaunch { get; } = new();
    public RobloxWatcher Watcher { get; } = new();
    public ScreenshotTracker Tracker { get; }
    public WebApiServer? Api { get; private set; }
    public NexusServer? Nexus { get; private set; }

    public BackgroundServices(
        Func<IReadOnlyList<Account>> accounts, Func<Account?> selected,
        Func<Account, LaunchTarget, Task<JoinResult>> relauncher)
    {
        _accounts = accounts;
        _selected = selected;
        Tracker = new ScreenshotTracker(accounts);
        Tracker.Result += (name, ok, msg) => (ok ? (Action<string>)AppLog.Info : AppLog.Warn)($"[追蹤] {name}: {msg}");

        Relaunch.Relauncher = relauncher;
        Relaunch.Relaunched += (a, r) => AppLog.Add(r.Ok ? "INFO" : "WARN", $"[自動重開] {a.Username}: {r.Message}");
        Relaunch.GaveUp += (a, why) => AppLog.Warn($"[自動重開] {a.Username}: {why}");

        Watcher.Killed += (pid, why) => AppLog.Warn($"[監控] 已結束 PID {pid}：{why}");

        _trimTimer.Tick += (_, _) =>
        {
            var pids = RobloxProcesses.List().Select(i => i.Pid).ToList();
            int n = ProcessTuning.TrimMemory(pids);
            AppLog.Info($"[記憶體] 定時釋放：{n}/{pids.Count} 個實例");
        };
        _priorityTimer.Tick += (_, _) =>
        {
            if (!AppSettings.Current.LowerPriority) return;
            _ = Task.Run(() =>
            {
                var pids = RobloxProcesses.List().Select(i => i.Pid).ToList();
                ProcessTuning.ApplyLowerPriority(pids, true);
            });
        };
    }

    /// <summary>依目前設定啟動 / 停止各項服務。</summary>
    public void Apply(AppSettings s)
    {
        // 斷線自動重開
        if (s.RelaunchOnDisconnect) Relaunch.Start(); else { Relaunch.Stop(); Relaunch.ForgetAll(); }

        // 行程監控
        Watcher.CloseIfMemoryLow = s.WatcherCloseIfMemoryLow;
        Watcher.MemoryLowMb = Math.Max(1, s.WatcherMemoryLowMb);
        if (s.WatcherEnabled) Watcher.Start(); else Watcher.Stop();

        // 定時釋放記憶體
        _trimTimer.Stop();
        if (s.AutoTrimEnabled)
        {
            _trimTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, s.AutoTrimMinutes));
            _trimTimer.Start();
        }

        // 多開降優先權
        if (s.LowerPriority) _priorityTimer.Start(); else _priorityTimer.Stop();

        // 追蹤
        if (s.TrackingEnabled && ScreenshotTracker.IsValidWebhook(s.TrackingWebhookUrl)) Tracker.Start(s); else Tracker.Stop();

        // Web API
        RestartApi(s);
        // Nexus
        RestartNexus(s);

        // 設定檔裡的 FPS / 渲染 / 畫質旗標
        if (s.Renderer != "Default" && Enum.TryParse<Renderer>(s.Renderer, out var r)) FastFlags.SetRenderer(r);
        if (s.QualityLevel > 0) FastFlags.SetQualityLevel(s.QualityLevel);
    }

    /// <summary>網頁控制面板的操作（主視窗）。</summary>
    public IWebControl? WebControl { get; set; }

    private string _apiKey = "";

    private void RestartApi(AppSettings s)
    {
        // 連接埠、密碼或區網設定改了都要重開
        var key = $"{s.WebApiPort}|{s.WebApiPassword}|{s.WebApiAllowLan}";
        if (Api is { } old && (!s.WebApiEnabled || key != _apiKey)) { old.Stop(); Api = null; }
        if (!s.WebApiEnabled || Api is not null) return;
        _apiKey = key;
        try
        {
            Api = new WebApiServer(s.WebApiPort, s.WebApiPassword, () => _accounts(), _selected, s.WebApiAllowLan)
            {
                Control = WebControl,
                Mixer = new InstanceMixer(() => _accounts()) { BeforeKill = pid => Relaunch.ForgetPid(pid) },
                JoinHandler = async (acc, place, job) =>
                {
                    var r = await GameLauncher.JoinServerAsync(acc, place, job);
                    if (r.Ok) Relaunch.Register(acc, string.IsNullOrEmpty(job) ? new LaunchTarget(TargetKind.Place, place) : new LaunchTarget(TargetKind.Job, place, job), r.Pid);
                    return r;
                },
            };
            Api.Start();
            AppLog.Info($"[網頁控制] 已啟動 {LocalUrl(s.WebApiPort)}");
            if (Api.Lan)
                foreach (var ip in LanAddresses()) AppLog.Info($"[網頁控制] 區網裝置請開 http://{ip}:{s.WebApiPort}");
            else if (Api.LanRequested)
                AppLog.Warn("[網頁控制] 已勾選「允許區網連線」但沒有設定密碼，為了安全只開放本機。請在 設定 → 服務 設定密碼。");
        }
        catch (Exception e) when (e is SocketException or InvalidOperationException)
        {
            Api = null;
            _apiKey = "";
            AppLog.Error($"[網頁控制] 啟動失敗（port {s.WebApiPort} 可能已被占用）：{e.Message}");
        }
    }

    public static string LocalUrl(int port) => $"http://localhost:{port}/";

    /// <summary>這台電腦的區網 IPv4（排除迴路與 APIPA）。</summary>
    public static List<string> LanAddresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address.ToString();
                    if (ip.StartsWith("169.254.")) continue;
                    // 虛擬網卡（VMware / Hyper-V / VirtualBox）放最後，手機通常用不到
                    bool virt = ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase)
                        || ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                        || ni.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase);
                    if (virt) result.Add(ip); else result.Insert(0, ip);
                }
            }
        }
        catch { }
        return result.Distinct().ToList();
    }

    private void RestartNexus(AppSettings s)
    {
        if (Nexus is { } old && (!s.NexusEnabled || old.Port != s.NexusPort)) { old.Stop(); Nexus = null; }
        if (!s.NexusEnabled || Nexus is not null) return;
        try
        {
            Nexus = new NexusServer(s.NexusPort, s.NexusPassword);
            Nexus.AccountConnected += a => AppLog.Info($"[Nexus] {a.Username} 已連線 (job {a.JobId})");
            Nexus.AccountDisconnected += a => AppLog.Info($"[Nexus] {a.Username} 已中斷");
            Nexus.Start();
            AppLog.Info($"[Nexus] 已啟動 ws://localhost:{s.NexusPort}");
        }
        catch (Exception e) when (e is HttpListenerException or InvalidOperationException)
        {
            Nexus = null;
            AppLog.Error($"[Nexus] 啟動失敗（port {s.NexusPort} 可能已被占用）：{e.Message}");
        }
    }

    public void Dispose()
    {
        _trimTimer.Stop();
        _priorityTimer.Stop();
        Relaunch.Dispose();
        Watcher.Dispose();
        Tracker.Dispose();
        Api?.Dispose();
        Nexus?.Dispose();
    }
}
