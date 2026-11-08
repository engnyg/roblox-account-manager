namespace RobloxAccountManager.Core.Services;

/// <summary>
/// 用 Place ID 啟動（沒有指定 Job ID）時，Roblox 自己挑伺服器，常把同一個帳號放回同一台。
/// 這裡改由管理器從公開伺服器清單挑一台有空位、而且和「上一次」不同的：
///  · 單獨啟動：每次都換一台。
///  · 批次啟動（勾選 / 群組一次開多個）：同一批的帳號一起進同一台，下一批再換另一台。
/// 清單抓不到（被限流、遊戲沒有公開伺服器…）就回傳 null，呼叫端退回一般加入。
/// </summary>
public static class ServerPicker
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> LastByUser = new(StringComparer.OrdinalIgnoreCase);  // 用戶名 → 上次挑的 Job ID
    private static readonly Dictionary<long, string> LastByPlace = new();                                    // Place → 上次挑的 Job ID
    private static readonly Dictionary<long, (DateTime At, List<ServerInfo> Servers)> Cache = new();
    private static readonly Dictionary<long, Task<List<ServerInfo>>> InFlight = new();                       // 同一個遊戲同時只抓一次
    private static readonly AsyncLocal<Dictionary<long, string>?> Batch = new();                             // 目前這一批已經選好的伺服器
    private static readonly Random Rng = new();
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    /// <summary>抓清單失敗（被限流 / 暫時連不上）時的退避等待；測試可縮短。</summary>
    internal static TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3)];

    /// <summary>測試用：清掉記憶與快取。</summary>
    public static void Reset()
    {
        lock (Gate) { LastByUser.Clear(); LastByPlace.Clear(); Cache.Clear(); InFlight.Clear(); }
        Batch.Value = null;
        RetryDelays = [TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3)];
    }

    private sealed class BatchScope : IDisposable
    {
        public void Dispose() { Batch.Value = null; BatchSize.Value = 1; }
    }

    private static readonly AsyncLocal<int> BatchSize = new();

    /// <summary>
    /// 開始一批：之後在同一個非同步流程裡啟動的帳號會共用同一台伺服器；Dispose 結束這一批。
    /// size 是這批的帳號數，挑伺服器時會優先挑容得下整批的。
    /// </summary>
    public static IDisposable BeginBatch(int size = 1)
    {
        Batch.Value = new Dictionary<long, string>();
        BatchSize.Value = Math.Max(1, size);
        return new BatchScope();
    }

    /// <summary>從 servers 隨機挑一台有空位的，排除 avoid；沒有符合的回傳 null。</summary>
    public static ServerInfo? Choose(IEnumerable<ServerInfo> servers, string? avoid, Random rng) =>
        Choose(servers, avoid is null ? null : [avoid], rng);

    public static ServerInfo? Choose(IEnumerable<ServerInfo> servers, ICollection<string>? avoid, Random rng)
    {
        var ok = servers.Where(s => s.Playing < s.MaxPlayers
                                    && (avoid is null || !avoid.Contains(s.Id, StringComparer.OrdinalIgnoreCase))).ToList();
        return ok.Count == 0 ? null : ok[rng.Next(ok.Count)];
    }

    /// <summary>取得伺服器清單：優先用快取；同時有很多帳號啟動時只打一次 API，失敗會退避重試。</summary>
    private static async Task<List<ServerInfo>> GetListAsync(long placeId, CancellationToken ct)
    {
        Task<List<ServerInfo>> task;
        lock (Gate)
        {
            if (Cache.TryGetValue(placeId, out var c) && DateTime.UtcNow - c.At < CacheFor) return c.Servers;
            if (!InFlight.TryGetValue(placeId, out task!))
                InFlight[placeId] = task = FetchAsync(placeId);
        }
        return await task.WaitAsync(ct);
    }

    private static async Task<List<ServerInfo>> FetchAsync(long placeId)
    {
        try
        {
            var delays = RetryDelays;
            for (int attempt = 0; ; attempt++)
            {
                // 兩頁（最多 200 台）就夠分散
                var fetched = await GameDiscovery.FetchServersAsync(placeId, maxPages: 2);
                if (fetched.Count > 0)
                {
                    lock (Gate) Cache[placeId] = (DateTime.UtcNow, fetched);
                    return fetched;
                }
                if (attempt >= delays.Length) return [];
                await Task.Delay(delays[attempt]);
            }
        }
        finally
        {
            lock (Gate) InFlight.Remove(placeId);
        }
    }

    /// <summary>
    /// 為這個帳號挑伺服器，回傳 Job ID；挑不到回傳 null。
    /// 在 <see cref="BeginBatch"/> 之內，同一批的帳號共用同一台（滿了才換）；否則挑一台和上次不同的。
    /// </summary>
    public static async Task<string?> PickAsync(string username, long placeId, CancellationToken ct = default)
    {
        var list = await GetListAsync(placeId, ct);
        if (list.Count == 0) return null;

        lock (Gate)
        {
            var batch = Batch.Value;
            ServerInfo? pick = null;

            // 同一批已經選好的那台，還有位置就沿用
            if (batch is not null && batch.TryGetValue(placeId, out var shared))
            {
                var s = list.FirstOrDefault(x => string.Equals(x.Id, shared, StringComparison.OrdinalIgnoreCase));
                if (s is not null && s.Playing < s.MaxPlayers) pick = s;
            }

            if (pick is null)
            {
                var avoid = new List<string>(2);
                if (LastByUser.TryGetValue(username, out var u)) avoid.Add(u);
                if (LastByPlace.TryGetValue(placeId, out var p)) avoid.Add(p);
                var need = batch is null ? 1 : BatchSize.Value;
                // 先找容得下整批的；沒有就退而求其次，再不行才放寬「和上次不同」的限制
                var roomy = need > 1 ? list.Where(x => x.Playing + need <= x.MaxPlayers).ToList() : null;
                pick = (roomy is { Count: > 0 } ? Choose(roomy, avoid, Rng) : null)
                       ?? Choose(list, avoid, Rng)
                       ?? Choose(list, (ICollection<string>?)null, Rng);
                if (pick is null) return null;
                LastByPlace[placeId] = pick.Id;
                if (batch is not null) batch[placeId] = pick.Id;
            }

            LastByUser[username] = pick.Id;
            // 人數先記上這個帳號，避免同一批的人都擠進最後一個空位
            var i = list.FindIndex(x => x.Id == pick.Id);
            if (i >= 0) list[i] = pick with { Playing = pick.Playing + 1 };
            return pick.Id;
        }
    }
}
