namespace RobloxAccountManager.Core.Services;

public sealed record LogEntry(DateTime Time, string Level, string Message)
{
    public override string ToString() => $"{Time:HH:mm:ss} [{Level}] {Message}";
}

/// <summary>簡單的記憶體內日誌（環狀緩衝），供日誌視窗即時顯示與搜尋。</summary>
public static class AppLog
{
    private const int Capacity = 5000;
    private static readonly LinkedList<LogEntry> Buffer = new();
    private static readonly object Gate = new();

    /// <summary>新增一筆日誌時觸發（可能在任何執行緒）。</summary>
    public static event Action<LogEntry>? Added;

    public static void Info(string message) => Add("INFO", message);
    public static void Warn(string message) => Add("WARN", message);
    public static void Error(string message) => Add("ERROR", message);

    public static void Add(string level, string message)
    {
        var e = new LogEntry(DateTime.Now, level, message);
        lock (Gate)
        {
            Buffer.AddLast(e);
            while (Buffer.Count > Capacity) Buffer.RemoveFirst();
        }
        Added?.Invoke(e);
    }

    public static List<LogEntry> Snapshot(string? filter = null)
    {
        lock (Gate)
        {
            IEnumerable<LogEntry> q = Buffer;
            if (!string.IsNullOrWhiteSpace(filter))
                q = q.Where(e => e.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase));
            return q.ToList();
        }
    }

    public static void Clear() { lock (Gate) Buffer.Clear(); }
}
