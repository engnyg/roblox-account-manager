using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Tests;

/// <summary>
/// Roblox 0.746：遊戲結束後會留一個 `RobloxPlayerBeta.exe --launch-to-tray` 常駐，
/// 之後我們啟動的行程只是中轉，真正的遊戲跑在常駐行程裡（命令列看不出 tracker，要從日誌讀）。
/// </summary>
[Collection("native")]
public class TrayHostTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ramv3_logs_" + Guid.NewGuid().ToString("N"));

    public TrayHostTests() { Directory.CreateDirectory(_dir); RobloxProcesses.LogsDirOverride = _dir; }
    public void Dispose()
    {
        RobloxProcesses.LogsDirOverride = null;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string WriteLog(string stampUtc, string suffix, string text, string kind = "Player")
    {
        var p = Path.Combine(_dir, $"0.746.0.7461267_{stampUtc}Z_{kind}_{suffix}_last.log");
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void IsTrayHost_recognises_the_background_process_only()
    {
        Assert.True(RobloxProcesses.IsTrayHost("\"C:\\x\\RobloxPlayerBeta.exe\" --launch-to-tray"));
        Assert.False(RobloxProcesses.IsTrayHost("\"C:\\x\\RobloxPlayerBeta.exe\" roblox-player:1+launchmode:play"));
        Assert.False(RobloxProcesses.IsTrayHost(null));
        // 中轉行程（帶 roblox-player: URI）仍照舊解析，不當成常駐行程
        Assert.Equal("123", RobloxProcesses.ParseCommandLine("RobloxPlayerBeta.exe roblox-player:1+browsertrackerid:123+x"));
        Assert.Null(RobloxProcesses.ParseCommandLine("RobloxPlayerBeta.exe --launch-to-tray"));
    }

    [Fact]
    public void Tracker_is_read_from_the_web_launch_line_and_the_latest_join_wins()
    {
        var text = string.Join('\n',
            "2026-11-08T12:04:24.398Z [FLog::LogWin32BTId] LogWin32BTId, App, cookie Native => Engine set RBXEventTrackerV2 as CreateDate=x&rbxid=1&rbxuid=2&browserid=111",
            "2026-11-08T12:05:45.857Z,Warning [FLog::LogWin32BTId] LogWin32BTId, App, web launch, websiteBTId is 222",
            "2026-11-08T12:09:00.000Z,Warning [FLog::LogWin32BTId] LogWin32BTId, App, web launch, websiteBTId is 333");
        Assert.Equal("333", RobloxProcesses.TrackerFromLogText(text));
        Assert.Equal("111", RobloxProcesses.TrackerFromLogText("... set RBXEventTrackerV2 as CreateDate=x&browserid=111"));
        Assert.Null(RobloxProcesses.TrackerFromLogText("nothing here"));
    }

    [Fact]
    public void Host_log_is_matched_by_start_time_and_crash_handler_logs_are_ignored()
    {
        var start = new DateTime(2026, 11, 8, 12, 4, 22, DateTimeKind.Utc);
        WriteLog("20261108T120130", "OLD", "web launch, websiteBTId is 999");                       // 太早：別的行程
        WriteLog("20261108T120424", "41C91_CrashHandler", "web launch, websiteBTId is 888");   // 真實命名：..._Player_41C91_CrashHandler_last.log
        WriteLog("20261108T120424", "HOST", "x\nweb launch, websiteBTId is 113931521064\n");        // 比行程啟動晚 2 秒
        WriteLog("20261108T120500", "NEXT", "web launch, websiteBTId is 777");                     // 太晚：下一個行程
        Assert.Equal("113931521064", RobloxProcesses.TrackerFromHostLog(start.ToLocalTime()));
    }

    [Fact]
    public void Host_log_returns_null_when_no_log_matches()
    {
        WriteLog("20261108T090000", "A", "web launch, websiteBTId is 1");
        Assert.Null(RobloxProcesses.TrackerFromHostLog(new DateTime(2026, 11, 8, 12, 4, 22, DateTimeKind.Utc).ToLocalTime()));
    }

    [Fact]
    public void Tracker_can_be_taken_from_a_launcher_uri()
    {
        Assert.Equal("113931521064", RobloxLauncher.TrackerOfUri("roblox-player:1+launchmode:play+browsertrackerid:113931521064+robloxLocale:en_us"));
        Assert.Null(RobloxLauncher.TrackerOfUri("roblox-player:1+launchmode:play"));
    }

    [Fact]
    public async Task Supervisor_does_not_relaunch_when_the_launched_pid_was_only_a_relay()
    {
        var acc = new Account { Username = "alice", BrowserTrackerId = "555" };
        int relaunches = 0;
        var sup = new RelaunchSupervisor
        {
            Grace = TimeSpan.Zero,
            IsAlive = _ => false,                       // 中轉行程早就結束了
            HasRunningInstance = a => a.BrowserTrackerId == "555",   // 但這個帳號的實例還在
            Relauncher = (_, _) => { relaunches++; return Task.FromResult(new JoinResult(true, "ok")); },
        };
        sup.Register(acc, new LaunchTarget(TargetKind.Place, 123), pid: 42);
        Assert.Equal(0, await sup.TickAsync(DateTime.Now.AddMinutes(1)));
        Assert.Equal(0, relaunches);
        Assert.Equal(1, sup.TrackedCount);              // 仍在追蹤，之後真的斷線才會重開

        sup.HasRunningInstance = _ => false;            // 實例真的不見了
        Assert.Equal(1, await sup.TickAsync(DateTime.Now.AddMinutes(1)));
        Assert.Equal(1, relaunches);
    }
}
