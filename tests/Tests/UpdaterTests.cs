using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using RobloxAccountManager.Core.Services;
using Xunit;

namespace RobloxAccountManager.Tests;

[Collection("native")]
public class UpdaterTests
{
    private const string Url = "https://github.com/engnyg/roblox-account-manager/releases/download/v3.1.0/RobloxAccountManagerV3-v3.1.0.zip";

    private static JsonNode Release(string tag = "v3.1.0", string url = Url, string name = "RobloxAccountManagerV3-v3.1.0.zip",
        bool prerelease = false, string? digest = null) =>
        JsonNode.Parse(new JsonObject
        {
            ["tag_name"] = tag, ["prerelease"] = prerelease, ["draft"] = false, ["body"] = "改進",
            ["assets"] = new JsonArray(new JsonObject { ["name"] = name, ["browser_download_url"] = url, ["size"] = 10, ["digest"] = digest }),
        }.ToJsonString())!;

    [Theory]
    [InlineData("v3.1.0", true, "3.1.0")]
    [InlineData("3.0", true, "3.0.0")]
    [InlineData("V3.2.1-beta.1", true, "3.2.1")]
    [InlineData("latest", false, "0.0.0")]
    [InlineData("", false, "0.0.0")]
    public void Tags_are_parsed_leniently(string tag, bool ok, string expected)
    {
        Assert.Equal(ok, Updater.TryParseVersion(tag, out var v));
        Assert.Equal(expected, v.ToString(3));
    }

    [Fact]
    public void Only_newer_stable_releases_with_a_trusted_zip_count()
    {
        var current = new Version(3, 0, 0);
        var info = Updater.Parse(Release(), current);
        Assert.NotNull(info);
        Assert.Equal(new Version(3, 1, 0), info!.Version);
        Assert.Equal("改進", info.Notes);

        Assert.Null(Updater.Parse(Release("v3.0.0"), current));                                  // 同版本
        Assert.Null(Updater.Parse(Release("v2.9.0"), current));                                  // 舊版
        Assert.Null(Updater.Parse(Release(prerelease: true), current));                          // 預覽版
        Assert.Null(Updater.Parse(Release(url: "https://evil.example/x.zip"), current));         // 來源不受信任
        Assert.Null(Updater.Parse(Release(url: "https://github.com/someone/else/releases/download/v3.1.0/RobloxAccountManagerV3.zip"), current));
        Assert.Null(Updater.Parse(Release(name: "notes.txt"), current));                         // 不是 zip
        Assert.Null(Updater.Parse(null, current));
    }

    [Fact]
    public async Task CheckAsync_reads_the_latest_release_and_swallows_network_errors()
    {
        var ok = new Fake(_ => Fake.Json(Release().ToJsonString()));
        Updater.HandlerOverride = ok;
        try
        {
            Assert.NotNull(await Updater.CheckAsync(new Version(3, 0, 0)));
            Assert.Contains("api.github.com/repos/engnyg/roblox-account-manager/releases/latest", ok.Urls[0]);

            Updater.HandlerOverride = new Fake(_ => new HttpResponseMessage(HttpStatusCode.NotFound));   // 還沒有任何 Release
            Assert.Null(await Updater.CheckAsync(new Version(3, 0, 0)));
            Updater.HandlerOverride = new Fake(_ => throw new HttpRequestException("offline"));
            Assert.Null(await Updater.CheckAsync(new Version(3, 0, 0)));
        }
        finally { Updater.HandlerOverride = null; }
    }

    private static byte[] MakeZip(bool withExe, bool nested)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var prefix = nested ? "pkg/" : "";
            if (withExe) { using var w = new StreamWriter(z.CreateEntry(prefix + Updater.ExeName).Open()); w.Write("exe"); }
            using var w2 = new StreamWriter(z.CreateEntry(prefix + "lib.dll").Open()); w2.Write("dll");
        }
        return ms.ToArray();
    }

    private static UpdateInfo Info(byte[] zip, string? sha) => new(new Version(3, 1, 0), "v3.1.0", "", Url, "x.zip", zip.Length, sha);

    [Fact]
    public async Task Download_verifies_sha256_and_finds_the_exe()
    {
        var zip = MakeZip(withExe: true, nested: true);
        var sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        Updater.HandlerOverride = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
        try
        {
            var dir = await Updater.DownloadAsync(Info(zip, sha));
            try { Assert.True(File.Exists(Path.Combine(dir, Updater.ExeName))); Assert.True(File.Exists(Path.Combine(dir, "lib.dll"))); }
            finally { Directory.Delete(Directory.GetParent(dir)!.FullName.EndsWith("files") ? Directory.GetParent(Directory.GetParent(dir)!.FullName)!.FullName : Directory.GetParent(dir)!.FullName, true); }

            await Assert.ThrowsAsync<InvalidDataException>(() => Updater.DownloadAsync(Info(zip, new string('0', 64))));     // 雜湊不符
            Updater.HandlerOverride = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(MakeZip(false, false)) });
            await Assert.ThrowsAsync<InvalidDataException>(() => Updater.DownloadAsync(Info(zip, null)));                    // 沒有主程式
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Updater.DownloadAsync(Info(zip, null) with { AssetUrl = "https://evil.example/a.zip" }));
        }
        finally { Updater.HandlerOverride = null; }
    }

    [Fact]
    public void Install_script_waits_for_the_process_copies_files_and_restarts()
    {
        var script = Updater.BuildScript(@"C:\Temp\RAMv3_update_ab\files", @"C:\Apps\RAM", 4242);
        Assert.Contains("PID eq 4242", script);
        Assert.Contains("robocopy \"C:\\Temp\\RAMv3_update_ab\\files\" \"C:\\Apps\\RAM\"", script);
        Assert.Contains("start \"\" \"C:\\Apps\\RAM\\RobloxAccountManagerV3.exe\"", script);
        Assert.True(script.IndexOf("robocopy") < script.IndexOf("start \"\""));
        Assert.Throws<InvalidOperationException>(() => Updater.BuildScript(@"C:\a&b", @"C:\Apps\RAM", 1));     // 特殊字元會讓腳本被注入
        Assert.Throws<InvalidOperationException>(() => Updater.BuildScript(@"C:\ok", @"C:\%TEMP%", 1));
    }

    [Fact]
    public async Task Install_script_really_replaces_files_and_restarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "ramtest_upd_" + Guid.NewGuid().ToString("N")[..8]);
        var work = Path.Combine(root, "work"); var src = Path.Combine(work, "files"); var inst = Path.Combine(root, "app");
        Directory.CreateDirectory(src); Directory.CreateDirectory(inst);
        var stub = Path.Combine(Environment.SystemDirectory, "whoami.exe");     // 立刻結束的替身「主程式」
        File.Copy(stub, Path.Combine(src, Updater.ExeName));
        File.WriteAllText(Path.Combine(src, "new.txt"), "v2");
        File.WriteAllText(Path.Combine(inst, "old.txt"), "keep");               // 安裝資料夾裡不在 zip 的檔案不能被刪
        File.WriteAllText(Path.Combine(inst, "new.txt"), "v1");
        var cmd = Path.Combine(root, "run.cmd");
        File.WriteAllText(cmd, Updater.BuildScript(src, inst, 999999));         // 不存在的 PID：不用等
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { CreateNoWindow = true, UseShellExecute = false })!;
            Assert.True(p.WaitForExit(30000));
            Assert.Equal("v2", File.ReadAllText(Path.Combine(inst, "new.txt")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(inst, "old.txt")));
            Assert.True(File.Exists(Path.Combine(inst, Updater.ExeName)));
            for (int i = 0; i < 20 && Directory.Exists(work); i++) await Task.Delay(250);
            Assert.False(Directory.Exists(work));                               // 暫存的更新檔被清掉
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Development_builds_do_not_self_update()
    {
        Assert.False(Updater.IsInstalledBuild(@"C:\src\App\bin\Debug\net10.0-windows\"));
        Assert.True(Updater.IsInstalledBuild(@"C:\Users\me\Apps\RobloxAccountManagerV3\"));
        Assert.True(new AppSettings().AutoCheckUpdates);
    }
}
