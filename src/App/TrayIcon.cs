using System.Windows;

namespace RobloxAccountManager.App;

/// <summary>系統匣圖示：關閉視窗時可縮到系統匣繼續執行（預設關閉）。左鍵還原、右鍵選單可結束。</summary>
public sealed class TrayIcon : IDisposable
{
    private System.Windows.Forms.NotifyIcon? _icon;
    private readonly Window _window;
    private readonly Action _quit;

    public TrayIcon(Window window, Action quit) { _window = window; _quit = quit; }

    public bool Visible
    {
        get => _icon?.Visible == true;
        set
        {
            if (value && _icon is null) Create();
            if (_icon is not null) _icon.Visible = value;
        }
    }

    private void Create()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Core.Services.Loc.T("Open"), null, (_, _) => Restore());
        menu.Items.Add(Core.Services.Loc.T("Exit"), null, (_, _) => _quit());

        System.Drawing.Icon? ico = null;
        try { if (Environment.ProcessPath is { } p) ico = System.Drawing.Icon.ExtractAssociatedIcon(p); } catch { }

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = ico ?? System.Drawing.SystemIcons.Application,
            Text = "Roblox Account Manager V3",
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Restore(); };
    }

    public void Restore()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}
