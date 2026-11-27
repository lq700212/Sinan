namespace Sinan.Core.Utils;

/// <summary>
/// 手机扫码配对的二维码载荷（契约：shared/api/sync-api.md 第 4.1 节）。
///
/// 只编码「服务器地址 + 一次性配对码」，不编码 token；手机端扫码后仍走
/// POST /api/pair 换 token，配对码的 10 分钟有效期与一次性语义完全不变。
/// </summary>
public static class PairQrPayload
{
    public const string Scheme = "sinan";
    public const string Host = "pair";
    public const int Version = 1;

    /// <summary>
    /// 生成形如 <c>sinan://pair?v=1&amp;url=http%3A%2F%2F192.168.0.89%3A5180&amp;code=824716</c>
    /// 的二维码文本。url 必须做 URL 编码，否则查询串里的 <c>:</c>、<c>/</c> 会被手机端解析截断。
    /// </summary>
    public static string Build(string baseUrl, string code)
    {
        var url = (baseUrl ?? "").Trim().TrimEnd('/');
        var pairCode = (code ?? "").Trim();
        return $"{Scheme}://{Host}?v={Version}" +
               $"&url={Uri.EscapeDataString(url)}" +
               $"&code={Uri.EscapeDataString(pairCode)}";
    }
}
