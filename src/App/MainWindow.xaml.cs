using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Account> _accounts = new();
    private readonly ICollectionView _view;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly AntiAfk _antiAfk;
    private readonly BackgroundServices _bg;
    private readonly TrayIcon _tray;
    private readonly DispatcherTimer _runningTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _launching;
    private bool _loading = true;
    private bool _quitting;
    private bool _refreshingRunning;
    private CancellationTokenSource? _launchCts;
    private readonly DispatcherTimer _destTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private CancellationTokenSource? _destCts;
    private PlaceInfo? _destination;
    private string _groupSignature = "";

    public MainWindow()
    {
        InitializeComponent();
        // 選單列放進自繪標題列（標題右邊），省一整列高度
        ((DockPanel)Content).Children.Remove(MainMenu);
        // Fluent 的頂層選單項目比 36px 標題列高；用負邊距讓它在標題列裡垂直置中，而不是被從上方對齊後切掉下半
        MainMenu.Margin = new Thickness(0, -12, 0, -12);
        Chrome.Attach(this, MainMenu);
        _antiAfk = new AntiAfk(TimeSpan.FromSeconds(Math.Max(10, _settings.AntiAfkIntervalSeconds)));

        // 帳號檔若是用自訂密碼加密的，要先解鎖才讀得到
        if (AccountStore.IsPasswordProtected() && !UnlockStore())
        {
            _bg = null!; _tray = null!;
            Application.Current.Shutdown();
            return;
        }

        _view = CollectionViewSource.GetDefaultView(_accounts);
        _view.Filter = FilterAccount;
        Grid.ItemsSource = _view;
        Tiles.ItemsSource = _view;

        // Web API / 截圖追蹤會在背景執行緒讀帳號，所以一律切回 UI 執行緒取資料
        _bg = new BackgroundServices(
            () => Dispatcher.Invoke(() => _accounts.ToList()),
            () => Dispatcher.Invoke(() => SelectedAccounts.FirstOrDefault()),
            RelaunchAsync) { WebControl = this };
        _tray = new TrayIcon(this, () => { _quitting = true; Close(); });

        BuildPriorityMenu();
        LoadAccounts();
        PlaceBox.Text = _settings.LastPlaceId;
        _destTimer.Tick += async (_, _) => await RefreshDestinationAsync();

        if (_settings.MultiRobloxOnStartup) EnableMulti(); else UpdateMultiMenu();
        MiAfk.IsChecked = _settings.AntiAfkEnabled;
        if (_settings.AntiAfkEnabled) _antiAfk.Start();

        ApplyLayout(_settings.AccountLayout);
        ApplyBackground();
        UiSound.Configure(_settings);
        _bg.Apply(_settings);

        _loading = false;
        ApplyLanguage();
        Closing += OnClosing;

        _runningTimer.Tick += async (_, _) => await RefreshRunningAsync();
        Loaded += async (_, _) =>
        {
            _runningTimer.Start();
            _ = LoadAvatarsAsync();
            _ = RefreshDestinationAsync();
            await RefreshRunningAsync();
            var v = await RobloxVersion.GetLatestAsync(_settings.Channel);
            if (v is not null) VersionText.Text = "Roblox " + v.Replace("version-", "");
        };

        AppLog.Info("Roblox Account Manager V3 已啟動");

        // 測試用啟動參數
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--open-mixer")) Loaded += (_, _) => Mixer_Click(this, new RoutedEventArgs());
        if (args.Contains("--open-settings")) Loaded += (_, _) => Settings_Click(this, new RoutedEventArgs());
        if (args.Contains("--open-charts")) Loaded += (_, _) => Charts_Click(this, new RoutedEventArgs());
        if (args.Contains("--open-servers")) Loaded += (_, _) => ServerList_Click(this, new RoutedEventArgs());
        if (args.Contains("--open-logs")) Loaded += (_, _) => Logs_Click(this, new RoutedEventArgs());
        if (args.Contains("--open-tools")) Loaded += (_, _) => { Grid.SelectedIndex = 0; AccountTools_Click(this, new RoutedEventArgs()); };
        if (args.Contains("--open-edit")) Loaded += (_, _) => { Grid.SelectedIndex = 0; CmEdit_Click(this, new RoutedEventArgs()); };
        if (args.Contains("--open-tracking")) Loaded += (_, _) => Tracking_Click(this, new RoutedEventArgs());
        if (args.Contains("--open-nexus")) Loaded += (_, _) => Nexus_Click(this, new RoutedEventArgs());
        if (args.Contains("--open-theme")) Loaded += (_, _) => new ThemeEditorWindow(_settings.Theme) { Owner = this }.Show();
        if (args.Contains("--open-login")) Loaded += (_, _) => WebWindow.ForLogin(this).Show();
    }

    private bool UnlockStore()
    {
        for (int tries = 0; tries < 5; tries++)
        {
            var pw = InputDialog.Ask(null, T("Roblox Account Manager V3"),
                tries == 0 ? T("Enter the password that protects your accounts:") : T("Wrong password, try again:"), secret: true);
            if (pw is null) return false;
            if (AccountStore.Unlock(pw)) return true;
        }
        return false;
    }

    /// <summary>視窗剛建立、還沒顯示時就把標題列換成主題色，避免先閃一條白色標題列。</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyToWindow(this);
    }

    // ------------------------------------------------------------------ //
    // 文字 / 語言
    // ------------------------------------------------------------------ //

    private static string T(string k) => Loc.T(k);

    private void ApplyLanguage()
    {
        Title = T("Roblox Account Manager V3");
        MenuFile.Header = T("File"); MenuAccounts.Header = T("Accounts"); MenuGame.Header = T("Game"); MenuTools.Header = T("Tools"); MenuView.Header = T("View");
        MiAddLogin.Header = T("Add Account by Logging In..."); MiImportFile.Header = T("Import V2 Accounts..."); MiImportDat.Header = T("Import Encrypted accounts.dat (V2 / RAM)...");
        MiImportCookies.Header = T("Import Cookies..."); MiExportCookies.Header = T("Export Cookies"); MiExit.Header = T("Exit");
        MiEdit.Header = T("Edit Account..."); MiTools.Header = T("Account Tools..."); MiBrowser.Header = T("Open in Browser");
        MiCheckAll.Header = T("Check All Visible"); MiUncheckAll.Header = T("Uncheck All");
        MiKillSel.Header = T("Kill Selected Instances"); MiKillChecked.Header = T("Kill Checked Instances"); MiTrimSel.Header = T("Trim Memory (Selected)");
        MiRefreshInfo.Header = T("Refresh Account Info");
        MiJoin.Header = T("Join Server"); MiJoinPrivate.Header = T("Join Private Server Link..."); MiServers.Header = T("Server List...");
        MiCharts.Header = T("Game Charts..."); MiMulti.Header = T("Multi-Roblox"); MiAfk.Header = T("Anti-AFK"); MiFps.Header = T("FPS Cap...");
        MiLayoutList.Header = T("List"); MiLayoutTiles.Header = T("Tiles");
        MiMixer.Header = T("Mixer"); MiLogs.Header = T("Logs"); MiTracking.Header = T("Tracking..."); MiNexus.Header = "Nexus..."; MiWebPanel.Header = T("Open Web Control Panel");
        MiSettings.Header = T("Settings"); MiHelp.Header = T("Help");
        LblSearch.Text = T("Search accounts, aliases, groups…"); LblGroup.Text = T("Groups");
        LblDest.Text = T("Destination"); LblPlace.Text = "Place ID"; LblJob.Text = T("Job ID or join link (optional)");
        LblRunning.Text = T("Running"); RunningOf.Text = T("Roblox instances");
        BtnLaunchGroup.Content = T("Launch Group"); BtnKillChecked.Content = T("Kill Checked"); BtnJoin.Content = T("Join with Selected");
        BtnViewList.ToolTip = T("List"); BtnViewTiles.ToolTip = T("Tiles");
        ColCheck.Header = ""; ColStatus.Header = T("Status"); ColUser.Header = T("Account");
        ColGroup.Header = T("Group"); ColTarget.Header = T("Target"); ColLast.Header = T("Last Use"); ColValid.Header = T("Valid");
        ShowDestination(_destination);
        CmJoin.Header = T("Join Server"); CmEdit.Header = T("Edit Account..."); CmCheck.Header = T("Check Selected"); CmUncheck.Header = T("Uncheck Selected");
        CmAlias.Header = T("Set Alias"); CmGroup.Header = T("Set Group"); CmDesc.Header = T("Set Description");
        CmKill.Header = T("Kill Instance"); CmTrim.Header = T("Trim Memory"); CmPriority.Header = T("Process Priority"); CmBrowser.Header = T("Open in Browser");
        CmCopyUser.Header = T("Copy Username"); CmCopyCookie.Header = T("Copy Cookie"); CmRemove.Header = T("Remove");
        RefreshGroups();
        UpdateCheckedLabel();
        CountText.Text = $"{_accounts.Count} {T("accounts")}";
        if (StatusText.Text.Length == 0) StatusText.Text = T("Ready");
    }

    private void Status(string text) => Dispatcher.Invoke(() => StatusText.Text = text);

    // ------------------------------------------------------------------ //
    // 版面 / 背景
    // ------------------------------------------------------------------ //

    private void ApplyLayout(string layout)
    {
        bool tiles = layout == "Tiles";
        Grid.Visibility = tiles ? Visibility.Collapsed : Visibility.Visible;
        Tiles.Visibility = tiles ? Visibility.Visible : Visibility.Collapsed;
        MiLayoutList.IsChecked = !tiles;
        MiLayoutTiles.IsChecked = tiles;
        BtnViewList.IsChecked = !tiles;
        BtnViewTiles.IsChecked = tiles;
        _settings.AccountLayout = tiles ? "Tiles" : "List";
        if (!_loading) _settings.Save();
    }

    private void LayoutList_Click(object s, RoutedEventArgs e) => ApplyLayout("List");
    private void LayoutTiles_Click(object s, RoutedEventArgs e) => ApplyLayout("Tiles");

    private void ApplyBackground()
    {
        var (path, opacity) = ThemeManager.Background;
        if (path.Length > 0 && File.Exists(path))
        {
            try
            {
                var img = new System.Windows.Media.Imaging.BitmapImage(new Uri(path));
                Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill, Opacity = opacity };
                return;
            }
            catch { /* 圖片壞掉就忽略 */ }
        }
        ClearValue(BackgroundProperty);
        ThemeManager.ApplyToWindow(this);
    }

    // ------------------------------------------------------------------ //
    // 帳號載入 / 儲存 / 篩選
    // ------------------------------------------------------------------ //

    private void LoadAccounts()
    {
        foreach (var a in AccountStore.Load()) AddAccount(a);
        var checkedSet = new HashSet<string>(_settings.CheckedUsernames, StringComparer.OrdinalIgnoreCase);
        foreach (var a in _accounts) a.Checked = checkedSet.Contains(a.Username);
        StatusText.Text = $"{T("Loaded")} {_accounts.Count} {T("accounts")}";
    }

    private void AddAccount(Account a)
    {
        a.PropertyChanged += OnAccountChanged;
        _accounts.Add(a);
    }

    private void OnAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        if (e.PropertyName == nameof(Account.Checked))
        {
            PersistChecked();
            UpdateCheckedLabel();
        }
        else if (e.PropertyName is nameof(Account.Group))
        {
            RefreshGroups();
            SaveAccounts();
        }
        else if (e.PropertyName is nameof(Account.Alias) or nameof(Account.Description))
        {
            SaveAccounts();
        }
    }

    private void PersistChecked()
    {
        _settings.CheckedUsernames = _accounts.Where(a => a.Checked).Select(a => a.Username).ToList();
        _settings.Save();
    }

    private async Task LoadAvatarsAsync()
    {
        try
        {
            int n = await AvatarService.FillAsync(_accounts.ToList());
            AppLog.Info($"[大頭貼] 取得 {n} 張");
        }
        catch (Exception e) { AppLog.Warn("載入大頭貼失敗: " + e.Message); }
    }

    private void SaveAccounts()
    {
        try { AccountStore.Save(_accounts); }
        catch (Exception e) { Status("Save failed: " + e.Message); }
    }

    private void ClearAccounts()
    {
        AccountStore.ClearAll();
        _bg.Relaunch.ForgetAll();
        _loading = true;
        foreach (var a in _accounts) a.PropertyChanged -= OnAccountChanged;
        _accounts.Clear();
        _loading = false;
        PersistChecked();
        RefreshGroups();
        UpdateCheckedLabel();
        CountText.Text = $"0 {T("accounts")}";
        Status(T("All accounts cleared"));
        AppLog.Warn("已清除所有帳號資料");
    }

    private bool FilterAccount(object o)
    {
        if (o is not Account a) return false;
        var group = GroupList.SelectedValue as string ?? "";
        if (group.Length > 0 && a.Group != group) return false;
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) return true;
        return a.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
            || a.Alias.Contains(q, StringComparison.OrdinalIgnoreCase)
            || a.Group.Contains(q, StringComparison.OrdinalIgnoreCase)
            || a.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshGroups()
    {
        var current = GroupList.SelectedValue as string ?? "";
        var items = new List<GroupItem> { new(T("All Groups"), "", _accounts.Count, _accounts.Count(a => a.Running)) };
        items.AddRange(_accounts.GroupBy(a => a.Group).Where(g => g.Key.Length > 0).OrderBy(g => g.Key)
            .Select(g => new GroupItem(g.Key == "Default" ? T("Ungrouped") : g.Key, g.Key, g.Count(), g.Count(a => a.Running))));

        // 每 3 秒的執行狀態更新也會呼叫這裡；內容沒變就不重建，免得打斷使用者的點選
        var signature = string.Join("|", items.Select(i => $"{i.Name}:{i.Value}:{i.Count}:{i.Running}"));
        if (signature == _groupSignature) return;
        _groupSignature = signature;

        GroupList.SelectionChanged -= Group_Changed;
        GroupList.ItemsSource = items;
        GroupList.SelectedValue = items.Any(i => i.Value == current) ? current : "";
        GroupList.SelectionChanged += Group_Changed;
        _view?.Refresh();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => _view?.Refresh();
    private void Group_Changed(object sender, SelectionChangedEventArgs e) => _view?.Refresh();

    private void UpdateCheckedLabel()
    {
        LaunchCheckedText.Text = T("Launch Checked");
        LaunchCheckedCount.Text = _accounts.Count(a => a.Checked).ToString();
    }

    private IEnumerable<Account> Visible => _view.Cast<Account>();

    private List<Account> SelectedAccounts =>
        (Tiles.Visibility == Visibility.Visible ? Tiles.SelectedItems : Grid.SelectedItems).Cast<Account>().ToList();

    /// <summary>要執行動作的帳號：有勾選就用勾選的，否則用選取的。</summary>
    private List<Account> TargetAccounts()
    {
        var checkedList = _accounts.Where(a => a.Checked).ToList();
        return checkedList.Count > 0 ? checkedList : SelectedAccounts;
    }

    // ------------------------------------------------------------------ //
    // 勾選
    // ------------------------------------------------------------------ //

    private void SetChecked(IEnumerable<Account> accounts, bool value)
    {
        _loading = true;               // 批次設定時只存一次
        foreach (var a in accounts.ToList()) a.Checked = value;
        _loading = false;
        PersistChecked();
        UpdateCheckedLabel();
    }

    private void CheckAll_Click(object s, RoutedEventArgs e) => SetChecked(Visible, true);
    private void UncheckAll_Click(object s, RoutedEventArgs e) => SetChecked(_accounts, false);

    // ------------------------------------------------------------------ //
    // 啟動 / 加入
    // ------------------------------------------------------------------ //

    private void Place_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _settings.LastPlaceId = PlaceBox.Text.Trim();
        _settings.Save();
        _destTimer.Stop();
        _destTimer.Start();      // 停止輸入 0.5 秒後才查遊戲資訊
    }

    // ------------------------------------------------------------------ //
    // 目的地卡片
    // ------------------------------------------------------------------ //

    private async Task RefreshDestinationAsync()
    {
        _destTimer.Stop();
        _destCts?.Cancel();
        var cts = _destCts = new CancellationTokenSource();
        if (!long.TryParse(PlaceBox.Text.Trim(), out var place) || place <= 0)
        {
            _destination = null;
            ShowDestination(null, PlaceBox.Text.Trim().Length > 0);
            return;
        }
        GameMeta.Text = T("Looking up the game…");
        try
        {
            var info = await GameDiscovery.GetPlaceInfoAsync(place, cts.Token);
            if (cts.IsCancellationRequested) return;
            _destination = info;
            ShowDestination(info, notFound: info is null);
        }
        catch (OperationCanceledException) { }
    }

    private void ShowDestination(PlaceInfo? info, bool notFound = false)
    {
        if (info is null)
        {
            GameIcon.Source = null;
            GameGlyph.Visibility = Visibility.Visible;
            GameName.Text = notFound ? T("Game not found") : T("Choose a destination");
            GameMeta.Text = notFound
                ? T("Check the Place ID, or paste a game / server link into the Job ID box.")
                : T("Enter a Place ID, or paste a game / server link into the Job ID box.");
            return;
        }
        GameName.Text = info.Name;
        GameMeta.Text = $"{info.Playing:N0} {T("playing now")}  ·  Universe {info.UniverseId}";
        GameGlyph.Visibility = info.IconUrl.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        try { GameIcon.Source = info.IconUrl.Length > 0 ? new System.Windows.Media.Imaging.BitmapImage(new Uri(info.IconUrl)) : null; }
        catch { GameIcon.Source = null; GameGlyph.Visibility = Visibility.Visible; }
    }

    /// <summary>讀取 Place ID / Job ID 欄位。Job ID 欄可直接貼整條加入連結，連結內的 placeId 會寫回 Place ID 欄。</summary>
    private async Task<(long PlaceId, string JobId)?> ReadTargetAsync()
    {
        long? typed = long.TryParse(PlaceBox.Text.Trim(), out var t) && t > 0 ? t : null;
        var (place, job) = await JoinTarget.NormalizeAsync(typed, JobBox.Text);
        if (place is null) { Status(T("Enter a valid Place ID")); return null; }
        if (place != typed) PlaceBox.Text = place.Value.ToString();
        if (job != JobBox.Text.Trim()) JobBox.Text = job;
        return (place.Value, job);
    }

    /// <summary>啟動單一帳號：優先用強制目標，其次是帳號自己的目標，最後才是主視窗的 Place/Job 欄位。</summary>
    private async Task<JoinResult> LaunchOneAsync(Account acc, LaunchTarget? forced, (long Place, string Job)? main)
    {
        var t = forced ?? AccountTargets.Get(acc);
        LaunchTarget registered;
        JoinResult r;
        if (t.Kind != TargetKind.None)
        {
            registered = t;
            r = await GameLauncher.LaunchTargetAsync(acc, t, main?.Place ?? 0, main?.Job ?? "");
        }
        else if (main is { } m)
        {
            registered = m.Job.Length > 0 ? new LaunchTarget(TargetKind.Job, m.Place, m.Job) : new LaunchTarget(TargetKind.Place, m.Place);
            r = await GameLauncher.JoinServerAsync(acc, m.Place, m.Job);
        }
        else return new JoinResult(false, T("No target: enter a Place ID or set one on the account"));

        if (r.Ok && _settings.RelaunchOnDisconnect) _bg.Relaunch.Register(acc, registered, r.Pid);
        AppLog.Add(r.Ok ? "INFO" : "WARN", $"[啟動] {acc.Username} → {AccountTargets.Describe(registered)}: {r.Message}");
        return r;
    }

    /// <summary>自動重開用：Job 失敗（伺服器已關）時退回只用 Place。</summary>
    private async Task<JoinResult> RelaunchAsync(Account acc, LaunchTarget target)
    {
        var r = await Dispatcher.InvokeAsync(() => LaunchOneAsync(acc, target, null)).Task.Unwrap();
        if (!r.Ok && target.Kind == TargetKind.Job && target.PlaceId is { } place)
            r = await Dispatcher.InvokeAsync(() => LaunchOneAsync(acc, new LaunchTarget(TargetKind.Place, place), null)).Task.Unwrap();
        return r;
    }

    private async void Join_Click(object s, RoutedEventArgs e)
    {
        var acc = SelectedAccounts.FirstOrDefault();
        if (acc is null) { Status(T("No account selected")); return; }
        await JoinAsync(acc);
    }

    private async Task JoinAsync(Account acc)
    {
        (long, string)? main = null;
        if (AccountTargets.Get(acc).Kind == TargetKind.None)
        {
            main = await ReadTargetAsync();
            if (main is null) return;
        }
        Status($"{T("Launching")} {acc.Username}...");
        var r = await LaunchOneAsync(acc, null, main);
        Status($"{acc.Username}: {r.Message}");
        SaveAccounts();
    }

    private async void Grid_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 只在雙擊資料列（而非勾選框 / 標題）時加入
        if (e.OriginalSource is DependencyObject d && FindParent<DataGridRow>(d) is { Item: Account acc }
            && FindParent<CheckBox>(d) is null)
            await JoinAsync(acc);
    }

    private async void Tiles_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<ListBoxItem>(d) is { Content: Account acc }
            && FindParent<CheckBox>(d) is null)
            await JoinAsync(acc);
    }

    private void Row_RightClick(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject d) return;
        object? item = FindParent<DataGridRow>(d)?.Item ?? FindParent<ListBoxItem>(d)?.Content;
        if (item is not Account acc || SelectedAccounts.Contains(acc)) return;
        if (Tiles.Visibility == Visibility.Visible) { Tiles.SelectedItems.Clear(); Tiles.SelectedItems.Add(acc); }
        else { Grid.SelectedItems.Clear(); Grid.SelectedItems.Add(acc); }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var p = child; p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
    }

    private void LaunchChecked_Click(object s, RoutedEventArgs e)
    {
        var list = _accounts.Where(a => a.Checked).ToList();
        if (list.Count == 0) { Status(T("No accounts checked")); return; }
        _ = LaunchBatchAsync(list);
    }

    private void LaunchGroup_Click(object s, RoutedEventArgs e)
    {
        var group = GroupList.SelectedValue as string ?? "";
        var list = _accounts.Where(a => group.Length == 0 || a.Group == group).ToList();
        if (list.Count == 0) { Status(T("No accounts in this group")); return; }
        _ = LaunchBatchAsync(list);
    }

    private async Task LaunchBatchAsync(List<Account> list, LaunchTarget? forced = null)
    {
        if (_launching) { Status(T("A launch is already in progress")); return; }
        if (list.Count == 0) { Status(T("No account selected")); return; }

        (long, string)? main = null;
        if (forced is null && list.Any(a => AccountTargets.Get(a).Kind == TargetKind.None))
        {
            main = await ReadTargetAsync();
            if (main is null) return;
        }

        _launching = true;
        _launchCts = new CancellationTokenSource();
        var delay = TimeSpan.FromSeconds(Math.Max(0, _settings.AccountJoinDelay));
        try
        {
            for (int i = 0; i < list.Count; i++)
            {
                _launchCts.Token.ThrowIfCancellationRequested();
                var acc = list[i];
                var r = await LaunchOneAsync(acc, forced, main);
                Status($"{T("Launching")} ({i + 1}/{list.Count}) {acc.Username}: {r.Message}");
                if (i < list.Count - 1) await Task.Delay(delay, _launchCts.Token);
            }
            Status($"{T("Group launched")}: {list.Count} {T("accounts")}");
        }
        catch (OperationCanceledException) { }
        finally
        {
            _launching = false;
            _launchCts = null;
            SaveAccounts();
        }
    }

    // ------------------------------------------------------------------ //
    // 私人伺服器 / 伺服器清單 / 排行榜
    // ------------------------------------------------------------------ //

    private async void JoinPrivate_Click(object s, RoutedEventArgs e)
    {
        var list = TargetAccounts();
        if (list.Count == 0) { Status(T("No account selected")); return; }
        var url = InputDialog.Ask(this, T("Join Private Server Link..."),
            T("Paste a private server link (…/games/ID/…?privateServerLinkCode=… or roblox.com/share?code=…&type=Server):"));
        if (string.IsNullOrWhiteSpace(url)) return;
        var (place, code) = PrivateServer.ParseUrl(url.Trim());
        if (code is null) { Status(T("Not a private server link")); return; }
        await LaunchBatchAsync(list, new LaunchTarget(TargetKind.Private, place, LinkCode: code));
    }

    private void ServerList_Click(object s, RoutedEventArgs e) => OpenServerList(PlaceBox.Text.Trim());

    private void OpenServerList(string place)
    {
        new ServerListWindow(place, async (p, job) =>
        {
            var list = TargetAccounts();
            if (list.Count == 0) { Status(T("No account selected")); return; }
            await LaunchBatchAsync(list, new LaunchTarget(TargetKind.Job, p, job));
        }) { Owner = this }.Show();
    }

    private void Charts_Click(object s, RoutedEventArgs e) =>
        new ChartsWindow(
            place => PlaceBox.Text = place.ToString(),
            place => OpenServerList(place.ToString()),
            async place =>
            {
                var list = TargetAccounts();
                if (list.Count == 0) { Status(T("No account selected")); return; }
                await LaunchBatchAsync(list, new LaunchTarget(TargetKind.Place, place));
            }) { Owner = this }.Show();

    // ------------------------------------------------------------------ //
    // 實例管理：結束 / 釋放記憶體 / 優先權 / 執行狀態
    // ------------------------------------------------------------------ //

    private async Task RefreshRunningAsync()
    {
        if (_refreshingRunning) return;
        _refreshingRunning = true;
        try
        {
            var list = await Task.Run(RobloxProcesses.List);
            foreach (var a in _accounts.ToList())
            {
                var inst = a.BrowserTrackerId.Length > 0 ? list.FirstOrDefault(i => i.TrackerId == a.BrowserTrackerId) : null;
                a.Pid = inst?.Pid ?? 0;
            }
            RunningText.Text = list.Count.ToString();
            RefreshGroups();
        }
        finally { _refreshingRunning = false; }
    }

    private int KillAccounts(IEnumerable<Account> accounts)
    {
        int n = 0;
        foreach (var a in accounts.Where(a => a.Pid != 0).ToList())
        {
            _bg.Relaunch.ForgetPid(a.Pid);
            _bg.Relaunch.Forget(a.Username);
            try { using var p = Process.GetProcessById(a.Pid); p.Kill(); n++; AppLog.Info($"[結束] {a.Username} (PID {a.Pid})"); }
            catch { /* 已經結束 */ }
        }
        return n;
    }

    private async void CmKill_Click(object s, RoutedEventArgs e)
    {
        Status($"{T("Kill Instance")}: {KillAccounts(SelectedAccounts)}");
        await Task.Delay(400);
        await RefreshRunningAsync();
    }

    private async void KillChecked_Click(object s, RoutedEventArgs e)
    {
        var group = GroupList.SelectedValue as string ?? "";
        var targets = _accounts.Where(a => a.Checked || (group.Length > 0 && a.Group == group)).ToList();
        Status($"{T("Kill Instance")}: {KillAccounts(targets)}");
        await Task.Delay(400);
        await RefreshRunningAsync();
    }

    private void CmTrim_Click(object s, RoutedEventArgs e)
    {
        var pids = SelectedAccounts.Where(a => a.Pid != 0).Select(a => a.Pid).ToList();
        Status($"{T("Trim Memory")}: {ProcessTuning.TrimMemory(pids)}/{pids.Count}");
    }

    private void BuildPriorityMenu()
    {
        foreach (var (name, value) in ProcessTuning.PriorityChoices)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) =>
            {
                var pids = SelectedAccounts.Where(a => a.Pid != 0).Select(a => a.Pid).ToList();
                int ok = pids.Count(pid => ProcessTuning.SetPriority(pid, value));
                Status($"{T("Process Priority")}: {name} → {ok}/{pids.Count}");
            };
            CmPriority.Items.Add(item);
        }
    }

    // ------------------------------------------------------------------ //
    // 右鍵選單 / 帳號編輯
    // ------------------------------------------------------------------ //

    private void Context_Opened(object s, RoutedEventArgs e)
    {
        bool any = SelectedAccounts.Count > 0;
        bool running = SelectedAccounts.Any(a => a.Pid != 0);
        CmKill.IsEnabled = running; CmTrim.IsEnabled = running; CmPriority.IsEnabled = running;
        CmEdit.IsEnabled = any; CmBrowser.IsEnabled = any;
    }

    private async void CmJoin_Click(object s, RoutedEventArgs e)
    {
        if (SelectedAccounts.FirstOrDefault() is { } a) await JoinAsync(a);
    }

    private void CmCheck_Click(object s, RoutedEventArgs e) => SetChecked(SelectedAccounts, true);
    private void CmUncheck_Click(object s, RoutedEventArgs e) => SetChecked(SelectedAccounts, false);

    private void CmEdit_Click(object s, RoutedEventArgs e)
    {
        if (SelectedAccounts.FirstOrDefault() is not { } a) { Status(T("No account selected")); return; }
        if (new AccountEditWindow(a) { Owner = this }.ShowDialog() == true)
        {
            SaveAccounts();
            RefreshGroups();
        }
    }

    private void AccountTools_Click(object s, RoutedEventArgs e)
    {
        var sel = SelectedAccounts;
        if (sel.Count == 0) { Status(T("No account selected")); return; }
        new AccountToolsWindow(sel, SaveAccounts, (place, job) => { PlaceBox.Text = place.ToString(); JobBox.Text = job; }) { Owner = this }.Show();
    }

    private void CmBrowser_Click(object s, RoutedEventArgs e)
    {
        if (SelectedAccounts.FirstOrDefault() is not { } a || a.SecurityToken.Length == 0) { Status(T("No account selected")); return; }
        WebWindow.ForAccount(this, a.Username, a.SecurityToken).Show();
    }

    private void EditField(string title, Func<Account, string> get, Action<Account, string> set)
    {
        var sel = SelectedAccounts;
        if (sel.Count == 0) return;
        var value = InputDialog.Ask(this, title, sel.Count == 1 ? sel[0].Username : $"{sel.Count} {T("accounts")}", get(sel[0]));
        if (value is null) return;
        foreach (var a in sel) set(a, value.Trim());
        SaveAccounts();
    }

    private void CmAlias_Click(object s, RoutedEventArgs e) => EditField(T("Set Alias"), a => a.Alias, (a, v) => a.Alias = v);
    private void CmGroup_Click(object s, RoutedEventArgs e) => EditField(T("Set Group"), a => a.Group, (a, v) => a.Group = v);
    private void CmDesc_Click(object s, RoutedEventArgs e) => EditField(T("Set Description"), a => a.Description, (a, v) => a.Description = v);

    private void CmCopyUser_Click(object s, RoutedEventArgs e)
    {
        if (SelectedAccounts.FirstOrDefault() is { } a) Clipboard.SetText(a.Username);
    }

    private void CmCopyCookie_Click(object s, RoutedEventArgs e)
    {
        if (SelectedAccounts.FirstOrDefault() is { } a && a.SecurityToken.Length > 0) Clipboard.SetText(a.SecurityToken);
    }

    private void CmRemove_Click(object s, RoutedEventArgs e)
    {
        var sel = SelectedAccounts;
        if (sel.Count == 0) return;
        if (MessageBox.Show(this, $"{T("Remove")} {sel.Count} {T("accounts")}?", T("Remove"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var a in sel) { a.PropertyChanged -= OnAccountChanged; _accounts.Remove(a); _bg.Relaunch.Forget(a.Username); }
        PersistChecked();
        SaveAccounts();
        RefreshGroups();
        CountText.Text = $"{_accounts.Count} {T("accounts")}";
        UpdateCheckedLabel();
    }

    // ------------------------------------------------------------------ //
    // 匯入 / 匯出 / 登入
    // ------------------------------------------------------------------ //

    private void MergeIn(IEnumerable<Account> incoming)
    {
        var existing = _accounts.ToList();
        var fresh = new List<Account>();
        foreach (var inc in incoming)
        {
            var match = existing.FirstOrDefault(a => string.Equals(a.Username, inc.Username, StringComparison.OrdinalIgnoreCase) && inc.Username.Length > 0);
            if (match is null) { fresh.Add(inc); existing.Add(inc); }
            else match.SecurityToken = inc.SecurityToken;
        }
        foreach (var a in fresh) AddAccount(a);
        _ = LoadAvatarsAsync();
        SaveAccounts();
        RefreshGroups();
        CountText.Text = $"{_accounts.Count} {T("accounts")}";
        Status($"+{fresh.Count} / {_accounts.Count} {T("accounts")}");
    }

    private void ImportFile_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON (*.json)|*.json|All files|*.*", Title = T("Import V2 Accounts...") };
        // 預設指到相鄰的 V2 資料夾
        var v2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "roblox-acc-genV2", "data"));
        if (Directory.Exists(v2)) dlg.InitialDirectory = v2;
        if (dlg.ShowDialog(this) != true) return;
        try { MergeIn(AccountStore.ImportFile(dlg.FileName)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, T("Import V2 Accounts..."), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void ImportDat_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "accounts.dat|*.dat|All files|*.*", Title = T("Import Encrypted accounts.dat (V2 / RAM)...") };
        var v2 = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "roblox-acc-genV2", "data"));
        if (Directory.Exists(v2)) dlg.InitialDirectory = v2;
        if (dlg.ShowDialog(this) != true) return;
        if (!LegacyCrypto.HasHeader(dlg.FileName)) { MessageBox.Show(this, T("This is not a Roblox Account Manager encrypted file."), dlg.Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var pw = InputDialog.Ask(this, dlg.Title, T("Password of the file:"), secret: true);
        if (pw is null) return;
        Status("...");
        try
        {
            var list = await Task.Run(() => LegacyCrypto.ImportFile(dlg.FileName, pw));
            MergeIn(list);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, dlg.Title, MessageBoxButton.OK, MessageBoxImage.Warning); Status(ex.Message); }
    }

    private async void AddByLogin_Click(object s, RoutedEventArgs e)
    {
        var w = WebWindow.ForLogin(this);
        if (w.ShowDialog() != true || w.Cookie is null) return;
        var acc = new Account { SecurityToken = w.Cookie };
        if (!await RobloxApi.FetchInfoAsync(acc)) { Status(T("Could not read the account after login")); return; }
        MergeIn([acc]);
    }

    private async void ImportCookies_Click(object s, RoutedEventArgs e)
    {
        var text = InputDialog.Ask(this, T("Import Cookies..."), ".ROBLOSECURITY（每行一個）", "", multiline: true);
        if (string.IsNullOrWhiteSpace(text)) return;
        Status("...");
        var (accs, failed) = await AccountStore.ImportCookieLinesAsync(text.Split('\n'));
        MergeIn(accs);
        if (failed > 0) Status($"{StatusText.Text}  (失敗 {failed})");
    }

    private void ExportCookies_Click(object s, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "cookies.txt" };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, AccountStore.ExportCookies(SelectedAccounts.Count > 0 ? SelectedAccounts : _accounts.ToList()));
        Status(dlg.FileName);
    }

    private async void RefreshInfo_Click(object s, RoutedEventArgs e)
    {
        int ok = 0;
        var list = SelectedAccounts.Count > 0 ? SelectedAccounts : _accounts.ToList();
        foreach (var a in list)
        {
            if (await RobloxApi.FetchInfoAsync(a)) ok++;
            else a.Valid = false;
        }
        SaveAccounts();
        Status($"{ok}/{list.Count}");
    }

    private void Exit_Click(object s, RoutedEventArgs e) { _quitting = true; Close(); }

    // ------------------------------------------------------------------ //
    // 多開 / AFK / FPS / 工具視窗
    // ------------------------------------------------------------------ //

    private void EnableMulti()
    {
        MultiRoblox.Enable(TimeSpan.FromSeconds(1));
        UpdateMultiMenu();
        Status(MultiRoblox.State == MultiRobloxState.Active
            ? T("Multi-Roblox enabled")
            : T("Multi-Roblox waiting: another Roblox owns the mutex; close it and it will take over"));
    }

    private void UpdateMultiMenu() => MiMulti.IsChecked = MultiRoblox.State != MultiRobloxState.Off;

    private void MultiRoblox_Click(object s, RoutedEventArgs e)
    {
        if (MiMulti.IsChecked)
        {
            EnableMulti();
            _settings.MultiRobloxOnStartup = true;
        }
        else
        {
            MultiRoblox.Disable();
            _settings.MultiRobloxOnStartup = false;
            Status(T("Multi-Roblox disabled"));
        }
        _settings.Save();
    }

    private void AntiAfk_Click(object s, RoutedEventArgs e)
    {
        _settings.AntiAfkEnabled = MiAfk.IsChecked;
        _settings.Save();
        if (MiAfk.IsChecked) _antiAfk.Start(); else _antiAfk.Stop();
    }

    private void Fps_Click(object s, RoutedEventArgs e)
    {
        var text = InputDialog.Ask(this, T("FPS Cap..."), T("FPS Cap:") + " (0 = " + T("Off") + ")", _settings.FpsCap.ToString());
        if (text is null || !int.TryParse(text.Trim(), out var fps)) return;
        _settings.FpsCap = Math.Max(0, fps);
        _settings.Save();
        Status(FpsUnlocker.SetFpsCap(_settings.FpsCap) ? $"FPS: {_settings.FpsCap}" : "FPS: failed");
    }

    private void Mixer_Click(object s, RoutedEventArgs e) => new MixerWindow(() => _accounts.ToList(), _bg) { Owner = this }.Show();
    private void Logs_Click(object s, RoutedEventArgs e) => new LogWindow { Owner = this }.Show();
    private void Tracking_Click(object s, RoutedEventArgs e) => new TrackingWindow(_accounts.ToList(), _bg) { Owner = this }.Show();
    private void Nexus_Click(object s, RoutedEventArgs e) => new NexusWindow(_bg) { Owner = this }.Show();

    private void WebPanel_Click(object s, RoutedEventArgs e)
    {
        if (_bg.Api is not { IsRunning: true } api)
        {
            MessageBox.Show(this, T("The web control panel is off. Turn it on in Settings → Services."), T("Open Web Control Panel"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo(BackgroundServices.LocalUrl(api.Port)) { UseShellExecute = true }); }
        catch (Exception ex) { Status(ex.Message); }
    }

    private void Help_Click(object s, RoutedEventArgs e) =>
        MessageBox.Show(this, T("HelpText"), T("Help"), MessageBoxButton.OK, MessageBoxImage.Information);

    private void Settings_Click(object s, RoutedEventArgs e)
    {
        if (new SettingsWindow(SaveAccounts, ClearAccounts) { Owner = this }.ShowDialog() != true) return;

        Loc.Language = _settings.Language;
        ThemeManager.Apply(_settings.Theme);
        ApplyBackground();
        UiSound.Configure(_settings);
        _bg.Apply(_settings);
        if (_settings.MultiRobloxOnStartup && MultiRoblox.State == MultiRobloxState.Off) EnableMulti();
        if (!_settings.HideToTray) _tray.Visible = false;
        ApplyLanguage();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_settings.HideToTray && !_quitting)
        {
            e.Cancel = true;
            Hide();
            _tray.Visible = true;
            return;
        }
        _runningTimer.Stop();
        _launchCts?.Cancel();
        _antiAfk.Dispose();
        _bg.Dispose();
        _tray.Dispose();
        SaveAccounts();
        MultiRoblox.Disable();
    }
}

/// <summary>左側群組清單的一列。</summary>
public sealed record GroupItem(string Name, string Value, int Count, int Running)
{
    public string RunningText => Running > 0 ? $"● {Running}" : "";
}
