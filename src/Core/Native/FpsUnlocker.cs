namespace RobloxAccountManager.Core.Native;

/// <summary>透過修改 ClientSettings/ClientAppSettings.json 來設定 Roblox FPS 上限（0 = 不限制）。實作見 <see cref="FastFlags"/>。</summary>
public static class FpsUnlocker
{
    /// <summary>測試用：覆寫設定檔位置。</summary>
    public static string? PathOverride
    {
        get => FastFlags.PathOverride;
        set => FastFlags.PathOverride = value;
    }

    public static bool SetFpsCap(int fps) => FastFlags.SetFpsCap(fps);
}
