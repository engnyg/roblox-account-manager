using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.App;

/// <summary>
/// 主題：System / Light / Dark 直接用 WPF Fluent 的 ThemeMode；
/// 其餘（Aurora、Ocean、Forest、Amber、Rose、Sunset、Graphite、Mono 與自訂主題）以顏色 token 覆寫 Fluent 的畫筆資源。
/// 自訂主題存在 %LOCALAPPDATA%\RobloxAccountManagerV3\custom_themes.json，可附背景圖與不透明度。
/// </summary>
public static class ThemeManager
{
    public static readonly string[] Tokens = ["bg_window", "bg_panel", "bg_button", "bg_hover", "fg_text", "fg_dim", "accent", "border"];

    public static readonly Dictionary<string, string> TokenLabels = new()
    {
        ["bg_window"] = "Window Background", ["bg_panel"] = "Panel Background", ["bg_button"] = "Button Background",
        ["bg_hover"] = "Button Hover", ["fg_text"] = "Primary Text", ["fg_dim"] = "Secondary Text",
        ["accent"] = "Accent Color", ["border"] = "Border",
    };

    public static readonly string[] BaseThemes = ["System", "Light", "Dark"];

    private static Dictionary<string, string> C(string win, string panel, string btn, string hover, string text, string dim, string accent, string border) => new()
    {
        ["bg_window"] = win, ["bg_panel"] = panel, ["bg_button"] = btn, ["bg_hover"] = hover,
        ["fg_text"] = text, ["fg_dim"] = dim, ["accent"] = accent, ["border"] = border,
    };

    public static readonly Dictionary<string, Dictionary<string, string>> Builtin = new()
    {
        ["Aurora"] = C("#14201f", "#0f1a19", "#1f3331", "#2c4845", "#d8f3ee", "#8db8b0", "#5eead4", "#2c4845"),
        ["Ocean"] = C("#101a2b", "#0b1422", "#1b2c47", "#26406a", "#dbe8ff", "#8aa3c9", "#4ea1ff", "#26406a"),
        ["Forest"] = C("#16201a", "#101811", "#233328", "#31483a", "#dcebd9", "#92a893", "#7bc47f", "#31483a"),
        ["Amber"] = C("#241c10", "#1a140a", "#3a2c14", "#52401d", "#fbe9c8", "#c3a878", "#fbbf24", "#52401d"),
        ["Rose"] = C("#241519", "#1a0e12", "#3a1f27", "#522c38", "#fbdde5", "#c394a2", "#fb7185", "#522c38"),
        ["Sunset"] = C("#241a1f", "#1a1217", "#3b2430", "#533345", "#ffe3d6", "#c9a291", "#fb923c", "#533345"),
        ["Graphite"] = C("#1f2124", "#17191b", "#2e3135", "#3d4146", "#e6e8ea", "#9aa0a6", "#8ab4f8", "#3d4146"),
        ["Mono"] = C("#f4f4f5", "#e4e4e7", "#d4d4d8", "#c4c4ca", "#18181b", "#52525b", "#3f3f46", "#a1a1aa"),
    };

    private static string CustomFile => Path.Combine(AppSettings.DataDir, "custom_themes.json");

    public sealed record CustomTheme(Dictionary<string, string> Colors, string BackgroundPath, double BackgroundOpacity);

    public static Dictionary<string, CustomTheme> LoadCustom()
    {
        var result = new Dictionary<string, CustomTheme>();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(CustomFile)) is not JsonObject root) return result;
            foreach (var kv in root)
            {
                var colors = new Dictionary<string, string>();
                if (kv.Value?["colors"] is JsonObject co) foreach (var c in co) colors[c.Key] = c.Value?.ToString() ?? "";
                result[kv.Key] = new CustomTheme(colors,
                    kv.Value?["background"]?["path"]?.ToString() ?? "",
                    kv.Value?["background"]?["opacity"]?.GetValue<double>() ?? 0.5);
            }
        }
        catch { /* 沒有檔案或壞掉 */ }
        return result;
    }

    public static void SaveCustom(string name, Dictionary<string, string> colors, string bgPath, double bgOpacity)
    {
        var all = LoadCustom();
        all[name] = new CustomTheme(colors, bgPath, bgOpacity);
        WriteCustom(all);
    }

    public static void DeleteCustom(string name)
    {
        var all = LoadCustom();
        if (all.Remove(name)) WriteCustom(all);
    }

    private static void WriteCustom(Dictionary<string, CustomTheme> all)
    {
        var root = new JsonObject();
        foreach (var (name, t) in all)
        {
            var colors = new JsonObject();
            foreach (var c in t.Colors) colors[c.Key] = c.Value;
            root[name] = new JsonObject
            {
                ["colors"] = colors,
                ["background"] = new JsonObject { ["path"] = t.BackgroundPath, ["opacity"] = t.BackgroundOpacity },
            };
        }
        File.WriteAllText(CustomFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static List<string> AllNames() =>
        [.. BaseThemes, .. Builtin.Keys, .. LoadCustom().Keys.Where(k => !Builtin.ContainsKey(k) && !BaseThemes.Contains(k))];

    public static Dictionary<string, string> ColorsOf(string name)
    {
        if (LoadCustom().TryGetValue(name, out var c)) return Merge(Builtin["Graphite"], c.Colors);
        return Builtin.TryGetValue(name, out var b) ? new(b) : new(Builtin["Graphite"]);
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string> baseColors, Dictionary<string, string> over)
    {
        var d = new Dictionary<string, string>(baseColors);
        foreach (var kv in over) d[kv.Key] = kv.Value;
        return d;
    }

    // ------------------------------------------------------------------ //

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static readonly List<object> Overridden = new();

    /// <summary>目前背景圖設定（供主視窗套用）。</summary>
    public static (string Path, double Opacity) Background { get; private set; } = ("", 0.5);

    /// <summary>目前是否啟用內建 / 自訂的彩色主題（非 System / Light / Dark）。</summary>
    public static bool CustomActive { get; private set; }

    /// <summary>目前主題是否為深色（決定標題列要用深色還是淺色）。</summary>
    public static bool IsDark { get; private set; }

    /// <summary>彩色主題的視窗底色；標題列在 Windows 11 會塗成這個顏色（Windows 10 只能切深 / 淺）。</summary>
    public static Color? WindowColor { get; private set; }
    public static Color? TextColor { get; private set; }

    /// <summary>給網頁控制面板用的目前主題顏色（讓手機上的面板跟桌面同一套配色）。</summary>
    public static Dictionary<string, string> WebColors()
    {
        var colors = new Dictionary<string, string>(
            CustomActive ? ColorsOf(AppSettings.Current.Theme) : Builtin[IsDark ? "Graphite" : "Mono"]);
        try { colors["on_accent"] = Luma(Parse(colors["accent"])) > 0.6 ? "#141414" : "#ffffff"; }
        catch { colors["on_accent"] = "#ffffff"; }
        return colors;
    }

    /// <summary>彩色主題下，視窗本身要用主題的視窗底色（Fluent 預設是 Mica 背景，不會吃我們覆寫的畫筆）。</summary>
    public static void ApplyToWindow(Window w)
    {
        TitleBar.Apply(w, IsDark, WindowColor, TextColor);
        if (w.Background is ImageBrush) return;
        // 自繪標題列（WindowChrome）會讓 Fluent 的 Mica 背景失效，所以每種主題都用實色視窗底色
        w.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
    }

    public static void Apply(string name)
    {
        var app = Application.Current;
        foreach (var k in Overridden) app.Resources.Remove(k);
        Overridden.Clear();
        Background = ("", 0.5);
        CustomActive = false;
        WindowColor = null; TextColor = null;
        IsDark = !TitleBar.SystemUsesLightTheme();
        try { ApplyCore(app, name); }
        finally
        {
            RebuildBaseStyles(app);
            foreach (Window w in app.Windows) ApplyToWindow(w);
        }
    }

    private static readonly object[] BaseStyleKeys = [typeof(TextBox), typeof(DataGrid), typeof(DataGridColumnHeader), "MonoBox"];

    /// <summary>
    /// 全域樣式要以「目前」Fluent 主題的樣式為基底，所以每次切換主題都重建
    /// （XAML 的 BasedOn StaticResource 只會在解析時綁一次，切到 Dark / System 就會拿到錯的顏色）。
    /// 顏色一律用 DynamicResource，主題畫筆換掉時會跟著變。
    /// </summary>
    private static void RebuildBaseStyles(Application app)
    {
        foreach (var k in BaseStyleKeys) app.Resources.Remove(k);
        Style? Fluent(Type t) => app.TryFindResource(t) as Style;
        static Setter Dyn(DependencyProperty p, string key) => new(p, new DynamicResourceExtension(key));

        var textBox = new Style(typeof(TextBox), Fluent(typeof(TextBox)));
        textBox.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80))));
        textBox.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        textBox.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
        app.Resources[typeof(TextBox)] = textBox;

        var mono = new Style(typeof(TextBox), textBox);
        mono.Setters.Add(new Setter(Control.FontFamilyProperty, app.Resources["MonoFont"]));
        mono.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        mono.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        mono.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        app.Resources["MonoBox"] = mono;

        // 表格：去掉框線與底色，只留細分隔線，讓卡片負責區塊感
        var grid = new Style(typeof(DataGrid), Fluent(typeof(DataGrid)));
        grid.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        grid.Setters.Add(new Setter(DataGrid.RowBackgroundProperty, Brushes.Transparent));
        grid.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        grid.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.Horizontal));
        grid.Setters.Add(Dyn(DataGrid.HorizontalGridLinesBrushProperty, "DividerStrokeColorDefaultBrush"));
        grid.Setters.Add(new Setter(DataGrid.HeadersVisibilityProperty, DataGridHeadersVisibility.Column));
        grid.Setters.Add(new Setter(DataGrid.CanUserResizeRowsProperty, false));
        grid.Setters.Add(new Setter(DataGrid.RowHeightProperty, 36.0));
        app.Resources[typeof(DataGrid)] = grid;

        var header = new Style(typeof(DataGridColumnHeader), Fluent(typeof(DataGridColumnHeader)));
        header.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        header.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        header.Setters.Add(Dyn(Control.ForegroundProperty, "TextFillColorSecondaryBrush"));
        header.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        header.Setters.Add(Dyn(Control.BorderBrushProperty, "DividerStrokeColorDefaultBrush"));
        header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        header.Setters.Add(new Setter(DataGridColumnHeader.SeparatorVisibilityProperty, Visibility.Collapsed));
        app.Resources[typeof(DataGridColumnHeader)] = header;
    }

    private static void ApplyCore(Application app, string name)
    {
        switch (name)
        {
            case "Light": app.ThemeMode = ThemeMode.Light; IsDark = false; return;
            case "Dark": app.ThemeMode = ThemeMode.Dark; IsDark = true; return;
            case "System": case "": app.ThemeMode = ThemeMode.System; return;
        }

        var custom = LoadCustom();
        Dictionary<string, string> colors;
        if (custom.TryGetValue(name, out var ct))
        {
            colors = Merge(Builtin["Graphite"], ct.Colors);
            Background = (ct.BackgroundPath, ct.BackgroundOpacity);
        }
        else if (Builtin.TryGetValue(name, out var b)) colors = b;
        else { app.ThemeMode = ThemeMode.System; return; }

        CustomActive = true;
        var win = Parse(colors["bg_window"]);
        bool dark = Luma(win) < 0.5;
        IsDark = dark;
        WindowColor = win;
        TextColor = Parse(colors["fg_text"]);
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

        void Set(object key, Color c)
        {
            app.Resources[key] = new SolidColorBrush(c);
            Overridden.Add(key);
        }

        var panel = Parse(colors["bg_panel"]);
        var text = Parse(colors["fg_text"]);
        var dim = Parse(colors["fg_dim"]);
        var accent = Parse(colors["accent"]);
        var onAccent = Luma(accent) > 0.6 ? Color.FromRgb(20, 20, 20) : Colors.White;

        foreach (var key in WindowKeys) Set(key, win);
        foreach (var key in PanelKeys) Set(key, panel);
        foreach (var key in AccentKeys) Set(key, accent);
        foreach (var key in AccentHoverKeys) Set(key, Mix(accent, dark ? Colors.White : Colors.Black, 0.12));
        foreach (var key in OnAccentKeys) Set(key, onAccent);
        foreach (var key in new[] { "TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush", "TextControlPlaceholderForeground" }) Set(key, dim);
        Set("TextFillColorPrimaryBrush", text);

        // 其餘「前景」類畫筆：原本是中性色（深色主題的白 / 淺色主題的黑）就換成主題文字色
        foreach (var (key, brush) in EnumerateBrushes(app.Resources))
        {
            if (Overridden.Contains(key) || key is not string keyName) continue;
            if (!keyName.Contains("Foreground") || keyName.Contains("Accent") || keyName.Contains("Hyperlink") || keyName.Contains("Checked") || keyName.Contains("OnAccent")) continue;
            var c = brush.Color;
            bool neutral = Math.Abs(c.R - c.G) < 8 && Math.Abs(c.G - c.B) < 8;
            double l = Luma(c);
            if (neutral && (dark ? l > 0.85 : l < 0.15)) Set(key, text);
        }

        // 老式 System 畫筆（部分控制項仍然使用）
        Set(SystemColors.WindowBrushKey, win);
        Set(SystemColors.ControlBrushKey, panel);
        Set(SystemColors.ControlTextBrushKey, text);
        Set(SystemColors.WindowTextBrushKey, text);
        Set(SystemColors.HighlightBrushKey, accent);
        Set(SystemColors.HighlightTextBrushKey, onAccent);
        Set(SystemColors.MenuBrushKey, panel);
        Set(SystemColors.MenuTextBrushKey, text);
    }

    private static readonly string[] WindowKeys =
    [
        "ApplicationBackgroundBrush", "WindowBackground", "NavigationWindowBackground", "SolidBackgroundFillColorBaseBrush",
        "SolidBackgroundFillColorBaseAltBrush", "ContentDialogBackground", "MessageBoxBackground", "LoadingScreenBackground",
    ];

    private static readonly string[] PanelKeys =
    [
        "SolidBackgroundFillColorSecondaryBrush", "SolidBackgroundFillColorTertiaryBrush", "SolidBackgroundFillColorQuarternaryBrush",
        "LayerFillColorDefaultBrush", "LayerOnMicaBaseAltFillColorDefaultBrush", "LayerOnMicaBaseAltFillColorTertiaryBrush",
        "NavigationViewContentBackground", "ComboBoxDropDownBackground", "ContextMenuBackground", "FlyoutBackground",
        "DatePickerPopupBackground", "SnackBarBackground", "ToolTipBackground", "ToolTipBackgroundBrush", "CalendarViewBackground",
        "ControlFillColorInputActiveBrush", "SystemFillColorSolidNeutralBackgroundBrush", "SystemFillColorSolidAttentionBackgroundBrush",
        "ControlSolidFillColorDefaultBrush",
    ];

    private static readonly string[] AccentKeys =
    [
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "SystemFillColorAttentionBrush",
        "AccentButtonBackground", "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
        "ToggleSwitchFillOn", "ToggleSwitchStrokeOn", "ToggleButtonBackgroundChecked", "CheckBoxCheckBackgroundFillChecked",
        "CheckBoxCheckBackgroundFillIndeterminate", "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeIndeterminate",
        "ProgressBarForeground", "ProgressRingForegroundThemeBrush", "SliderThumbBackground", "NavigationViewSelectionIndicatorForeground",
        "ListViewItemPillFillBrush", "ComboBoxItemPillFillBrush", "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke",
        "TreeViewItemSelectionIndicatorForeground", "DataGridRowSelectedBackgroundThemeBrush", "ListBoxItemSelectedBackgroundThemeBrush",
        "AccentFillColorSelectedTextBackgroundBrush", "HyperlinkForeground", "HyperlinkButtonForeground", "CalendarViewSelectedBackground",
        "CalendarViewSelectedBorderBrush", "CalendarViewTodayBackground", "RatingControlSelectedForeground",
    ];

    private static readonly string[] AccentHoverKeys = ["AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed"];

    private static readonly string[] OnAccentKeys =
    [
        "AccentButtonForeground", "CheckBoxCheckGlyphForeground", "DataGridRowSelectedForegroundThemeBrush", "ToggleButtonForegroundChecked",
        "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush", "RadioButtonCheckGlyphFill", "ToggleSwitchKnobFillOn",
        "ListBoxItemSelectedForegroundThemeBrush", "CalendarViewTodayForeground",
    ];

    private static IEnumerable<(object Key, SolidColorBrush Brush)> EnumerateBrushes(ResourceDictionary d)
    {
        foreach (var k in d.Keys.Cast<object>().ToList())
            if (d[k] is SolidColorBrush b) yield return (k, b);
        foreach (var m in d.MergedDictionaries)
            foreach (var x in EnumerateBrushes(m)) yield return x;
    }

    /// <summary>除錯：列出目前套用中的 Fluent 資源鍵（用來確認要覆寫的鍵名）。</summary>
    public static IEnumerable<string> DumpKeys()
    {
        var seen = new SortedSet<string>();
        void Walk(ResourceDictionary d)
        {
            foreach (var k in d.Keys) seen.Add($"{k} = {(d[k] is SolidColorBrush b ? b.Color.ToString() : d[k]?.GetType().Name)}");
            foreach (var m in d.MergedDictionaries) Walk(m);
        }
        Walk(Application.Current.Resources);
        return seen;
    }
}
