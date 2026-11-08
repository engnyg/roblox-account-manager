using System.Text;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Tests;

/// <summary>
/// 用 Place ID 啟動（RequestGame）時是 Roblox 自己挑伺服器，同一個帳號常被放回同一台。
/// 「每次加入不同的伺服器」開啟後，改由管理器從公開伺服器清單挑一台有空位、且不是這個帳號上次進過的。
/// </summary>
[Collection("http")]
public class ServerPickerTests : IDisposable
{
    public ServerPickerTests() => ServerPicker.Reset();
    public void Dispose() { ServerPicker.Reset(); RobloxApi.HandlerOverride = null; }

    private static ServerInfo S(string id, int playing, int max = 12) => new(id, playing, max, 50, 60);

    private static string ServersJson(params (string Id, int Playing, int Max)[] s) =>
        "{\"data\":[" + string.Join(",", s.Select(x => $"{{\"id\":\"{x.Id}\",\"playing\":{x.Playing},\"maxPlayers\":{x.Max},\"ping\":50,\"fps\":60}}")) + "],\"nextPageCursor\":null}";

    [Fact]
    public void Choose_skips_full_servers_and_the_one_used_last_time()
    {
        var servers = new[] { S("full", 12), S("last", 3), S("ok", 5) };
        for (int i = 0; i < 30; i++)
            Assert.Equal("ok", ServerPicker.Choose(servers, avoid: "last", new Random(i))?.Id);
    }

    [Fact]
    public void Choose_returns_null_when_nothing_else_has_room()
    {
        Assert.Null(ServerPicker.Choose([S("full", 12), S("last", 3)], "last", new Random(1)));
        Assert.Null(ServerPicker.Choose([], (string?)null, new Random(1)));
    }

    [Fact]
    public void Choose_spreads_over_all_eligible_servers()
    {
        var servers = Enumerable.Range(0, 5).Select(i => S("s" + i, 2)).ToArray();
        var seen = Enumerable.Range(0, 200).Select(i => ServerPicker.Choose(servers, (string?)null, new Random(i))!.Id).ToHashSet();
        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public async Task PickAsync_never_gives_the_same_account_the_same_server_twice_in_a_row()
    {
        var h = new Fake(_ => Fake.Json(ServersJson(("a", 3, 12), ("b", 4, 12))));
        var picks = new List<string?>();
        await Fake.With(h, async () =>
        {
            for (int i = 0; i < 6; i++) picks.Add(await ServerPicker.PickAsync("alice", 123));
            return 0;
        });
        Assert.All(picks, p => Assert.NotNull(p));
        for (int i = 1; i < picks.Count; i++) Assert.NotEqual(picks[i - 1], picks[i]);
    }

    [Fact]
    public async Task PickAsync_remembers_per_account_and_caches_the_list()
    {
        var h = new Fake(_ => Fake.Json(ServersJson(("a", 3, 12), ("b", 4, 12))));
        await Fake.With(h, async () =>
        {
            await ServerPicker.PickAsync("alice", 123);
            await ServerPicker.PickAsync("bob", 123);
            await ServerPicker.PickAsync("alice", 123);
            return 0;
        });
        Assert.Single(h.Urls);       // 批次啟動很多帳號時不要每個都打一次 Roblox API（會被限流）
        Assert.Contains("/v1/games/123/servers/Public", h.Urls[0]);
    }

    private static HttpResponseMessage RobloxFake(HttpRequestMessage r, List<string> gameJoinBodies, string servers)
    {
        var host = r.RequestUri!.Host;
        if (host == "games.roblox.com") return Fake.Json(servers);
        if (host == "gamejoin.roblox.com")
        {
            gameJoinBodies.Add(r.Content!.ReadAsStringAsync().Result);
            return Fake.Json("""{"status":2}""");
        }
        if (Fake.IsCsrfProbe(r))
        {
            if (!r.Headers.Contains("X-CSRF-TOKEN")) return Fake.Csrf403();
            var ok = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            ok.Headers.Add("rbx-authentication-ticket", "TICKET");
            return ok;
        }
        return Fake.Json("{}");
    }

    [Fact]
    public async Task Join_without_a_job_id_launches_into_a_server_we_picked_not_the_one_roblox_would_reuse()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ramv3_pick_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var captured = Path.Combine(dir, "uri.txt");
        var stub = Path.Combine(dir, "FakeRoblox.cmd");
        File.WriteAllText(stub, $"@echo off\r\n> \"{captured}\" echo %~1\r\nping -n 20 127.0.0.1 >nul\r\n");
        var data = Path.Combine(dir, "data");
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("RAM_V3_DATA", data);
        AppSettings.Reload();
        AppSettings.Current.DirectLaunch = true;
        RobloxLauncher.FindExeOverride = () => stub;

        var gameJoin = new List<string>();
        var servers = ServersJson(("only-good-one", 3, 12), ("full-one", 12, 12));
        var h = new Fake(r => RobloxFake(r, gameJoin, servers));
        int? pid = null;
        try
        {
            var acc = new RobloxAccountManager.Core.Models.Account { Username = "alice", SecurityToken = "cookie" };
            var res = await Fake.With(h, () => GameLauncher.JoinServerAsync(acc, 129604661913557, ""));
            pid = res.Pid;

            Assert.True(res.Ok, res.Message);
            Assert.Contains("已挑選不同的伺服器", res.Message);
            Assert.Single(gameJoin);
            Assert.Contains("only-good-one", gameJoin[0]);                   // 預檢的是我們挑的那台

            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && !File.Exists(captured)) await Task.Delay(100);
            var uri = Uri.UnescapeDataString(File.ReadAllText(captured));
            Assert.Contains("request=RequestGameJob", uri);
            Assert.Contains("gameId=only-good-one", uri);
        }
        finally
        {
            RobloxLauncher.FindExeOverride = null;
            if (pid is not null) try { System.Diagnostics.Process.GetProcessById(pid.Value).Kill(true); } catch { }
            Environment.SetEnvironmentVariable("RAM_V3_DATA", null);
            AppSettings.Reload();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task Join_with_an_explicit_job_id_never_picks_a_server()
    {
        var gameJoin = new List<string>();
        var h = new Fake(r => RobloxFake(r, gameJoin, ServersJson(("other", 1, 12))));
        RobloxLauncher.FindExeOverride = () => null;   // 不真的啟動；只看有沒有去抓伺服器清單
        try
        {
            var acc = new RobloxAccountManager.Core.Models.Account { Username = "alice", SecurityToken = "cookie" };
            await Fake.With(h, async () =>
            {
                try { await GameLauncher.JoinServerAsync(acc, 123, "11111111-2222-3333-4444-555555555555"); } catch { }
                return 0;
            });
        }
        finally { RobloxLauncher.FindExeOverride = null; }
        Assert.DoesNotContain(h.Urls, u => u.Contains("/servers/Public"));
    }

    [Fact]
    public async Task PickAsync_returns_null_when_the_server_list_cannot_be_fetched()
    {
        ServerPicker.RetryDelays = [TimeSpan.Zero];
        var h = new Fake(_ => Fake.Json("{}", System.Net.HttpStatusCode.TooManyRequests));
        var r = await Fake.With(h, () => ServerPicker.PickAsync("alice", 123));
        Assert.Null(r);                                           // 呼叫端會退回一般加入
    }

    [Fact]
    public async Task PickAsync_retries_after_being_rate_limited()
    {
        ServerPicker.RetryDelays = [TimeSpan.Zero, TimeSpan.Zero];
        int calls = 0;
        var h = new Fake(_ => ++calls <= 2
            ? Fake.Json("{}", System.Net.HttpStatusCode.TooManyRequests)
            : Fake.Json(ServersJson(("a", 3, 12))));
        var r = await Fake.With(h, () => ServerPicker.PickAsync("alice", 123));
        Assert.Equal("a", r);
    }

    private static ServerInfo[] Many(int n) => Enumerable.Range(0, n).Select(i => S("s" + i, 2, 16)).ToArray();
    private static string ManyJson(int n) => ServersJson(Enumerable.Range(0, n).Select(i => ("s" + i, 2, 16)).ToArray());

    [Fact]
    public async Task Accounts_launched_in_one_batch_all_share_one_server_and_the_next_batch_uses_another()
    {
        var h = new Fake(_ => Fake.Json(ManyJson(50)));
        var batch1 = new List<string?>();
        var batch2 = new List<string?>();
        await Fake.With(h, async () =>
        {
            using (ServerPicker.BeginBatch())
                foreach (var u in new[] { "a", "b", "c", "d" }) batch1.Add(await ServerPicker.PickAsync(u, 123));
            using (ServerPicker.BeginBatch())
                foreach (var u in new[] { "a", "b", "c", "d" }) batch2.Add(await ServerPicker.PickAsync(u, 123));
            return 0;
        });
        Assert.NotNull(batch1[0]);
        Assert.All(batch1, j => Assert.Equal(batch1[0], j));      // 同一批一起進同一台
        Assert.All(batch2, j => Assert.Equal(batch2[0], j));
        Assert.NotEqual(batch1[0], batch2[0]);                    // 下一批換另一台
    }

    [Fact]
    public async Task A_batch_moves_on_to_another_server_once_the_shared_one_is_full()
    {
        var h = new Fake(_ => Fake.Json(ServersJson(("x", 14, 16), ("y", 14, 16))));   // 兩台都只剩 2 個位置
        var picks = new List<string?>();
        await Fake.With(h, async () =>
        {
            using (ServerPicker.BeginBatch())
                foreach (var u in new[] { "a", "b", "c", "d" }) picks.Add(await ServerPicker.PickAsync(u, 123));
            return 0;
        });
        Assert.All(picks, Assert.NotNull);
        Assert.Equal(picks[0], picks[1]);                         // 還有位置就一起
        Assert.NotEqual(picks[0], picks[2]);                      // 滿了才換
        Assert.Equal(picks[2], picks[3]);
    }

    [Fact]
    public async Task Accounts_picking_at_the_same_moment_fetch_the_server_list_only_once()
    {
        var h = new Fake(_ => Fake.Json(ManyJson(30)));
        string?[] r = [];
        await Fake.With(h, async () =>
        {
            using (ServerPicker.BeginBatch())
                r = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => ServerPicker.PickAsync("u" + i, 123)));
            return 0;
        });
        Assert.Single(h.Urls);                                    // 不會每個帳號都打一次而被限流
        Assert.All(r, x => Assert.NotNull(x));
    }

    [Fact]
    public async Task A_batch_prefers_a_server_that_has_room_for_the_whole_batch()
    {
        // tight 只剩 2 個位置，roomy 容得下 4 個帳號：整批應該都進 roomy
        var h = new Fake(_ => Fake.Json(ServersJson(("tight", 14, 16), ("roomy", 2, 16))));
        for (int round = 0; round < 10; round++)
        {
            ServerPicker.Reset();
            var picks = new List<string?>();
            await Fake.With(h, async () =>
            {
                using (ServerPicker.BeginBatch(4))
                    foreach (var u in new[] { "a", "b", "c", "d" }) picks.Add(await ServerPicker.PickAsync(u, 123));
                return 0;
            });
            Assert.All(picks, p => Assert.Equal("roomy", p));
        }
    }

    [Fact]
    public void Choose_avoids_every_listed_server()
    {
        for (int i = 0; i < 40; i++)
            Assert.Equal("s2", ServerPicker.Choose(Many(3), new[] { "s0", "s1" }, new Random(i))?.Id);
    }
}
