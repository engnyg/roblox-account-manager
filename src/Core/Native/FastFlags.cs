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

        if (hasFps) SetFramerateCap(fps);   // 真正會生效的官方設定（DFIntTaskSchedulerTargetFps 新版 Roblox 會拒絕）

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

    /// <summary>
    /// FPS 上限：≤0 或 ≥10000 代表不限制（移除旗標）。
    /// 同時寫入 Roblox 官方的 FramerateCap（見 <see cref="SetFramerateCap"/>）——新版 Roblox 會拒絕 DFIntTaskSchedulerTargetFps，
    /// 真正生效的是 FramerateCap。回傳值只代表旗標檔（ClientAppSettings.json）是否寫成功。
    /// </summary>
    public static bool SetFpsCap(int fps)
    {
        var ok = Update(o =>
        {
            if (fps <= 0 || fps >= 10000) o.Remove(FpsFlag);
            else o[FpsFlag] = fps;
        });
        SetFramerateCap(fps);
        return ok;
    }

    /// <summary>Roblox 官方「幀率上限」設定所在的檔案。測試可用 <see cref="GlobalSettingsOverride"/> 覆寫。</summary>
    public static string? GlobalSettingsOverride { get; set; }

    internal static string? GlobalSettingsPath()
    {
        if (GlobalSettingsOverride is not null) return GlobalSettingsOverride;
        if (PathOverride is not null) return Path.Combine(Path.GetDirectoryName(PathOverride)!, "GlobalBasicSettings_13.xml");
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        return string.IsNullOrEmpty(local) ? null : Path.Combine(local, "Roblox", "GlobalBasicSettings_13.xml");
    }

    private static readonly System.Text.RegularExpressions.Regex FramerateCapRx =
        new("""(<int name="FramerateCap">)-?\d+(</int>)""", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 設定 GlobalBasicSettings_13.xml 的 FramerateCap（Roblox 遊戲內「幀率上限」的同一個設定，啟動時讀取）。
    /// ≤0 或 ≥999 代表不限制（Roblox 用 999）。檔案不存在（Roblox 還沒跑過）就不建立；只改這一個欄位，其餘原樣保留。
    /// </summary>
    public static bool SetFramerateCap(int fps)
    {
        var path = GlobalSettingsPath();
        if (path is null || !File.Exists(path)) return false;
        try
        {
            var cap = fps <= 0 || fps >= 999 ? 999 : fps;
            var text = File.ReadAllText(path);
            string updated;
            if (FramerateCapRx.IsMatch(text)) updated = FramerateCapRx.Replace(text, $"${{1}}{cap}${{2}}", 1);
            else
            {
                var i = text.IndexOf("<Properties>", StringComparison.Ordinal);
                if (i < 0) return false;
                i += "<Properties>".Length;
                updated = text.Insert(i, $"\r\n<int name=\"FramerateCap\">{cap}</int>");
            }
            if (updated != text) WriteKeepingReadOnly(path, updated);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 有些人（或工具）會把設定檔設成唯讀來鎖住幀率，一般寫入會被拒絕。這裡先暫時解除唯讀、寫完再恢復，
    /// 這樣「鎖」還在（Roblox 結束時一樣改不掉它），但我們設定的值也寫得進去。
    /// </summary>
    private static void WriteKeepingReadOnly(string path, string text)
    {
        var attrs = File.GetAttributes(path);
        var readOnly = (attrs & FileAttributes.ReadOnly) != 0;
        if (readOnly) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        try { File.WriteAllText(path, text); }
        finally { if (readOnly) File.SetAttributes(path, attrs); }
    }

    /// <summary>目前 GlobalBasicSettings_13.xml 裡的 FramerateCap；沒有檔案或沒有這個欄位回傳 null（介面用來確認有沒有寫成功）。</summary>
    public static int? ReadFramerateCap()
    {
        try
        {
            var path = GlobalSettingsPath();
            if (path is null || !File.Exists(path)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), """<int name="FramerateCap">(-?\d+)</int>""");
            return m.Success ? int.Parse(m.Groups[1].Value) : null;
        }
        catch { return null; }
    }

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
