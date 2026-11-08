using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>即時日誌檢視。Ctrl+F 跳到搜尋框，輸入即過濾；新紀錄用附加的方式加入，不重繪整份清單。</summary>
public sealed class LogWindow : Window
{
    private readonly TextBox _filter = Box();
    private readonly TextBox _view = Box(multiline: true);
    private readonly CheckBox _follow = Check(T("Auto-scroll"), true);
    private string _current = "";

    public LogWindow()
    {
        Title = T("Logs");
        Chrome.Attach(this);
        Width = 820; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var top = new DockPanel { Margin = new Thickness(10, 10, 10, 0) };
        var clear = Btn(T("Clear"), (_, _) => { AppLog.Clear(); Reload(); }, left: 8);
        var copy = Btn(T("Copy"), (_, _) => { if (_view.Text.Length > 0) Clipboard.SetText(_view.Text); }, left: 8);
        DockPanel.SetDock(clear, Dock.Right); DockPanel.SetDock(copy, Dock.Right);
        _follow.Margin = Gap(8, 0, 0, 0);
        _follow.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_follow, Dock.Right);
        top.Children.Add(clear); top.Children.Add(copy); top.Children.Add(_follow);
        var label = new TextBlock { Text = T("Search (Ctrl+F):"), VerticalAlignment = VerticalAlignment.Center, Margin = Gap(0, 0, 6, 0) };
        DockPanel.SetDock(label, Dock.Left);
        top.Children.Add(label);
        top.Children.Add(_filter);

        _view.IsReadOnly = true;
        _view.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        _view.Margin = new Thickness(10);

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(_view);
        Content = dock;

        _filter.TextChanged += (_, _) => Reload();
        InputBindings.Add(new KeyBinding(new RelayCommand(() => { _filter.Focus(); _filter.SelectAll(); }), Key.F, ModifierKeys.Control));

        AppLog.Added += OnAdded;
        Closed += (_, _) => AppLog.Added -= OnAdded;
        Reload();
    }

    private void OnAdded(LogEntry e) => Dispatcher.BeginInvoke(() =>
    {
        var f = _filter.Text.Trim();
        if (f.Length > 0 && !e.ToString().Contains(f, StringComparison.OrdinalIgnoreCase)) return;
        _view.AppendText(e + Environment.NewLine);
        if (_follow.IsChecked == true) _view.ScrollToEnd();
    });

    private void Reload()
    {
        var lines = AppLog.Snapshot(_filter.Text.Trim());
        _current = string.Join(Environment.NewLine, lines.Select(l => l.ToString()));
        _view.Text = _current.Length > 0 ? _current + Environment.NewLine : "";
        if (_follow.IsChecked == true) _view.ScrollToEnd();
    }
}

internal sealed class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
