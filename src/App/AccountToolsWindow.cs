using System.Windows;
using System.Windows.Controls;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>
/// 帳號工具：PIN 解鎖、追蹤隱私、改密碼 / 信箱、顯示名稱、登出其他裝置、封鎖 / 解除封鎖、加好友、找玩家所在伺服器。
/// 對選取的帳號執行（改密碼 / 信箱 / 顯示名稱只對第一個選取的帳號）。
/// </summary>
public sealed class AccountToolsWindow : Window
{
    private readonly List<Account> _accounts;
    private readonly Action _save;
    private readonly Action<long, string> _setTarget;
    private readonly TextBox _log = Box(multiline: true);
    private readonly TextBox _pin = Box(width: 80), _curPw = Box(width: 160), _newPw = Box(width: 160),
        _emailPw = Box(width: 160), _newEmail = Box(width: 220), _display = Box(width: 200),
        _blockUser = Box(width: 160), _friend = Box(width: 160), _find = Box(width: 160);
    private readonly ComboBox _privacy = new() { ItemsSource = AccountApi.FollowPrivacy, SelectedIndex = 0, Width = 140 };

    public AccountToolsWindow(List<Account> accounts, Action save, Action<long, string> setTarget)
    {
        _accounts = accounts; _save = save; _setTarget = setTarget;
        Title = T("Account Tools");
        Chrome.Attach(this);
        Width = 640; Height = 720; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(Label($"{T("Selected:")} {string.Join(", ", accounts.Take(5).Select(a => a.Username))}{(accounts.Count > 5 ? $" … (+{accounts.Count - 5})" : "")}", 10));

        void Section(string title, params UIElement[] rows)
        {
            panel.Children.Add(Card(title, rows));
        }
        static Label0 L(string s) => new(s);

        Section(T("PIN"), Row(L(T("Account PIN (4 digits):")), _pin, Btn(T("Unlock"), async (_, _) => await ForEach(T("Unlock PIN"),
            async a => (await AccountApi.UnlockPinAsync(a, _pin.Text.Trim())) ? new ApiResult(true, "OK") : new ApiResult(false, "PIN 錯誤或帳號未啟用 PIN")), left: 8)));
        Section(T("Privacy"), Row(L(T("Who can follow me:")), _privacy, Btn(T("Apply"), async (_, _) => await ForEach(T("Follow privacy"),
            a => AccountApi.SetFollowPrivacyAsync(a, _privacy.SelectedIndex)), left: 8)));
        Section(T("Password"), Row(L(T("Current:")), _curPw, L(T("New:")), _newPw, Btn(T("Change"), async (_, _) =>
        {
            if (_newPw.Text.Length < 8) { Append(T("Password must be at least 8 characters")); return; }
            await First(T("Change password"), a => AccountApi.ChangePasswordAsync(a, _curPw.Text.Length > 0 ? _curPw.Text : a.Password, _newPw.Text));
            _save();
        }, left: 8)));
        Section(T("Email"), Row(L(T("Password:")), _emailPw, L(T("New email:")), _newEmail, Btn(T("Change"), async (_, _) =>
        {
            await First(T("Change email"), a => AccountApi.ChangeEmailAsync(a, _emailPw.Text.Length > 0 ? _emailPw.Text : a.Password, _newEmail.Text.Trim()));
            _save();
        }, left: 8)));
        Section(T("Display name"), Row(_display, Btn(T("Set"), async (_, _) => await First(T("Display name"),
            a => AccountApi.SetDisplayNameAsync(a, _display.Text.Trim())), left: 8)));
        Section(T("Sessions"), Row(Btn(T("Log out other sessions"), async (_, _) =>
        {
            if (!Confirm(this, T("This signs the selected accounts out everywhere else and issues a new cookie. Continue?"))) return;
            await ForEach(T("Log out other sessions"), a => AccountApi.LogoutOtherSessionsAsync(a));
            _save();
        })));
        Section(T("Blocking"), Row(L(T("Username:")), _blockUser,
            Btn(T("Block / Unblock"), async (_, _) => await ForEach(T("Block"), a => AccountApi.ToggleBlockAsync(a, _blockUser.Text.Trim())), left: 8),
            Btn(T("Unblock Everyone"), async (_, _) =>
            {
                if (Confirm(this, T("Unblock every blocked user on the selected accounts?")))
                    await ForEach(T("Unblock everyone"), a => AccountApi.UnblockEveryoneAsync(a));
            }, left: 8)));
        Section(T("Friends"), Row(L(T("Username:")), _friend, Btn(T("Send Friend Request"), async (_, _) =>
        {
            var uid = await AccountApi.GetUserIdAsync(_friend.Text.Trim());
            if (uid is null) { Append($"找不到用戶 {_friend.Text}"); return; }
            await ForEach(T("Friend request"), a => AccountApi.SendFriendRequestAsync(a, uid.Value));
        }, left: 8)));
        Section(T("Find player"), Row(L(T("Username:")), _find, Btn(T("Find Server"), async (_, _) => await FindAsync(), left: 8)));

        var sv = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _log.IsReadOnly = true; _log.Height = 150; _log.Margin = Gap(14, 0, 14, 14);
        var dock = new DockPanel();
        DockPanel.SetDock(_log, Dock.Bottom);
        dock.Children.Add(_log);
        dock.Children.Add(sv);
        Content = dock;
    }

    /// <summary>行內標籤（避免跟 WPF 的 Label 控制項混淆）。</summary>
    private sealed class Label0 : TextBlock
    {
        public Label0(string s) { Text = s; Margin = new Thickness(0, 0, 0, 0); VerticalAlignment = VerticalAlignment.Center; }
    }

    private void Append(string line) => _log.AppendText(line + Environment.NewLine);

    private async Task ForEach(string what, Func<Account, Task<ApiResult>> op)
    {
        foreach (var a in _accounts)
        {
            var r = await op(a);
            Append($"[{a.Username}] {what}: {(r.Ok ? "✔" : "✘")} {r.Message}");
            AppLog.Add(r.Ok ? "INFO" : "WARN", $"[帳號工具] {a.Username} {what}: {r.Message}");
        }
        _save();
    }

    private async Task First(string what, Func<Account, Task<ApiResult>> op)
    {
        if (_accounts.Count == 0) return;
        if (_accounts.Count > 1) Append(T("(Only the first selected account is used for this action)"));
        var a = _accounts[0];
        var r = await op(a);
        Append($"[{a.Username}] {what}: {(r.Ok ? "✔" : "✘")} {r.Message}");
        AppLog.Add(r.Ok ? "INFO" : "WARN", $"[帳號工具] {a.Username} {what}: {r.Message}");
    }

    private async Task FindAsync()
    {
        if (_accounts.Count == 0) return;
        var (ok, msg, place, job) = await AccountApi.FindPlayerServerAsync(_accounts[0], _find.Text.Trim());
        Append(ok ? $"{_find.Text}: Place {place}  Job {job}" : $"{_find.Text}: {msg}");
        if (ok) { _setTarget(place, job); Append(T("Place ID / Job ID filled in the main window")); }
    }
}
