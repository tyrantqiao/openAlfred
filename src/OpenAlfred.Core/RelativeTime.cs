namespace OpenAlfred.Core;

/// <summary>iOS 风格相对时间：刚刚 / n 分钟前 / 今天 HH:mm / 昨天 HH:mm / MM/dd。</summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset time, DateTimeOffset? now = null)
    {
        now ??= DateTimeOffset.Now;
        var diff = now.Value - time;

        if (diff.TotalMinutes < 1) return "刚刚";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} 分钟前";

        var today = now.Value.Date;
        if (time.Date == today) return $"今天 {time:HH:mm}";
        if (time.Date == today.AddDays(-1)) return $"昨天 {time:HH:mm}";
        if (time.Date == today.AddDays(-2)) return $"前天 {time:HH:mm}";
        if (time.Year == now.Value.Year) return time.ToString("M月d日");
        return time.ToString("yyyy/M/d");
    }

    /// <summary>文件大小：12 KB / 3.4 MB。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{(long)size} B" : $"{size:0.#} {units[unit]}";
    }
}
