using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace RobloxAccountManager.Core.Services;

public sealed class MiniRequest
{
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = "/";
    public NameValueCollection Query { get; init; } = new();
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; init; } = "";
    public IPEndPoint? Remote { get; init; }

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;
}

public sealed record MiniResponse(int Status, string Body, string ContentType = "text/plain")
{
    public static MiniResponse Json(string json, int status = 200) => new(status, json, "application/json");
    public static MiniResponse Text(string text, int status = 200) => new(status, text);
}

/// <summary>
/// 極簡 HTTP/1.1 伺服器（TcpListener）。用它而不是 HttpListener，是因為 HttpListener 要接區網連線
/// （http://+:port/）必須用系統管理員身分或先 netsh 註冊 URL；TcpListener 不需要。
/// 只支援：Content-Length 的請求本文（不支援 chunked）、每個連線一個請求（Connection: close）。
/// </summary>
public sealed class MiniHttpServer : IDisposable
{
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly List<TcpListener> _listeners = new();
    private readonly Func<MiniRequest, Task<MiniResponse>> _handler;
    private CancellationTokenSource? _cts;

    /// <param name="lan">true = 所有網卡（IPv4 + IPv6）；false = 只有本機迴路位址。</param>
    public MiniHttpServer(int port, bool lan, Func<MiniRequest, Task<MiniResponse>> handler)
    {
        Port = port; Lan = lan; _handler = handler;
    }

    public int Port { get; }
    public bool Lan { get; }
    public bool IsRunning => _listeners.Count > 0;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        try
        {
            if (Lan)
            {
                var l = new TcpListener(IPAddress.IPv6Any, Port);
                l.Server.DualMode = true;            // 同時接 IPv4 與 IPv6
                l.Start();
                _listeners.Add(l);
            }
            else
            {
                var v4 = new TcpListener(IPAddress.Loopback, Port);
                v4.Start();
                _listeners.Add(v4);
                // 瀏覽器開 localhost 常先試 ::1；IPv6 不可用時就只用 IPv4
                try { var v6 = new TcpListener(IPAddress.IPv6Loopback, Port); v6.Start(); _listeners.Add(v6); }
                catch (SocketException) { }
            }
        }
        catch
        {
            Stop();
            throw;
        }
        foreach (var l in _listeners) _ = AcceptLoopAsync(l, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        foreach (var l in _listeners) { try { l.Stop(); } catch { } }
        _listeners.Clear();
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ReadTimeout);
                var stream = client.GetStream();
                var (req, error) = await ReadRequestAsync(stream, client.Client.RemoteEndPoint as IPEndPoint, timeout.Token);
                MiniResponse resp;
                if (req is null) resp = MiniResponse.Text(error ?? "Bad Request", 400);
                else
                {
                    try { resp = await _handler(req); }
                    catch (Exception e) { resp = MiniResponse.Text("Internal error: " + e.Message, 500); }
                }
                await WriteResponseAsync(stream, resp, ct);
            }
            catch { /* 連線中斷 / 逾時 */ }
        }
    }

    internal static async Task<(MiniRequest? Request, string? Error)> ReadRequestAsync(Stream stream, IPEndPoint? remote, CancellationToken ct)
    {
        // 讀到標頭結束（\r\n\r\n）
        var buffer = new byte[8192];
        var head = new MemoryStream();
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int n = await stream.ReadAsync(buffer, ct);
            if (n == 0) return (null, "Connection closed");
            head.Write(buffer, 0, n);
            if (head.Length > MaxHeaderBytes + MaxBodyBytes) return (null, "Request too large");
            headerEnd = IndexOf(head.GetBuffer(), (int)head.Length, "\r\n\r\n"u8);
            if (headerEnd < 0 && head.Length > MaxHeaderBytes) return (null, "Headers too large");
        }

        var all = head.GetBuffer();
        var headerText = Encoding.UTF8.GetString(all, 0, headerEnd);   // 標頭通常是 ASCII；用 UTF-8 解才不會弄壞非英文的值
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 3 || !requestLine[2].StartsWith("HTTP/1.")) return (null, "Bad request line");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        if (headers.TryGetValue("Transfer-Encoding", out var te) && te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            return (null, "Chunked request bodies are not supported");

        int length = 0;
        if (headers.TryGetValue("Content-Length", out var cl) && (!int.TryParse(cl, out length) || length < 0 || length > MaxBodyBytes))
            return (null, "Bad Content-Length");

        // 本文：標頭後已讀到的部分 + 不足的再讀
        int bodyStart = headerEnd + 4;
        var body = new byte[length];
        int have = Math.Min(length, (int)head.Length - bodyStart);
        Array.Copy(all, bodyStart, body, 0, have);
        while (have < length)
        {
            int n = await stream.ReadAsync(body.AsMemory(have, length - have), ct);
            if (n == 0) return (null, "Body truncated");
            have += n;
        }

        var target = requestLine[1];
        int q = target.IndexOf('?');
        var path = Uri.UnescapeDataString(q >= 0 ? target[..q] : target);
        var query = HttpUtility.ParseQueryString(q >= 0 ? target[(q + 1)..] : "");
        return (new MiniRequest
        {
            Method = requestLine[0].ToUpperInvariant(),
            Path = path,
            Query = query,
            Headers = headers,
            Body = Encoding.UTF8.GetString(body),
            Remote = remote,
        }, null);
    }

    private static async Task WriteResponseAsync(Stream stream, MiniResponse resp, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(resp.Body);
        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append(resp.Status).Append(' ').Append(Reason(resp.Status)).Append("\r\n")
            .Append("Content-Type: ").Append(resp.ContentType).Append("; charset=utf-8\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK", 202 => "Accepted", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden",
        404 => "Not Found", 409 => "Conflict", 500 => "Internal Server Error", 503 => "Service Unavailable", _ => "Status",
    };

    private static int IndexOf(byte[] data, int length, ReadOnlySpan<byte> pattern)
    {
        var span = new ReadOnlySpan<byte>(data, 0, length);
        return span.IndexOf(pattern);
    }
}
