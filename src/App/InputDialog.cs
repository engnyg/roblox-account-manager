using System.Windows;
using System.Windows.Controls;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.App;

/// <summary>簡單的文字輸入對話框（單行、多行或密碼）。</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox? _box;
    private readonly PasswordBox? _pw;

    private InputDialog(string title, string prompt, string initial, bool multiline, bool secret)
    {
        Title = title;
        Chrome.Attach(this);
        Width = multiline ? 560 : 440;
        SizeToContent = multiline ? SizeToContent.Manual : SizeToContent.Height;
        Height = multiline ? 380 : double.NaN;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = Loc.T("OK"), Width = 80, IsDefault = !multiline, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Loc.T("Cancel"), Width = 80, IsCancel = true };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var label = new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(label, Dock.Top);
        root.Children.Add(label);

        if (secret)
        {
            _pw = new PasswordBox();
            root.Children.Add(_pw);
            Loaded += (_, _) => _pw.Focus();
        }
        else
        {
            _box = new TextBox
            {
                Text = initial,
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.NoWrap : TextWrapping.Wrap,
                VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            };
            root.Children.Add(_box);
            Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        }
        Content = root;
    }

    private string Value => _pw?.Password ?? _box?.Text ?? "";

    public static string? Ask(Window? owner, string title, string prompt, string initial = "", bool multiline = false, bool secret = false)
    {
        var dlg = new InputDialog(title, prompt, initial, multiline, secret);
        if (owner is { IsLoaded: true }) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }
}
