using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Shell;

namespace RobloxAccountManager.App;

/// <summary>
/// 自繪標題列：用 WPF 內建的 WindowChrome 把系統標題列藏起來，改在視窗內容最上面畫一條跟主題同色的標題列
/// （Windows 10 的系統標題列只能深 / 淺色，換不了主題色）。
/// 保留系統行為：拖曳移動、雙擊最大化、右鍵 / Alt+Space 系統選單、邊框縮放、Aero Snap。
/// </summary>
internal static class Chrome
{
    public const double BarHeight = 36;

    private static readonly DependencyProperty AttachedProperty =
        DependencyProperty.RegisterAttached("ChromeAttached", typeof(bool), typeof(Chrome), new PropertyMetadata(false));

    /// <summary>
    /// 替視窗換上自繪標題列。視窗已建立就立即套用，否則等 SourceInitialized（第一次顯示前）。
    /// <paramref name="extra"/> 會放在標題右邊（主視窗用來放選單列）。
    /// </summary>
    public static void Attach(Window w, UIElement? extra = null)
    {
        if ((bool)w.GetValue(AttachedProperty)) return;
        w.SetValue(AttachedProperty, true);
        if (PresentationSource.FromVisual(w) is not null) Build(w, extra);
        else w.SourceInitialized += (_, _) => Build(w, extra);
    }

    private static void Build(Window w, UIElement? extra)
    {
        var content = w.Content as UIElement;
        w.Content = null;

        WindowChrome.SetWindowChrome(w, new WindowChrome
        {
            CaptionHeight = BarHeight,
            ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
            GlassFrameThickness = new Thickness(0, 0, 0, 1),     // 留 1px 讓 DWM 保留視窗陰影
            UseAeroCaptionButtons = false,
            CornerRadius = new CornerRadius(0),
        });

        // ---- 右側：最小化 / 最大化 / 關閉 ----
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        Button? maxBtn = null;
        if (w.ResizeMode != ResizeMode.NoResize)
            buttons.Children.Add(CaptionButton("", "Minimize", (_, _) => w.WindowState = WindowState.Minimized));
        if (w.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
        {
            maxBtn = CaptionButton("", "Maximize", (_, _) =>
                w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            buttons.Children.Add(maxBtn);
        }
        buttons.Children.Add(CaptionButton("", "Close", (_, _) => w.Close(), close: true));

        // ---- 左側：標題（＋額外內容，例如選單） ----
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 1), FontSize = 12 };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = w });
        DockPanel.SetDock(title, Dock.Left);

        var bar = new DockPanel { Height = BarHeight, LastChildFill = true, Background = System.Windows.Media.Brushes.Transparent };
        bar.Children.Add(buttons);
        bar.Children.Add(title);
        if (extra is not null)
        {
            WindowChrome.SetIsHitTestVisibleInChrome(extra, true);
            if (extra is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(extra, Dock.Left);
            bar.Children.Add(extra);
        }
        bar.Children.Add(new Border());      // 剩下的空白都是可拖曳的標題區

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        if (content is not null) root.Children.Add(content);

        var frame = new Border { Child = root };
        frame.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        w.Content = frame;

        void SyncState()
        {
            bool max = w.WindowState == WindowState.Maximized;
            // 最大化時視窗會超出螢幕一圈縮放邊框，補回來才不會切掉內容
            var rb = SystemParameters.WindowResizeBorderThickness;
            frame.Margin = max ? new Thickness(rb.Left + 4, rb.Top + 4, rb.Right + 4, rb.Bottom + 4) : new Thickness(0);
            frame.BorderThickness = max ? new Thickness(0) : new Thickness(1);
            if (maxBtn is not null)
            {
                ((TextBlock)maxBtn.Content).Text = max ? "" : "";
                maxBtn.ToolTip = max ? Core.Services.Loc.T("Restore") : Core.Services.Loc.T("Maximize");
            }
        }
        void SyncActive() => title.SetResourceReference(TextBlock.ForegroundProperty,
            w.IsActive ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");

        w.StateChanged += (_, _) => SyncState();
        w.Activated += (_, _) => SyncActive();
        w.Deactivated += (_, _) => SyncActive();
        SyncState();
        SyncActive();
    }

    private static Button CaptionButton(string glyph, string tip, RoutedEventHandler onClick, bool close = false)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, FontSize = 10 },
            ToolTip = Core.Services.Loc.T(tip),
            Focusable = false,
            IsTabStop = false,
        };
        b.SetResourceReference(FrameworkElement.StyleProperty, close ? "CaptionCloseButton" : "CaptionButton");
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.Click += onClick;
        return b;
    }
}
