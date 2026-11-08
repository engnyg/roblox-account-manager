using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace RobloxAccountManager.App;

/// <summary>
/// 讓系統標題列跟著主題走（不然深色主題上面會留一條白色標題列）。
/// Windows 10 20H1+：只能切換深 / 淺色；Windows 11：還可以把標題列、邊框、標題文字塗成主題顏色。
/// </summary>
internal static class TitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;   // Windows 10 1809 ~ 1909
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;                  // 以下三個只有 Windows 11 支援
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);

    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_NCACTIVATE = 0x0086;

    public static void Apply(Window w, bool dark, Color? caption, Color? text)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;     // 視窗還沒建立；之後的 Loaded 會再套用一次
        try
        {
            int v = dark ? 1 : 0;
            if (DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int)) != 0)
                DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref v, sizeof(int));

            int cap = caption is { } c ? ToColorRef(c) : DWMWA_COLOR_DEFAULT;
            int txt = text is { } t ? ToColorRef(t) : DWMWA_COLOR_DEFAULT;
            DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, ref cap, sizeof(int));
            DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref cap, sizeof(int));
            DwmSetWindowAttribute(h, DWMWA_TEXT_COLOR, ref txt, sizeof(int));

            // 已經顯示的視窗：要求重畫非工作區，並來回切一次「作用中」狀態，Windows 10 才會立刻重畫標題列
            if (w.IsVisible)
            {
                SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                bool active = w.IsActive;
                SendMessage(h, WM_NCACTIVATE, active ? IntPtr.Zero : 1, IntPtr.Zero);
                SendMessage(h, WM_NCACTIVATE, active ? 1 : IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch { /* 舊系統沒有 dwmapi 屬性就維持原樣 */ }
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    /// <summary>Windows「應用程式模式」是否為淺色（System 主題時用）。</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int i || i != 0;
        }
        catch { return true; }
    }
}
