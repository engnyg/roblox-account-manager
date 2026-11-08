using System.Windows;
using System.Windows.Controls;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.App;

/// <summary>用程式碼組 UI 的小工具（讓次要視窗不需要各自一份 XAML）。</summary>
internal static class Ui
{
    public static string T(string k) => Loc.T(k);

    public static TextBlock Label(string text, double bottom = 4, bool dim = false) => new()
    {
        Text = text, Margin = new Thickness(0, 0, 0, bottom), TextWrapping = TextWrapping.Wrap, Opacity = dim ? 0.7 : 1,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Button Btn(string text, RoutedEventHandler onClick, double width = 0, double left = 0)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(left, 0, 0, 0) };
        if (width > 0) b.MinWidth = width;
        b.Click += onClick;
        return b;
    }

    public static StackPanel Row(params UIElement[] kids)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        for (int i = 0; i < kids.Length; i++)
        {
            // 同一列的元件之間自動留白：新的一組（標籤）前面留寬一點，其餘留窄一點
            if (i > 0 && kids[i] is FrameworkElement fe && fe.Margin.Left == 0)
                fe.Margin = new Thickness(kids[i] is TextBlock ? 16 : 8, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            p.Children.Add(kids[i]);
        }
        return p;
    }

    public static TextBox Box(string text = "", double width = 0, bool multiline = false)
    {
        var t = new TextBox { Text = text, VerticalContentAlignment = VerticalAlignment.Center };
        if (width > 0) t.Width = width;
        if (multiline)
        {
            t.AcceptsReturn = true;
            t.VerticalContentAlignment = VerticalAlignment.Top;
            t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            t.TextWrapping = TextWrapping.NoWrap;
            t.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        return t;
    }

    /// <summary>一個有標題的卡片區塊（取代 GroupBox，和主視窗的卡片一致）。</summary>
    public static Border Card(string title, params UIElement[] rows)
    {
        var sp = new StackPanel();
        if (title.Length > 0)
        {
            var t = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 10) };
            t.SetResourceReference(FrameworkElement.StyleProperty, "SectionTitle");
            sp.Children.Add(t);
        }
        foreach (var r in rows) sp.Children.Add(r);
        var b = new Border { Child = sp, Padding = new Thickness(16, 12, 16, 6), Margin = new Thickness(0, 0, 0, 10) };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        return b;
    }

    public static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value, Margin = new Thickness(0, 0, 0, 8) };

    public static Thickness Gap(double left = 0, double top = 0, double right = 0, double bottom = 0) => new(left, top, right, bottom);

    public static void Info(Window owner, string text, string title = "")
        => MessageBox.Show(owner, text, title.Length > 0 ? title : owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(Window owner, string text, string title = "")
        => MessageBox.Show(owner, text, title.Length > 0 ? title : owner.Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
