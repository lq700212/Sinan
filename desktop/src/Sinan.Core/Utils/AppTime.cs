using System.Globalization;

namespace Sinan.Core.Utils;

/// <summary>
/// 全局时间口径：数据库存 UTC ISO-8601，本地展示按系统时区。
/// 业务日期（due_date）用 DateOnly，只有"日"没有时区，避免跨端差一天。
/// </summary>
public static class AppTime
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static string UtcNowIso() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string LocalNowIso() =>
        DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public static string TodayIso() => Today.ToString("yyyy-MM-dd");

    public static DateTime? ParseIso(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            return utc;
        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var local))
            return DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        return null;
    }

    public static DateTime? ToLocal(string? iso) => ParseIso(iso)?.ToLocalTime();

    public static string Display(string? iso, string format = "MM-dd HH:mm")
    {
        var local = ToLocal(iso);
        return local?.ToString(format, CultureInfo.InvariantCulture) ?? "—";
    }

    public static string DisplayDate(DateOnly? date, string format = "MM-dd") =>
        date?.ToString(format, CultureInfo.InvariantCulture) ?? "—";

    public static string DisplayDate(string? dateText, string format = "MM-dd")
    {
        if (DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, out var d))
            return d.ToString(format, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(dateText) ? "—" : dateText;
    }

    /// <summary>把 DateOnly 降级为 ISO 字符串入库。</summary>
    public static string? ToDb(DateOnly? date) => date?.ToString("yyyy-MM-dd");

    public static DateOnly? FromDb(string? value) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>相对今天的自然语言，用于详情页"距计划日已过 22 天"。</summary>
    public static string RelativeToToday(DateOnly? date)
    {
        if (!date.HasValue) return "未排期";
        var days = Today.DayNumber - date.Value.DayNumber;
        return days switch
        {
            > 0 => $"已过 {days} 天",
            0 => "今天到期",
            _ => $"还有 {-days} 天"
        };
    }
}
