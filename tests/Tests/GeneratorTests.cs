using System.Net;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Models;
using RobloxAccountManager.Core.Services;

namespace RobloxAccountManager.Tests;

[Collection("http")]
public class GeneratorTests
{
    private const string BloxKey = "BLOX-secret-key";
    private const string AltKey = "MG_secret-key";

    [Theory]
    [InlineData("BLOX-abc", GeneratorProvider.BloxGen)]
    [InlineData("  BLOX-abc  ", GeneratorProvider.BloxGen)]
    [InlineData("MG_abc", GeneratorProvider.Altgen)]
    [InlineData("blox-abc", GeneratorProvider.Unknown)]
    [InlineData("", GeneratorProvider.Unknown)]
    [InlineData(null, GeneratorProvider.Unknown)]
    public void Detect_uses_key_prefix(string? key, GeneratorProvider expected) =>
        Assert.Equal(expected, AccountGenerator.Detect(key));

    private static string BloxOk(string name) =>
        "{\"success\":true,\"data\":{\"username\":\"" + name + "\",\"password\":\"pw-" + name +
        "\",\"cookie\":\"cookie-" + name + "\",\"type\":\"alt\",\"cost\":1}}";

    [Fact]
    public async Task BloxGen_sends_key_and_type_in_body_and_buys_one_per_request()
    {
        var bodies = new List<JsonNode>();
        int n = 0;
        var h = new Fake(r =>
        {
            bodies.Add(JsonNode.Parse(r.Content!.ReadAsStringAsync().Result)!);
            return Fake.Json(BloxOk("user" + (++n)));
        });

        var got = new List<Account>();
        var res = await Fake.With(h, () => AccountGenerator.GenerateAsync(BloxKey, 2, "+1 year old", a => { got.Add(a); return Task.CompletedTask; }));

        Assert.Null(res.Error);
        Assert.Equal(["POST https://core.bloxgen.net/api/generate", "POST https://core.bloxgen.net/api/generate"], h.Urls);
        Assert.All(bodies, b => { Assert.Equal(BloxKey, (string?)b["apiKey"]); Assert.Equal("+1 year old", (string?)b["type"]); });
        Assert.Equal(["user1", "user2"], got.Select(a => a.Username));
        Assert.Equal("cookie-user1", res.Accounts[0].SecurityToken);
        Assert.Equal("pw-user1", res.Accounts[0].Password);
        Assert.Equal("Generated", res.Accounts[0].Group);
    }

    [Fact]
    public async Task BloxGen_stops_at_first_failure_but_keeps_accounts_already_bought()
    {
        int n = 0;
        var h = new Fake(_ => ++n == 1
            ? Fake.Json(BloxOk("first"))
            : Fake.Json("""{"success":false,"message":"Insufficient balance"}""", HttpStatusCode.PaymentRequired));

        var saved = new List<string>();
        var res = await Fake.With(h, () => AccountGenerator.GenerateAsync(BloxKey, 5, "alt", a => { saved.Add(a.Username); return Task.CompletedTask; }));

        Assert.Equal("Insufficient balance", res.Error);
        Assert.Equal(["first"], saved);            // 買到的那個已經交給呼叫端存檔
        Assert.Equal(2, h.Urls.Count);             // 失敗後不再繼續扣款
        Assert.DoesNotContain(BloxKey, res.Error);
    }

    [Fact]
    public async Task BloxGen_error_can_be_a_string_in_the_error_field_and_non_json_falls_back_to_http_status()
    {
        var r1 = await Fake.With(new Fake(_ => Fake.Json("""{"success":false,"error":"Invalid key"}""")),
            () => AccountGenerator.GenerateAsync(BloxKey, 1));
        Assert.Equal("Invalid key", r1.Error);

        var r2 = await Fake.With(new Fake(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>oops</html>") }),
            () => AccountGenerator.GenerateAsync(BloxKey, 1));
        Assert.Equal("HTTP 502", r2.Error);
        Assert.Empty(r2.Accounts);
    }

    [Fact]
    public async Task Altgen_uses_bearer_header_and_requests_whole_quantity_at_once()
    {
        string? auth = null; JsonNode? body = null;
        var h = new Fake(r =>
        {
            auth = r.Headers.Authorization?.ToString();
            body = JsonNode.Parse(r.Content!.ReadAsStringAsync().Result);
            return Fake.Json("""
                {"success":true,"data":{"accounts":[
                  {"username":"a1","password":"p1","cookie":"c1"},
                  {"username":"a2","password":"p2","cookie":"c2"},
                  {"username":"a3","password":"p3","cookie":""}]}}
                """);
        });

        var res = await Fake.With(h, () => AccountGenerator.GenerateAsync(AltKey, 3));

        Assert.Equal(["POST https://api.altgen.me/api/v1/generate"], h.Urls);
        Assert.Equal("Bearer " + AltKey, auth);
        Assert.Equal("ROBLOX_NORMAL", (string?)body!["type"]);
        Assert.Equal(3, (int?)body["quantity"]);
        Assert.Null(res.Error);
        Assert.Equal(["a1", "a2"], res.Accounts.Select(a => a.Username));   // 沒有 cookie 的那筆用不到，略過
    }

    [Fact]
    public async Task Altgen_error_message_comes_from_error_object()
    {
        var h = new Fake(_ => Fake.Json("""{"success":false,"error":{"code":"NO_FUNDS","message":"Not enough credits"}}""", HttpStatusCode.PaymentRequired));
        var res = await Fake.With(h, () => AccountGenerator.GenerateAsync(AltKey, 1));
        Assert.Equal("Not enough credits", res.Error);
        Assert.Empty(res.Accounts);
    }

    [Fact]
    public async Task Altgen_quantity_is_clamped_to_1_100()
    {
        var qty = new List<int?>();
        var h = new Fake(r => { qty.Add((int?)JsonNode.Parse(r.Content!.ReadAsStringAsync().Result)!["quantity"]); return Fake.Json("""{"success":true,"data":{"accounts":[]}}"""); });
        await Fake.With(h, () => AccountGenerator.GenerateAsync(AltKey, 9999));
        await Fake.With(h, () => AccountGenerator.GenerateAsync(AltKey, 0));
        Assert.Equal([100, 1], qty);
    }

    [Fact]
    public async Task Unknown_key_makes_no_request()
    {
        var h = new Fake(_ => Fake.Json("{}"));
        var res = await Fake.With(h, () => AccountGenerator.GenerateAsync("not-a-key", 1));
        Assert.Empty(h.Urls);
        Assert.NotNull(res.Error);
        Assert.Empty(res.Accounts);
    }

    [Fact]
    public async Task Network_failure_is_reported_not_thrown()
    {
        var h = new Fake(_ => throw new HttpRequestException("offline"));
        var res = await Fake.With(h, () => AccountGenerator.GenerateAsync(BloxKey, 1));
        Assert.Equal("offline", res.Error);
    }
}
