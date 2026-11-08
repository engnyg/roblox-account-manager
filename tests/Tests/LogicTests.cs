using System.Net;
using System.Text;
using System.Text.Json;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Tests;

public class JoinTargetTests
{
    private const string Job = "10a94f61-b29b-40b4-8fa2-bd2640bd1e52";

    [Theory]
    [InlineData(Job, null, Job)]
    [InlineData("  " + Job + "  ", null, Job)]
    [InlineData("10A94F61-B29B-40B4-8FA2-BD2640BD1E52", null, Job)]
    [InlineData("roblox://experiences/start?placeId=123&gameInstanceId=" + Job, 123L, Job)]
    [InlineData("https://www.roblox.com/games/start?placeId=456&gameInstanceId=" + Job, 456L, Job)]
    [InlineData("https://www.roblox.com/games/789/Some-Name?gameInstanceId=" + Job, 789L, Job)]
    [InlineData("not-a-uuid", null, "not-a-uuid")]
    [InlineData("", null, "")]
    public void Parse_extracts_place_and_job(string input, long? place, string job)
    {
        var (p, j) = JoinTarget.Parse(input);
        Assert.Equal(place, p);
        Assert.Equal(job, j);
    }

    [Fact]
    public async Task Normalize_prefers_bot_place_for_job()
    {
        JoinTarget.BotLookupOverride = (_, _) => Task.FromResult<long?>(999);
        try
        {
            var (p, j) = await JoinTarget.NormalizeAsync(111, Job);
            Assert.Equal(999, p);
            Assert.Equal(Job, j);
        }
        finally { JoinTarget.BotLookupOverride = null; }
    }

    [Fact]
    public async Task Normalize_keeps_place_without_job()
    {
        var (p, j) = await JoinTarget.NormalizeAsync(111, "");
        Assert.Equal(111, p);
        Assert.Equal("", j);
    }
}

public class ProcessParsingTests
{
    [Theory]
    [InlineData("x.exe --app -t TICKET -j https://a -b 12345 --launchtime=1", "12345")]
    [InlineData("x.exe roblox-player:1+launchmode:play+gameinfo:T+browsertrackerid:98765+robloxLocale:en_us", "98765")]
    [InlineData("x.exe roblox-player:1+launchmode:play+gameinfo:T", "")]
    public void Main_processes_return_tracker(string cmd, string tracker)
        => Assert.Equal(tracker, RobloxProcesses.ParseCommandLine(cmd));

    [Theory]
    [InlineData("x.exe")]
    [InlineData(@"\??\C:\x.exe -t a")]
    [InlineData("")]
    [InlineData(null)]
    public void Secondary_processes_are_ignored(string? cmd) => Assert.Null(RobloxProcesses.ParseCommandLine(cmd));
}

public class LauncherUriTests
{
    [Fact]
    public void Uri_contains_expected_parts()
    {
        var url = GameLauncher.BuildPlaceLauncherUrl("111222", 71874690745115, "abc");
        Assert.Contains("request=RequestGameJob", url);
        Assert.Contains("placeId=71874690745115", url);
        Assert.Contains("gameId=abc", url);

        var uri = GameLauncher.BuildLauncherUri("TICKET", 1700000000000, url, "111222");
        Assert.StartsWith("roblox-player:1+launchmode:play+gameinfo:TICKET+launchtime:1700000000000+placelauncherurl:", uri);
        Assert.Contains("+browsertrackerid:111222+", uri);
        Assert.DoesNotContain(" ", uri);
        // 啟動器 URL 必須被編碼，否則 '+' 會破壞 URI 的分隔
        Assert.Contains(Uri.EscapeDataString(url), uri);
        // 其他情況
        Assert.Contains("RequestGame&", GameLauncher.BuildPlaceLauncherUrl("1", 5, ""));
        Assert.Contains("RequestFollowUser&userId=5", GameLauncher.BuildPlaceLauncherUrl("1", 5, "", true));
    }

    [Fact]
    public void Tracker_is_numeric()
    {
        var t = GameLauncher.NewBrowserTracker();
        Assert.True(long.TryParse(t, out _));
        Assert.InRange(t.Length, 11, 13);
    }
}

[Collection("http")]
public class ValidateJobIdTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
    {
        public int GameJoinCalls;
        public string? LastUserAgent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host == "gamejoin.roblox.com")
            {
                GameJoinCalls++;
                LastUserAgent = string.Join(" ", request.Headers.GetValues("User-Agent"));
            }
            return Task.FromResult(fn(request));
        }
    }

    private static HttpResponseMessage Csrf403()
    {
        var r = new HttpResponseMessage(HttpStatusCode.Forbidden);
        r.Headers.Add("x-csrf-token", "TOK");
        return r;
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static async Task<(bool, string, bool, FakeHandler)> Run(params string[] statusBodies)
    {
        int i = 0;
        var h = new FakeHandler(req => req.RequestUri!.Host == "auth.roblox.com"
            ? Csrf403()
            : Json(statusBodies[Math.Min(i++, statusBodies.Length - 1)]));
        RobloxApi.HandlerOverride = h;
        try
        {
            var acc = new Account { Username = "u", SecurityToken = "cookie" };
            var (ok, msg, def) = await RobloxApi.ValidateJobIdAsync(acc, 1, "job", "123", default, TimeSpan.Zero);
            return (ok, msg, def, h);
        }
        finally { RobloxApi.HandlerOverride = null; }
    }

    [Fact]
    public async Task Status2_is_ok_and_uses_roblox_client_ua()
    {
        var (ok, _, def, h) = await Run("""{"status":2}""");
        Assert.True(ok); Assert.False(def);
        Assert.Equal("Roblox/WinInet", h.LastUserAgent);
    }

    [Theory]
    [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task Definitive_failures_block(int status)
    {
        var (ok, msg, def, _) = await Run($$"""{"status":{{status}}}""");
        Assert.False(ok); Assert.True(def);
        Assert.Contains($"status={status}", msg);
    }

    [Fact]
    public async Task Status12_is_not_definitive()
    {
        var (ok, _, def, _) = await Run("""{"status":12,"message":"Unable to join Game 311"}""");
        Assert.False(ok); Assert.False(def);
    }

    [Fact]
    public async Task Retries_while_preparing_then_succeeds()
    {
        var (ok, _, _, h) = await Run("""{"status":0}""", """{"status":1}""", """{"status":2}""");
        Assert.True(ok);
        Assert.Equal(3, h.GameJoinCalls);
    }

    [Fact]
    public async Task Http_error_is_not_definitive()
    {
        var h = new FakeHandler(req => req.RequestUri!.Host == "auth.roblox.com" ? Csrf403() : Json("x", HttpStatusCode.InternalServerError));
        RobloxApi.HandlerOverride = h;
        try
        {
            var (ok, _, def) = await RobloxApi.ValidateJobIdAsync(new Account { SecurityToken = "c" }, 1, "j", "1", default, TimeSpan.Zero);
            Assert.False(ok); Assert.False(def);
        }
        finally { RobloxApi.HandlerOverride = null; }
    }
}

[Collection("data-dir")]
public class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ramv3_" + Guid.NewGuid().ToString("N"));

    public StoreTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("RAM_V3_DATA", _dir);
        AppSettings.Reload();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RAM_V3_DATA", null);
        AppSettings.Reload();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Save_load_roundtrip_is_encrypted_on_disk()
    {
        var accounts = new List<Account>
        {
            new() { Username = "alice", UserId = 1, SecurityToken = "SECRET-COOKIE-VALUE", Group = "A", Alias = "al" },
            new() { Username = "bob", UserId = 2, SecurityToken = "OTHER", Fields = { ["k"] = "v" } },
        };
        AccountStore.Save(accounts);
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(AccountStore.FilePath));
        Assert.DoesNotContain("SECRET-COOKIE-VALUE", raw);
        Assert.DoesNotContain("alice", raw);

        var back = AccountStore.Load();
        Assert.Equal(2, back.Count);
        Assert.Equal("SECRET-COOKIE-VALUE", back[0].SecurityToken);
        Assert.Equal("A", back[0].Group);
        Assert.Equal("v", back[1].Fields["k"]);
        Assert.True(back[0].Valid);
    }

    [Fact]
    public void Imports_v2_json_with_snake_case_fields()
    {
        var path = Path.Combine(_dir, "v2.json");
        File.WriteAllText(path, """
        [{"security_token":"tok1","username":"u1","user_id":11,"alias":"a","description":"d","password":"p",
          "email":"","email_password":"","group":"G","fields":{"x":"y"},"last_use":"2026-06-06T15:41:47",
          "last_attempted_refresh":null,"browser_tracker_id":"123"},
         {"security_token":"","username":"nocookie"}]
        """);
        var list = AccountStore.ImportFile(path);
        var a = Assert.Single(list);
        Assert.Equal("u1", a.Username);
        Assert.Equal(11, a.UserId);
        Assert.Equal("G", a.Group);
        Assert.Equal("123", a.BrowserTrackerId);
        Assert.Equal(new DateTime(2026, 6, 6, 15, 41, 47), a.LastUse);
        Assert.Equal("y", a.Fields["x"]);
    }

    [Fact]
    public void Imports_generator_cookies_json()
    {
        var path = Path.Combine(_dir, "cookies.json");
        File.WriteAllText(path, """
        [{"username":"gen1","password":"pw","cookies":[{"name":"other","value":"1"},{"name":".ROBLOSECURITY","value":"COOK"}]},
         {"username":"nocookie","cookies":[]}]
        """);
        var a = Assert.Single(AccountStore.ImportFile(path));
        Assert.Equal("COOK", a.SecurityToken);
        Assert.Equal("gen1", a.Username);
    }

    [Fact]
    public void Merge_updates_existing_and_adds_new()
    {
        var existing = new List<Account> { new() { Username = "Alice", SecurityToken = "old" } };
        var (added, updated) = AccountStore.Merge(existing, new[]
        {
            new Account { Username = "alice", SecurityToken = "new" },
            new Account { Username = "carol", SecurityToken = "c" },
        });
        Assert.Equal((1, 1), (added, updated));
        Assert.Equal("new", existing[0].SecurityToken);
        Assert.Equal(2, existing.Count);
    }

    [Fact]
    public void Settings_persist_checked_usernames()
    {
        AppSettings.Current.CheckedUsernames = new() { "a", "b" };
        AppSettings.Current.DefaultCpuPercent = 12;
        AppSettings.Current.Save();
        AppSettings.Reload();
        Assert.Equal(new[] { "a", "b" }, AppSettings.Current.CheckedUsernames);
        Assert.Equal(12, AppSettings.Current.DefaultCpuPercent);
    }

    [Fact]
    public void FpsUnlocker_writes_and_removes_cap()
    {
        var path = Path.Combine(_dir, "ClientAppSettings.json");
        File.WriteAllText(path, """{"Other":1}""");
        FpsUnlocker.PathOverride = path;
        try
        {
            Assert.True(FpsUnlocker.SetFpsCap(30));
            var doc = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            Assert.Equal(30, doc.GetProperty("DFIntTaskSchedulerTargetFps").GetInt32());
            Assert.Equal(1, doc.GetProperty("Other").GetInt32());

            Assert.True(FpsUnlocker.SetFpsCap(0));
            doc = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            Assert.False(doc.TryGetProperty("DFIntTaskSchedulerTargetFps", out _));
            Assert.Equal(1, doc.GetProperty("Other").GetInt32());
        }
        finally { FpsUnlocker.PathOverride = null; }
    }
}

[CollectionDefinition("data-dir", DisableParallelization = true)]
public class DataDirCollection;
