using System.Net.Http.Headers;
using System.Text.Json;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;

namespace RobloxAccountManager.Core.Services;

/// <summary>
/// 追蹤：定時對每個執行中的 Roblox 視窗截圖（整個視窗，或設定的擷取區域），上傳到 Discord webhook。
/// 對應 MultiRoblox-RAM 的 Tracking 分頁。Webhook 網址由使用者自行設定。
/// </summary>
public sealed class ScreenshotTracker : IDisposable
{
    private readonly Func<IReadOnlyList<Account>> _getAccounts;
    private Timer? _timer;
    private int _busy;

    /// <summary>測試用：替換實際上傳。</summary>
    public Func<string, string, byte[], string, CancellationToken, Task<bool>> Uploader { get; set; } = PostToWebhookAsync;

    /// <summary>測試用：替換擷取。</summary>
    public Func<int, (int W, int H, byte[] Bgra)?> Capturer { get; set; } = pid =>
    {
        var hwnd = WindowLayout.FindWindow(pid);
        return hwnd == IntPtr.Zero ? null : ScreenCapture.CaptureWindow(hwnd);
    };

    public Func<List<RobloxInstance>> ListInstances { get; set; } = RobloxProcesses.List;

    public event Action<string, bool, string>? Result; // (username, ok, message)

    public ScreenshotTracker(Func<IReadOnlyList<Account>> getAccounts) { _getAccounts = getAccounts; }

    public static bool IsValidWebhook(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Length > 0;

    /// <summary>執行一次：回傳送出的圖片數。</summary>
    public async Task<int> CaptureOnceAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (!IsValidWebhook(settings.TrackingWebhookUrl)) return 0;
        var accounts = _getAccounts();
        int sent = 0;
        foreach (var inst in ListInstances())
        {
            ct.ThrowIfCancellationRequested();
            var acc = inst.TrackerId.Length == 0 ? null : accounts.FirstOrDefault(a => a.BrowserTrackerId == inst.TrackerId);
            var name = acc?.Username ?? $"PID {inst.Pid}";

            var shot = Capturer(inst.Pid);
            if (shot is null) { Result?.Invoke(name, false, "擷取失敗（視窗不存在或被最小化）"); continue; }
            var (w, h, bgra) = shot.Value;

            var regions = new List<(string Label, (int W, int H, byte[] Bgra) Img)>();
            if (acc is not null && settings.TrackingRegions.TryGetValue(acc.Username, out var list) && list.Count > 0)
            {
                int i = 1;
                foreach (var text in list)
                {
                    if (ScreenCapture.TryParseRegion(text, out var r) && ScreenCapture.Crop(w, h, bgra, r) is { } c)
                        regions.Add(($"{name} · region {i}", c));
                    i++;
                }
            }
            if (regions.Count == 0) regions.Add((name, (w, h, bgra)));

            foreach (var (label, img) in regions)
            {
                var png = ScreenCapture.EncodePng(img.W, img.H, img.Bgra);
                bool ok = await Uploader(settings.TrackingWebhookUrl, label, png, $"{label}.png", ct);
                Result?.Invoke(name, ok, ok ? "已上傳" : "上傳失敗");
                if (ok) sent++;
            }
        }
        return sent;
    }

    public static async Task<bool> PostToWebhookAsync(string url, string label, byte[] png, string fileName, CancellationToken ct)
    {
        try
        {
            using var http = RobloxApi.CreateClient();
            using var form = new MultipartFormDataContent
            {
                { new StringContent(JsonSerializer.Serialize(new { content = $"**{label}** · {DateTime.Now:yyyy-MM-dd HH:mm:ss}" }),
                    System.Text.Encoding.UTF8, "application/json"), "payload_json" },
            };
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "files[0]", fileName.Replace('"', '_'));
            using var resp = await http.PostAsync(url, form, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    public void Start(AppSettings settings)
    {
        Stop();
        var iv = TimeSpan.FromMinutes(Math.Max(1, settings.TrackingIntervalMinutes));
        _timer = new Timer(async _ =>
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try { await CaptureOnceAsync(settings); } catch { } finally { Interlocked.Exchange(ref _busy, 0); }
        }, null, iv, iv);
    }

    public void Stop() { _timer?.Dispose(); _timer = null; }
    public void Dispose() => Stop();
}
