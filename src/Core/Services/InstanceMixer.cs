using System.Diagnostics;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;

namespace RobloxAccountManager.Core.Services;

/// <summary>網頁面板「混音台」的一個執行中 Roblox 實例。</summary>
public sealed record WebInstance(int Pid, string Account, double MemoryMb, double CpuPercent, int? Volume, bool Muted, string Priority, string Limit);

/// <summary>混音台目前的設定值（讓網頁的輸入框有預設值）。</summary>
public sealed record WebMixerSettings(
    int FpsCap, int CpuLimit, int RamLimit, bool AutoLimits,
    int WinWidth, int WinHeight, bool Tile, bool ApplyOnLaunch,
    string Renderer, int Quality, string[] Priorities, string[] Renderers);

/// <summary>網頁面板的混音台操作。</summary>
public interface IWebMixer
{
    Task<(List<WebInstance> Instances, WebMixerSettings Settings)> SnapshotAsync();
    /// <summary>執行一個動作；pids 空 = 全部實例。參數錯誤丟 <see cref="ArgumentException"/>。回傳給使用者看的結果。</summary>
    Task<string> ApplyAsync(string action, IReadOnlyList<int> pids, JsonNode? args);
}

/// <summary>
/// 混音台的實際操作（與桌面版混音台相同的底層功能）：音量 / 靜音、釋放記憶體、優先權、CPU・RAM 限制、
/// FPS 上限、視窗大小與排列、渲染器與畫質、結束實例。可在任何執行緒呼叫。
/// </summary>
public sealed class InstanceMixer : IWebMixer
{
    private readonly Func<IReadOnlyList<Account>> _accounts;
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _cpu = new();
    private readonly object _gate = new();

    /// <summary>結束實例前呼叫（讓自動重開忘記這個 PID，免得又被開回來）。</summary>
    public Action<int>? BeforeKill { get; set; }

    public InstanceMixer(Func<IReadOnlyList<Account>> accounts) => _accounts = accounts;

    public Task<(List<WebInstance> Instances, WebMixerSettings Settings)> SnapshotAsync() => Task.Run(() =>
    {
        var accounts = _accounts();
        var list = new List<WebInstance>();
        var now = DateTime.UtcNow;
        foreach (var inst in RobloxProcesses.List())
        {
            var name = inst.TrackerId.Length > 0
                ? accounts.FirstOrDefault(a => a.BrowserTrackerId == inst.TrackerId)?.Username ?? "未知"
                : "未知";
            double cpu = 0;
            try
            {
                using var p = Process.GetProcessById(inst.Pid);
                var total = p.TotalProcessorTime;
                lock (_gate)
                {
                    if (_cpu.TryGetValue(inst.Pid, out var prev) && (now - prev.At).TotalSeconds > 0)
                        cpu = (total - prev.Cpu).TotalSeconds / (now - prev.At).TotalSeconds / Environment.ProcessorCount * 100;
                    _cpu[inst.Pid] = (total, now);
                }
            }
            catch { }
            int? volume = null; bool muted = false;
            try
            {
                if (AudioSessions.GetVolume(inst.Pid) is { } v) volume = (int)Math.Round(v * 100);
                muted = AudioSessions.IsMuted(inst.Pid) == true;
            }
            catch { }
            list.Add(new WebInstance(inst.Pid, name, Math.Round(RobloxProcesses.MemoryMb(inst.Pid)), Math.Round(cpu, 1),
                volume, muted, ProcessTuning.GetPriority(inst.Pid)?.ToString() ?? "-",
                ResourceLimits.GetApplied(inst.Pid).Describe()));
        }
        lock (_gate)
            foreach (var gone in _cpu.Keys.Except(list.Select(i => i.Pid)).ToList()) _cpu.Remove(gone);

        var s = AppSettings.Current;
        var ws = WindowLayout.Load();
        var settings = new WebMixerSettings(
            s.FpsCap, s.DefaultCpuPercent, s.DefaultMemoryMb, s.AutoApplyLimits,
            ws.Width, ws.Height, ws.Tile, ws.ApplyOnLaunch,
            FastFlags.GetRenderer().ToString(), s.QualityLevel,
            ProcessTuning.PriorityChoices.Select(c => c.Name).ToArray(), Enum.GetNames<Renderer>());
        return (list.OrderBy(i => i.Account).ThenBy(i => i.Pid).ToList(), settings);
    });

    private static int Int(JsonNode? args, string key, int fallback = 0) =>
        int.TryParse(args?[key]?.ToString(), out var v) ? v : fallback;

    public Task<string> ApplyAsync(string action, IReadOnlyList<int> requested, JsonNode? args) => Task.Run(() =>
    {
        var running = RobloxProcesses.List().Select(i => i.Pid).ToList();
        // 只接受目前真的是 Roblox 的 PID，避免網頁送任意 PID 進來操作別的程式
        var pids = requested.Count == 0 ? running : requested.Where(running.Contains).ToList();
        string Count(int ok) => $"{ok}/{pids.Count}";
        var s = AppSettings.Current;

        switch (action)
        {
            case "volume":
            {
                int v = Math.Clamp(Int(args, "value", 100), 0, 100);
                int ok = pids.Count(p => AudioSessions.SetVolume(p, v / 100f));
                return $"音量 {v}% → {Count(ok)}" + (ok < pids.Count ? "（有的實例還沒有音訊工作階段）" : "");
            }
            case "mute":
            {
                bool m = args?["muted"]?.GetValue<bool>() ?? true;
                int ok = pids.Count(p => AudioSessions.SetMuted(p, m));
                return $"{(m ? "靜音" : "取消靜音")} → {Count(ok)}";
            }
            case "trim":
                return $"釋放記憶體 → {Count(ProcessTuning.TrimMemory(pids))}";
            case "priority":
            {
                var name = args?["value"]?.ToString() ?? "";
                var choice = ProcessTuning.PriorityChoices.FirstOrDefault(c => c.Name == name);
                if (choice.Name is null) throw new ArgumentException("不認得的優先權：" + name);
                return $"優先權 {choice.Name} → {Count(pids.Count(p => ProcessTuning.SetPriority(p, choice.Value)))}";
            }
            case "limits":
            {
                var limits = args?["clear"]?.GetValue<bool>() == true ? new Limits() : ResourceLimits.Clamp(Int(args, "cpu"), Int(args, "ram"));
                if (limits.Active)
                {
                    s.DefaultCpuPercent = limits.CpuPercent; s.DefaultMemoryMb = limits.MemoryMb;
                }
                if (args?["auto"] is { } auto) s.AutoApplyLimits = auto.GetValue<bool>();
                s.Save();
                int ok = 0; var errors = new List<string>();
                foreach (var p in pids)
                {
                    try { ResourceLimits.Apply(p, limits); ok++; }
                    catch (LimitException e) { errors.Add($"{p}: {e.Message}"); }
                }
                return $"{(limits.Active ? limits.Describe() : "清除限制")} → {Count(ok)}" + (errors.Count > 0 ? "；" + string.Join("；", errors) : "");
            }
            case "kill":
            {
                if (requested.Count == 0 && args?["all"]?.GetValue<bool>() != true)
                    throw new ArgumentException("要結束全部實例請明確指定 all");
                int ok = 0;
                foreach (var p in pids)
                {
                    BeforeKill?.Invoke(p);
                    try { using var proc = Process.GetProcessById(p); proc.Kill(); ok++; } catch { }
                }
                return $"已結束 {Count(ok)}";
            }
            case "fps":
            {
                int fps = Math.Clamp(Int(args, "value"), 0, 1000);
                s.FpsCap = fps; s.Save();
                return FastFlags.SetFpsCap(fps) ? $"FPS 上限 {(fps == 0 ? "不限制" : fps.ToString())}（新開的實例生效）" : "寫入 FPS 設定失敗（Roblox 有安裝嗎？）";
            }
            case "arrange":
            {
                var (w, h) = WindowLayout.ClampSize(Int(args, "width", 800), Int(args, "height", 600));
                var ws = new WindowSettings(args?["applyOnLaunch"]?.GetValue<bool>() ?? WindowLayout.Load().ApplyOnLaunch, w, h,
                    args?["tile"]?.GetValue<bool>() ?? true);
                WindowLayout.Save(ws);
                var (ok, total) = WindowLayout.ArrangeAll(pids, ws);
                return $"視窗 {w}×{h}{(ws.Tile ? "、格狀排列" : "")} → {ok}/{total}" + (ok < total ? "（全螢幕或還沒開好的視窗無法調整）" : "");
            }
            case "graphics":
            {
                var r = Enum.TryParse<Renderer>(args?["renderer"]?.ToString(), out var rr) ? rr : Renderer.Default;
                int q = Math.Clamp(Int(args, "quality"), 0, 21);
                s.Renderer = r.ToString(); s.QualityLevel = q; s.Save();
                bool ok = FastFlags.SetRenderer(r) & FastFlags.SetQualityLevel(q);
                return ok ? $"渲染器 {r}、畫質 {(q == 0 ? "不覆寫" : q.ToString())}（下次啟動生效）" : "寫入 ClientAppSettings.json 失敗（Roblox 有安裝嗎？）";
            }
            default:
                throw new ArgumentException("不認得的動作：" + action);
        }
    });
}
