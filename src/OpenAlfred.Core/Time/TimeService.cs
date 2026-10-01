using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenAlfred.Core.Time;

public enum TimeStatus
{
    Ok,
    UnknownZone,
    BadOffset,
}

public readonly record struct TimeInfo(
    TimeStatus Status,
    DateTimeOffset Now,
    TimeZoneInfo Zone,
    string ZoneLabel,
    string? Error);

/// <summary>
/// 时间查询服务：本机时间、城市/时区查询、相对时间换算（+3d / -2h）。
/// </summary>
public static partial class TimeService
{
    /// <summary>常用城市 → Windows 时区 Id。</summary>
    private static readonly Dictionary<string, string> CityZones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["北京"] = "China Standard Time",
        ["上海"] = "China Standard Time",
        ["东京"] = "Tokyo Standard Time",
        ["首尔"] = "Korea Standard Time",
        ["新加坡"] = "Singapore Standard Time",
        ["悉尼"] = "AUS Eastern Standard Time",
        ["纽约"] = "Eastern Standard Time",
        ["洛杉矶"] = "Pacific Standard Time",
        ["旧金山"] = "Pacific Standard Time",
        ["温哥华"] = "Pacific Standard Time",
        ["伦敦"] = "GMT Standard Time",
        ["巴黎"] = "Romance Standard Time",
        ["柏林"] = "W. Europe Standard Time",
        ["莫斯科"] = "Russian Standard Time",
        ["迪拜"] = "Arabian Standard Time",
        ["印度"] = "India Standard Time",
        ["new york"] = "Eastern Standard Time",
        ["los angeles"] = "Pacific Standard Time",
        ["london"] = "GMT Standard Time",
        ["paris"] = "Romance Standard Time",
        ["berlin"] = "W. Europe Standard Time",
        ["tokyo"] = "Tokyo Standard Time",
        ["sydney"] = "AUS Eastern Standard Time",
        ["dubai"] = "Arabian Standard Time",
        ["singapore"] = "Singapore Standard Time",
        ["shanghai"] = "China Standard Time",
        ["beijing"] = "China Standard Time",
    };

    [GeneratedRegex(@"^([+-]\d+)([ywdhms])$", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeRegex();

    /// <summary>解析 time 关键词后的参数。空参数返回本机时间。</summary>
    public static TimeInfo Resolve(string argument, DateTimeOffset? localNow = null)
    {
        var now = localNow ?? DateTimeOffset.Now;
        argument = argument.Trim();

        if (argument.Length == 0)
            return new TimeInfo(TimeStatus.Ok, now, TimeZoneInfo.Local, "本机", null);

        // 相对时间：+3d / -2h
        var relative = RelativeRegex().Match(argument);
        if (relative.Success)
        {
            var amount = int.Parse(relative.Groups[1].Value, CultureInfo.InvariantCulture);
            var shifted = relative.Groups[2].Value.ToLowerInvariant() switch
            {
                "y" => now.AddYears(amount),
                "w" => now.AddDays(amount * 7),
                "d" => now.AddDays(amount),
                "h" => now.AddHours(amount),
                "m" => now.AddMinutes(amount),
                "s" => now.AddSeconds(amount),
                _ => now,
            };
            return new TimeInfo(TimeStatus.Ok, shifted, TimeZoneInfo.Local,
                $"本机 {argument} 后", null);
        }

        // UTC±n 形式
        if (argument.StartsWith("UTC", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(argument[3..].Replace("+", "").Replace(" ", ""),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var utcOffset)
            && Math.Abs(utcOffset) <= 14)
        {
            var offset = TimeSpan.FromHours(utcOffset);
            var zone = TimeZoneInfo.CreateCustomTimeZone($"UTC{argument[3..]}", offset, argument, argument);
            return new TimeInfo(TimeStatus.Ok, now.ToOffset(offset), zone, argument, null);
        }
        if (string.Equals(argument, "UTC", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "GMT", StringComparison.OrdinalIgnoreCase))
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("UTC");
            return new TimeInfo(TimeStatus.Ok, now.ToOffset(TimeSpan.Zero), zone, "UTC", null);
        }

        // 城市别名
        if (CityZones.TryGetValue(argument, out var zoneId))
            return ResolveZoneId(zoneId, argument, now);

        // 直接尝试当作 Windows 时区 Id（如 "Eastern Standard Time"）
        return ResolveZoneId(argument, argument, now,
            new TimeInfo(TimeStatus.UnknownZone, now, TimeZoneInfo.Local, argument,
                $"未识别的时区或城市 \"{argument}\""));
    }

    private static TimeInfo ResolveZoneId(string zoneId, string label, DateTimeOffset now, TimeInfo? fallback = null)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return new TimeInfo(TimeStatus.Ok, TimeZoneInfo.ConvertTime(now, zone), zone, label, null);
        }
        catch (TimeZoneNotFoundException)
        {
            return fallback ?? new TimeInfo(TimeStatus.UnknownZone, now, TimeZoneInfo.Local, label,
                $"未识别的时区 \"{label}\"");
        }
    }

    /// <summary>本地化星期名（一二三四五六日）。</summary>
    public static string WeekdayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日",
    };

    /// <summary>相对本机的偏移描述，如「快 1 小时」「相同」。</summary>
    public static string OffsetVsLocal(TimeInfo info)
    {
        var diff = info.Now.Offset - TimeZoneInfo.Local.GetUtcOffset(info.Now.LocalDateTime);
        if (diff == TimeSpan.Zero) return "与本机相同";
        var ahead = diff > TimeSpan.Zero ? "快" : "慢";
        var abs = diff.Duration();
        var hours = (int)abs.TotalHours;
        var minutes = abs.Minutes;
        return minutes == 0 ? $"{ahead} {hours} 小时" : $"{ahead} {hours} 小时 {minutes} 分";
    }
}
