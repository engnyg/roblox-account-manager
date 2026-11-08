using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;

namespace RobloxAccountManager.Core.Services;

/// <summary>網頁控制面板看到的一個帳號（不含 cookie / 密碼）。</summary>
public sealed record WebAccount(
    string Username, string Alias, string Group, long UserId, string AvatarUrl,
    bool Checked, int Pid, string Target, bool Valid, string Subtitle);

/// <summary>網頁控制面板每次輪詢拿到的整體狀態。</summary>
public sealed record WebState(
    List<WebAccount> Accounts, int Running, string PlaceId, string JobId, bool Launching,
    List<LogEntry> Log, Dictionary<string, string> Theme, string Version);

/// <summary>主程式提供給網頁控制面板的操作（實作需自行切回 UI 執行緒）。</summary>
public interface IWebControl
{
    Task<WebState> GetStateAsync();
    /// <summary>啟動帳號。usernames 空 = 已勾選的帳號；place/job 空 = 用各帳號的目標或主視窗的 Place / Job。</summary>
    Task<(bool Ok, string Message)> LaunchAsync(IReadOnlyList<string> usernames, string place, string job);
    Task<int> KillAsync(IReadOnlyList<string> usernames);
    Task SetCheckedAsync(IReadOnlyList<string> usernames, bool value);
}

/// <summary>
/// 內建 Web API 與網頁控制面板（預設 port 7963）。
///  - GET  /                      網頁控制面板（手機 / 電腦瀏覽器都能用）
///  - GET  /api/state             帳號、執行狀態、目的地、記錄、主題顏色
///  - GET  /api/place?id=         遊戲名稱 / 人數 / 圖示（目的地卡片）
///  - POST /api/launch {usernames, placeId, jobId}  · /api/kill {usernames} · /api/check {usernames, checked}
///  - GET  /api/mixer             混音台：執行中實例（記憶體 / CPU / 音量 / 優先權 / 限制）與目前設定
///  - POST /api/mixer {action, pids, ...}  volume / mute / trim / priority / limits / kill / fps / arrange / graphics
///  - 舊版相容：GET /GetCSRFToken?username= 、GET /GetAccounts 、POST /JoinServer 、GET /GetCurrentAccount
/// 密碼放在 header「password」或 query「password」。/api 的 POST 另需 header「X-RAM-Panel: 1」，
/// 讓其他網站無法用表單偷偷對本機送出操作（跨站請求）。
/// 區網模式（lan）只有在設定了密碼時才會真的對區網開放，否則只聽本機。
/// </summary>
public sealed class WebApiServer : IDisposable
{
    public const string PanelHeader = "X-RAM-Panel";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Lazy<string> PanelHtml = new(LoadPanel);

    private readonly int _port;
    private readonly string _password;
    private readonly Func<IReadOnlyList<Account>> _getAccounts;
    private readonly Func<Account?> _getSelected;
    private MiniHttpServer? _http;

    /// <summary>加入伺服器的實作（預設走 <see cref="GameLauncher.JoinServerAsync"/>，測試可替換）。</summary>
    public Func<Account, long, string, Task<JoinResult>> JoinHandler { get; set; } =
        (acc, place, job) => GameLauncher.JoinServerAsync(acc, place, job);

    /// <summary>取得 CSRF 的實作（預設走 <see cref="RobloxApi.GetCsrfTokenAsync"/>，測試可替換）。</summary>
    public Func<Account, Task<(bool Ok, string Value)>> CsrfHandler { get; set; } =
        acc => RobloxApi.GetCsrfTokenAsync(acc);

    /// <summary>查遊戲資訊的實作（測試可替換）。</summary>
    public Func<long, Task<PlaceInfo?>> PlaceInfoHandler { get; set; } = id => GameDiscovery.GetPlaceInfoAsync(id);

    /// <summary>網頁控制面板的操作；null = 只有舊版 API，面板會顯示「無法控制」。</summary>
    public IWebControl? Control { get; set; }

    /// <summary>網頁面板的混音台（實例管理）；null = 面板不顯示混音台功能。</summary>
    public IWebMixer? Mixer { get; set; }

    public WebApiServer(int port, string password, Func<IReadOnlyList<Account>> getAccounts, Func<Account?> getSelected, bool lan = false)
    {
        _port = port; _password = password ?? ""; _getAccounts = getAccounts; _getSelected = getSelected;
        LanRequested = lan;
    }

    public int Port => _port;
    public bool LanRequested { get; }
    /// <summary>實際是否對區網開放（區網模式且有密碼）。</summary>
    public bool Lan => LanRequested && _password.Length > 0;
    public bool IsRunning => _http?.IsRunning == true;

    public void Start()
    {
        if (IsRunning) return;
        _http = new MiniHttpServer(_port, Lan, HandleAsync);
        _http.Start();
    }

    public void Stop()
    {
        _http?.Stop();
        _http = null;
    }

    public void Dispose() => Stop();

    private static string LoadPanel()
    {
        using var s = typeof(WebApiServer).Assembly.GetManifestResourceStream("RAM.panel.html");
        if (s is null) return "<!doctype html><title>Panel missing</title><p>panel.html was not embedded.</p>";
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private bool Authorized(MiniRequest req)
    {
        if (_password.Length == 0) return true;
        var given = req.Header("password") ?? req.Query["password"] ?? "";
        // 瀏覽器的 HTTP 標頭只能放英文字元，網頁會把密碼做 URL 編碼（中文密碼才送得出去），這裡兩種都接受
        string decoded;
        try { decoded = Uri.UnescapeDataString(given); } catch { decoded = given; }
        return Same(given) | Same(decoded);
        bool Same(string s) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(s), Encoding.UTF8.GetBytes(_password));
    }

    private Account? Find(string? username)
    {
        if (string.IsNullOrEmpty(username)) return _getSelected();
        return _getAccounts().FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Names(JsonNode? body) =>
        (body?["usernames"] as JsonArray)?.Select(n => n?.ToString() ?? "").Where(s => s.Length > 0).ToList() ?? [];

    private static MiniResponse JsonOk(object value) => MiniResponse.Json(JsonSerializer.Serialize(value, Json));
    private static MiniResponse JsonError(string message, int status) =>
        MiniResponse.Json(new JsonObject { ["error"] = message }.ToJsonString(), status);

    internal async Task<MiniResponse> HandleAsync(MiniRequest req)
    {
        var path = req.Path.TrimEnd('/').ToLowerInvariant();
        var method = req.Method;

        // 面板本身不含任何資料，不用密碼也能載入；資料都走 /api（需要密碼）
        if (path is "" or "/index.html" && method == "GET")
            return new MiniResponse(200, PanelHtml.Value, "text/html");

        if (!Authorized(req))
            return path.StartsWith("/api/") ? JsonError("需要密碼", 401) : MiniResponse.Text("Unauthorized", 401);

        if (path.StartsWith("/api/"))
            return await HandleApiAsync(path, method, req);

        switch (path)
        {
            case "/getcsrftoken" when method == "GET":
            {
                var acc = Find(req.Query["username"]);
                if (acc is null) return MiniResponse.Text("Account not found", 404);
                var (ok, value) = await CsrfHandler(acc);
                return MiniResponse.Text(value, ok ? 200 : 403);
            }
            case "/getaccounts" when method == "GET":
            {
                var arr = new JsonArray();
                foreach (var a in _getAccounts())
                    arr.Add(new JsonObject { ["username"] = a.Username, ["group"] = a.Group, ["valid"] = a.Valid });
                return MiniResponse.Json(arr.ToJsonString());
            }
            case "/joinserver" when method == "POST":
            {
                JsonNode? body;
                try { body = JsonNode.Parse(req.Body); }
                catch { return MiniResponse.Text("Invalid JSON", 400); }
                var acc = Find(body?["username"]?.ToString());
                if (acc is null) return MiniResponse.Text("Account not found", 404);
                long.TryParse(body?["placeId"]?.ToString(), out var place);
                var job = body?["jobId"]?.ToString() ?? "";
                var r = await JoinHandler(acc, place, job);
                return MiniResponse.Text(r.Ok ? "Success" : r.Message, r.Ok ? 200 : 500);
            }
            case "/getcurrentaccount" when method == "GET":
            {
                var acc = _getSelected();
                if (acc is null) return MiniResponse.Text("No account selected", 404);
                return MiniResponse.Json(new JsonObject { ["username"] = acc.Username, ["userId"] = acc.UserId }.ToJsonString());
            }
            default:
                return MiniResponse.Text("Not found", 404);
        }
    }

    private async Task<MiniResponse> HandleApiAsync(string path, string method, MiniRequest req)
    {
        if (method == "POST" && req.Header(PanelHeader) != "1")
            return JsonError("缺少 " + PanelHeader + " 標頭", 403);

        if (path == "/api/place" && method == "GET")
        {
            if (!long.TryParse(req.Query["id"], out var id) || id <= 0) return JsonError("Place ID 不正確", 400);
            var info = await PlaceInfoHandler(id);
            return info is null ? JsonError("找不到這個遊戲", 404) : JsonOk(info);
        }

        if (path.StartsWith("/api/mixer"))
            return await HandleMixerAsync(method, req);

        if (Control is not { } c) return JsonError("主程式沒有提供控制功能", 503);

        JsonNode? body = null;
        if (method == "POST")
        {
            try { body = JsonNode.Parse(req.Body.Length == 0 ? "{}" : req.Body); }
            catch { return JsonError("JSON 格式錯誤", 400); }
        }

        switch (path)
        {
            case "/api/state" when method == "GET":
                return JsonOk(await c.GetStateAsync());
            case "/api/launch" when method == "POST":
            {
                var (ok, message) = await c.LaunchAsync(Names(body), body?["placeId"]?.ToString()?.Trim() ?? "", body?["jobId"]?.ToString()?.Trim() ?? "");
                return ok ? JsonOk(new { message }) : JsonError(message, 409);
            }
            case "/api/kill" when method == "POST":
                return JsonOk(new { killed = await c.KillAsync(Names(body)) });
            case "/api/check" when method == "POST":
                await c.SetCheckedAsync(Names(body), body?["checked"]?.GetValue<bool>() ?? true);
                return JsonOk(new { ok = true });
            default:
                return JsonError("Not found", 404);
        }
    }

    private async Task<MiniResponse> HandleMixerAsync(string method, MiniRequest req)
    {
        if (Mixer is not { } m) return JsonError("主程式沒有提供混音台功能", 503);
        if (method == "GET")
        {
            var (instances, settings) = await m.SnapshotAsync();
            return JsonOk(new { instances, settings });
        }
        if (method != "POST") return JsonError("Not found", 404);

        JsonNode? body;
        try { body = JsonNode.Parse(req.Body.Length == 0 ? "{}" : req.Body); }
        catch { return JsonError("JSON 格式錯誤", 400); }
        var action = body?["action"]?.ToString() ?? "";
        var pids = (body?["pids"] as JsonArray)?.Select(n => int.TryParse(n?.ToString(), out var v) ? v : 0).Where(v => v > 0).ToList() ?? [];
        try { return JsonOk(new { message = await m.ApplyAsync(action, pids, body) }); }
        catch (ArgumentException e) { return JsonError(e.Message, 400); }
    }
}
