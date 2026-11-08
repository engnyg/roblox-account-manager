using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>設定：一般 / 效能 / 資料與隱私 / 主題 / 音效 / 服務（Web API・Nexus）。</summary>
public sealed class SettingsWindow : Window
{
    private readonly AppSettings _s = AppSettings.Current;
    private readonly Action _resave;
    private readonly Action _clearAll;

    // General
    private readonly ComboBox _lang = new() { ItemsSource = new[] { "zh", "en" }, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _delay = Box(width: 90), _exe = Box(), _channel = Box(width: 160), _tp = Box();
    private readonly CheckBox _multi, _direct, _relaunch, _watcher, _watcherMem, _tray;
    private readonly TextBox _watcherMb = Box(width: 70);
    // Performance
    private readonly CheckBox _lowerPrio, _autoTrim;
    private readonly TextBox _trimMin = Box(width: 70), _quality = Box(width: 70);
    private readonly ComboBox _renderer = new() { ItemsSource = Enum.GetNames<Renderer>(), Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
    // Theme
    private readonly ComboBox _theme = new() { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
    // Sounds
    private readonly CheckBox _click;
    private readonly Slider _vol = new() { Minimum = 0, Maximum = 100, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _wav = Box();
    // Services
    private readonly CheckBox _api, _nexus, _apiLan;
    private readonly TextBox _apiPort = Box(width: 80), _apiPw = Box(width: 200), _nxPort = Box(width: 80), _nxPw = Box(width: 200);
    private readonly TextBlock _encStatus = new() { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };

    public SettingsWindow(Action resaveAccounts, Action clearAccounts)
    {
        _resave = resaveAccounts; _clearAll = clearAccounts;
        Title = T("Settings");
        Chrome.Attach(this);
        Width = 640; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;

        var tabs = new TabControl { Margin = new Thickness(10, 10, 10, 0) };

        StackPanel Tab(string header)
        {
            var sp = new StackPanel { Margin = new Thickness(14) };
            tabs.Items.Add(new TabItem { Header = header, Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            return sp;
        }

        // ---- 一般 ----
        var g = Tab(T("General"));
        g.Children.Add(Label(T("Language:"))); _lang.SelectedItem = _s.Language; _lang.Margin = Gap(0, 0, 0, 10); g.Children.Add(_lang);
        g.Children.Add(Label(T("Join delay (seconds):"))); _delay.Text = _s.AccountJoinDelay.ToString(CultureInfo.InvariantCulture); _delay.HorizontalAlignment = HorizontalAlignment.Left; _delay.Margin = Gap(0, 0, 0, 10); g.Children.Add(_delay);
        _multi = Check(T("Enable Multi-Roblox on startup"), _s.MultiRobloxOnStartup); g.Children.Add(_multi);
        _direct = Check(T("Launch Roblox directly (skip bootstrapper)"), _s.DirectLaunch); g.Children.Add(_direct);
        _relaunch = Check(T("Relaunch an account into its game if it disconnects or crashes"), _s.RelaunchOnDisconnect); g.Children.Add(_relaunch);
        _watcher = Check(T("Watch Roblox processes"), _s.WatcherEnabled); g.Children.Add(_watcher);
        _watcherMem = Check(T("…close an instance whose memory stays below (MB):"), _s.WatcherCloseIfMemoryLow);
        _watcherMem.Margin = Gap(24, 0, 0, 8);
        _watcherMb.Text = _s.WatcherMemoryLowMb.ToString(); _watcherMb.HorizontalAlignment = HorizontalAlignment.Left; _watcherMb.Margin = Gap(24, 0, 0, 10);
        g.Children.Add(_watcherMem); g.Children.Add(_watcherMb);
        _tray = Check(T("Hide to the system tray when the window is closed"), _s.HideToTray); g.Children.Add(_tray);
        g.Children.Add(Label(T("Roblox channel (blank = default):"))); _channel.Text = _s.Channel; _channel.HorizontalAlignment = HorizontalAlignment.Left; _channel.Margin = Gap(0, 0, 0, 10); g.Children.Add(_channel);
        g.Children.Add(Label(T("RobloxPlayerBeta.exe path (blank = auto):")));
        _exe.Text = _s.RobloxExePath;
        var browse = Btn("...", (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "RobloxPlayerBeta.exe|RobloxPlayerBeta.exe|exe|*.exe" };
            if (dlg.ShowDialog(this) == true) _exe.Text = dlg.FileName;
        });
        var exeRow = new DockPanel { Margin = Gap(0, 0, 0, 10) };
        DockPanel.SetDock(browse, Dock.Right); browse.Margin = Gap(6);
        exeRow.Children.Add(browse); exeRow.Children.Add(_exe);
        g.Children.Add(exeRow);
        g.Children.Add(Label(T("Teleport-Controller URL:"))); _tp.Text = _s.TeleportControllerUrl; g.Children.Add(_tp);

        // ---- 效能 ----
        var p = Tab(T("Performance"));
        _lowerPrio = Check(T("Lower CPU priority (Below Normal) while 2 or more instances are running"), _s.LowerPriority); p.Children.Add(_lowerPrio);
        _autoTrim = Check(T("Automatically trim memory every (minutes):"), _s.AutoTrimEnabled); p.Children.Add(_autoTrim);
        _trimMin.Text = _s.AutoTrimMinutes.ToString(); _trimMin.HorizontalAlignment = HorizontalAlignment.Left; _trimMin.Margin = Gap(0, 0, 0, 6); p.Children.Add(_trimMin);
        p.Children.Add(Btn(T("Trim memory now"), (_, _) =>
        {
            var pids = RobloxProcesses.List().Select(i => i.Pid).ToList();
            Info(this, $"{ProcessTuning.TrimMemory(pids)}/{pids.Count}");
        }));
        ((Button)p.Children[^1]).HorizontalAlignment = HorizontalAlignment.Left;
        ((Button)p.Children[^1]).Margin = Gap(0, 0, 0, 14);
        p.Children.Add(Label(T("Force a rendering engine (applies to newly started instances):"))); _renderer.SelectedItem = _s.Renderer; _renderer.Margin = Gap(0, 0, 0, 10); p.Children.Add(_renderer);
        p.Children.Add(Label(T("Graphics quality override 1-21 (0 = don't override):"))); _quality.Text = _s.QualityLevel.ToString(); _quality.HorizontalAlignment = HorizontalAlignment.Left; p.Children.Add(_quality);
        p.Children.Add(Label(T("FPS cap, CPU / RAM limits, window size and volume are in the Mixer."), 0, dim: true));
        ((TextBlock)p.Children[^1]).Margin = Gap(0, 14, 0, 0);

        // ---- 資料與隱私 ----
        var d = Tab(T("Data & Privacy"));
        d.Children.Add(_encStatus);
        d.Children.Add(Row(
            Btn(T("Set a password..."), (_, _) => SetPassword()),
            Btn(T("Remove password (use Windows account)"), (_, _) => RemovePassword(), left: 8)));
        d.Children.Add(Label(T("Accounts are encrypted on disk: with Windows DPAPI (tied to your Windows user) by default, or with AES-256-GCM using a password you choose. With a password set you must enter it every time the app starts."), 14, dim: true));
        d.Children.Add(Btn(T("Clear all accounts..."), (_, _) => ClearAll()));
        ((Button)d.Children[^1]).HorizontalAlignment = HorizontalAlignment.Left;
        UpdateEncStatus();

        // ---- 主題 ----
        var th = Tab(T("Themes"));
        th.Children.Add(Label(T("Theme:")));
        _theme.ItemsSource = ThemeManager.AllNames(); _theme.SelectedItem = _s.Theme; _theme.Margin = Gap(0, 0, 0, 10);
        th.Children.Add(_theme);
        var edit = Btn(T("Theme Editor..."), (_, _) =>
        {
            var ed = new ThemeEditorWindow(_theme.SelectedItem as string ?? "Graphite") { Owner = this };
            ed.ShowDialog();
            _theme.ItemsSource = ThemeManager.AllNames();
            _theme.SelectedItem = ed.AppliedTheme ?? _s.Theme;
        });
        edit.HorizontalAlignment = HorizontalAlignment.Left;
        th.Children.Add(edit);

        // ---- 音效 ----
        var so = Tab(T("Sounds"));
        _click = Check(T("Play a click sound on buttons and menus"), _s.ClickSounds); so.Children.Add(_click);
        so.Children.Add(Label(T("Volume:"))); _vol.Value = _s.ClickVolume; _vol.Margin = Gap(0, 0, 0, 10); so.Children.Add(_vol);
        so.Children.Add(Label(T("Custom sound (.wav, blank = built-in):")));
        _wav.Text = _s.ClickSoundFile;
        var wavBrowse = Btn("...", (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "WAV|*.wav" };
            if (dlg.ShowDialog(this) == true) _wav.Text = dlg.FileName;
        });
        var wavRow = new DockPanel { Margin = Gap(0, 0, 0, 10) };
        DockPanel.SetDock(wavBrowse, Dock.Right); wavBrowse.Margin = Gap(6);
        wavRow.Children.Add(wavBrowse); wavRow.Children.Add(_wav);
        so.Children.Add(wavRow);
        so.Children.Add(Btn(T("Test"), (_, _) =>
        {
            _s.ClickSounds = true; _s.ClickVolume = (int)_vol.Value; _s.ClickSoundFile = _wav.Text.Trim();
            UiSound.Configure(_s); UiSound.Play();
        }));
        ((Button)so.Children[^1]).HorizontalAlignment = HorizontalAlignment.Left;

        // ---- 服務 ----
        var sv = Tab(T("Services"));
        _api = Check(T("Enable the web control panel and Web API"), _s.WebApiEnabled); sv.Children.Add(_api);
        sv.Children.Add(Row(Label(T("Port:"), 0), _apiPort, Label(T("Password (optional):"), 0), _apiPw));
        _apiPort.Text = _s.WebApiPort.ToString(); _apiPw.Text = _s.WebApiPassword;
        ((StackPanel)sv.Children[^1]).Children[0].SetValue(FrameworkElement.MarginProperty, Gap(0, 0, 6, 0));
        ((StackPanel)sv.Children[^1]).Children[2].SetValue(FrameworkElement.MarginProperty, Gap(14, 0, 6, 0));
        _apiLan = Check(T("Allow phones and other devices on this network (requires a password)"), _s.WebApiAllowLan); sv.Children.Add(_apiLan);
        var urls = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = Gap(0, 0, 0, 6), FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["MonoFont"] };
        void UpdateUrls()
        {
            int.TryParse(_apiPort.Text, out var port);
            if (port <= 0) port = _s.WebApiPort;
            var lines = new List<string> { T("On this computer:") + " " + BackgroundServices.LocalUrl(port) };
            if (_apiLan.IsChecked == true)
            {
                if (_apiPw.Text.Length == 0) lines.Add(T("Set a password first; without one the panel only listens on this computer."));
                else lines.AddRange(BackgroundServices.LanAddresses().Select(ip => T("Other devices:") + $" http://{ip}:{port}/"));
            }
            urls.Text = string.Join(Environment.NewLine, lines);
        }
        _apiPort.TextChanged += (_, _) => UpdateUrls(); _apiPw.TextChanged += (_, _) => UpdateUrls();
        _apiLan.Checked += (_, _) => UpdateUrls(); _apiLan.Unchecked += (_, _) => UpdateUrls();
        UpdateUrls();
        sv.Children.Add(urls);
        sv.Children.Add(Label(T("The first time you allow other devices, Windows Firewall may ask — choose Private networks."), 6, dim: true));
        sv.Children.Add(Label("API: GET /api/state · POST /api/launch · /api/kill · /api/check · GET /GetAccounts · POST /JoinServer", 14, dim: true));
        _nexus = Check(T("Enable Nexus (WebSocket control of in-game scripts)"), _s.NexusEnabled); sv.Children.Add(_nexus);
        sv.Children.Add(Row(Label(T("Port:"), 0), _nxPort, Label(T("Password (optional):"), 0), _nxPw));
        _nxPort.Text = _s.NexusPort.ToString(); _nxPw.Text = _s.NexusPassword;
        ((StackPanel)sv.Children[^1]).Children[0].SetValue(FrameworkElement.MarginProperty, Gap(0, 0, 6, 0));
        ((StackPanel)sv.Children[^1]).Children[2].SetValue(FrameworkElement.MarginProperty, Gap(14, 0, 6, 0));
        sv.Children.Add(Label(T("Nexus only listens on this computer. Passwords are checked in the 'password' header or query string."), 0, dim: true));

        var ok = Btn(T("OK"), Ok_Click, 80); ok.IsDefault = true;
        var cancel = Btn(T("Cancel"), (_, _) => DialogResult = false, 80, 8); cancel.IsCancel = true;
        var buttons = Row(ok, cancel);
        buttons.HorizontalAlignment = HorizontalAlignment.Right; buttons.Margin = new Thickness(10);

        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(tabs);
        Content = dock;
    }

    private void UpdateEncStatus() => _encStatus.Text = AccountStore.UsesPassword
        ? T("Encryption: custom password (AES-256-GCM)")
        : T("Encryption: Windows DPAPI (current Windows user)");

    private void SetPassword()
    {
        var a = InputDialog.Ask(this, T("Set a password..."), T("New password (at least 6 characters). You will be asked for it on every start; if you forget it the accounts cannot be recovered."));
        if (a is null) return;
        if (a.Length < 6) { Info(this, T("Password is too short.")); return; }
        var b = InputDialog.Ask(this, T("Set a password..."), T("Type it again to confirm:"));
        if (b != a) { Info(this, T("Passwords do not match.")); return; }
        AccountStore.SetPassword(a);
        _resave();
        UpdateEncStatus();
    }

    private void RemovePassword()
    {
        if (!AccountStore.UsesPassword) return;
        AccountStore.SetPassword(null);
        _resave();
        UpdateEncStatus();
    }

    private void ClearAll()
    {
        if (!Confirm(this, T("This permanently deletes ALL saved accounts and their cookies from this app. Continue?"))) return;
        if (!Confirm(this, T("Are you really sure? This cannot be undone."))) return;
        _clearAll();
        UpdateEncStatus();
    }

    private void Ok_Click(object s, RoutedEventArgs e)
    {
        _s.Language = _lang.SelectedItem as string ?? "zh";
        if (double.TryParse(_delay.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dl)) _s.AccountJoinDelay = Math.Clamp(dl, 0, 600);
        _s.MultiRobloxOnStartup = _multi.IsChecked == true;
        _s.DirectLaunch = _direct.IsChecked == true;
        _s.RelaunchOnDisconnect = _relaunch.IsChecked == true;
        _s.WatcherEnabled = _watcher.IsChecked == true;
        _s.WatcherCloseIfMemoryLow = _watcherMem.IsChecked == true;
        if (int.TryParse(_watcherMb.Text, out var wm)) _s.WatcherMemoryLowMb = Math.Clamp(wm, 1, 100000);
        _s.HideToTray = _tray.IsChecked == true;
        _s.Channel = _channel.Text.Trim();
        _s.RobloxExePath = _exe.Text.Trim();
        _s.TeleportControllerUrl = _tp.Text.Trim();

        _s.LowerPriority = _lowerPrio.IsChecked == true;
        _s.AutoTrimEnabled = _autoTrim.IsChecked == true;
        if (int.TryParse(_trimMin.Text, out var tm)) _s.AutoTrimMinutes = Math.Clamp(tm, 1, 1440);
        _s.Renderer = _renderer.SelectedItem as string ?? "Default";
        if (int.TryParse(_quality.Text, out var q)) _s.QualityLevel = Math.Clamp(q, 0, 21);
        // 預設值要明確寫回旗標檔（才能移除之前設的旗標）
        if (Enum.TryParse<Renderer>(_s.Renderer, out var r)) FastFlags.SetRenderer(r);
        FastFlags.SetQualityLevel(_s.QualityLevel);

        _s.Theme = _theme.SelectedItem as string ?? "System";
        _s.ClickSounds = _click.IsChecked == true;
        _s.ClickVolume = (int)_vol.Value;
        _s.ClickSoundFile = _wav.Text.Trim();

        _s.WebApiEnabled = _api.IsChecked == true;
        if (int.TryParse(_apiPort.Text, out var ap)) _s.WebApiPort = Math.Clamp(ap, 1024, 65535);
        _s.WebApiPassword = _apiPw.Text;
        _s.WebApiAllowLan = _apiLan.IsChecked == true;
        _s.NexusEnabled = _nexus.IsChecked == true;
        if (int.TryParse(_nxPort.Text, out var np)) _s.NexusPort = Math.Clamp(np, 1024, 65535);
        _s.NexusPassword = _nxPw.Text;

        _s.Save();
        DialogResult = true;
    }
}
