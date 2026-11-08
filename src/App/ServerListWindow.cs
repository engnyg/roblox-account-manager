using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>公開伺服器清單：依人數 / Ping / FPS 檢視，雙擊或按鈕用勾選（或選取）的帳號加入指定 Job ID。</summary>
public sealed class ServerListWindow : Window
{
    private readonly TextBox _place;
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private readonly ObservableCollection<ServerInfo> _rows = new();
    private readonly Func<long, string, Task> _join;
    private CancellationTokenSource? _cts;

    public ServerListWindow(string initialPlace, Func<long, string, Task> join)
    {
        _join = join;
        Title = T("Server List");
        Chrome.Attach(this);
        Width = 760; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        _place = Box(initialPlace, 140);
        var refresh = Btn(T("Refresh"), async (_, _) => await RefreshAsync(), left: 8);
        var pagesLabel = Label(T("Pages:"), 0); pagesLabel.Margin = Gap(12, 0, 4, 0);
        var pages = new ComboBox { Width = 60, ItemsSource = new[] { 1, 2, 3, 5, 10 }, SelectedItem = 3 };
        pages.Name = "Pages";
        var top = Row(Label(T("Place ID:"), 0), _place, refresh, pagesLabel, pages);
        top.Children[0].SetValue(FrameworkElement.MarginProperty, Gap(0, 0, 6, 0));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        _status = new TextBlock { Margin = Gap(0, 6, 0, 0) };
        var joinBtn = Btn(T("Join Selected Server"), async (_, _) => await JoinSelected(), left: 0);
        var copy = Btn(T("Copy Job ID"), (_, _) => { if (_grid!.SelectedItem is ServerInfo s) Clipboard.SetText(s.Id); }, left: 8);
        var bottom = new DockPanel { Margin = Gap(0, 8, 0, 0) };
        var btns = Row(joinBtn, copy); btns.Margin = Gap();
        DockPanel.SetDock(btns, Dock.Left);
        bottom.Children.Add(btns);
        bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        _grid = new DataGrid
        {
            ItemsSource = _rows, AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column, CanUserAddRows = false,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Job ID", Binding = new System.Windows.Data.Binding("Id"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = T("Players"), Binding = new System.Windows.Data.Binding("Occupancy"), SortMemberPath = "Playing", Width = 100 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Ping", Binding = new System.Windows.Data.Binding("Ping") { StringFormat = "F0" }, Width = 80 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "FPS", Binding = new System.Windows.Data.Binding("Fps") { StringFormat = "F0" }, Width = 80 });
        _grid.MouseDoubleClick += async (_, _) => await JoinSelected();
        root.Children.Add(_grid);
        Content = root;

        Loaded += async (_, _) => { if (long.TryParse(_place.Text, out _)) await RefreshAsync(); };
        Closed += (_, _) => _cts?.Cancel();
        _pagesBox = pages;
    }

    private readonly ComboBox _pagesBox;

    private async Task RefreshAsync()
    {
        if (!long.TryParse(_place.Text.Trim(), out var place) || place <= 0) { _status.Text = T("Enter a valid Place ID"); return; }
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _status.Text = "...";
        try
        {
            int pageCount = _pagesBox.SelectedItem is int n ? n : 3;
            var list = await GameDiscovery.FetchServersAsync(place, pageCount, _cts.Token);
            _rows.Clear();
            foreach (var s in list.OrderByDescending(s => s.Playing)) _rows.Add(s);
            var name = await GameDiscovery.GetPlaceNameAsync(place, _cts.Token);
            if (!string.IsNullOrEmpty(name)) Title = $"{T("Server List")} - {name}";
            _status.Text = $"{_rows.Count} {T("servers")}";
        }
        catch (OperationCanceledException) { }
    }

    private async Task JoinSelected()
    {
        if (_grid.SelectedItem is not ServerInfo s) { _status.Text = T("Select a server first"); return; }
        if (!long.TryParse(_place.Text.Trim(), out var place)) return;
        await _join(place, s.Id);
    }
}
