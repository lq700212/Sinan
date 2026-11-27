using System;
using System.Windows.Media;
using System.Windows.Threading;
using Sinan.App.Mvvm;
using Sinan.App.Services;

namespace Sinan.App.ViewModels;

/// <summary>
/// 手机扫码配对弹窗（顶栏手机图标 / 设置页共用同一份 <see cref="PairingSession"/>）。
///
/// 为什么倒计时放在 VM 里每秒算：二维码 10 分钟一次性，弹窗必须让用户看到"还剩多久"，
/// 过期后立刻把二维码压暗并提示重新生成，避免现场反复扫一张已经失效的码。
/// 不自动续期：手机可能已经扫了但还没点确认，自动换码会让刚扫到的码当场失效。
/// </summary>
public sealed class PairQrDialogViewModel : ObservableObject, IDisposable
{
    private readonly PairingSession _session;
    private readonly DispatcherTimer _timer;
    private PairQrSnapshot? _snapshot;

    public PairQrDialogViewModel(PairingSession session)
    {
        _session = session;
        RegenerateCommand = new RelayCommand(Regenerate);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _snapshot = _session.Ensure();
    }

    public RelayCommand RegenerateCommand { get; }

    public ImageSource? QrImage => _snapshot?.QrImage;
    public string Code => _snapshot?.Code ?? "------";
    public string ServerUrl => _snapshot?.ServerUrl ?? "";
    public string RemainingText => _snapshot?.RemainingText ?? "配对码已失效，请重新生成";

    public bool IsExpired => _snapshot == null || _snapshot.ExpiresAt <= DateTimeOffset.UtcNow;

    /// <summary>过期遮罩文案：覆盖在二维码上，明确告诉现场"这张码不能再用"。</summary>
    public string ExpiredText => "已过期，请点下方「重新生成」";

    private void Tick()
    {
        OnPropertiesChanged(nameof(RemainingText), nameof(IsExpired));
    }

    private void Regenerate()
    {
        _snapshot = _session.Ensure(forceNew: true);
        OnPropertiesChanged(nameof(QrImage), nameof(Code), nameof(ServerUrl),
            nameof(RemainingText), nameof(IsExpired));
    }

    public void Dispose() => _timer.Stop();
}
