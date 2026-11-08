using System.Text.Json;
using System.Text.Json.Nodes;

namespace RobloxAccountManager.Core.Native;

public enum Renderer { Default, D3D11, OpenGL, Vulkan }

/// <summary>
/// 讀寫 Roblox 的 ClientSettings/ClientAppSettings.json（Fast Flags）：FPS 上限、渲染引擎、畫質等級。
/// 功能對應 MultiRoblox-RAM 的 Mixer / Performance 設定。
/// </summary>
public static class FastFlags
{
    public const string FpsFlag = "DFIntTaskSchedulerTargetFps";
    public const string QualityFlag = "DFIntDebugFRMQualityLevelOverride";
    private static readonly string[] RendererFlags =
        ["FFlagDebugGraphicsPreferD3D11", "FFlagDebugGraphicsPreferOpenGL", "FFlagDebugGraphicsPreferVulkan"];

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>測試用：覆寫設定檔位置。</summary>
    public static string? PathOverride { get; set; }

    internal static string? SettingsPath()
    {
        if (PathOverride is not null) return PathOverride;
        var exe = RobloxLauncher.FindRobloxExe();
        if (exe is null) return null;
        var dir = Path.Combine(Path.GetDirectoryName(exe)!, "ClientSettings");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ClientAppSettings.json");
    }

    public static JsonObject Read()
    {
        var path = SettingsPath();
        if (path is null) return new JsonObject();
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    /// <summary>讀出、套用修改、寫回。找不到 Roblox 安裝位置或寫入失敗回傳 false。</summary>
    public static bool Update(Action<JsonObject> mutate) => UpdateAt(SettingsPath(), mutate);

    private static bool UpdateAt(string? path, Action<JsonObject> mutate)
    {
        if (path is null) return false;
        try
        {
            JsonObject obj;
            try { obj = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
            catch (Exception e) when (e is FileNotFoundException or JsonException or DirectoryNotFoundException)
            { obj = new JsonObject(); }
            mutate(obj);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, obj.ToJsonString(Pretty));
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 把已儲存的 FPS 上限 / 渲染引擎 / 畫質寫進 <paramref name="exe"/> 旁邊的 ClientSettings（沒給 exe 就用 <see cref="SettingsPath"/> 找到的那個）。
    /// Roblox 只在「啟動時」讀這個檔，所以每次啟動實例前都要呼叫：Roblox 更新換了版本資料夾、或旗標被別的工具洗掉時才不會失效。
    /// 只寫「有設定」的項目（FPS &gt; 0、渲染引擎不是 Default、畫質 &gt; 0）；沒設定的不動，也不會清掉別人手動設的值。
    /// </summary>
    public static bool ApplySaved(Services.AppSettings s, string? exe = null)
    {
        var fps = s.FpsCap;
        var hasFps = fps > 0 && fps < 10000;
        var hasRenderer = s.Renderer != "Default" && Enum.TryParse<Renderer>(s.Renderer, out var r) && r != Renderer.Default;
        var hasQuality = s.QualityLevel > 0;
        if (!hasFps && !hasRenderer && !hasQuality) return true;   // 沒有要寫的，不建檔

        var path = PathOverride ?? (exe is null ? SettingsPath() : Path.Combine(Path.GetDirectoryName(exe)!, "ClientSettings", "ClientAppSettings.json"));
        return UpdateAt(path, o =>
        {
            if (hasFps) o[FpsFlag] = fps;
            if (hasRenderer)
            {
                Enum.TryParse<Renderer>(s.Renderer, out var rr);
                foreach (var f in RendererFlags) o.Remove(f);
                o[RendererFlags[(int)rr - 1]] = true;
            }
            if (hasQuality) o[QualityFlag] = Math.Clamp(s.QualityLevel, 1, 21);
        });
    }

    /// <summary>FPS 上限：≤0 或 ≥10000 代表不限制（移除旗標）。</summary>
    public static bool SetFpsCap(int fps) => Update(o =>
    {
        if (fps <= 0 || fps >= 10000) o.Remove(FpsFlag);
        else o[FpsFlag] = fps;
    });

    /// <summary>強制使用指定的渲染引擎；Default 則移除所有相關旗標。</summary>
    public static bool SetRenderer(Renderer r) => Update(o =>
    {
        foreach (var f in RendererFlags) o.Remove(f);
        switch (r)
        {
            case Renderer.D3D11: o[RendererFlags[0]] = true; break;
            case Renderer.OpenGL: o[RendererFlags[1]] = true; break;
            case Renderer.Vulkan: o[RendererFlags[2]] = true; break;
        }
    });

    public static Renderer GetRenderer()
    {
        var o = Read();
        for (int i = 0; i < RendererFlags.Length; i++)
            if (o[RendererFlags[i]]?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                return (Renderer)(i + 1);
        return Renderer.Default;
    }

    /// <summary>畫質等級 1~21；0 代表不覆寫（移除旗標）。</summary>
    public static bool SetQualityLevel(int level) => Update(o =>
    {
        if (level <= 0) o.Remove(QualityFlag);
        else o[QualityFlag] = Math.Clamp(level, 1, 21);
    });
}
