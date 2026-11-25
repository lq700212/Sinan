using Sinan.Core.Utils;
using Xunit;

namespace Sinan.Tests.Core;

/// <summary>密码哈希：PBKDF2 存储与固定时间比较。</summary>
public class PasswordHasherTests
{
    [Fact]
    public void Hash_Then_Verify_成功()
    {
        var hash = PasswordHasher.Hash("123456");
        Assert.StartsWith("pbkdf2$", hash);
        Assert.True(PasswordHasher.Verify("123456", hash));
    }

    [Fact]
    public void Verify_错误密码_失败()
    {
        var hash = PasswordHasher.Hash("123456");
        Assert.False(PasswordHasher.Verify("654321", hash));
        Assert.False(PasswordHasher.Verify("123456 ", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Hash_同一密码两次结果不同_盐随机()
    {
        var a = PasswordHasher.Hash("123456", iterations: 1000);
        var b = PasswordHasher.Hash("123456", iterations: 1000);
        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("123456", a));
        Assert.True(PasswordHasher.Verify("123456", b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain-text")]
    [InlineData("pbkdf2$abc$xx$yy")]
    [InlineData("pbkdf2$999$xx$yy")]        // 迭代次数过低
    [InlineData("md5$1000$xx$yy")]          // 算法不认识
    public void Verify_损坏或未知格式_失败(string? stored)
    {
        Assert.False(PasswordHasher.Verify("123456", stored));
    }

    [Fact]
    public void Hash_空密码_抛异常()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(""));
    }

    [Fact]
    public void Sha256Hex_固定值()
    {
        // "abc" 的 SHA-256 标准测试向量
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            PasswordHasher.Sha256Hex("abc"));
    }

    [Fact]
    public void NewToken_长度与唯一性()
    {
        var a = PasswordHasher.NewToken();
        var b = PasswordHasher.NewToken();
        Assert.Equal(48, a.Length); // 24 字节 → 48 位十六进制
        Assert.NotEqual(a, b);
    }
}
