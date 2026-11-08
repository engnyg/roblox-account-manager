using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>遊戲探索排行榜（熱門 / 精選 / 吸金榜…）：可搜尋、選為目標、查看伺服器、直接用勾選帳號啟動。</summary>
public sealed class ChartsWindow : Window
{
    private readonly ListBox _sorts = new() { Width = 190, Margin = new Thickness(0, 0, 8, 0) };
    private readonly DataGrid _grid;
    private readonly TextBox _search = Box(width: 180);
    private readonly TextBlock _status = new() { Margin = new Thickness(14, 6, 0, 0) };
    private readonly ObservableCollection<ChartGame> _rows = new();
    private List<ChartSort> _charts = new();
    private readonly Action<long> _usePlace;
    private readonly Action<long> _openServers;
    private readonly Func<long, Task> _launch;

    public ChartsWindow(Action<long> usePlace, Action<long> openServers, Func<long, Task> launch)
    {
        _usePlace = usePlace; _openServers = openServers; _launch = launch;
        Title = T("Game Charts");
        Chrome.Attach(this);
        Width = 860; Height = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        var top = Row(Btn(T("Refresh"), async (_, _) => await LoadAsync()), Label(T("Search:"), 0), _search);
        top.Children[1].SetValue(FrameworkElement.MarginProperty, Gap(14, 0, 6, 0));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var bottom = new DockPanel { Margin = Gap(0, 8, 0, 0) };
        var btns = Row(
            Btn(T("Use This Game"), (_, _) => { if (Selected is { } g) { _usePlace(g.PlaceId); _status.Text = $"Place ID = {g.PlaceId}"; } }),
            Btn(T("Servers..."), (_, _) => { if (Selected is { } g) _openServers(g.PlaceId); }, left: 8),
            Btn(T("Launch Checked Here"), async (_, _) => { if (Selected is { } g) await _launch(g.PlaceId); }, left: 8));
        btns.Margin = Gap();
        DockPanel.SetDock(btns, Dock.Left);
        bottom.Children.Add(btns);
        bottom.Children.Add(_status);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        DockPanel.SetDock(_sorts, Dock.Left);
        _sorts.Background = System.Windows.Media.Brushes.Transparent;
        _sorts.BorderThickness = new Thickness(0);
        _sorts.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "NavItem");
        root.Children.Add(_sorts);

        _grid = new DataGrid
        {
            ItemsSource = _rows, AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column, CanUserAddRows = false,
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = T("Game"), Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var mono = (System.Windows.Media.FontFamily)Application.Current.Resources["MonoFont"];
        _grid.Columns.Add(new DataGridTextColumn { Header = T("Playing"), Binding = new System.Windows.Data.Binding("Playing") { StringFormat = "N0" }, Width = 100, FontFamily = mono });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Place ID", Binding = new System.Windows.Data.Binding("PlaceId"), Width = 140, FontFamily = mono });
        _grid.MouseDoubleClick += (_, _) => { if (Selected is { } g) { _usePlace(g.PlaceId); _status.Text = $"Place ID = {g.PlaceId}"; } };
        var card = new Border { Child = _grid, Padding = new Thickness(6, 4, 6, 4) };
        card.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        root.Children.Add(card);
        Content = root;

        _sorts.SelectionChanged += (_, _) => ShowSort();
        _search.TextChanged += (_, _) => ShowSort();
        Loaded += async (_, _) => await LoadAsync();
    }

    private ChartGame? Selected => _grid.SelectedItem as ChartGame;

    private async Task LoadAsync()
    {
        _status.Text = "...";
        try
        {
            _charts = await GameDiscovery.FetchChartsAsync();
            _sorts.ItemsSource = new[] { T("(All / Search)") }.Concat(_charts.Select(c => c.Name)).ToList();
            _sorts.SelectedIndex = 0;
            _status.Text = _charts.Count == 0 ? T("Charts are unavailable right now") : $"{_charts.Count} {T("lists")}";
        }
        catch (Exception e) { _status.Text = e.Message; }
    }

    private void ShowSort()
    {
        _rows.Clear();
        var q = _search.Text.Trim();
        IEnumerable<ChartGame> games = _sorts.SelectedIndex <= 0
            ? _charts.SelectMany(c => c.Games).GroupBy(g => g.PlaceId).Select(g => g.First())
            : _charts.ElementAtOrDefault(_sorts.SelectedIndex - 1)?.Games ?? [];
        if (q.Length > 0) games = games.Where(g => g.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || g.PlaceId.ToString() == q);
        foreach (var g in games) _rows.Add(g);
    }
}
