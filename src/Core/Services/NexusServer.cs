using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;

namespace RobloxAccountManager.Core.Services;

public static class NexusCommand
{
    // 從 Lua 腳本接收
    public const string Ping = "Ping";
    public const string Connect = "Connect";
    public const string Disconnect = "Disconnect";
    public const string Status = "Status";

    // 發送給 Lua 腳本
    public const string ExecuteScript = "ExecuteScript";
    public const string Kick = "Kick";
    public const string Chat = "Chat";
    public const string Rejoin = "Rejoin";
    public const string JoinServer = "JoinServer";

    public static string Make(string command, object? args = null)
    {
        var obj = new JsonObject { ["command"] = command };
        if (args is not null && JsonSerializer.SerializeToNode(args) is JsonObject extra)
            foreach (var kv in extra) obj[kv.Key] = kv.Value?.DeepClone();
        return obj.ToJsonString();
    }

    public static (string Command, JsonObject Data) Parse(string raw)
    {
        try
        {
            if (JsonNode.Parse(raw) is JsonObject o) return (o["command"]?.ToString() ?? "", o);
        }
        catch { /* 不是 JSON 就當純文字指令 */ }
        return (raw.Trim(), new JsonObject());
    }
}

/// <summary>以 Nexus 連進來的受控帳號（Lua 腳本端）。</summary>
public sealed class ControlledAccount
{
    public string Username { get; init; } = "";
    public long UserId { get; init; }
    public string JobId { get; set; } = "UNKNOWN";
    public DateTime? ConnectedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    internal WebSocket? Socket { get; set; }
    public bool IsConnected => Socket is { State: WebSocketState.Open };
}

/// <summary>
/// Nexus WebSocket 伺服器（預設 port 7964）。Lua 腳本以 ws://localhost:7964/?name=名稱&amp;id=UserId&amp;jobId=Job ID 連入
/// （設了密碼則要加 &amp;password=…）；管理器可對單一帳號或全部帳號送出指令。
/// </summary>
public sealed class NexusServer : IDisposable
{
    private readonly int _port;
    private readonly string _password;
    private readonly ConcurrentDictionary<string, ControlledAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public event Action<ControlledAccount>? AccountConnected;
    public event Action<ControlledAccount>? AccountDisconnected;
    public event Action<ControlledAccount, string, JsonObject>? MessageReceived;

    public NexusServer(int port = 7964, string password = "") { _port = port; _password = password; }

    public bool IsRunning => _listener?.IsListening == true;
    public int Port => _port;
    public IReadOnlyList<ControlledAccount> Accounts => _accounts.Values.OrderBy(a => a.Username).ToList();

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        _ = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
        foreach (var a in _accounts.Values)
            try { a.Socket?.Abort(); } catch { }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(ctx, ct), ct);
        }
    }

    private bool PasswordOk(string? given) =>
        _password.Length == 0 ||
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given ?? ""), Encoding.UTF8.GetBytes(_password));

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (!ctx.Request.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        var q = HttpUtility.ParseQueryString(ctx.Request.Url?.Query ?? "");
        var name = (q["name"] ?? "").Trim();
        var idText = (q["id"] ?? "").Trim();
        var job = (q["jobId"] ?? "UNKNOWN").Trim();

        if (!PasswordOk(q["password"])) { ctx.Response.StatusCode = 401; ctx.Response.Close(); return; }
        if (name.Length == 0 || !long.TryParse(idText, out var userId)) { ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }

        WebSocket ws;
        try { ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket; }
        catch { return; }

        var acc = _accounts.GetOrAdd(name, _ => new ControlledAccount { Username = name, UserId = userId });
        acc.Socket = ws;
        acc.JobId = job;
        acc.ConnectedAt = DateTime.Now;
        acc.DisconnectedAt = null;
        AccountConnected?.Invoke(acc);

        var buf = new byte[16 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buf, ct);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        // 回應 close frame，讓對方能正常完成關閉握手
                        try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
                        goto done;
                    }
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);

                var raw = Encoding.UTF8.GetString(ms.ToArray());
                var (cmd, data) = NexusCommand.Parse(raw);
                if (cmd == NexusCommand.Ping) await SendRawAsync(acc, NexusCommand.Make("Pong"), ct);
                MessageReceived?.Invoke(acc, cmd, data);
            }
        }
        catch { /* 連線中斷 */ }
        done:
        acc.Socket = null;
        acc.DisconnectedAt = DateTime.Now;
        try { ws.Abort(); ws.Dispose(); } catch { }
        AccountDisconnected?.Invoke(acc);
    }

    private static async Task SendRawAsync(ControlledAccount acc, string message, CancellationToken ct)
    {
        var ws = acc.Socket;
        if (ws is not { State: WebSocketState.Open }) return;
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct); }
        catch { acc.Socket = null; }
    }

    public async Task<bool> SendAsync(string username, string message, CancellationToken ct = default)
    {
        if (!_accounts.TryGetValue(username, out var acc) || !acc.IsConnected) return false;
        await SendRawAsync(acc, message, ct);
        return true;
    }

    public async Task<int> BroadcastAsync(string message, CancellationToken ct = default)
    {
        int n = 0;
        foreach (var acc in _accounts.Values.Where(a => a.IsConnected))
        {
            await SendRawAsync(acc, message, ct);
            n++;
        }
        return n;
    }
}
