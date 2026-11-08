using NAudio.CoreAudioApi;

namespace RobloxAccountManager.Core.Native;

/// <summary>
/// 每個行程的系統音量（Windows Core Audio 工作階段）。對應 V2 manager/mixer.py 的 pycaw 功能。
/// 只對「已經有音訊工作階段」的行程有效（Roblox 載入遊戲、開始發聲後才會出現）。
/// 沒有音訊裝置時所有方法都安全地回傳 null / false。
/// </summary>
public static class AudioSessions
{
    private static List<(uint Pid, AudioSessionControl Session)> Snapshot(MMDevice device)
    {
        var list = new List<(uint, AudioSessionControl)>();
        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            try
            {
                var s = sessions[i];
                list.Add((s.GetProcessID, s));
            }
            catch { /* 工作階段在列舉過程中消失 */ }
        }
        return list;
    }

    private static T? WithSessions<T>(int pid, Func<IEnumerable<AudioSessionControl>, T?> body)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var mine = Snapshot(device).Where(x => x.Pid == (uint)pid).Select(x => x.Session).ToList();
            return mine.Count == 0 ? default : body(mine);
        }
        catch { return default; }
    }

    /// <summary>0.0~1.0；找不到該行程的音訊工作階段回傳 null。</summary>
    public static float? GetVolume(int pid) =>
        WithSessions<float?>(pid, s => s.First().SimpleAudioVolume.Volume);

    public static bool? IsMuted(int pid) =>
        WithSessions<bool?>(pid, s => s.First().SimpleAudioVolume.Mute);

    public static bool SetVolume(int pid, float volume) =>
        WithSessions<bool?>(pid, sessions =>
        {
            var v = Math.Clamp(volume, 0f, 1f);
            foreach (var s in sessions) s.SimpleAudioVolume.Volume = v;
            return true;
        }) == true;

    public static bool SetMuted(int pid, bool muted) =>
        WithSessions<bool?>(pid, sessions =>
        {
            foreach (var s in sessions) s.SimpleAudioVolume.Mute = muted;
            return true;
        }) == true;
}
