using System.Diagnostics;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Tests;

/// <summary>
/// FPS 上限有兩個地方：
///  • ClientAppSettings.json 的 DFIntTaskSchedulerTargetFps —— 只在 Roblox「啟動時」讀取，而且新版 Roblox 會直接拒絕
///    （日誌：Denied local configuration for: DFIntTaskSchedulerTargetFps），所以單靠它沒有用。
///  • GlobalBasicSettings_13.xml 的 FramerateCap —— Roblox 官方的「幀率上限」設定，這個才會真的生效。
/// 每次啟動實例前，都要把已儲存的設定寫進這兩處。測試一律隔離到暫存資料夾，不會碰到真正的 Roblox 設定。
/// </summary>
[Collection("native")]
public class FastFlagsApplyTests : IDisposable
{
    private const string Xml =
        "<roblox version=\"4\">\r\n<Item class=\"UserGameSettings\" referent=\"RBX0\">\r\n<Properties>\r\n" +
        "<bool name=\"Fullscreen\">false</bool>\r\n<int name=\"FramerateCap\">999</int>\r\n<int name=\"GraphicsQualityLevel\">3</int>\r\n" +
        "</Properties>\r\n</Item>\r\n</roblox>\r\n";

    private readonly string _dir;
    private readonly string _exe;
    private readonly string _json;
    private readonly string _xml;

    public FastFlagsApplyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ramv3_ff_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _exe = Path.Combine(_dir, "RobloxPlayerBeta.exe");
        _json = Path.Combine(_dir, "ClientSettings", "ClientAppSettings.json");
        _xml = Path.Combine(_dir, "GlobalBasicSettings_13.xml");
        FastFlags.GlobalSettingsOverride = _xml;       // 絕不碰真正的 %LOCALAPPDATA%\Roblox
        FastFlags.PathOverride = _json;
    }

    public void Dispose()
    {
        FastFlags.GlobalSettingsOverride = null;
        FastFlags.PathOverride = null;
        try { if (File.Exists(_xml)) File.SetAttributes(_xml, FileAttributes.Normal); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static AppSettings Saved(int fps = 0, string renderer = "Default", int quality = 0) =>
        new() { FpsCap = fps, Renderer = renderer, QualityLevel = quality };

    private static int? Fps(string jsonPath) => (int?)JsonNode.Parse(File.ReadAllText(jsonPath))!["DFIntTaskSchedulerTargetFps"];

    // ---------------- ClientAppSettings.json ----------------

    [Fact]
    public void ApplySaved_writes_fps_next_to_the_given_exe_and_keeps_other_flags()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_json)!);
        File.WriteAllText(_json, """{"FFlagSomethingElse": true}""");

        Assert.True(FastFlags.ApplySaved(Saved(fps: 30), _exe));

        var o = JsonNode.Parse(File.ReadAllText(_json))!.AsObject();
        Assert.Equal(30, (int?)o["DFIntTaskSchedulerTargetFps"]);
        Assert.Equal(true, (bool?)o["FFlagSomethingElse"]);
    }

    [Fact]
    public void ApplySaved_replaces_a_stale_value_written_by_someone_else()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_json)!);
        File.WriteAllText(_json, """{"DFIntTaskSchedulerTargetFps": 10}""");
        FastFlags.ApplySaved(Saved(fps: 60), _exe);
        Assert.Equal(60, Fps(_json));
    }

    [Fact]
    public void ApplySaved_with_no_cap_configured_does_not_touch_anything()
    {
        File.WriteAllText(_xml, Xml);
        Assert.True(FastFlags.ApplySaved(Saved(), _exe));
        Assert.False(File.Exists(_json));                       // 什麼都不用寫，就不要建檔
        Assert.Equal(Xml, File.ReadAllText(_xml));              // 也不動官方設定

        Directory.CreateDirectory(Path.GetDirectoryName(_json)!);
        File.WriteAllText(_json, """{"DFIntTaskSchedulerTargetFps": 10}""");
        FastFlags.ApplySaved(Saved(), _exe);
        Assert.Equal(10, Fps(_json));                           // 0 = 沒設定，不清掉別人手動設的值
    }

    [Fact]
    public void ApplySaved_also_reapplies_renderer_and_quality()
    {
        FastFlags.ApplySaved(Saved(renderer: "Vulkan", quality: 5), _exe);
        var o = JsonNode.Parse(File.ReadAllText(_json))!.AsObject();
        Assert.Equal(true, (bool?)o["FFlagDebugGraphicsPreferVulkan"]);
        Assert.Equal(5, (int?)o["DFIntDebugFRMQualityLevelOverride"]);
    }

    // ---------------- GlobalBasicSettings_13.xml（真正會生效的那個）----------------

    [Fact]
    public void ApplySaved_sets_the_official_FramerateCap_and_leaves_the_rest_of_the_xml_alone()
    {
        File.WriteAllText(_xml, Xml);

        FastFlags.ApplySaved(Saved(fps: 30), _exe);

        Assert.Equal(Xml.Replace("<int name=\"FramerateCap\">999</int>", "<int name=\"FramerateCap\">30</int>"), File.ReadAllText(_xml));
    }

    [Fact]
    public void FramerateCap_is_inserted_when_the_xml_does_not_have_it_yet()
    {
        File.WriteAllText(_xml, Xml.Replace("<int name=\"FramerateCap\">999</int>\r\n", ""));

        FastFlags.ApplySaved(Saved(fps: 45), _exe);

        var text = File.ReadAllText(_xml);
        Assert.Contains("<int name=\"FramerateCap\">45</int>", text);
        Assert.Contains("<bool name=\"Fullscreen\">false</bool>", text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "FramerateCap"));
    }

    [Fact]
    public void FramerateCap_is_written_even_when_the_file_is_read_only_and_the_lock_is_put_back()
    {
        // 實際遇到的情況：使用者（或某個工具）把 GlobalBasicSettings_13.xml 設成唯讀來鎖住 999，一般寫入會被拒絕
        File.WriteAllText(_xml, Xml);
        File.SetAttributes(_xml, FileAttributes.ReadOnly | FileAttributes.Archive);

        FastFlags.ApplySaved(Saved(fps: 20), _exe);

        Assert.Contains("<int name=\"FramerateCap\">20</int>", File.ReadAllText(_xml));
        Assert.True(new FileInfo(_xml).IsReadOnly);              // 寫完恢復唯讀，Roblox 結束時一樣改不掉它
        Assert.Equal(20, FastFlags.ReadFramerateCap());
    }

    [Fact]
    public void ReadFramerateCap_returns_null_when_there_is_no_file()
    {
        Assert.Null(FastFlags.ReadFramerateCap());
    }

    [Fact]
    public void FramerateCap_never_creates_the_official_settings_file()
    {
        // Roblox 還沒跑過就沒有這個檔：不要自己建一個不完整的
        FastFlags.ApplySaved(Saved(fps: 30), _exe);
        Assert.False(File.Exists(_xml));
        Assert.Equal(30, Fps(_json));                           // 但旗標檔照寫
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(144, 144)]
    [InlineData(5000, 999)]      // 超過上限就等於不限制（Roblox 用 999 代表不限制）
    public void FramerateCap_is_clamped_to_what_roblox_understands(int configured, int written)
    {
        File.WriteAllText(_xml, Xml);
        FastFlags.SetFpsCap(configured);
        Assert.Contains($"<int name=\"FramerateCap\">{written}</int>", File.ReadAllText(_xml));
    }

    [Fact]
    public void Setting_the_cap_to_zero_restores_unlimited_in_the_official_setting()
    {
        File.WriteAllText(_xml, Xml.Replace("999", "30"));
        FastFlags.SetFpsCap(0);
        Assert.Contains("<int name=\"FramerateCap\">999</int>", File.ReadAllText(_xml));
    }

    // ---------------- 啟動 ----------------

    [Fact]
    public void Launch_writes_the_saved_fps_cap_before_starting()
    {
        var stub = Path.Combine(_dir, "FakeRoblox.cmd");
        File.WriteAllText(stub, "@echo off\r\nping -n 30 127.0.0.1 >nul\r\n");
        File.WriteAllText(_xml, Xml);
        var data = Path.Combine(_dir, "data");
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
            Assert.Equal(24, Fps(Path.Combine(_dir, "ClientSettings", "ClientAppSettings.json")));
            Assert.Contains("<int name=\"FramerateCap\">24</int>", File.ReadAllText(_xml));
        }
        finally
        {
            RobloxLauncher.FindExeOverride = null;
            if (pid is not null) try { Process.GetProcessById(pid.Value).Kill(true); } catch { }
            Environment.SetEnvironmentVariable("RAM_V3_DATA", null);
            AppSettings.Reload();
        }
    }
}
