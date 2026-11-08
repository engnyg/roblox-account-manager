using System.Diagnostics;
using System.Drawing;
using Region = RobloxAccountManager.Core.Native.Region;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Native;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Tests;

[CollectionDefinition("http", DisableParallelization = true)]
public class HttpCollection;

internal sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> fn) : HttpMessageHandler
{
    public List<string> Urls { get; } = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Urls.Add(request.Method + " " + request.RequestUri);
        return Task.FromResult(fn(request));
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Csrf403()
    {
        var r = new HttpResponseMessage(HttpStatusCode.Forbidden);
        r.Headers.Add("x-csrf-token", "TOK");
        return r;
    }

    /// <summary>auth.roblox.com 的 authentication-ticket 一律回 403 + token（取 CSRF 用）。</summary>
    public static bool IsCsrfProbe(HttpRequestMessage r) =>
        r.RequestUri!.Host == "auth.roblox.com" && r.RequestUri.AbsolutePath.StartsWith("/v1/authentication-ticket");

    public static async Task<T> With<T>(Fake h, Func<Task<T>> body)
    {
        RobloxApi.HandlerOverride = h;
        try { return await body(); }
        finally { RobloxApi.HandlerOverride = null; }
    }
}

// ---------------------------------------------------------------------- //
// 私人伺服器
// ---------------------------------------------------------------------- //

[Collection("http")]
public class PrivateServerTests
{
    private const string Uuid = "10a94f61-b29b-40b4-8fa2-bd2640bd1e52";
    private static Account Acc() => new() { Username = "u", SecurityToken = "cookie" };

    [Theory]
    [InlineData("https://www.roblox.com/games/123/Name?privateServerLinkCode=ABC123", 123L, "ABC123")]
    [InlineData("https://www.roblox.com/share?code=deadbeef01&type=Server", null, "deadbeef01")]
    [InlineData("https://www.roblox.com/games/123/Name", null, null)]
    [InlineData("", null, null)]
    public void ParseUrl_handles_both_formats(string url, long? place, string? code)
    {
        var (p, c) = PrivateServer.ParseUrl(url);
        Assert.Equal(place, p);
        Assert.Equal(code, c);
    }

    [Fact]
    public async Task FetchAccessCode_finds_uuid_in_page()
    {
        var h = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent($"<script>Roblox.GameLauncher.joinPrivateGame(55, '{Uuid}')</script>") });
        var (ok, v) = await Fake.With(h, () => PrivateServer.FetchAccessCodeAsync(Acc(), 55, "LINK"));
        Assert.True(ok); Assert.Equal(Uuid, v);
        Assert.Contains("privateServerLinkCode=LINK", h.Urls[0]);
    }

    [Fact]
    public async Task FetchAccessCode_reports_missing_code()
    {
        var h = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") });
        var (ok, v) = await Fake.With(h, () => PrivateServer.FetchAccessCodeAsync(Acc(), 55, "LINK"));
        Assert.False(ok); Assert.Contains("accessCode", v);
    }

    [Fact]
    public async Task ResolveJobId_uses_servers_api_first()
    {
        var h = new Fake(r =>
        {
            var u = r.RequestUri!.ToString();
            if (u.Contains("/universe")) return Fake.Json("""{"universeId":5}""");
            if (u.Contains("/servers/Private")) return Fake.Json("""{"data":[{"id":"job-1"},{"id":"job-2"}]}""");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var (ok, v) = await Fake.With(h, () => PrivateServer.ResolveJobIdAsync(Acc(), 77, "LINK"));
        Assert.True(ok); Assert.Equal("job-1", v);
        Assert.DoesNotContain(h.Urls, x => x.Contains("gamejoin"));
    }

    [Fact]
    public async Task ResolveJobId_resolves_share_code_place_from_page()
    {
        var h = new Fake(r =>
        {
            var u = r.RequestUri!.ToString();
            if (u.Contains("/share")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""..."placeId":4242,...""") };
            if (u.Contains("/places/4242/universe")) return Fake.Json("""{"universeId":9}""");
            if (u.Contains("/games/9/servers/Private")) return Fake.Json("""{"data":[{"id":"shared-job"}]}""");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var (ok, v) = await Fake.With(h, () => PrivateServer.ResolveJobIdAsync(Acc(), null, "cafe01"));
        Assert.True(ok); Assert.Equal("shared-job", v);
    }

    [Fact]
    public async Task ResolveJobId_falls_back_to_gamejoin_and_polls()
    {
        int joins = 0;
        var h = new Fake(r =>
        {
            var u = r.RequestUri!;
            if (Fake.IsCsrfProbe(r)) return Fake.Csrf403();
            if (u.Host == "apis.roblox.com") return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (u.Host == "www.roblox.com") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"\"accessCode\":\"{Uuid}\"") };
            if (u.Host == "gamejoin.roblox.com")
                return ++joins < 3 ? Fake.Json("""{"status":0}""") : Fake.Json("""{"status":2,"jobId":"final-job"}""");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var (ok, v) = await Fake.With(h, () => PrivateServer.ResolveJobIdAsync(Acc(), 77, "LINK", TimeSpan.FromSeconds(10), TimeSpan.Zero));
        Assert.True(ok); Assert.Equal("final-job", v);
        Assert.Equal(3, joins);
    }

    [Fact]
    public async Task ResolveJobId_gamejoin_reads_joinScript_string()
    {
        var h = new Fake(r =>
        {
            var u = r.RequestUri!;
            if (Fake.IsCsrfProbe(r)) return Fake.Csrf403();
            if (u.Host == "apis.roblox.com") return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (u.Host == "www.roblox.com") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("nothing") };
            return Fake.Json("""{"status":2,"joinScript":"{\"GameId\":\"from-script\"}"}""");
        });
        var (ok, v) = await Fake.With(h, () => PrivateServer.ResolveJobIdAsync(Acc(), 77, "LINK", TimeSpan.FromSeconds(5), TimeSpan.Zero));
        Assert.True(ok); Assert.Equal("from-script", v);
    }

    [Fact]
    public async Task ResolveJobId_status12_with_share_fallback_explains()
    {
        var h = new Fake(r =>
        {
            if (Fake.IsCsrfProbe(r)) return Fake.Csrf403();
            if (r.RequestUri!.Host == "gamejoin.roblox.com") return Fake.Json("""{"status":12,"message":"Unable"}""");
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });
        var (ok, v) = await Fake.With(h, () => PrivateServer.ResolveJobIdAsync(Acc(), 77, "LINK", TimeSpan.FromSeconds(5), TimeSpan.Zero));
        Assert.False(ok); Assert.Contains("直接加入", v);
    }

    [Fact]
    public void BuildPlaceLauncherUrl_formats_both_modes()
    {
        var full = PrivateServer.BuildPlaceLauncherUrl("T1", 5, "AC", "LC");
        Assert.Contains("request=RequestPrivateGame", full);
        Assert.Contains("&placeId=5&accessCode=AC&linkCode=LC", full);
        Assert.DoesNotContain("placeId", PrivateServer.BuildPlaceLauncherUrl("T1", null, "AC", "LC"));
    }
}

// ---------------------------------------------------------------------- //
// 伺服器清單 / 排行榜
// ---------------------------------------------------------------------- //

[Collection("http")]
public class GameDiscoveryTests
{
    [Fact]
    public async Task FetchServers_follows_cursor_and_stops()
    {
        var h = new Fake(r =>
        {
            var q = r.RequestUri!.Query;
            return q.Contains("cursor=NEXT")
                ? Fake.Json("""{"data":[{"id":"c","playing":3,"maxPlayers":10}],"nextPageCursor":null}""")
                : Fake.Json("""{"data":[{"id":"a","playing":1,"maxPlayers":10,"ping":50.5,"fps":59},{"id":"b","playing":2,"maxPlayers":10}],"nextPageCursor":"NEXT"}""");
        });
        var list = await Fake.With(h, () => GameDiscovery.FetchServersAsync(1));
        Assert.Equal(["a", "b", "c"], list.Select(s => s.Id));
        Assert.Equal(50.5, list[0].Ping);
        Assert.Equal("1/10", list[0].Occupancy);
        Assert.Equal(2, h.Urls.Count);
    }

    [Fact]
    public async Task FetchServers_returns_empty_on_error()
    {
        var h = new Fake(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Empty(await Fake.With(h, () => GameDiscovery.FetchServersAsync(1)));
    }

    [Fact]
    public void NormalizeGame_handles_field_variants()
    {
        var a = GameDiscovery.NormalizeGame(JsonNode.Parse("""{"universeId":10,"rootPlaceId":20,"name":"A","playerCount":300}""")!);
        Assert.Equal(new ChartGame("A", 20, 10, 300), a);
        var b = GameDiscovery.NormalizeGame(JsonNode.Parse("""{"id":11,"gameName":"B","playing":5}""")!);
        Assert.Equal(new ChartGame("B", 11, 11, 5), b);   // 沒有 placeId 時退回 universeId
    }

    [Fact]
    public async Task FetchCharts_uses_explore_api()
    {
        var h = new Fake(r =>
        {
            var u = r.RequestUri!.ToString();
            if (u.Contains("get-sorts")) return Fake.Json("""{"sorts":[{"sortId":"s1","sortDisplayName":"Popular"},{"sortId":"s2","sortDisplayName":"Empty"}]}""");
            if (u.Contains("sortId=s1")) return Fake.Json("""{"games":[{"universeId":1,"rootPlaceId":2,"name":"G","playerCount":9}]}""");
            return Fake.Json("""{"games":[]}""");
        });
        var charts = await Fake.With(h, () => GameDiscovery.FetchChartsAsync());
        var c = Assert.Single(charts);
        Assert.Equal("Popular", c.Name);
        Assert.Equal(2, c.Games[0].PlaceId);
    }

    [Fact]
    public async Task FetchCharts_falls_back_to_legacy_api()
    {
        var h = new Fake(r =>
        {
            var u = r.RequestUri!.ToString();
            if (u.Contains("explore-api")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (u.Contains("/v1/games/sorts")) return Fake.Json("""{"sorts":[{"token":"tok","name":"Legacy"}]}""");
            if (u.Contains("sortToken=tok")) return Fake.Json("""{"games":[{"placeId":7,"name":"L","playerCount":1}]}""");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var charts = await Fake.With(h, () => GameDiscovery.FetchChartsAsync());
        Assert.Equal("Legacy", Assert.Single(charts).Name);
    }
}

// ---------------------------------------------------------------------- //
// 帳號工具
// ---------------------------------------------------------------------- //

[Collection("http")]
public class AccountApiTests
{
    private static Account Acc() => new() { Username = "u", UserId = 99, SecurityToken = "old-cookie", Password = "p" };

    private static HttpResponseMessage PinOff() => Fake.Json("""{"isEnabled":false}""");

    [Fact]
    public async Task ChangePassword_updates_password_and_cookie()
    {
        var h = new Fake(r =>
        {
            if (Fake.IsCsrfProbe(r)) return Fake.Csrf403();
            if (r.RequestUri!.AbsolutePath == "/v1/account/pin/") return PinOff();
            if (r.RequestUri.AbsolutePath == "/v2/user/passwords/change")
            {
                var ok = new HttpResponseMessage(HttpStatusCode.OK);
                ok.Headers.Add("Set-Cookie", ".ROBLOSECURITY=new-cookie; path=/; secure; httponly");
                return ok;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var acc = Acc();
        var res = await Fake.With(h, () => AccountApi.ChangePasswordAsync(acc, "p", "newpass"));
        Assert.True(res.Ok);
        Assert.Equal("newpass", acc.Password);
        Assert.Equal("new-cookie", acc.SecurityToken);
    }

    [Fact]
    public async Task Locked_pin_blocks_sensitive_actions()
    {
        var h = new Fake(r => Fake.IsCsrfProbe(r) ? Fake.Csrf403()
            : Fake.Json("""{"isEnabled":true,"unlockedUntil":0}"""));
        var res = await Fake.With(h, () => AccountApi.ChangeEmailAsync(Acc(), "p", "a@b.c"));
        Assert.False(res.Ok); Assert.Contains("PIN", res.Message);
        Assert.DoesNotContain(h.Urls, u => u.Contains("/v1/email"));
    }

    [Fact]
    public async Task CsrfRetry_uses_fresh_token_on_403()
    {
        int calls = 0; string? sent = null;
        var h = new Fake(r =>
        {
            if (r.RequestUri!.Host == "users.roblox.com")
            {
                calls++;
                sent = r.Headers.TryGetValues("X-CSRF-TOKEN", out var v) ? v.First() : null;
                return calls == 1 ? Fake.Csrf403() : Fake.Json("{}");
            }
            return Fake.Csrf403();
        });
        var acc = Acc(); acc.CsrfToken = "stale";
        var res = await Fake.With(h, () => AccountApi.SetDisplayNameAsync(acc, "Neo"));
        Assert.True(res.Ok);
        Assert.Equal(2, calls);
        Assert.Equal("TOK", sent);
    }

    [Fact]
    public async Task ToggleBlock_blocks_then_unblocks()
    {
        bool blocked = false;
        var h = new Fake(r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (Fake.IsCsrfProbe(r)) return Fake.Csrf403();
            if (p == "/v1/account/pin/") return PinOff();
            if (p == "/v1/usernames/users") return Fake.Json("""{"data":[{"id":500,"name":"victim"}]}""");
            if (p == "/v1/users/get-detailed-blocked-users")
                return Fake.Json(blocked ? """{"blockedUsers":[{"userId":500}]}""" : """{"blockedUsers":[]}""");
            if (p == "/v1/users/500/block") { blocked = true; return Fake.Json("{}"); }
            if (p == "/v1/users/500/unblock") { blocked = false; return Fake.Json("{}"); }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var acc = Acc();
        var r1 = await Fake.With(h, () => AccountApi.ToggleBlockAsync(acc, "victim"));
        Assert.True(r1.Ok); Assert.Equal("已封鎖", r1.Message); Assert.True(blocked);
        var r2 = await Fake.With(h, () => AccountApi.ToggleBlockAsync(acc, "victim"));
        Assert.True(r2.Ok); Assert.Equal("已解除封鎖", r2.Message); Assert.False(blocked);
    }

    [Fact]
    public async Task ResolveShareLink_returns_place_and_code()
    {
        var h = new Fake(r => Fake.IsCsrfProbe(r) ? Fake.Csrf403()
            : Fake.Json("""{"privateServerInviteData":{"placeId":321,"linkCode":"REALCODE","status":"Valid"}}"""));
        var (ok, place, code, _) = await Fake.With(h, () => AccountApi.ResolveShareLinkAsync(Acc(), "sharecode"));
        Assert.True(ok); Assert.Equal(321, place); Assert.Equal("REALCODE", code);
        Assert.Contains(h.Urls, u => u.Contains("sharelinks/v1/resolve-link"));
    }

    [Fact]
    public async Task ResolveShareLink_reports_expired()
    {
        var h = new Fake(r => Fake.IsCsrfProbe(r) ? Fake.Csrf403()
            : Fake.Json("""{"privateServerInviteData":{"status":"Expired"}}"""));
        var (ok, _, _, msg) = await Fake.With(h, () => AccountApi.ResolveShareLinkAsync(Acc(), "x"));
        Assert.False(ok); Assert.Contains("Expired", msg);
    }

    [Fact]
    public async Task FindPlayerServer_requires_in_game_presence()
    {
        string presence = """{"userPresences":[{"userId":500,"userPresenceType":2,"placeId":123,"gameId":"jjj"}]}""";
        var h = new Fake(r =>
        {
            if (Fake.IsCsrfProbe(r)) return Fake.Csrf403();
            if (r.RequestUri!.AbsolutePath == "/v1/usernames/users") return Fake.Json("""{"data":[{"id":500}]}""");
            return Fake.Json(presence);
        });
        var (ok, _, place, job) = await Fake.With(h, () => AccountApi.FindPlayerServerAsync(Acc(), "target"));
        Assert.True(ok); Assert.Equal(123, place); Assert.Equal("jjj", job);

        presence = """{"userPresences":[{"userId":500,"userPresenceType":1}]}""";
        var (ok2, msg, _, _) = await Fake.With(h, () => AccountApi.FindPlayerServerAsync(Acc(), "target"));
        Assert.False(ok2); Assert.Contains("不在遊戲", msg);
    }

    [Fact]
    public async Task RobloxVersion_reads_client_version()
    {
        var h = new Fake(_ => Fake.Json("""{"version":"0.700.0.1","clientVersionUpload":"version-abc123"}"""));
        Assert.Equal("version-abc123", await Fake.With(h, () => RobloxVersion.GetLatestAsync()));
        var bad = new Fake(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        Assert.Null(await Fake.With(bad, () => RobloxVersion.GetLatestAsync()));
    }
}

// ---------------------------------------------------------------------- //
// V2 / 原版加密檔
// ---------------------------------------------------------------------- //

public class LegacyCryptoTests
{
    [Fact]
    public void Roundtrip_and_wrong_password()
    {
        const string json = """[{"security_token":"c1","username":"alice","user_id":7,"group":"G","fields":{"k":"v"}}]""";
        var blob = LegacyCrypto.Encrypt(json, "pw");
        Assert.True(LegacyCrypto.HasHeader(blob));
        Assert.Equal(json, LegacyCrypto.Decrypt(blob, "pw"));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => LegacyCrypto.Decrypt(blob, "wrong"));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => LegacyCrypto.Decrypt([1, 2, 3], "pw"));
    }

    [Fact]
    public void Header_matches_original_RAM_bytes()
    {
        Assert.Equal(64, LegacyCrypto.Header.Length);
        Assert.Equal(82, LegacyCrypto.Header[0]);       // 'R'
        Assert.Equal(46, LegacyCrypto.Header[^1]);      // '.'
        Assert.StartsWith("Roblox Account Manager created by ic3w0lf22", Encoding.ASCII.GetString(LegacyCrypto.Header));
    }

    [Fact]
    public void ParseAccounts_accepts_v2_and_original_names()
    {
        const string json = """
        [
          {"security_token":"tok1","username":"snake","user_id":1,"group":"A"},
          {"SecurityToken":"tok2","Username":"Pascal","UserID":2,"Alias":"al","Description":"d","Group":"B","Fields":{"x":"y"}},
          {"username":"no-token"}
        ]
        """;
        var list = LegacyCrypto.ParseAccounts(json);
        Assert.Equal(2, list.Count);
        Assert.Equal(("tok1", "snake", 1L, "A"), (list[0].SecurityToken, list[0].Username, list[0].UserId, list[0].Group));
        Assert.Equal(("tok2", "Pascal", 2L, "B"), (list[1].SecurityToken, list[1].Username, list[1].UserId, list[1].Group));
        Assert.Equal("al", list[1].Alias);
        Assert.Equal("y", list[1].Fields["x"]);
    }

    [Fact]
    public void File_helpers_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "ramv3_leg_" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            LegacyCrypto.ExportFile(path, [new Account { SecurityToken = "ck", Username = "zed", UserId = 3 }], "pw");
            Assert.True(LegacyCrypto.HasHeader(path));
            var back = Assert.Single(LegacyCrypto.ImportFile(path, "pw"));
            Assert.Equal("zed", back.Username);
            Assert.Equal("ck", back.SecurityToken);
        }
        finally { File.Delete(path); }
    }
}

// ---------------------------------------------------------------------- //
// 密碼加密的帳號檔
// ---------------------------------------------------------------------- //

[Collection("data-dir")]
public class PasswordStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ramv3_pw_" + Guid.NewGuid().ToString("N"));

    public PasswordStoreTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("RAM_V3_DATA", _dir);
        AppSettings.Reload();
        AccountStore.ResetProtection();
    }

    public void Dispose()
    {
        AccountStore.ResetProtection();
        Environment.SetEnvironmentVariable("RAM_V3_DATA", null);
        AppSettings.Reload();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Password_mode_roundtrip_and_lock()
    {
        AccountStore.SetPassword("hunter2");
        AccountStore.Save([new Account { Username = "a", SecurityToken = "TOPSECRETCOOKIE" }]);
        Assert.True(AccountStore.IsPasswordProtected());
        Assert.DoesNotContain("TOPSECRETCOOKIE", File.ReadAllText(AccountStore.FilePath, Encoding.Latin1));
        Assert.Equal("a", Assert.Single(AccountStore.Load()).Username);

        AccountStore.ResetProtection();
        Assert.Empty(AccountStore.Load());                // 沒密碼讀不到
        Assert.False(AccountStore.Unlock("wrong"));
        Assert.Empty(AccountStore.Load());
        Assert.True(AccountStore.Unlock("hunter2"));
        Assert.Single(AccountStore.Load());
    }

    [Fact]
    public void Switching_back_to_dpapi_and_clear()
    {
        AccountStore.SetPassword("pw");
        AccountStore.Save([new Account { Username = "a", SecurityToken = "c" }]);
        AccountStore.SetPassword(null);
        AccountStore.Save([new Account { Username = "a", SecurityToken = "c" }]);
        Assert.False(AccountStore.IsPasswordProtected());
        Assert.Single(AccountStore.Load());

        AccountStore.ClearAll();
        Assert.False(File.Exists(AccountStore.FilePath));
        Assert.Empty(AccountStore.Load());
    }
}

// ---------------------------------------------------------------------- //
// Fast flags / 優先權 / 記憶體 / 音量
// ---------------------------------------------------------------------- //

[Collection("native")]
public class TuningTests
{
    [Fact]
    public void FastFlags_renderer_quality_and_fps_coexist()
    {
        var path = Path.Combine(Path.GetTempPath(), "ramv3_ff_" + Guid.NewGuid().ToString("N") + ".json");
        FastFlags.PathOverride = path;
        try
        {
            Assert.True(FastFlags.SetFpsCap(144));
            Assert.True(FastFlags.SetRenderer(Renderer.Vulkan));
            Assert.True(FastFlags.SetQualityLevel(3));
            Assert.Equal(Renderer.Vulkan, FastFlags.GetRenderer());
            var o = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.Equal(144, (int)o["DFIntTaskSchedulerTargetFps"]!);
            Assert.Equal(3, (int)o["DFIntDebugFRMQualityLevelOverride"]!);

            Assert.True(FastFlags.SetRenderer(Renderer.D3D11));   // 換引擎要移除舊的旗標
            o = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.Null(o["FFlagDebugGraphicsPreferVulkan"]);
            Assert.True((bool)o["FFlagDebugGraphicsPreferD3D11"]!);

            Assert.True(FastFlags.SetRenderer(Renderer.Default));
            Assert.True(FastFlags.SetQualityLevel(0));
            Assert.True(FastFlags.SetFpsCap(0));
            Assert.Equal("{}", JsonNode.Parse(File.ReadAllText(path))!.ToJsonString());
            Assert.Equal(Renderer.Default, FastFlags.GetRenderer());
        }
        finally { FastFlags.PathOverride = null; File.Delete(path); }
    }

    [Fact]
    public void FastFlags_keeps_foreign_flags()
    {
        var path = Path.Combine(Path.GetTempPath(), "ramv3_ff_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{"FFlagSomethingElse":true}""");
        FastFlags.PathOverride = path;
        try
        {
            FastFlags.SetRenderer(Renderer.OpenGL);
            FastFlags.SetRenderer(Renderer.Default);
            Assert.True((bool)JsonNode.Parse(File.ReadAllText(path))!["FFlagSomethingElse"]!);
        }
        finally { FastFlags.PathOverride = null; File.Delete(path); }
    }

    private static Process Sleeper()
    {
        var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep 60") { CreateNoWindow = true, UseShellExecute = false };
        return Process.Start(psi)!;
    }

    [Fact]
    public void Priority_and_lower_priority_rule()
    {
        using var a = Sleeper(); using var b = Sleeper();
        try
        {
            Assert.True(ProcessTuning.SetPriority(a.Id, ProcessPriorityClass.BelowNormal));
            Assert.Equal(ProcessPriorityClass.BelowNormal, ProcessTuning.GetPriority(a.Id));

            // 2 個實例 → 全部 BelowNormal
            ProcessTuning.ApplyLowerPriority([a.Id, b.Id], true);
            Assert.Equal(ProcessPriorityClass.BelowNormal, ProcessTuning.GetPriority(a.Id));
            Assert.Equal(ProcessPriorityClass.BelowNormal, ProcessTuning.GetPriority(b.Id));
            // 只剩 1 個 → 還原 Normal
            ProcessTuning.ApplyLowerPriority([a.Id], true);
            Assert.Equal(ProcessPriorityClass.Normal, ProcessTuning.GetPriority(a.Id));
            // 關閉選項 → 不動
            Assert.Equal(0, ProcessTuning.ApplyLowerPriority([a.Id, b.Id], false));
            Assert.Equal(ProcessPriorityClass.Normal, ProcessTuning.GetPriority(a.Id));
        }
        finally { a.Kill(); b.Kill(); }
    }

    [Fact]
    public void TrimMemory_works_on_live_process_and_fails_on_dead_pid()
    {
        using var p = Sleeper();
        try
        {
            Thread.Sleep(800);
            Assert.True(ProcessTuning.TrimMemory(p.Id));
        }
        finally { p.Kill(); }
        p.WaitForExit();
        Assert.False(ProcessTuning.TrimMemory(p.Id));
        Assert.False(ProcessTuning.SetPriority(p.Id, ProcessPriorityClass.Normal));
    }

    [Fact]
    public void AudioSessions_are_safe_for_unknown_pid()
    {
        Assert.Null(AudioSessions.GetVolume(int.MaxValue));
        Assert.False(AudioSessions.SetVolume(int.MaxValue, 0.5f));
        Assert.False(AudioSessions.SetMuted(int.MaxValue, true));
    }
}

// ---------------------------------------------------------------------- //
// 目標解析 / 斷線重開 / 行程監控 / 日誌
// ---------------------------------------------------------------------- //

public class TargetTests
{
    private const string Job = "10a94f61-b29b-40b4-8fa2-bd2640bd1e52";

    [Fact]
    public void Parse_recognises_each_kind()
    {
        Assert.Equal(new LaunchTarget(TargetKind.Place, 920587237), AccountTargets.Parse("920587237"));
        Assert.Equal(new LaunchTarget(TargetKind.Place, 123), AccountTargets.Parse("https://www.roblox.com/games/123/Name"));
        Assert.Equal(new LaunchTarget(TargetKind.Job, 55, Job),
            AccountTargets.Parse("https://www.roblox.com/games/start?placeId=55&gameInstanceId=" + Job));
        Assert.Equal(new LaunchTarget(TargetKind.Job, null, Job), AccountTargets.Parse(Job));
        Assert.Equal(new LaunchTarget(TargetKind.Private, 9, LinkCode: "XYZ"),
            AccountTargets.Parse("https://www.roblox.com/games/9/n?privateServerLinkCode=XYZ"));
        Assert.Equal(new LaunchTarget(TargetKind.Private, null, LinkCode: "ab12"),
            AccountTargets.Parse("https://www.roblox.com/share?code=ab12&type=Server"));
        Assert.Equal(TargetKind.None, AccountTargets.Parse("hello").Kind);
        Assert.Equal(TargetKind.None, AccountTargets.Parse("").Kind);
    }

    [Fact]
    public void Account_target_roundtrip_and_follow_priority()
    {
        var a = new Account { Username = "x" };
        Assert.Equal(TargetKind.None, AccountTargets.Get(a).Kind);
        AccountTargets.Set(a, "12345");
        Assert.Equal(12345, AccountTargets.Get(a).PlaceId);
        AccountTargets.Set(a, "12345", followUserId: 777);
        Assert.Equal(new LaunchTarget(TargetKind.Follow, FollowUserId: 777), AccountTargets.Get(a));
        AccountTargets.Set(a, "", 0);
        Assert.Empty(a.Fields);
    }

    [Fact]
    public void Uri_uses_channel_setting()
    {
        var url = GameLauncher.BuildPlaceLauncherUrl("1", 5, "");
        Assert.Contains("+channel:zlive+", GameLauncher.BuildLauncherUri("T", 1, url, "1", "zlive"));
        Assert.Contains("+channel:+", GameLauncher.BuildLauncherUri("T", 1, url, "1", ""));
    }
}

public class RelaunchTests
{
    private static Account Acc(string n) => new() { Username = n };

    [Fact]
    public async Task Dead_instance_is_relaunched_and_retracked()
    {
        var alive = new HashSet<int> { 100 };
        var launched = new List<string>();
        using var sup = new RelaunchSupervisor
        {
            Grace = TimeSpan.Zero,
            IsAlive = alive.Contains,
            Relauncher = (a, t) => { launched.Add($"{a.Username}:{t.PlaceId}"); alive.Add(200); return Task.FromResult(new JoinResult(true, "ok", 200)); },
        };
        sup.Register(Acc("a"), new LaunchTarget(TargetKind.Place, 5), 100);

        Assert.Equal(0, await sup.TickAsync());          // 還活著
        alive.Remove(100);
        Assert.Equal(1, await sup.TickAsync());
        Assert.Equal(["a:5"], launched);
        Assert.Equal(0, await sup.TickAsync());          // 新 pid 200 還活著
        alive.Remove(200);
        Assert.Equal(1, await sup.TickAsync());
    }

    [Fact]
    public async Task Forgotten_instance_is_not_relaunched()
    {
        using var sup = new RelaunchSupervisor { Grace = TimeSpan.Zero, IsAlive = _ => false };
        sup.Register(Acc("a"), new LaunchTarget(TargetKind.Place, 5), 100);
        sup.Forget("a");
        Assert.Equal(0, await sup.TickAsync());
        sup.Register(Acc("b"), new LaunchTarget(TargetKind.Place, 5), 101);
        sup.ForgetPid(101);
        Assert.Equal(0, await sup.TickAsync());
    }

    [Fact]
    public async Task Crash_loop_gives_up()
    {
        int next = 100; int relaunches = 0; string? gaveUp = null;
        using var sup = new RelaunchSupervisor
        {
            Grace = TimeSpan.Zero, MaxRelaunches = 3,
            IsAlive = _ => false,                         // 每次都立刻死掉
            Relauncher = (_, _) => { relaunches++; return Task.FromResult(new JoinResult(true, "", ++next)); },
        };
        sup.GaveUp += (a, _) => gaveUp = a.Username;
        sup.Register(Acc("loop"), new LaunchTarget(TargetKind.Place, 5), next);

        for (int i = 0; i < 6; i++) await sup.TickAsync();
        Assert.Equal(3, relaunches);
        Assert.Equal("loop", gaveUp);
        Assert.Equal(0, sup.TrackedCount);
    }

    [Fact]
    public async Task Failed_relaunch_is_reported_and_retried_within_budget()
    {
        int tries = 0;
        using var sup = new RelaunchSupervisor
        {
            Grace = TimeSpan.Zero, IsAlive = _ => false,
            Relauncher = (_, _) => { tries++; return Task.FromResult(new JoinResult(false, "boom")); },
        };
        JoinResult? seen = null;
        sup.Relaunched += (_, r) => seen = r;
        sup.Register(Acc("a"), new LaunchTarget(TargetKind.Place, 5), 1);
        await sup.TickAsync();
        Assert.False(seen!.Value.Ok);
        Assert.Equal(1, tries);
        Assert.Equal(1, sup.TrackedCount);               // 失敗時保留，下一輪再試
    }
}

public class WatcherTests
{
    [Fact]
    public void Low_memory_instances_are_killed_after_grace()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var killed = new List<int>();
        var w = new RobloxWatcher
        {
            CloseIfMemoryLow = true, MemoryLowMb = 200,
            ListOverride = () =>
            [
                new RobloxInstance(1, "t", "", now.AddMinutes(-5)),     // 舊 + 低記憶體 → 殺
                new RobloxInstance(2, "t", "", now.AddMinutes(-5)),     // 舊 + 正常
                new RobloxInstance(3, "t", "", now.AddSeconds(-5)),     // 剛啟動 → 跳過
                new RobloxInstance(4, "t", "", null),                   // 不知道啟動時間 → 跳過
            ],
            MemoryOverride = pid => pid switch { 1 => 90, 2 => 900, _ => 10 },
            KillOverride = killed.Add,
        };
        var reasons = new List<string>();
        w.Killed += (_, r) => reasons.Add(r);
        Assert.Equal(1, w.Tick(now));
        Assert.Equal([1], killed);
        Assert.Contains("記憶體", reasons[0]);
    }

    [Fact]
    public void Disabled_option_kills_nothing()
    {
        var now = DateTime.Now;
        var w = new RobloxWatcher
        {
            CloseIfMemoryLow = false,
            ListOverride = () => [new RobloxInstance(1, "t", "", now.AddHours(-1))],
            MemoryOverride = _ => 1, KillOverride = _ => throw new InvalidOperationException(),
        };
        Assert.Equal(0, w.Tick(now));
    }
}

public class AppLogTests
{
    [Fact]
    public void Log_filters_and_raises_event()
    {
        AppLog.Clear();
        int seen = 0;
        void H(LogEntry _) => seen++;
        AppLog.Added += H;
        try
        {
            AppLog.Info("alpha joined");
            AppLog.Warn("beta failed");
            AppLog.Error("ALPHA crashed");
        }
        finally { AppLog.Added -= H; }
        Assert.Equal(3, seen);
        Assert.Equal(3, AppLog.Snapshot().Count);
        Assert.Equal(2, AppLog.Snapshot("alpha").Count);   // 不分大小寫
        Assert.Empty(AppLog.Snapshot("zzz"));
        AppLog.Clear();
    }
}

// ---------------------------------------------------------------------- //
// 截圖
// ---------------------------------------------------------------------- //

public class ScreenCaptureTests
{
    private static (int W, int H, byte[] Bgra) Gradient(int w, int h)
    {
        var d = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                d[i] = (byte)(x * 10);        // B
                d[i + 1] = (byte)(y * 20);    // G
                d[i + 2] = 200;               // R
                d[i + 3] = 255;
            }
        return (w, h, d);
    }

    [Fact]
    public void EncodePng_is_decodable_with_correct_pixels()
    {
        var (w, h, d) = Gradient(7, 5);
        var png = ScreenCapture.EncodePng(w, h, d);
        using var bmp = new Bitmap(new MemoryStream(png));
        Assert.Equal((7, 5), (bmp.Width, bmp.Height));
        var px = bmp.GetPixel(3, 2);
        Assert.Equal((200, 40, 30), (px.R, px.G, px.B));
    }

    [Fact]
    public void Region_parse_and_crop()
    {
        Assert.True(ScreenCapture.TryParseRegion(" 1, 2,3 ,4", out var r));
        Assert.Equal(new Region(1, 2, 3, 4), r);
        Assert.False(ScreenCapture.TryParseRegion("1,2,3", out _));
        Assert.False(ScreenCapture.TryParseRegion("1,2,0,4", out _));
        Assert.False(ScreenCapture.TryParseRegion("a,b,c,d", out _));

        var (w, h, d) = Gradient(10, 8);
        var c = ScreenCapture.Crop(w, h, d, new Region(2, 3, 4, 2))!.Value;
        Assert.Equal((4, 2), (c.W, c.H));
        Assert.Equal((byte)20, c.Bgra[0]);                 // B = x(2)*10
        Assert.Equal((byte)60, c.Bgra[1]);                 // G = y(3)*20
        var clipped = ScreenCapture.Crop(w, h, d, new Region(8, 6, 100, 100))!.Value;
        Assert.Equal((2, 2), (clipped.W, clipped.H));
        Assert.Null(ScreenCapture.Crop(w, h, d, new Region(50, 50, 5, 5)));
    }

    [Fact]
    public void CaptureWindow_rejects_invalid_handle()
        => Assert.Null(ScreenCapture.CaptureWindow(IntPtr.Zero));

    [Fact]
    public async Task Tracker_uploads_full_window_or_regions()
    {
        var uploads = new List<(string Label, string File)>();
        var accounts = new List<Account> { new() { Username = "alice", BrowserTrackerId = "111" }, new() { Username = "bob", BrowserTrackerId = "222" } };
        var tracker = new ScreenshotTracker(() => accounts)
        {
            ListInstances = () => [new RobloxInstance(10, "111", "", null), new RobloxInstance(11, "222", "", null), new RobloxInstance(12, "999", "", null)],
            Capturer = pid => pid == 11 ? null : Gradient(20, 20),
            Uploader = (_, label, png, file, _) => { uploads.Add((label, file)); Assert.True(png.Length > 50); return Task.FromResult(true); },
        };
        var s = new AppSettings
        {
            TrackingWebhookUrl = "https://discord.com/api/webhooks/1/abc",
            TrackingRegions = { ["alice"] = ["0,0,10,10", "bad", "5,5,10,10"] },
        };
        var failures = new List<string>();
        tracker.Result += (n, ok, _) => { if (!ok) failures.Add(n); };

        int sent = await tracker.CaptureOnceAsync(s);
        Assert.Equal(3, sent);                                      // alice×2 區域 + 未知 PID 12 整個視窗
        Assert.Contains(uploads, u => u.Label == "alice · region 1");
        Assert.Contains(uploads, u => u.Label == "alice · region 3");
        Assert.Contains(uploads, u => u.Label == "PID 12");
        Assert.Equal(["bob"], failures);                            // 擷取失敗有回報
    }

    [Fact]
    public async Task Tracker_ignores_invalid_webhook()
    {
        var tracker = new ScreenshotTracker(() => [])
        {
            ListInstances = () => throw new InvalidOperationException("should not list"),
        };
        Assert.Equal(0, await tracker.CaptureOnceAsync(new AppSettings { TrackingWebhookUrl = "http://insecure" }));
        Assert.False(ScreenshotTracker.IsValidWebhook(""));
        Assert.True(ScreenshotTracker.IsValidWebhook("https://discord.com/api/webhooks/1/x"));
    }
}

// ---------------------------------------------------------------------- //
// Nexus / Web API
// ---------------------------------------------------------------------- //

public class ServerTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket ws)
    {
        var buf = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var r = await ws.ReceiveAsync(buf, cts.Token);
        return Encoding.UTF8.GetString(buf, 0, r.Count);
    }

    [Fact]
    public async Task Nexus_connect_ping_command_and_disconnect()
    {
        int port = FreePort();
        using var server = new NexusServer(port);
        var connected = new TaskCompletionSource<ControlledAccount>();
        var disconnected = new TaskCompletionSource<ControlledAccount>();
        var messages = new TaskCompletionSource<(string, JsonObject)>();
        server.AccountConnected += a => connected.TrySetResult(a);
        server.AccountDisconnected += a => disconnected.TrySetResult(a);
        server.MessageReceived += (_, c, d) => { if (c == "Status") messages.TrySetResult((c, d)); };
        server.Start();

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/?name=alice&id=42&jobId=JJ"), CancellationToken.None);
        var acc = await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("alice", 42L, "JJ"), (acc.Username, acc.UserId, acc.JobId));
        Assert.True(acc.IsConnected);

        await ws.SendAsync(Encoding.UTF8.GetBytes("""{"command":"Ping"}"""), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Contains("Pong", await ReceiveAsync(ws));

        await ws.SendAsync(Encoding.UTF8.GetBytes("""{"command":"Status","hp":100}"""), WebSocketMessageType.Text, true, CancellationToken.None);
        var (_, data) = await messages.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(100, (int)data["hp"]!);

        Assert.True(await server.SendAsync("alice", NexusCommand.Make(NexusCommand.Chat, new { text = "hi" })));
        var got = JsonNode.Parse(await ReceiveAsync(ws))!;
        Assert.Equal("Chat", (string)got["command"]!);
        Assert.Equal("hi", (string)got["text"]!);
        Assert.False(await server.SendAsync("nobody", "x"));
        Assert.Equal(1, await server.BroadcastAsync(NexusCommand.Make(NexusCommand.Rejoin)));
        Assert.Contains("Rejoin", await ReceiveAsync(ws));

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(acc.IsConnected);
        Assert.NotNull(acc.DisconnectedAt);
    }

    [Fact]
    public async Task Nexus_rejects_missing_name_and_wrong_password()
    {
        int port = FreePort();
        using var server = new NexusServer(port, "secret");
        server.Start();

        using (var ws = new ClientWebSocket())
            await Assert.ThrowsAnyAsync<Exception>(() => ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/?name=a&id=1&password=nope"), CancellationToken.None));
        using (var ws = new ClientWebSocket())
            await Assert.ThrowsAnyAsync<Exception>(() => ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/?id=1&password=secret"), CancellationToken.None));
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/?name=a&id=1&password=secret"), CancellationToken.None);
            Assert.Equal(WebSocketState.Open, ws.State);
        }
    }

    [Fact]
    public void Nexus_command_helpers()
    {
        var m = JsonNode.Parse(NexusCommand.Make("Kick", new { reason = "afk" }))!;
        Assert.Equal("Kick", (string)m["command"]!);
        Assert.Equal("afk", (string)m["reason"]!);
        Assert.Equal(("Ping", 0), (NexusCommand.Parse("Ping").Command, NexusCommand.Parse("Ping").Data.Count));
        Assert.Equal("Status", NexusCommand.Parse("""{"command":"Status"}""").Command);
    }

    private static WebApiServer MakeApi(int port, string password, List<Account> accounts, Account? selected, out List<string> joins)
    {
        var list = new List<string>();
        joins = list;
        return new WebApiServer(port, password, () => accounts, () => selected)
        {
            JoinHandler = (a, place, job) => { list.Add($"{a.Username}|{place}|{job}"); return Task.FromResult(new JoinResult(a.Username != "bad", "failed!")); },
            CsrfHandler = a => Task.FromResult((a.Username != "nocsrf", "CSRF-" + a.Username)),
        };
    }

    [Fact]
    public async Task WebApi_routes_work_without_password()
    {
        int port = FreePort();
        var alice = new Account { Username = "Alice", UserId = 1, Group = "G", SecurityToken = "c" };
        var bad = new Account { Username = "bad", SecurityToken = "c" };
        using var api = MakeApi(port, "", [alice, bad], alice, out var joins);
        api.Start();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        var arr = JsonNode.Parse(await http.GetStringAsync("/GetAccounts"))!.AsArray();
        Assert.Equal(2, arr.Count);
        Assert.Equal("Alice", (string)arr[0]!["username"]!);
        Assert.Equal("G", (string)arr[0]!["group"]!);

        Assert.Equal("CSRF-Alice", await http.GetStringAsync("/GetCSRFToken?username=alice"));   // 不分大小寫
        Assert.Equal("CSRF-Alice", await http.GetStringAsync("/GetCSRFToken"));                  // 沒指定 → 目前選取的帳號
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/GetCSRFToken?username=ghost")).StatusCode);

        var cur = JsonNode.Parse(await http.GetStringAsync("/GetCurrentAccount"))!;
        Assert.Equal(1, (long)cur["userId"]!);

        var ok = await http.PostAsync("/JoinServer", new StringContent("""{"username":"alice","placeId":123,"jobId":"jj"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Success", await ok.Content.ReadAsStringAsync());
        Assert.Equal(["Alice|123|jj"], joins);

        var fail = await http.PostAsync("/JoinServer", new StringContent("""{"username":"bad","placeId":1}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.InternalServerError, fail.StatusCode);
        Assert.Equal("failed!", await fail.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/JoinServer", new StringContent("not json"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/nope")).StatusCode);
    }

    [Fact]
    public async Task WebApi_password_via_header_or_query()
    {
        int port = FreePort();
        using var api = MakeApi(port, "pw", [], null, out _);
        api.Start();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/GetAccounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/GetAccounts?password=wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/GetAccounts?password=pw")).StatusCode);

        using var req = new HttpRequestMessage(HttpMethod.Get, "/GetAccounts");
        req.Headers.Add("password", "pw");
        Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(req)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/GetCurrentAccount?password=pw")).StatusCode);   // 沒選帳號
    }

    [Fact]
    public async Task WebApi_stop_releases_port()
    {
        int port = FreePort();
        var api = MakeApi(port, "", [], null, out _);
        api.Start();
        Assert.True(api.IsRunning);
        api.Stop();
        Assert.False(api.IsRunning);
        using var again = MakeApi(port, "", [], null, out _);
        again.Start();
        Assert.True(again.IsRunning);
        await Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------- //
// 大頭貼
// ---------------------------------------------------------------------- //

[Collection("http")]
public class AvatarServiceTests
{
    [Fact]
    public async Task Fill_sets_AvatarUrl_only_for_completed_thumbnails()
    {
        var h = new Fake(_ => Fake.Json("""
            {"data":[
              {"targetId":1,"state":"Completed","imageUrl":"https://tr.rbxcdn.com/a.png"},
              {"targetId":2,"state":"Pending","imageUrl":""},
              {"targetId":3,"state":"Blocked","imageUrl":null}]}
            """));
        var accs = new List<Account> { new() { Username = "a", UserId = 1 }, new() { Username = "b", UserId = 2 }, new() { Username = "c", UserId = 3 }, new() { Username = "d" } };

        var n = await Fake.With(h, () => AvatarService.FillAsync(accs));

        Assert.Equal(1, n);
        Assert.Equal("https://tr.rbxcdn.com/a.png", accs[0].AvatarUrl);
        Assert.Equal("", accs[1].AvatarUrl);
        Assert.Single(h.Urls);
        Assert.Contains("userIds=1,2,3", h.Urls[0]);   // 沒有 UserId 的帳號不會被查
    }

    [Fact]
    public async Task Fill_skips_accounts_that_already_have_an_avatar_and_survives_http_errors()
    {
        var h = new Fake(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var done = new Account { UserId = 9, AvatarUrl = "x" };
        var todo = new Account { UserId = 10 };

        var n = await Fake.With(h, () => AvatarService.FillAsync([done, todo]));

        Assert.Equal(0, n);
        Assert.Equal("x", done.AvatarUrl);
        Assert.Contains("userIds=10", h.Urls[0]);
        Assert.DoesNotContain("9", h.Urls[0].Split("userIds=")[1].Split('&')[0].Split(','));
    }
}

// ---------------------------------------------------------------------- //
// 目的地卡片：遊戲資訊
// ---------------------------------------------------------------------- //

[Collection("http")]
public class PlaceInfoTests
{
    private static Fake Api(bool iconReady = true) => new(r =>
    {
        var u = r.RequestUri!.ToString();
        if (u.Contains("/universes/v1/places/123/universe")) return Fake.Json("""{"universeId":999}""");
        if (u.Contains("games.roblox.com/v1/games?universeIds=999")) return Fake.Json("""{"data":[{"id":999,"rootPlaceId":123,"name":"RIVALS","playing":4567}]}""");
        if (u.Contains("/v1/places/gameicons?placeIds=123"))
            return Fake.Json(iconReady
                ? """{"data":[{"targetId":123,"state":"Completed","imageUrl":"https://tr.rbxcdn.com/icon.png"}]}"""
                : """{"data":[{"targetId":123,"state":"Pending","imageUrl":""}]}""");
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    [Fact]
    public async Task Resolves_name_players_and_icon_without_a_cookie()
    {
        GameDiscovery.ClearPlaceCache();
        var h = Api();
        var info = await Fake.With(h, () => GameDiscovery.GetPlaceInfoAsync(123));

        Assert.NotNull(info);
        Assert.Equal("RIVALS", info!.Name);
        Assert.Equal(999, info.UniverseId);
        Assert.Equal(4567, info.Playing);
        Assert.Equal("https://tr.rbxcdn.com/icon.png", info.IconUrl);
        Assert.DoesNotContain(h.Urls, u => u.Contains("multiget-place-details"));   // 需要登入的端點不能用
    }

    [Fact]
    public async Task Pending_icon_gives_empty_url_and_results_are_cached()
    {
        GameDiscovery.ClearPlaceCache();
        var h = Api(iconReady: false);
        var first = await Fake.With(h, () => GameDiscovery.GetPlaceInfoAsync(123));
        int calls = h.Urls.Count;
        var second = await Fake.With(h, () => GameDiscovery.GetPlaceInfoAsync(123));

        Assert.Equal("", first!.IconUrl);
        Assert.Same(first, second);
        Assert.Equal(calls, h.Urls.Count);
    }

    [Fact]
    public async Task Unknown_place_returns_null()
    {
        GameDiscovery.ClearPlaceCache();
        var info = await Fake.With(Api(), () => GameDiscovery.GetPlaceInfoAsync(55555));
        Assert.Null(info);
        Assert.Null(await GameDiscovery.GetPlaceInfoAsync(0));
    }
}

public class AccountDisplayTests
{
    [Fact]
    public void Subtitle_joins_alias_and_description_and_PidText_follows_Pid()
    {
        var a = new Account { Username = "u" };
        var changed = new List<string?>();
        a.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("", a.Subtitle);
        a.Alias = "main";
        a.Description = "farm bot";
        Assert.Equal("main  ·  farm bot", a.Subtitle);
        Assert.Contains(nameof(Account.Subtitle), changed);

        Assert.Equal("—", a.PidText);
        a.Pid = 4321;
        Assert.Equal("PID 4321", a.PidText);
        Assert.Contains(nameof(Account.PidText), changed);
    }
}

// ---------------------------------------------------------------------- //
// 網頁控制面板
// ---------------------------------------------------------------------- //

internal sealed class FakeWebControl : IWebControl
{
    public List<string> Calls { get; } = new();
    public Task<WebState> GetStateAsync() => Task.FromResult(new WebState(
        [new WebAccount("alice", "main", "Farm", 1, "", true, 1234, "", true, "main")], 1, "123", "", false, [], new() { ["accent"] = "#fff" }, "v1"));
    public Task<(bool Ok, string Message)> LaunchAsync(IReadOnlyList<string> usernames, string place, string job)
    {
        Calls.Add($"launch [{string.Join(",", usernames)}] {place} {job}");
        return Task.FromResult(place == "bad" ? (false, "Place ID 不正確") : (true, "開始啟動"));
    }
    public Task<int> KillAsync(IReadOnlyList<string> usernames) { Calls.Add($"kill [{string.Join(",", usernames)}]"); return Task.FromResult(usernames.Count); }
    public Task SetCheckedAsync(IReadOnlyList<string> usernames, bool value) { Calls.Add($"check [{string.Join(",", usernames)}] {value}"); return Task.CompletedTask; }
}

[Collection("native")]
public class WebPanelTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static (WebApiServer Api, HttpClient Http, FakeWebControl Ctl) Start(string password = "", IWebControl? control = null, bool noControl = false)
    {
        int port = FreePort();
        var ctl = new FakeWebControl();
        var api = new WebApiServer(port, password, () => [], () => null)
        {
            Control = noControl ? null : control ?? ctl,
            PlaceInfoHandler = id => Task.FromResult(id == 42 ? new PlaceInfo(42, 7, "RIVALS", 999, "https://x/icon.png") : null),
        };
        api.Start();
        return (api, new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") }, ctl);
    }

    private static HttpRequestMessage Post(string path, string json, string? password = null, bool panelHeader = true)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (panelHeader) req.Headers.Add(WebApiServer.PanelHeader, "1");
        if (password is not null) req.Headers.Add("password", password);
        return req;
    }

    [Fact]
    public async Task Panel_page_loads_without_password_but_api_requires_it()
    {
        var (api, http, _) = Start("pw");
        using (api) using (http)
        {
            var page = await http.GetAsync("/");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("text/html", page.Content.Headers.ContentType!.ToString());
            Assert.Contains("X-RAM-Panel", await page.Content.ReadAsStringAsync());   // 嵌入的 panel.html 確實送出了

            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/state")).StatusCode);
            using var ok = new HttpRequestMessage(HttpMethod.Get, "/api/state");
            ok.Headers.Add("password", "pw");
            var state = JsonNode.Parse(await (await http.SendAsync(ok)).Content.ReadAsStringAsync())!;
            Assert.Equal("alice", (string)state["accounts"]![0]!["username"]!);
            Assert.Equal(1234, (int)state["accounts"]![0]!["pid"]!);
            Assert.Null(state["accounts"]![0]!["securityToken"]);   // 絕不能把 cookie 送到網頁
        }
    }

    [Fact]
    public async Task Api_posts_need_the_panel_header_and_route_to_the_control()
    {
        var (api, http, ctl) = Start();
        using (api) using (http)
        {
            // 沒有 X-RAM-Panel：其他網站的表單送不進來
            Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Post("/api/kill", """{"usernames":["alice"]}""", panelHeader: false))).StatusCode);
            Assert.Empty(ctl.Calls);

            var launch = await http.SendAsync(Post("/api/launch", """{"usernames":[],"placeId":"123","jobId":"abc"}"""));
            Assert.Equal(HttpStatusCode.OK, launch.StatusCode);
            Assert.Equal("開始啟動", (string)JsonNode.Parse(await launch.Content.ReadAsStringAsync())!["message"]!);

            var bad = await http.SendAsync(Post("/api/launch", """{"usernames":["alice"],"placeId":"bad"}"""));
            Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);
            Assert.Equal("Place ID 不正確", (string)JsonNode.Parse(await bad.Content.ReadAsStringAsync())!["error"]!);

            var kill = JsonNode.Parse(await (await http.SendAsync(Post("/api/kill", """{"usernames":["alice","bob"]}"""))).Content.ReadAsStringAsync())!;
            Assert.Equal(2, (int)kill["killed"]!);
            await http.SendAsync(Post("/api/check", """{"usernames":["alice"],"checked":false}"""));
            Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(Post("/api/check", "not json"))).StatusCode);

            Assert.Equal(["launch [] 123 abc", "launch [alice] bad ", "kill [alice,bob]", "check [alice] False"], ctl.Calls);
        }
    }

    [Fact]
    public async Task Place_lookup_and_missing_control()
    {
        var (api, http, _) = Start(noControl: true);
        using (api) using (http)
        {
            var g = JsonNode.Parse(await http.GetStringAsync("/api/place?id=42"))!;
            Assert.Equal("RIVALS", (string)g["name"]!);
            Assert.Equal("https://x/icon.png", (string)g["iconUrl"]!);
            Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/place?id=5")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/api/place?id=abc")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.GetAsync("/api/state")).StatusCode);
        }
    }

    private sealed class FakeMixer : IWebMixer
    {
        public List<string> Calls { get; } = new();
        public Task<(List<WebInstance> Instances, WebMixerSettings Settings)> SnapshotAsync() => Task.FromResult((
            new List<WebInstance> { new(1234, "alice", 812, 3.5, 70, false, "Normal", "") },
            new WebMixerSettings(60, 50, 2048, true, 800, 600, true, false, "D3D11", 0, ["Normal", "Idle"], ["Default", "D3D11"])));
        public Task<string> ApplyAsync(string action, IReadOnlyList<int> pids, JsonNode? args)
        {
            Calls.Add($"{action} [{string.Join(",", pids)}] {args?["value"]}");
            if (action == "bad") throw new ArgumentException("不認得的動作：bad");
            return Task.FromResult("完成 " + action);
        }
    }

    [Fact]
    public async Task Mixer_routes_snapshot_actions_and_errors()
    {
        var (api, http, _) = Start();
        var mixer = new FakeMixer();
        api.Mixer = mixer;
        using (api) using (http)
        {
            var snap = JsonNode.Parse(await http.GetStringAsync("/api/mixer"))!;
            Assert.Equal(1234, (int)snap["instances"]![0]!["pid"]!);
            Assert.Equal("alice", (string)snap["instances"]![0]!["account"]!);
            Assert.Equal(2048, (int)snap["settings"]!["ramLimit"]!);
            Assert.Equal("Idle", (string)snap["settings"]!["priorities"]![1]!);

            // POST 一樣需要 X-RAM-Panel
            Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(Post("/api/mixer", """{"action":"trim"}""", panelHeader: false))).StatusCode);
            Assert.Empty(mixer.Calls);

            var ok = await http.SendAsync(Post("/api/mixer", """{"action":"volume","pids":[1234,"x",-5],"value":30}"""));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("完成 volume", (string)JsonNode.Parse(await ok.Content.ReadAsStringAsync())!["message"]!);
            Assert.Equal(["volume [1234] 30"], mixer.Calls);   // 不合法的 PID 在進到混音台前就被濾掉

            var bad = await http.SendAsync(Post("/api/mixer", """{"action":"bad"}"""));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Contains("不認得", (string)JsonNode.Parse(await bad.Content.ReadAsStringAsync())!["error"]!);
            Assert.Equal(HttpStatusCode.BadRequest, (await http.SendAsync(Post("/api/mixer", "not json"))).StatusCode);
        }
    }

    [Fact]
    public async Task Mixer_needs_password_and_is_unavailable_without_a_mixer()
    {
        var (api, http, _) = Start("pw");
        using (api) using (http)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/mixer")).StatusCode);
            using var withPw = new HttpRequestMessage(HttpMethod.Get, "/api/mixer");
            withPw.Headers.Add("password", "pw");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.SendAsync(withPw)).StatusCode);   // 沒掛混音台
        }
    }

    [Fact]
    public async Task Non_ascii_password_works_url_encoded_like_the_browser_sends_it()
    {
        var (api, http, _) = Start("測試密碼");
        using (api) using (http)
        {
            using var encoded = new HttpRequestMessage(HttpMethod.Get, "/api/state");
            encoded.Headers.Add("password", Uri.EscapeDataString("測試密碼"));
            Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(encoded)).StatusCode);

            using var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/state");
            wrong.Headers.Add("password", Uri.EscapeDataString("測試密碼x"));
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(wrong)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/state?password=" + Uri.EscapeDataString("測試密碼"))).StatusCode);
        }
    }

    [Fact]
    public async Task Instance_mixer_rejects_unknown_actions_and_unsafe_kill()
    {
        var m = new InstanceMixer(() => []);
        await Assert.ThrowsAsync<ArgumentException>(() => m.ApplyAsync("nope", [], null));
        await Assert.ThrowsAsync<ArgumentException>(() => m.ApplyAsync("priority", [], JsonNode.Parse("""{"value":"Banana"}""")));
        // 沒指定 PID 又沒有明確的 all，不能結束全部
        await Assert.ThrowsAsync<ArgumentException>(() => m.ApplyAsync("kill", [], JsonNode.Parse("{}")));
        // 不是 Roblox 的 PID 會被濾掉：自己的行程不會被結束
        var msg = await m.ApplyAsync("kill", [Environment.ProcessId], JsonNode.Parse("{}"));
        Assert.StartsWith("已結束 0/0", msg);
    }

    [Fact]
    public void Lan_is_only_opened_when_a_password_is_set()
    {
        Assert.False(new WebApiServer(1, "", () => [], () => null, lan: true).Lan);
        Assert.True(new WebApiServer(1, "", () => [], () => null, lan: true).LanRequested);
        Assert.True(new WebApiServer(1, "pw", () => [], () => null, lan: true).Lan);
        Assert.False(new WebApiServer(1, "pw", () => [], () => null, lan: false).Lan);
    }

    [Fact]
    public async Task MiniHttp_parses_query_body_and_rejects_bad_requests()
    {
        async Task<(MiniRequest? Req, string? Err)> Parse(string raw)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(raw));
            return await MiniHttpServer.ReadRequestAsync(ms, null, CancellationToken.None);
        }
        var body = "{\"a\":\"中文\"}";
        var bytes = Encoding.UTF8.GetByteCount(body);
        var (req, err) = await Parse($"POST /api/x%20y?id=5&n=%E4%B8%AD HTTP/1.1\r\nHost: h\r\nContent-Length: {bytes}\r\nX-Test: 1\r\n\r\n{body}");
        Assert.Null(err);
        Assert.Equal("POST", req!.Method);
        Assert.Equal("/api/x y", req.Path);
        Assert.Equal("5", req.Query["id"]);
        Assert.Equal("中", req.Query["n"]);
        Assert.Equal("1", req.Header("x-test"));
        Assert.Equal(body, req.Body);

        Assert.NotNull((await Parse("POST / HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n0\r\n\r\n")).Err);
        Assert.NotNull((await Parse("POST / HTTP/1.1\r\nContent-Length: 99\r\n\r\nshort")).Err);
        Assert.NotNull((await Parse("garbage\r\n\r\n")).Err);
    }
}
