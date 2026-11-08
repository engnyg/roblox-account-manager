using System.Windows;
using System.Windows.Controls;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Services;
using static RobloxAccountManager.App.Ui;

namespace RobloxAccountManager.App;

/// <summary>編輯單一帳號：別名 / 群組 / 描述、專屬加入目標（遊戲 ID / 邀請連結 / 私人伺服器連結 / 追蹤玩家）、自訂欄位。</summary>
public sealed class AccountEditWindow : Window
{
    private readonly Account _acc;
    private readonly TextBox _alias, _group, _desc, _target, _follow, _email, _emailPw, _password, _fields;
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 0) };
    private long _followId;

    public AccountEditWindow(Account acc)
    {
        _acc = acc;
        Title = $"{T("Edit Account")} - {acc.Username}";
        Chrome.Attach(this);
        Width = 560; Height = 760; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var p = new StackPanel { Margin = new Thickness(16) };
        TextBox Field(string label, string value, bool multiline = false, double height = 0)
        {
            p.Children.Add(Label(label));
            var b = Box(value, multiline: multiline);
            b.Margin = Gap(0, 0, 0, 10);
            if (height > 0) b.Height = height;
            p.Children.Add(b);
            return b;
        }

        _alias = Field(T("Alias"), acc.Alias);
        _group = Field(T("Group"), acc.Group);
        _desc = Field(T("Description"), acc.Description);

        p.Children.Add(Label(T("Launch target for this account (Place ID, game URL, Job ID link, or private server link; blank = use the main window's Place/Job):")));
        _target = Box(acc.Fields.TryGetValue(AccountTargets.TargetField, out var t) ? t : "");
        _target.Margin = Gap(0, 0, 0, 4);
        _target.TextChanged += (_, _) => UpdateHint();
        p.Children.Add(_target);
        p.Children.Add(_hint);

        p.Children.Add(Label(T("Follow a player instead (username; overrides the target above):"), 4));
        _follow = Box(acc.Fields.TryGetValue("FollowUserName", out var fn) ? fn : "");
        _follow.Margin = Gap(0, 0, 0, 4);
        var lookup = Btn(T("Look up"), async (_, _) => await LookupAsync());
        var frow = new DockPanel();
        DockPanel.SetDock(lookup, Dock.Right); lookup.Margin = Gap(8);
        frow.Children.Add(lookup); frow.Children.Add(_follow);
        p.Children.Add(frow);
        _followId = acc.Fields.TryGetValue(AccountTargets.FollowField, out var fid) && long.TryParse(fid, out var fl) ? fl : 0;
        p.Children.Add(new Border { Height = 10 });

        _email = Field("Email", acc.Email);
        _emailPw = Field(T("Email password"), acc.EmailPassword);
        _password = Field(T("Account password (used by Account Tools)"), acc.Password);
        _fields = Field(T("Custom fields (key=value per line)"), string.Join(Environment.NewLine,
            acc.Fields.Where(kv => kv.Key is not (AccountTargets.TargetField or AccountTargets.FollowField or "FollowUserName")).Select(kv => $"{kv.Key}={kv.Value}")),
            multiline: true, height: 90);

        // 確定 / 取消固定在底部，不跟著捲動
        var btns = Row(Btn(T("OK"), Ok_Click, 80), Btn(T("Cancel"), (_, _) => DialogResult = false, 80, 8));
        btns.HorizontalAlignment = HorizontalAlignment.Right;
        btns.Margin = Gap(16, 8, 16, 14);
        var dock = new DockPanel();
        DockPanel.SetDock(btns, Dock.Bottom);
        dock.Children.Add(btns);
        dock.Children.Add(new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
        UpdateHint();
    }

    private void UpdateHint()
    {
        var tg = AccountTargets.Parse(_target.Text);
        _hint.Text = _target.Text.Trim().Length == 0 ? "" : tg.Kind == TargetKind.None ? T("Not recognised - this target will be ignored.") : "→ " + AccountTargets.Describe(tg);
    }

    private async Task LookupAsync()
    {
        var name = _follow.Text.Trim();
        if (name.Length == 0) { _followId = 0; return; }
        var id = await AccountApi.GetUserIdAsync(name);
        _followId = id ?? 0;
        _hint.Text = id is null ? T("User not found.") : $"{name} = {id}";
    }

    private async void Ok_Click(object s, RoutedEventArgs e)
    {
        if (_follow.Text.Trim().Length > 0 && _followId == 0) await LookupAsync();
        if (_follow.Text.Trim().Length > 0 && _followId == 0) return; // 找不到玩家：留在視窗讓使用者修正

        _acc.Alias = _alias.Text.Trim();
        _acc.Group = _group.Text.Trim();
        _acc.Description = _desc.Text.Trim();
        _acc.Email = _email.Text.Trim();
        _acc.EmailPassword = _emailPw.Text;
        _acc.Password = _password.Text;

        var keep = new Dictionary<string, string>();
        foreach (var line in _fields.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var i = line.IndexOf('=');
            if (i > 0) keep[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        _acc.Fields.Clear();
        foreach (var kv in keep) _acc.Fields[kv.Key] = kv.Value;
        AccountTargets.Set(_acc, _target.Text, _follow.Text.Trim().Length > 0 ? _followId : 0);
        if (_follow.Text.Trim().Length > 0) _acc.Fields["FollowUserName"] = _follow.Text.Trim();
        _acc.NotifyAll();
        DialogResult = true;
    }
}
