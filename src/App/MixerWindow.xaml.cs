using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.App;

public sealed class InstanceRow
{
    public int Pid { get; set; }
    public string Account { get; set; } = "";
    public double MemoryMb { get; set; }
    public double CpuPercent { get; set; }
    public string LimitText { get; set; } = "-";
    public string VolumeText { get; set; } = "-";
    public string PriorityText { get; set; } = "-";
}

/// <summary>實例管理：記憶體 / CPU 顯示、每個實例的 CPU・RAM 限制、視窗大小與排列、FPS 上限、全部結束。</summary>
public partial class MixerWindow : Window
{
    private readonly Func<List<Account>> _getAccounts;
    private readonly ObservableCollection<InstanceRow> _rows = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _cpuSamples = new();
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly BackgroundServices? _bg;
    private bool _refreshing;

    public MixerWindow(Func<List<Account>> getAccounts, BackgroundServices? bg = null)
    {
        _getAccounts = getAccounts;
        _bg = bg;
        InitializeComponent();
        Chrome.Attach(this);
        Grid.ItemsSource = _rows;

        PrioBox.ItemsSource = ProcessTuning.PriorityChoices.Select(c => new PriorityItem(c.Name, c.Value)).ToList();
        PrioBox.SelectedIndex = Array.FindIndex(ProcessTuning.PriorityChoices, c => c.Value == ProcessPriorityClass.Normal);
        RenderBox.ItemsSource = Enum.GetNames<Renderer>();
        RenderBox.SelectedItem = FastFlags.GetRenderer().ToString();
        QualityBox.Text = _settings.QualityLevel.ToString();

        var d = ResourceLimits.Defaults();
        CpuBox.Text = d.CpuPercent.ToString();
        RamBox.Text = d.MemoryMb.ToString();
        ChkAuto.IsChecked = _settings.AutoApplyLimits;
        var ws = WindowLayout.Load();
        WinW.Text = ws.Width.ToString(); WinH.Text = ws.Height.ToString();
        ChkTile.IsChecked = ws.Tile; ChkWinLaunch.IsChecked = ws.ApplyOnLaunch;
        FpsBox.Text = _settings.FpsCap.ToString();

        ApplyLanguage();
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { await RefreshAsync(); _timer.Start(); };
        Closed += (_, _) => { _timer.Stop(); SaveDefaults(); SaveWindowSettings(); };
    }

    private static string T(string k) => Loc.T(k);

    private void ApplyLanguage()
    {
        Title = T("Mixer");
        BtnKill.Content = T("Kill All Roblox");
        LblFps.Text = T("FPS Cap:") + " (0 = " + T("Off") + ")"; BtnFps.Content = T("Apply to All");
        LblWin.Text = T("Window size:"); ChkTile.Content = T("Tile in a grid"); ChkWinLaunch.Content = T("Apply on launch");
        BtnWinApply.Content = T("Apply to Running Windows");
        LblCpu.Text = T("CPU limit:"); LblRam.Text = T("RAM limit:");
        BtnApplySel.Content = T("Apply to Selected"); BtnApplyAll.Content = T("Apply to All"); BtnClear.Content = T("Clear Limits");
        ChkAuto.Content = T("Auto-apply these limits to newly launched instances");
        LblHint.Text = T("CPU limit is a percentage of the whole machine (all cores). RAM limit caps the working set (min 256 MB); Roblox pages out instead of crashing, but may stutter.");
        ColAcc.Header = T("Account"); ColPid.Header = T("PID"); ColMem.Header = T("Memory (MB)"); ColCpu.Header = T("CPU %"); ColLimit.Header = T("Limit");
        ColVol.Header = T("Volume"); ColPrio.Header = T("Priority");
        SecLimits.Text = T("Performance limits"); SecWindow.Text = T("Window"); SecAudio.Text = T("Audio and process");
        SecGfx.Text = T("Graphics"); SecInstances.Text = T("Running instances");
        LblVol.Text = T("Volume:"); BtnVolSel.Content = T("Set Selected"); BtnVolAll.Content = T("Set All");
        BtnMute.Content = T("Mute"); BtnUnmute.Content = T("Unmute");
        BtnTrimSel.Content = T("Trim Memory (Selected)"); BtnTrimAll.Content = T("Trim Memory (All)");
        LblPrio.Text = T("Priority:"); BtnPrioSel.Content = T("Set Selected"); BtnPrioAll.Content = T("Set All");
        BtnKillSel.Content = T("Kill Selected");
        LblRender.Text = T("Renderer:"); LblQuality.Text = T("Quality level:"); BtnGfx.Content = T("Apply Graphics Settings");
        LblGfxHint.Text = T("Written to ClientAppSettings.json; takes effect on the next launch. Quality 0 = do not override.");
    }

    private sealed record PriorityItem(string Name, ProcessPriorityClass Value);

    private void Status(string text) => StatusText.Text = text;

    // ------------------------------------------------------------------ //

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var selected = Grid.SelectedItems.Cast<InstanceRow>().Select(r => r.Pid).ToHashSet();
            var accounts = _getAccounts();
            var rows = await Task.Run(() =>
            {
                var list = new List<InstanceRow>();
                var now = DateTime.UtcNow;
                foreach (var inst in RobloxProcesses.List())
                {
                    string name = T("Unknown");
                    if (inst.TrackerId.Length > 0)
                        name = accounts.FirstOrDefault(a => a.BrowserTrackerId == inst.TrackerId)?.Username ?? name;

                    double cpu = 0;
                    try
                    {
                        using var p = Process.GetProcessById(inst.Pid);
                        var total = p.TotalProcessorTime;
                        if (_cpuSamples.TryGetValue(inst.Pid, out var prev))
                        {
                            var wall = (now - prev.At).TotalSeconds;
                            if (wall > 0) cpu = (total - prev.Cpu).TotalSeconds / wall / Environment.ProcessorCount * 100;
                        }
                        _cpuSamples[inst.Pid] = (total, now);
                    }
                    catch { _cpuSamples.Remove(inst.Pid); }

                    list.Add(new InstanceRow
                    {
                        Pid = inst.Pid, Account = name, MemoryMb = RobloxProcesses.MemoryMb(inst.Pid),
                        CpuPercent = cpu, LimitText = ResourceLimits.GetApplied(inst.Pid).Describe() is { Length: > 0 } d ? d : "-",
                        VolumeText = VolumeLabel(inst.Pid),
                        PriorityText = ProcessTuning.GetPriority(inst.Pid)?.ToString() ?? "-",
                    });
                }
                foreach (var pid in _cpuSamples.Keys.Except(list.Select(r => r.Pid)).ToList()) _cpuSamples.Remove(pid);
                return list;
            });

            _rows.Clear();
            foreach (var r in rows) _rows.Add(r);
            foreach (var r in _rows.Where(r => selected.Contains(r.Pid))) Grid.SelectedItems.Add(r);
        }
        finally { _refreshing = false; }
    }

    private static string VolumeLabel(int pid)
    {
        try
        {
            var v = AudioSessions.GetVolume(pid);
            if (v is null) return "-";
            return AudioSessions.IsMuted(pid) == true ? "🔇" : $"{Math.Round(v.Value * 100)}%";
        }
        catch { return "-"; }
    }

    private List<int> SelectedPids => Grid.SelectedItems.Cast<InstanceRow>().Select(r => r.Pid).ToList();
    private List<int> AllPids => _rows.Select(r => r.Pid).ToList();

    // ------------------------------------------------------------------ //
    // 限制
    // ------------------------------------------------------------------ //

    private Limits CurrentLimits()
    {
        int.TryParse(CpuBox.Text.Trim(), out var cpu);
        int.TryParse(RamBox.Text.Trim(), out var ram);
        var l = ResourceLimits.Clamp(cpu, ram);
        CpuBox.Text = l.CpuPercent.ToString();   // 回填修正後的值（例如 RAM 100 → 256）
        RamBox.Text = l.MemoryMb.ToString();
        return l;
    }

    private void SaveDefaults()
    {
        var l = CurrentLimits();
        _settings.DefaultCpuPercent = l.CpuPercent;
        _settings.DefaultMemoryMb = l.MemoryMb;
        _settings.AutoApplyLimits = ChkAuto.IsChecked == true;
        _settings.Save();
    }

    private async Task RunLimitsAsync(List<int> pids, Limits limits)
    {
        SaveDefaults();
        int ok = 0;
        var errors = new List<string>();
        foreach (var pid in pids)
        {
            try { ResourceLimits.Apply(pid, limits); ok++; }
            catch (LimitException e) { errors.Add($"{pid}: {e.Message}"); }
        }
        Status($"{T("Limit")}: {(limits.Active ? limits.Describe() : T("Cleared"))} → {ok}/{pids.Count}");
        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), T("Mixer"), MessageBoxButton.OK, MessageBoxImage.Warning);
        await RefreshAsync();
    }

    private async void ApplySel_Click(object s, RoutedEventArgs e)
    {
        var pids = SelectedPids;
        if (pids.Count == 0) { MessageBox.Show(this, T("Select at least one instance first"), T("Mixer")); return; }
        await RunLimitsAsync(pids, CurrentLimits());
    }

    private async void ApplyAll_Click(object s, RoutedEventArgs e)
    {
        var pids = AllPids;
        if (pids.Count == 0) { MessageBox.Show(this, T("No running Roblox instances"), T("Mixer")); return; }
        await RunLimitsAsync(pids, CurrentLimits());
    }

    private async void Clear_Click(object s, RoutedEventArgs e)
    {
        var pids = SelectedPids.Count > 0 ? SelectedPids : AllPids;
        if (pids.Count > 0) await RunLimitsAsync(pids, new Limits());
    }

    // ------------------------------------------------------------------ //
    // 視窗大小
    // ------------------------------------------------------------------ //

    private WindowSettings CurrentWindowSettings()
    {
        int.TryParse(WinW.Text.Trim(), out var w);
        int.TryParse(WinH.Text.Trim(), out var h);
        var (cw, ch) = WindowLayout.ClampSize(w, h);
        WinW.Text = cw.ToString(); WinH.Text = ch.ToString();
        return new WindowSettings(ChkWinLaunch.IsChecked == true, cw, ch, ChkTile.IsChecked == true);
    }

    private void SaveWindowSettings() => WindowLayout.Save(CurrentWindowSettings());

    private void WinApply_Click(object s, RoutedEventArgs e)
    {
        var ws = CurrentWindowSettings();
        WindowLayout.Save(ws);
        var pids = SelectedPids.Count > 0 ? SelectedPids : AllPids;
        if (pids.Count == 0) { MessageBox.Show(this, T("No running Roblox instances"), T("Mixer")); return; }
        var (ok, total) = WindowLayout.ArrangeAll(pids, ws);
        Status($"{T("Window size:")} {ok}/{total}");
        if (ok < total)
            MessageBox.Show(this, T("Some windows could not be resized (not created yet, or in fullscreen mode)."), T("Mixer"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ------------------------------------------------------------------ //

    private void Fps_Click(object s, RoutedEventArgs e)
    {
        int.TryParse(FpsBox.Text.Trim(), out var fps);
        _settings.FpsCap = Math.Max(0, fps);
        _settings.Save();
        Status(FpsUnlocker.SetFpsCap(_settings.FpsCap) ? $"FPS: {_settings.FpsCap}" : "FPS: failed");
    }

    // ------------------------------------------------------------------ //
    // 音量 / 記憶體 / 優先權 / 圖形設定 / 結束選取
    // ------------------------------------------------------------------ //

    private List<int> TargetPids() => SelectedPids.Count > 0 ? SelectedPids : AllPids;

    private async Task VolumeAsync(List<int> pids, Func<int, bool> action, string label)
    {
        if (pids.Count == 0) { MessageBox.Show(this, T("No running Roblox instances"), T("Mixer")); return; }
        int ok = await Task.Run(() => pids.Count(action));
        Status($"{label}: {ok}/{pids.Count}" + (ok < pids.Count ? "  (" + T("No audio session yet") + ")" : ""));
        await RefreshAsync();
    }

    private async void VolSel_Click(object s, RoutedEventArgs e)
    {
        float v = (float)VolSlider.Value / 100f;
        await VolumeAsync(SelectedPids, pid => AudioSessions.SetVolume(pid, v), T("Volume"));
    }

    private async void VolAll_Click(object s, RoutedEventArgs e)
    {
        float v = (float)VolSlider.Value / 100f;
        await VolumeAsync(AllPids, pid => AudioSessions.SetVolume(pid, v), T("Volume"));
    }

    private async void Mute_Click(object s, RoutedEventArgs e) =>
        await VolumeAsync(TargetPids(), pid => AudioSessions.SetMuted(pid, true), T("Mute"));

    private async void Unmute_Click(object s, RoutedEventArgs e) =>
        await VolumeAsync(TargetPids(), pid => AudioSessions.SetMuted(pid, false), T("Unmute"));

    private async void TrimSel_Click(object s, RoutedEventArgs e)
    {
        var pids = SelectedPids;
        if (pids.Count == 0) { MessageBox.Show(this, T("Select at least one instance first"), T("Mixer")); return; }
        Status($"{T("Trim Memory")}: {ProcessTuning.TrimMemory(pids)}/{pids.Count}");
        await RefreshAsync();
    }

    private async void TrimAll_Click(object s, RoutedEventArgs e)
    {
        var pids = AllPids;
        Status($"{T("Trim Memory")}: {ProcessTuning.TrimMemory(pids)}/{pids.Count}");
        await RefreshAsync();
    }

    private async Task SetPriorityAsync(List<int> pids)
    {
        if (PrioBox.SelectedItem is not PriorityItem item) return;
        if (pids.Count == 0) { MessageBox.Show(this, T("No running Roblox instances"), T("Mixer")); return; }
        int ok = pids.Count(p => ProcessTuning.SetPriority(p, item.Value));
        Status($"{T("Priority")}: {item.Name} → {ok}/{pids.Count}");
        await RefreshAsync();
    }

    private async void PrioSel_Click(object s, RoutedEventArgs e) => await SetPriorityAsync(SelectedPids);
    private async void PrioAll_Click(object s, RoutedEventArgs e) => await SetPriorityAsync(AllPids);

    private async void KillSel_Click(object s, RoutedEventArgs e)
    {
        var pids = SelectedPids;
        if (pids.Count == 0) { MessageBox.Show(this, T("Select at least one instance first"), T("Mixer")); return; }
        int n = 0;
        foreach (var pid in pids)
        {
            _bg?.Relaunch.ForgetPid(pid);
            try { using var p = Process.GetProcessById(pid); p.Kill(); n++; } catch { }
        }
        Status($"{T("Kill Selected")}: {n}/{pids.Count}");
        await Task.Delay(400);
        await RefreshAsync();
    }

    private void Gfx_Click(object s, RoutedEventArgs e)
    {
        var r = Enum.TryParse<Renderer>(RenderBox.SelectedItem as string, out var rr) ? rr : Renderer.Default;
        int.TryParse(QualityBox.Text.Trim(), out var q);
        q = Math.Clamp(q, 0, 21);
        QualityBox.Text = q.ToString();
        _settings.Renderer = r.ToString();
        _settings.QualityLevel = q;
        _settings.Save();
        bool ok = FastFlags.SetRenderer(r) & FastFlags.SetQualityLevel(q);
        Status(ok ? $"{T("Renderer:")} {r}, {T("Quality level:")} {q}" : T("Failed to write ClientAppSettings.json (is Roblox installed?)"));
    }

    private async void Kill_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, T("This will close every running Roblox window. Continue?"), T("Kill All Roblox"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _bg?.Relaunch.ForgetAll();
        Status($"{T("Kill All Roblox")}: {RobloxProcesses.KillAll()}");
        await RefreshAsync();
    }
}
