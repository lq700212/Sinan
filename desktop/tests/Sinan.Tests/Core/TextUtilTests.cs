using Sinan.Core.Utils;
using Xunit;

namespace Sinan.Tests.Core;

/// <summary>
/// 机台号识别与人员拆分。机台号规则见根 AGENTS.md 第 5 节第 5 条：
/// P7391 + 3 位数字 + 可选 -序号；P73913118（多一位）是已知笔误，不得静默改正。
/// </summary>
public class TextUtilTests
{
    [Theory]
    [InlineData("P7391101", true)]
    [InlineData("P7391312", true)]
    [InlineData("P7391101-2", true)]
    [InlineData("p7391101", true)]  // 规则不区分大小写，原文另行保留
    [InlineData(" P7391101 ", true)]
    [InlineData("P73913118", false)] // 已知笔误：4 位数字
    [InlineData("P7301101", false)]
    [InlineData("P739110", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsStrictMachineCode_严格规则(string? code, bool expected)
    {
        Assert.Equal(expected, TextUtil.IsStrictMachineCode(code));
    }

    [Fact]
    public void LooksLikeMachineCode_宽松规则放过一位笔误()
    {
        Assert.True(TextUtil.LooksLikeMachineCode("P73913118"));
        Assert.True(TextUtil.LooksLikeMachineCode("P7391101"));
        Assert.False(TextUtil.LooksLikeMachineCode("随便写的"));
    }

    [Theory]
    [InlineData("P7391106", 106)]
    [InlineData("P7391106-2", 106)]
    [InlineData("P7391312", 312)]
    [InlineData("P73913118", 3118)] // 宽松解析：从第 6 位起取数字；不用于归属
    [InlineData("", null)]
    [InlineData("abc", null)]
    public void MachineSeq_提取排序序号(string? code, int? expected)
    {
        Assert.Equal(expected, TextUtil.MachineSeq(code));
    }

    [Theory]
    [InlineData("P7391-01", "P7391")]
    [InlineData("P7391101", "P7391101")]
    [InlineData("", "")]
    public void MachineBaseCode_去掉短横后缀(string code, string expected)
    {
        Assert.Equal(expected, TextUtil.MachineBaseCode(code));
    }

    [Theory]
    [InlineData("张工/李工", 2)]
    [InlineData("张工／李工", 2)]      // 全角斜杠
    [InlineData("张工、李工", 2)]       // 顿号
    [InlineData("张工，李工", 2)]       // 中文逗号
    [InlineData("张工,李工", 2)]        // 英文逗号
    [InlineData("张工 / 李工 / 王工", 3)]
    [InlineData("张工//李工", 2)]       // 空段被忽略
    [InlineData("张工/张工", 1)]        // 去重
    [InlineData("—", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void SplitOwners_多人拆分(string? raw, int count)
    {
        Assert.Equal(count, TextUtil.SplitOwners(raw).Count);
    }

    [Fact]
    public void Normalize_全角转半角并去零宽字符()
    {
        Assert.Equal("P7391101", TextUtil.Normalize("Ｐ７３９１１０１"));
        Assert.Equal("/", TextUtil.Normalize("／"));
        Assert.Equal("11.5", TextUtil.Normalize("\u200B11.5"));
    }

    [Fact]
    public void Truncate_超长才加省略号()
    {
        Assert.Equal("12345", TextUtil.Truncate("12345", 5));
        Assert.Equal("1234…", TextUtil.Truncate("123456", 4));
        Assert.Equal("", TextUtil.Truncate(null, 4));
    }

    [Theory]
    [InlineData("张工", "张")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Initial_取首字(string? name, string expected)
    {
        Assert.Equal(expected, TextUtil.Initial(name));
    }
}
