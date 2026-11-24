using System.Text.RegularExpressions;

namespace Sinan.Core.Utils;

/// <summary>
/// 原表日期文本解析。原表是"11.1 / 11.15 / 已下图 / / / 空"混排，没有年份。
///
/// 规则（对应调研文档 5.4 难点 5/6/7）：
/// - "11.5" / "11-5" / "11/5" / "11月5日" → 当年 11-05；
/// - "2026.11.5" / "2026-11-05" → 指定年份；
/// - "已下图"、"待定"、"/"、空白 → 无法解析，due_date 留空，原文照存；
/// - 导入向导可用文件名年份（20261123 → 2026）作基准年。
/// </summary>
public static partial class RawDateParser
{
    [GeneratedRegex(@"^\s*(\d{1,4})\s*[.\-/年]\s*(\d{1,2})(?:\s*[.\-/月]\s*(\d{1,2}))?\s*日?\s*$")]
    private static partial Regex DatePattern();

    public static DateOnly? Parse(string? raw, int baseYear)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = TextUtil.Normalize(raw);
        if (text is "/" or "／" or "不适用" or "N/A" or "NA" or "-" or "—") return null;
        if (text.Contains("已下", StringComparison.Ordinal) ||
            text.Contains("待定", StringComparison.Ordinal) ||
            text.Contains("未定", StringComparison.Ordinal)) return null;

        var m = DatePattern().Match(text);
        if (!m.Success) return null;

        int year;
        int month;
        int day;
        if (m.Groups[3].Success)
        {
            year = int.Parse(m.Groups[1].Value);
            month = int.Parse(m.Groups[2].Value);
            day = int.Parse(m.Groups[3].Value);
            if (year < 100) year += 2000;
        }
        else
        {
            year = baseYear;
            month = int.Parse(m.Groups[1].Value);
            day = int.Parse(m.Groups[2].Value);
        }

        if (year is < 2000 or > 2100) return null;
        if (month is < 1 or > 12) return null;
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
        return new DateOnly(year, month, day);
    }

    /// <summary>系统内录入日期时，原文格式化为原表习惯的 "11.5"。</summary>
    public static string FormatRaw(DateOnly? date) =>
        date.HasValue ? $"{date.Value.Month}.{date.Value.Day}" : "";

    public static bool IsNotApplicable(string? raw)
    {
        var text = TextUtil.Normalize(raw);
        return text is "/" or "／" or "不适用" or "N/A" or "NA";
    }

    public static bool IsBlank(string? raw) => string.IsNullOrWhiteSpace(raw);

    /// <summary>"已下图"这类文本：不是日期，但代表阶段实际已完成。</summary>
    public static bool MeansDrawingIssued(string? raw)
    {
        var text = TextUtil.Normalize(raw);
        return text.Contains("已下图", StringComparison.Ordinal) ||
               text.Contains("已下", StringComparison.Ordinal);
    }
}
