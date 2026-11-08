using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>主題編輯器：以內建主題為底調整每個顏色 token、設定背景圖與不透明度，存成自訂主題（可套用 / 刪除）。</summary>
public sealed class ThemeEditorWindow : Window
{
    private readonly ComboBox _base = new() { Width = 180 };
    private readonly TextBox _name = Box("My Theme", 180);
    private readonly Dictionary<string, TextBox> _hex = new();
    private readonly Dictionary<string, Border> _swatch = new();
    private readonly TextBox _bg = Box();
    private readonly Slider _opacity = new() { Minimum = 0.05, Maximum = 1, Value = 0.5, Width = 160, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0) };

    public string? AppliedTheme { get; private set; }

    public ThemeEditorWindow(string current)
    {
        Title = T("Theme Editor");
        Chrome.Attach(this);
        Width = 560; Height = 700; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var p = new StackPanel { Margin = new Thickness(16) };
        p.Children.Add(Label(T("Start from:"), 4));
        var customNames = ThemeManager.LoadCustom().Keys;
        _base.ItemsSource = ThemeManager.Builtin.Keys.Concat(customNames).ToList();
        p.Children.Add(_base);
        p.Children.Add(new Border { Height = 10 });

        foreach (var token in ThemeManager.Tokens)
        {
            var hex = Box("#000000", 110);
            var sw = new Border { Width = 28, Height = 22, Margin = Gap(8, 0, 8, 0), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
            sw.Cursor = System.Windows.Input.Cursors.Hand;
            sw.MouseLeftButtonUp += (_, _) => PickColor(token);
            hex.TextChanged += (_, _) => UpdateSwatch(token);
            _hex[token] = hex; _swatch[token] = sw;
            var lbl = new TextBlock { Text = T(ThemeManager.TokenLabels[token]), Width = 160, VerticalAlignment = VerticalAlignment.Center };
            var row = Row(lbl, hex, sw);
            row.Margin = Gap(0, 0, 0, 6);
            p.Children.Add(row);
        }

        p.Children.Add(new Border { Height = 8 });
        p.Children.Add(Label(T("Background image (optional):"), 4));
        var browse = Btn("...", (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*" };
            if (dlg.ShowDialog(this) == true) _bg.Text = dlg.FileName;
        });
        var bgRow = new DockPanel { Margin = Gap(0, 0, 0, 6) };
        DockPanel.SetDock(browse, Dock.Right); browse.Margin = Gap(8);
        bgRow.Children.Add(browse); bgRow.Children.Add(_bg);
        p.Children.Add(bgRow);
        p.Children.Add(Row(new TextBlock { Text = T("Opacity:"), VerticalAlignment = VerticalAlignment.Center, Margin = Gap(0, 0, 8, 0) }, _opacity));

        p.Children.Add(Label(T("Theme name:"), 4));
        p.Children.Add(_name);
        var btns = Row(Btn(T("Save & Apply"), (_, _) => SaveApply()), Btn(T("Delete"), (_, _) => Delete(), left: 8), Btn(T("Close"), (_, _) => Close(), left: 8));
        btns.Margin = Gap(0, 12, 0, 0);
        p.Children.Add(btns);
        p.Children.Add(_status);
        Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _base.SelectionChanged += (_, _) => LoadBase(_base.SelectedItem as string);
        _base.SelectedItem = ThemeManager.Builtin.ContainsKey(current) || customNames.Contains(current) ? current : "Graphite";
        if (customNames.Contains(current)) _name.Text = current;
    }

    private void LoadBase(string? name)
    {
        if (name is null) return;
        foreach (var kv in ThemeManager.ColorsOf(name)) if (_hex.TryGetValue(kv.Key, out var tb)) tb.Text = kv.Value;
        var custom = ThemeManager.LoadCustom();
        if (custom.TryGetValue(name, out var c)) { _bg.Text = c.BackgroundPath; _opacity.Value = c.BackgroundOpacity; }
        else { _bg.Text = ""; _opacity.Value = 0.5; }
    }

    private void UpdateSwatch(string token)
    {
        try { _swatch[token].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_hex[token].Text.Trim())); }
        catch { _swatch[token].Background = Brushes.Transparent; }
    }

    private void PickColor(string token)
    {
        var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
        try { var c = (Color)ColorConverter.ConvertFromString(_hex[token].Text.Trim()); dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B); } catch { }
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            _hex[token].Text = $"#{dlg.Color.R:x2}{dlg.Color.G:x2}{dlg.Color.B:x2}";
    }

    private bool Valid(out Dictionary<string, string> colors)
    {
        colors = new();
        foreach (var (token, tb) in _hex)
        {
            try { _ = (Color)ColorConverter.ConvertFromString(tb.Text.Trim()); colors[token] = tb.Text.Trim(); }
            catch { _status.Text = $"{T("Invalid colour:")} {T(ThemeManager.TokenLabels[token])}"; return false; }
        }
        return true;
    }

    private void SaveApply()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0) { _status.Text = T("Enter a theme name."); return; }
        if (ThemeManager.BaseThemes.Contains(name) || ThemeManager.Builtin.ContainsKey(name)) { _status.Text = T("That name is used by a built-in theme."); return; }
        if (!Valid(out var colors)) return;
        ThemeManager.SaveCustom(name, colors, _bg.Text.Trim(), _opacity.Value);
        ThemeManager.Apply(name);
        AppliedTheme = name;
        _status.Text = T("Saved and applied.");
    }

    private void Delete()
    {
        var name = _name.Text.Trim();
        if (!ThemeManager.LoadCustom().ContainsKey(name)) { _status.Text = T("Only custom themes can be deleted."); return; }
        ThemeManager.DeleteCustom(name);
        if (AppSettings.Current.Theme == name) { AppSettings.Current.Theme = "System"; AppSettings.Current.Save(); ThemeManager.Apply("System"); AppliedTheme = "System"; }
        _status.Text = T("Deleted.");
    }
}
