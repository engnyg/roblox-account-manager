using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>
/// 內嵌瀏覽器（WebView2）。兩種用途：
///  - 登入：開 Roblox 登入頁，使用者自己登入（含人機驗證），偵測到 .ROBLOSECURITY 就取走 cookie 並關閉（<see cref="Cookie"/>）；
///  - 以帳號開啟：先把該帳號的 cookie 寫進瀏覽器，再開 Roblox 首頁。
/// </summary>
public sealed class WebWindow : Window
{
    private const string LoginProfile = "login";

    private readonly WebView2 _web = new();
    private readonly string? _inject;
    private readonly string _startUrl;
    private readonly string _profile;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>登入模式取得的 cookie。</summary>
    public string? Cookie { get; private set; }

    private bool IsLogin => _inject is null;

    private WebWindow(string title, string startUrl, string profile, string? inject)
    {
        Title = title;
        Chrome.Attach(this);
        Width = 1000; Height = 760; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _startUrl = startUrl; _profile = profile; _inject = inject;

        var dock = new DockPanel();
        if (IsLogin)
        {
            // 登入提示列：人機驗證載不出來時可以重新整理，或改用不需要驗證的「快速登入」
            var reload = Btn(T("Reload page"), (_, _) => { try { _web.CoreWebView2?.Reload(); } catch { } });
            DockPanel.SetDock(reload, Dock.Right);
            var hint = new TextBlock
            {
                Text = T("If the verification keeps failing: reload the page, or choose “Another login option → Quick Log In” and enter the code on a device where you're already signed in."),
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = Gap(0, 0, 12, 0),
            };
            hint.SetResourceReference(FrameworkElement.StyleProperty, "Dim");
            hint.TextTrimming = TextTrimming.None;
            var bar = new DockPanel { Margin = Gap(14, 4, 14, 8) };
            bar.Children.Add(reload);
            bar.Children.Add(hint);
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
        }
        dock.Children.Add(_web);
        Content = dock;

        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) => { _poll.Stop(); _web.Dispose(); };
    }

    public static WebWindow ForLogin(Window owner)
        => new(T("Log in to Roblox"), "https://www.roblox.com/login", LoginProfile, null) { Owner = owner };

    public static WebWindow ForAccount(Window owner, string username, string cookie)
    {
        var safe = new string(username.Where(char.IsLetterOrDigit).ToArray());
        return new WebWindow($"Roblox - {username}", "https://www.roblox.com/home", "acc_" + (safe.Length > 0 ? safe : "x"), cookie) { Owner = owner };
    }

    private async Task InitAsync()
    {
        try
        {
            var root = Path.Combine(AppSettings.DataDir, "WebView2");
            if (IsLogin) CleanupOldLoginProfiles(root);
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(root, _profile));
            await _web.EnsureCoreWebView2Async(env);

            // Edge 的「追蹤防護」會擋掉人機驗證（Arkose）iframe 需要的第三方儲存，造成「請重新載入挑戰」。
            // 這個瀏覽器只用來登入 Roblox，所以關掉追蹤防護，讓驗證跟一般瀏覽器一樣運作。
            // 不要對這個瀏覽器開 DevTools 協定（Runtime.enable 等）：Roblox 的機器人偵測會把它當成自動化工具。
            _web.CoreWebView2.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.None;

            if (!IsLogin)
            {
                var c = _web.CoreWebView2.CookieManager.CreateCookie(".ROBLOSECURITY", _inject, ".roblox.com", "/");
                c.IsHttpOnly = true; c.IsSecure = true;
                _web.CoreWebView2.CookieManager.AddOrUpdateCookie(c);
            }
            else
            {
                // 登入共用同一個瀏覽器資料夾（每次都是全新資料夾的話，驗證服務會把它當成可疑的新瀏覽器），
                // 只清掉 Roblox 自己的 cookie，避免直接登入成上一個帳號。
                foreach (var c in await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com"))
                    _web.CoreWebView2.CookieManager.DeleteCookie(c);
                _poll.Tick += async (_, _) => await CheckCookieAsync();
                _poll.Start();
            }
            _web.CoreWebView2.Navigate(_startUrl);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, $"{T("WebView2 is not available")}: {e.Message}\n\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }

    /// <summary>舊版每次登入都建一個 login_xxxxxxxx 資料夾，順手清掉。</summary>
    private static void CleanupOldLoginProfiles(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root, "login_*"))
                try { Directory.Delete(dir, recursive: true); } catch { /* 可能還在使用中 */ }
        }
        catch { }
    }

    private async Task CheckCookieAsync()
    {
        try
        {
            var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
            var c = cookies.FirstOrDefault(x => x.Name == ".ROBLOSECURITY" && x.Value.Length > 50);
            if (c is null) return;
            _poll.Stop();
            Cookie = c.Value;
            DialogResult = true;
        }
        catch { /* 頁面還在載入 */ }
    }
}
