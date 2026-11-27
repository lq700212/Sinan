using System;
using System.Linq;
using System.Windows.Media;
using Sinan.Core.Utils;
using Sinan.Data.Sync;

namespace Sinan.App.Services;

/// <summary>
/// 配对二维码的唯一会话（设置页与顶栏弹窗共用）。
///
/// 为什么不在两处各自 CreatePairCode：<see cref="SyncRepository.CreatePairCode"/>
/// 生成新码时会清掉旧的未使用码，两处各生成会让先显示的那张二维码立刻失效，
/// 现场表现就是"刚生成就提示配对码不存在"。
/// 所以由本会话持有当前未使用码，两个入口只读同一份快照。
/// </summary>
public sealed class PairingSession
{
    private readonly App _app;
    private PairCodeInfo? _code;
    private ImageSource? _qrImage;
    private string _payload = "";
    private string _serverUrl = "";

    public PairingSession(App app) => _app = app;

    public string Code => HasCode ? _code!.Code : "";
    public bool HasCode => _code != null && _code.ExpiresAt > DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt => HasCode ? _code!.ExpiresAt : null;
    public string Payload => _payload;
    public ImageSource? QrImage => HasCode ? _qrImage : null;
    public string ServerUrl => _serverUrl;

    /// <summary>
    /// 取当前二维码；没有未过期的码时生成一个新的。
    /// 服务未运行 / 没有局域网地址时返回 null，由调用方如实提示，不伪造二维码。
    /// </summary>
    /// <param name="forceNew">true = 用户点"重新生成"，作废旧码。</param>
    public PairQrSnapshot? Ensure(bool forceNew = false)
    {
        var server = _app.SyncServer;
        if (server is not { IsRunning: true }) return null;

        if (!forceNew && HasCode) return Snapshot();

        var url = server.Urls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var code = _app.Sync!.CreatePairCode();
            _code = code;
            _serverUrl = url;
            _payload = PairQrPayload.Build(url, code.Code);
            _qrImage = PairQrCode.Render(_payload);
            return Snapshot();
        }
        catch (Exception ex)
        {
            AppLog.Error("生成配对二维码失败", ex);
            return null;
        }
    }

    /// <summary>二维码过期后由页面/弹窗调用，清掉旧快照，避免继续展示一张已失效的码。</summary>
    public void Clear()
    {
        _code = null;
        _qrImage = null;
        _payload = "";
        _serverUrl = "";
    }

    public PairQrSnapshot? Snapshot()
    {
        if (!HasCode || _qrImage == null) return null;
        return new PairQrSnapshot
        {
            Code = _code!.Code,
            ServerUrl = _serverUrl,
            Payload = _payload,
            ExpiresAt = _code.ExpiresAt,
            QrImage = _qrImage
        };
    }
}

/// <summary>一次配对二维码的只读快照：弹窗与设置页都只读它，不各自持有码。</summary>
public sealed class PairQrSnapshot
{
    public string Code { get; init; } = "";
    public string ServerUrl { get; init; } = "";
    public string Payload { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public ImageSource QrImage { get; init; } = null!;

    /// <summary>剩余有效期文案（弹窗每秒重算，不缓存倒计时）。</summary>
    public string RemainingText
    {
        get
        {
            var left = ExpiresAt - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero) return "已过期，请重新生成";
            return $"剩余 {left.Minutes:00}:{left.Seconds:00} · 仅可使用一次";
        }
    }
}
