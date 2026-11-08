using System.Text.RegularExpressions;
using RobloxAccountManager.Core.Models;

namespace RobloxAccountManager.Core.Services;

public enum TargetKind { None, Place, Job, Private, Follow }

/// <summary>一個帳號要加入的目標：遊戲 ID / 遊戲網址 / Job ID 連結 / 私人伺服器連結 / 追蹤某位玩家。</summary>
public sealed record LaunchTarget(TargetKind Kind, long? PlaceId = null, string JobId = "", string LinkCode = "", long FollowUserId = 0)
{
    public static readonly LaunchTarget None = new(TargetKind.None);
}

public static partial class AccountTargets
{
    public const string TargetField = "Target";
    public const string FollowField = "FollowUser";

    [GeneratedRegex(@"^\d{3,}$")] private static partial Regex Digits();
    [GeneratedRegex(@"/games/(\d+)")] private static partial Regex GamesPath();
    [GeneratedRegex(@"[?&]placeId=(\d+)", RegexOptions.IgnoreCase)] private static partial Regex PlaceParam();

    /// <summary>解析使用者輸入的目標文字。</summary>
    public static LaunchTarget Parse(string? text)
    {
        var raw = (text ?? "").Trim().Trim('"', '\'', '<', '>').Trim();
        if (raw.Length == 0) return LaunchTarget.None;

        // 私人伺服器（VIP / 分享連結）
        var (pPlace, pCode) = PrivateServer.ParseUrl(raw);
        if (pCode is not null) return new LaunchTarget(TargetKind.Private, pPlace, LinkCode: pCode);

        // 純數字 = Place ID
        if (Digits().IsMatch(raw)) return new LaunchTarget(TargetKind.Place, long.Parse(raw));

        // Job ID（單純 UUID 或帶 gameInstanceId 的連結）
        var (jobPlace, job) = JoinTarget.Parse(raw);
        if (job.Length > 0 && Guid.TryParse(job, out _)) return new LaunchTarget(TargetKind.Job, jobPlace, job);

        // 一般遊戲網址
        var m = GamesPath().Match(raw);
        if (!m.Success) m = PlaceParam().Match(raw);
        if (m.Success) return new LaunchTarget(TargetKind.Place, long.Parse(m.Groups[1].Value));

        return LaunchTarget.None;
    }

    public static LaunchTarget Get(Account acc)
    {
        if (acc.Fields.TryGetValue(FollowField, out var f) && long.TryParse(f, out var uid) && uid > 0)
            return new LaunchTarget(TargetKind.Follow, FollowUserId: uid);
        return acc.Fields.TryGetValue(TargetField, out var t) ? Parse(t) : LaunchTarget.None;
    }

    public static void Set(Account acc, string? targetText, long followUserId = 0)
    {
        if (string.IsNullOrWhiteSpace(targetText)) acc.Fields.Remove(TargetField);
        else acc.Fields[TargetField] = targetText.Trim();
        if (followUserId > 0) acc.Fields[FollowField] = followUserId.ToString();
        else acc.Fields.Remove(FollowField);
    }

    public static string Describe(LaunchTarget t) => t.Kind switch
    {
        TargetKind.Place => $"Place {t.PlaceId}",
        TargetKind.Job => $"Job {t.JobId[..Math.Min(8, t.JobId.Length)]}…",
        TargetKind.Private => "Private server",
        TargetKind.Follow => $"Follow {t.FollowUserId}",
        _ => "",
    };
}
