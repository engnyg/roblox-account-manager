using System.Diagnostics;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Tests;

/// <summary>
/// FPS 上限只會在 Roblox「啟動時」被讀取（日誌：LoadClientSettingsFromLocal）。
/// 所以每次啟動實例前，都要把已儲存的旗標寫到「即將啟動的那個 exe」旁邊，Roblox 更新或旗標被洗掉也不影響。
/// </summary>
[Collection("native")]
public class FastFlagsApplyTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ramv3_ff_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static AppSettings Saved(int fps = 0, string renderer = "Default", int quality = 0) =>
        new() { FpsCap = fps, Renderer = renderer, QualityLevel = quality };

    [Fact]
    public void ApplySaved_writes_fps_next_to_the_given_exe_and_keeps_other_flags()
    {
        var dir = TempDir();
        try
        {
            var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
            var file = Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """{"FFlagSomethingElse": true}""");

            Assert.True(FastFlags.ApplySaved(Saved(fps: 30), exe));

            var o = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            Assert.Equal(30, (int?)o["DFIntTaskSchedulerTargetFps"]);
            Assert.Equal(true, (bool?)o["FFlagSomethingElse"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ApplySaved_replaces_a_stale_value_written_by_someone_else()
    {
        var dir = TempDir();
        try
        {
            var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
            var file = Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """{"DFIntTaskSchedulerTargetFps": 10}""");

            FastFlags.ApplySaved(Saved(fps: 60), exe);

            Assert.Equal(60, (int?)JsonNode.Parse(File.ReadAllText(file))!["DFIntTaskSchedulerTargetFps"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ApplySaved_with_no_cap_configured_does_not_touch_an_existing_flag_or_create_a_file()
    {
        var dir = TempDir();
        try
        {
            var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
            var file = Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");

            Assert.True(FastFlags.ApplySaved(Saved(), exe));
            Assert.False(File.Exists(file));                       // 什麼都不用寫，就不要建檔

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """{"DFIntTaskSchedulerTargetFps": 10}""");
            FastFlags.ApplySaved(Saved(), exe);
            Assert.Equal(10, (int?)JsonNode.Parse(File.ReadAllText(file))!["DFIntTaskSchedulerTargetFps"]);   // 0 = 沒設定，不清掉別人手動設的值
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ApplySaved_also_reapplies_renderer_and_quality()
    {
        var dir = TempDir();
        try
        {
            var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
            FastFlags.ApplySaved(Saved(fps: 0, renderer: "Vulkan", quality: 5), exe);
            var o = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "ClientSettings", "ClientAppSettings.json")))!.AsObject();
            Assert.Equal(true, (bool?)o["FFlagDebugGraphicsPreferVulkan"]);
            Assert.Equal(5, (int?)o["DFIntDebugFRMQualityLevelOverride"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Launch_writes_the_saved_fps_cap_into_the_exe_folder_before_starting()
    {
        var dir = TempDir();
        var stub = Path.Combine(dir, "FakeRoblox.cmd");
        File.WriteAllText(stub, "@echo off\r\nping -n 30 127.0.0.1 >nul\r\n");
        var data = Path.Combine(dir, "data");
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("RAM_V3_DATA", data);
        AppSettings.Reload();
        AppSettings.Current.DirectLaunch = true;
        AppSettings.Current.FpsCap = 24;
        RobloxLauncher.FindExeOverride = () => stub;
        int? pid = null;
        try
        {
            var (p, how) = RobloxLauncher.Launch("roblox-player:1+launchmode:play+browsertrackerid:1");
            pid = p;
            Assert.Equal("direct", how);
            var file = Path.Combine(dir, "ClientSettings", "ClientAppSettings.json");
            Assert.True(File.Exists(file));
            Assert.Equal(24, (int?)JsonNode.Parse(File.ReadAllText(file))!["DFIntTaskSchedulerTargetFps"]);
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
