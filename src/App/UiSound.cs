using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.App;

/// <summary>可選的介面點擊音效（預設關閉）。內建音效是即時產生的短促「滴」聲；也可指定自己的 .wav。</summary>
public static class UiSound
{
    private static bool _registered;
    private static SoundPlayer? _player;
    private static string _key = "";

    /// <summary>產生 16-bit 單聲道 PCM WAV：很短的衰減正弦波。volume 0~100。</summary>
    public static byte[] MakeClickWav(int volume, int freq = 1400, int ms = 35)
    {
        const int rate = 44100;
        int n = rate * ms / 1000;
        using var ms2 = new MemoryStream();
        using var w = new BinaryWriter(ms2);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        double amp = 32767 * 0.6 * Math.Clamp(volume, 0, 100) / 100.0;
        for (int i = 0; i < n; i++)
        {
            double env = Math.Exp(-6.0 * i / n);
            w.Write((short)(Math.Sin(2 * Math.PI * freq * i / rate) * env * amp));
        }
        return ms2.ToArray();
    }

    public static void Configure(AppSettings s)
    {
        if (!_registered)
        {
            _registered = true;
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => Play()));
            EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent, new RoutedEventHandler((_, _) => Play()));
            EventManager.RegisterClassHandler(typeof(TabControl), Selector.SelectionChangedEvent, new RoutedEventHandler((_, e) => { if (e.OriginalSource is TabControl) Play(); }));
        }
        _key = "";
        _player?.Dispose();
        _player = null;
    }

    public static void Play()
    {
        var s = AppSettings.Current;
        if (!s.ClickSounds) return;
        try
        {
            var key = s.ClickSoundFile + "|" + s.ClickVolume;
            if (_player is null || key != _key)
            {
                _player?.Dispose();
                _player = s.ClickSoundFile.Length > 0 && File.Exists(s.ClickSoundFile)
                    ? new SoundPlayer(s.ClickSoundFile)
                    : new SoundPlayer(new MemoryStream(MakeClickWav(s.ClickVolume)));
                _player.Load();
                _key = key;
            }
            _player.Play();
        }
        catch { /* 沒有音效裝置或檔案壞掉就靜音 */ }
    }
}
