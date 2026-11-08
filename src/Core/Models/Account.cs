using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace RobloxAccountManager.Core.Models;

/// <summary>
/// 一個 Roblox 帳號。JSON 欄位名稱與 V2（Python 版）的 accounts.dat.json 相同，可直接匯入。
/// </summary>
public sealed class Account : INotifyPropertyChanged
{
    private string _alias = "";
    private string _group = "Default";
    private string _description = "";
    private bool _checked;
    private bool _valid;

    [JsonPropertyName("security_token")] public string SecurityToken { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("user_id")] public long UserId { get; set; }

    [JsonPropertyName("alias")]
    public string Alias { get => _alias; set { Set(ref _alias, value); Raise(nameof(Subtitle)); Raise(nameof(DisplayName)); } }

    [JsonPropertyName("description")]
    public string Description { get => _description; set { Set(ref _description, value); Raise(nameof(Subtitle)); } }

    [JsonPropertyName("password")] public string Password { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("email_password")] public string EmailPassword { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get => _group; set => Set(ref _group, string.IsNullOrWhiteSpace(value) ? "Default" : value); }

    [JsonPropertyName("fields")] public Dictionary<string, string> Fields { get; set; } = new();
    [JsonPropertyName("last_use")] public DateTime? LastUse { get; set; }
    [JsonPropertyName("last_attempted_refresh")] public DateTime? LastAttemptedRefresh { get; set; }
    [JsonPropertyName("browser_tracker_id")] public string BrowserTrackerId { get; set; } = "";

    // ---- 執行期（不寫入檔案）----
    [JsonIgnore] public string CsrfToken { get; set; } = "";

    /// <summary>是否勾選為「要一起啟動」。由 UI 與 Settings.CheckedUsernames 同步。</summary>
    [JsonIgnore]
    public bool Checked { get => _checked; set => Set(ref _checked, value); }

    [JsonIgnore]
    public bool Valid
    {
        get => _valid || (!string.IsNullOrEmpty(Username) && UserId > 0);
        set { Set(ref _valid, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidMark))); }
    }

    [JsonIgnore] public string ValidMark => Valid ? "✔" : "✘";

    private string _avatarUrl = "";
    /// <summary>大頭貼圖片網址（由 AvatarService 取得，不寫入檔案）。</summary>
    [JsonIgnore]
    public string AvatarUrl { get => _avatarUrl; set => Set(ref _avatarUrl, value ?? ""); }

    private int _pid;
    /// <summary>此帳號目前執行中的 Roblox 行程（0 = 沒有）。由主視窗定時比對 browserTrackerId 更新。</summary>
    [JsonIgnore]
    public int Pid
    {
        get => _pid;
        set
        {
            if (_pid == value) return;
            _pid = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pid)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Running)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMark)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PidText)));
        }
    }

    [JsonIgnore] public bool Running => _pid != 0;
    [JsonIgnore] public string StatusMark => Running ? "●" : "○";
    [JsonIgnore] public string PidText => Running ? $"PID {_pid}" : "—";

    /// <summary>清單第二行：別名與描述（沒有就空白）。</summary>
    [JsonIgnore] public string Subtitle => string.Join("  ·  ", new[] { Alias, Description }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>此帳號設定的加入目標（遊戲 ID / 連結…），純顯示用。</summary>
    [JsonIgnore] public string TargetText => Fields.TryGetValue("Target", out var t) ? t : "";

    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Username : $"{Username} ({Alias})";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public override string ToString() => $"Account({Username}, group={Group}, valid={Valid})";
}
