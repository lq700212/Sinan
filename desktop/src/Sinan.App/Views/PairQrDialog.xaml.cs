using System.Windows;
using Kaleidoscope.UI.Views;
using Sinan.App.Services;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 手机扫码配对弹窗。code-behind 只做 glue：把 <see cref="PairingSession"/> 交给 VM，
/// 关闭时停掉倒计时；二维码生成、过期判断都在 VM / PairingSession 里。
/// </summary>
public partial class PairQrDialog : ChromeDialogWindow
{
    /// <summary>
    /// 关掉分层窗口：WPF 分层窗会全局禁用 ClearType，弹窗里的地址、配对码与说明
    /// 在 100% DPI 下会发虚（desktop/AGENTS.md 4.3）。改走非分层 + SetWindowRgn 物理圆角。
    /// </summary>
    protected override bool UseLayeredWindow => false;

    private PairQrDialogViewModel? _viewModel;

    public PairQrDialog()
    {
        InitializeComponent();
        Closed += (_, _) => _viewModel?.Dispose();
    }

    /// <summary>由 DialogService 在 ShowDialog 之前调用。</summary>
    public void Load(PairingSession session)
    {
        _viewModel = new PairQrDialogViewModel(session);
        DataContext = _viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
