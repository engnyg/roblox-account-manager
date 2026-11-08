using System.Diagnostics;
using RobloxAccountManager.Core.Native;
using Xunit;

namespace RobloxAccountManager.Tests;

[Collection("native")]
public class CrashHandlerTests
{
    [Fact]
    public void Kill_returns_zero_when_nothing_matches()
    {
        Assert.Equal(0, CrashHandler.Kill("RamTestNoSuchProcess_" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void Kill_ends_only_processes_with_the_exact_name()
    {
        // 用替身：把 ping.exe 複製成假的名稱再執行，絕不碰真正的 RobloxCrashHandler
        var dir = Path.Combine(Path.GetTempPath(), "ramtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var name = "RamFakeCrashHandler" + Guid.NewGuid().ToString("N")[..6];
        var fake = Path.Combine(dir, name + ".exe");
        var other = Path.Combine(dir, name + "Other.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), fake);
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), other);
        using var target = Process.Start(new ProcessStartInfo(fake, "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        using var bystander = Process.Start(new ProcessStartInfo(other, "-n 60 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            Assert.Equal(1, CrashHandler.Kill(name));
            Assert.True(target.WaitForExit(5000));
            Assert.False(bystander.HasExited);   // 名稱只是前綴相同的行程不能被誤殺
        }
        finally
        {
            try { if (!bystander.HasExited) bystander.Kill(); } catch { }
            try { if (!target.HasExited) target.Kill(); } catch { }
            bystander.WaitForExit(3000); target.WaitForExit(3000);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Setting_is_off_by_default()
    {
        Assert.False(new RobloxAccountManager.Core.Services.AppSettings().BlockCrashHandler);
    }
}
