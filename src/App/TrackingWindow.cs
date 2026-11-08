using System.Windows;
using System.Windows.Controls;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>追蹤：定時對每個 Roblox 視窗截圖並傳到 Discord webhook；可為每個帳號設定要擷取的區域。</summary>
public sealed class TrackingWindow : Window
{
    private readonly AppSettings _s = AppSettings.Current;
    private readonly BackgroundServices _bg;
    private readonly CheckBox _enable;
    private readonly TextBox _url, _interval, _regions;
    private readonly ComboBox _account;
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private string? _shown;

    public TrackingWindow(IEnumerable<Account> accounts, BackgroundServices bg)
    {
        _bg = bg;
        Title = T("Tracking");
        Chrome.Attach(this);
        Width = 620; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(Label(T("Take a screenshot of every running Roblox window on a timer and post it to a Discord webhook."), 10, dim: true));
        _enable = Check(T("Enable tracking"), _s.TrackingEnabled);
        p.Children.Add(_enable);
        p.Children.Add(Label(T("Discord webhook URL:")));
        _url = Box(_s.TrackingWebhookUrl); _url.Margin = Gap(0, 0, 0, 10);
        p.Children.Add(_url);
        _interval = Box(_s.TrackingIntervalMinutes.ToString(), 60);
        p.Children.Add(Row(Label(T("Every (minutes):"), 0), _interval));
        (_interval.Parent as StackPanel)!.Children[0].SetValue(FrameworkElement.MarginProperty, Gap(0, 0, 6, 0));

        p.Children.Add(Label(T("Capture regions per account (one per line: x,y,w,h in pixels from the window's top-left; empty = whole window):"), 4));
        _account = new ComboBox { ItemsSource = accounts.Select(a => a.Username).Where(n => n.Length > 0).ToList(), Margin = Gap(0, 0, 0, 6) };
        _account.SelectionChanged += (_, _) => { StoreRegions(); _shown = _account.SelectedItem as string; _regions!.Text = _shown is not null && _s.TrackingRegions.TryGetValue(_shown, out var r) ? string.Join(Environment.NewLine, r) : ""; };
        p.Children.Add(_account);
        _regions = Box(multiline: true); _regions.Height = 110;
        p.Children.Add(_regions);

        p.Children.Add(Row(Btn(T("Save"), (_, _) => Save()), Btn(T("Capture Now"), async (_, _) => await CaptureNowAsync(), left: 8)));
        ((StackPanel)p.Children[^1]).Margin = Gap(0, 12, 0, 0);
        p.Children.Add(_status);
        Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void StoreRegions()
    {
        if (_shown is null) return;
        var lines = _regions.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (lines.Count == 0) _s.TrackingRegions.Remove(_shown); else _s.TrackingRegions[_shown] = lines;
    }

    private void Save()
    {
        StoreRegions();
        _s.TrackingEnabled = _enable.IsChecked == true;
        _s.TrackingWebhookUrl = _url.Text.Trim();
        _s.TrackingIntervalMinutes = int.TryParse(_interval.Text, out var m) ? Math.Clamp(m, 1, 1440) : 5;
        var bad = _s.TrackingRegions.SelectMany(kv => kv.Value.Select(v => (kv.Key, v))).Where(x => !ScreenCapture.TryParseRegion(x.v, out _)).ToList();
        _s.Save();
        _bg.Apply(_s);
        _status.Text = bad.Count > 0
            ? $"{T("Saved, but these regions are invalid and will be skipped:")} {string.Join(", ", bad.Select(b => $"{b.Key}: {b.v}"))}"
            : (_s.TrackingEnabled && !ScreenshotTracker.IsValidWebhook(_s.TrackingWebhookUrl) ? T("Saved. Enter a valid https webhook URL to start tracking.") : T("Saved."));
    }

    private async Task CaptureNowAsync()
    {
        Save();
        if (!ScreenshotTracker.IsValidWebhook(_s.TrackingWebhookUrl)) { _status.Text = T("Enter a valid https webhook URL first."); return; }
        _status.Text = "...";
        int n = await _bg.Tracker.CaptureOnceAsync(_s);
        _status.Text = $"{T("Sent screenshots:")} {n}";
    }
}
