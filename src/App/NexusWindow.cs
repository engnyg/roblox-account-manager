using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

public sealed class NexusRow
{
    public string Username { get; set; } = "";
    public long UserId { get; set; }
    public string JobId { get; set; } = "";
    public string Connected { get; set; } = "";
}

/// <summary>Nexus：查看已連線的受控帳號，對選取 / 全部帳號送出指令（聊天、踢出、重新加入、執行腳本、加入伺服器）。</summary>
public sealed class NexusWindow : Window
{
    private readonly BackgroundServices _bg;
    private readonly ObservableCollection<NexusRow> _rows = new();
    private readonly DataGrid _grid;
    private readonly ComboBox _cmd = new() { ItemsSource = new[] { NexusCommand.Chat, NexusCommand.Kick, NexusCommand.Rejoin, NexusCommand.ExecuteScript, NexusCommand.JoinServer }, SelectedIndex = 0, Width = 150 };
    private readonly TextBox _arg = Box();
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public NexusWindow(BackgroundServices bg)
    {
        _bg = bg;
        Title = "Nexus";
        Chrome.Attach(this);
        Width = 760; Height = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        var s = AppSettings.Current;
        var info = Label(s.NexusEnabled
            ? $"ws://localhost:{s.NexusPort}/?name=<{T("username")}>&id=<UserId>&jobId=<JobId>" + (s.NexusPassword.Length > 0 ? "&password=…" : "")
            : T("Nexus is off. Enable it in Settings → Services."), 8, dim: true);
        DockPanel.SetDock(info, Dock.Top);
        root.Children.Add(info);

        var send = new StackPanel { Margin = Gap(0, 8, 0, 0) };
        DockPanel.SetDock(send, Dock.Bottom);
        send.Children.Add(Label(T("Argument (chat text / kick reason / script / \"placeId [jobId]\"):"), 4));
        send.Children.Add(_arg);
        var btns = Row(_cmd, Btn(T("Send to Selected"), async (_, _) => await SendAsync(all: false), left: 8), Btn(T("Send to All"), async (_, _) => await SendAsync(all: true), left: 8));
        btns.Margin = Gap(0, 8, 0, 0);
        send.Children.Add(btns);
        send.Children.Add(_status);
        root.Children.Add(send);

        _grid = new DataGrid
        {
            ItemsSource = _rows, AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended,
            HeadersVisibility = DataGridHeadersVisibility.Column, CanUserAddRows = false,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = T("Username"), Binding = new System.Windows.Data.Binding("Username"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "UserId", Binding = new System.Windows.Data.Binding("UserId"), Width = 110 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Job ID", Binding = new System.Windows.Data.Binding("JobId"), Width = 280 });
        _grid.Columns.Add(new DataGridTextColumn { Header = T("Connected"), Binding = new System.Windows.Data.Binding("Connected"), Width = 90 });
        root.Children.Add(_grid);
        Content = root;

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var sel = _grid.SelectedItems.Cast<NexusRow>().Select(r => r.Username).ToHashSet();
        _rows.Clear();
        foreach (var a in _bg.Nexus?.Accounts ?? [])
            _rows.Add(new NexusRow { Username = a.Username, UserId = a.UserId, JobId = a.JobId, Connected = a.IsConnected ? "✔" : "—" });
        foreach (var r in _rows.Where(r => sel.Contains(r.Username))) _grid.SelectedItems.Add(r);
    }

    private object? BuildArgs(string cmd)
    {
        var arg = _arg.Text;
        switch (cmd)
        {
            case NexusCommand.Chat: return new { text = arg };
            case NexusCommand.Kick: return new { reason = arg };
            case NexusCommand.ExecuteScript: return new { script = arg };
            case NexusCommand.JoinServer:
                var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return long.TryParse(parts.FirstOrDefault(), out var place) ? new { placeId = place, jobId = parts.Skip(1).FirstOrDefault() ?? "" } : null;
            default: return null;
        }
    }

    private async Task SendAsync(bool all)
    {
        if (_bg.Nexus is not { IsRunning: true } nexus) { _status.Text = T("Nexus is not running."); return; }
        var cmd = _cmd.SelectedItem as string ?? NexusCommand.Chat;
        if (cmd == NexusCommand.JoinServer && BuildArgs(cmd) is null) { _status.Text = T("JoinServer needs a Place ID."); return; }
        var msg = NexusCommand.Make(cmd, BuildArgs(cmd));
        int n = 0;
        if (all) n = await nexus.BroadcastAsync(msg);
        else foreach (var r in _grid.SelectedItems.Cast<NexusRow>()) if (await nexus.SendAsync(r.Username, msg)) n++;
        _status.Text = $"{cmd} → {n}";
        AppLog.Info($"[Nexus] {cmd} → {n} 個帳號");
    }
}
