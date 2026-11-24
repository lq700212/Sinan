namespace Sinan.Core.Utils;

/// <summary>
/// 业务 ID 生成。客户端也要能离线新建（Android），所以 ID 不能依赖数据库自增：
/// 采用"前缀 + 时间 + 随机"的字符串主键，碰撞概率可忽略，且天然可排序。
/// </summary>
public static class IdGen
{
    public static string New(string prefix) =>
        $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..Math.Min(64, prefix.Length + 1 + 14 + 9)];

    public static string NewIssueId() => New("ISS");
    public static string NewAttachmentId() => New("ATT");
    public static string NewBatchId() => New("IMP");
    public static string NewCommentId() => New("CMT");
    public static string NewDeviceId() => New("DEV");

    /// <summary>6 位数字配对码，便于现场手工输入；只用数字避免 0/O、1/I 混淆。</summary>
    public static string NewPairCode()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);
        var chars = new char[6];
        for (var i = 0; i < 6; i++) chars[i] = (char)('0' + bytes[i] % 10);
        return new string(chars);
    }
}
