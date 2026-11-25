using Sinan.Core.Utils;
using Xunit;

namespace Sinan.Tests.Core;

/// <summary>
/// 原表日期解析：原表是"11.1 / 11.15 / 已下图 / / / 空"混排且无年份。
/// 第 9 节无损验收里"11.5 不得变成 2026-11-05 导出"，依赖解析与原文双字段。
/// </summary>
public class RawDateParserTests
{
    private const int Year = 2026;

    [Theory]
    [InlineData("11.5")]
    [InlineData("11-5")]
    [InlineData("11/5")]
    [InlineData("11月5日")]
    [InlineData(" 11.5 ")]
    public void Parse_无年份短日期_用基准年(string raw)
    {
        Assert.Equal(new DateOnly(2026, 11, 5), RawDateParser.Parse(raw, Year));
    }

    [Theory]
    [InlineData("2026.11.5", 2026, 11, 5)]
    [InlineData("2026-11-05", 2026, 11, 5)]
    [InlineData("2025.12.31", 2025, 12, 31)]
    [InlineData("2026年3月8日", 2026, 3, 8)]
    public void Parse_带年份_使用原文年份(string raw, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), RawDateParser.Parse(raw, 2030));
    }

    [Fact]
    public void Parse_两位数年份_按2000补齐()
    {
        Assert.Equal(new DateOnly(2026, 11, 5), RawDateParser.Parse("26.11.5", Year));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("／")]
    [InlineData("不适用")]
    [InlineData("N/A")]
    [InlineData("NA")]
    [InlineData("-")]
    [InlineData("—")]
    [InlineData("已下图")]
    [InlineData("待定")]
    [InlineData("未定")]
    [InlineData("下周")]
    public void Parse_非日期文本_返回空(string? raw)
    {
        Assert.Null(RawDateParser.Parse(raw, Year));
    }

    [Theory]
    [InlineData("13.1")]   // 月越界
    [InlineData("0.5")]    // 月为 0
    [InlineData("2.30")]   // 非闰年 2 月没有 30 日
    [InlineData("2026.2.29")] // 2026 不是闰年
    [InlineData("11.0")]   // 日为 0
    [InlineData("1999.1.1")] // 年份早于 2000
    [InlineData("2101.1.1")] // 年份晚于 2100
    public void Parse_非法日期_返回空(string raw)
    {
        Assert.Null(RawDateParser.Parse(raw, Year));
    }

    [Fact]
    public void Parse_闰年2月29日_合法()
    {
        Assert.Equal(new DateOnly(2024, 2, 29), RawDateParser.Parse("2024.2.29", Year));
    }

    [Fact]
    public void FormatRaw_按原表习惯输出月点日()
    {
        Assert.Equal("11.5", RawDateParser.FormatRaw(new DateOnly(2026, 11, 5)));
        Assert.Equal("1.1", RawDateParser.FormatRaw(new DateOnly(2026, 1, 1)));
        Assert.Equal("", RawDateParser.FormatRaw(null));
    }

    [Fact]
    public void FormatRaw_与Parse互逆()
    {
        var date = new DateOnly(2026, 11, 15);
        var raw = RawDateParser.FormatRaw(date);
        Assert.Equal("11.15", raw);
        Assert.Equal(date, RawDateParser.Parse(raw, Year));
    }

    [Fact]
    public void IsNotApplicable_只认明确的斜杠语义()
    {
        Assert.True(RawDateParser.IsNotApplicable("/"));
        Assert.True(RawDateParser.IsNotApplicable("／"));
        Assert.True(RawDateParser.IsNotApplicable("不适用"));
        Assert.True(RawDateParser.IsNotApplicable("N/A"));
        Assert.False(RawDateParser.IsNotApplicable(""));
        Assert.False(RawDateParser.IsNotApplicable(null));
        Assert.False(RawDateParser.IsNotApplicable("已下图"));
    }

    [Fact]
    public void MeansDrawingIssued_已下图算已完成()
    {
        Assert.True(RawDateParser.MeansDrawingIssued("已下图"));
        Assert.True(RawDateParser.MeansDrawingIssued("已下"));
        Assert.False(RawDateParser.MeansDrawingIssued("待定"));
        Assert.False(RawDateParser.MeansDrawingIssued("11.5"));
        Assert.False(RawDateParser.MeansDrawingIssued(null));
    }

    [Fact]
    public void IsBlank_空白与斜杠区分()
    {
        Assert.True(RawDateParser.IsBlank(""));
        Assert.True(RawDateParser.IsBlank("  "));
        Assert.False(RawDateParser.IsBlank("/")); // 斜杠不是空白，是"不适用"
    }
}
