using System.Windows;
using Kaleidoscope.UI.Views;
using Sinan.App.Services;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 查看现场照片弹窗。code-behind 只负责装配 ViewModel；
/// 图片读取、缺失说明、翻页可用性都在 ViewModel。
/// </summary>
public partial class PhotoViewerDialog : ChromeDialogWindow
{
    /// <summary>
    /// 关掉分层窗口：WPF 分层窗会全局禁用 ClearType，弹窗里 9.5~11.5px 的中文
    /// （字段说明、错误提示、冲突明细）会糊。改走非分层 + SetWindowRgn 物理圆角。
    /// </summary>
    protected override bool UseLayeredWindow => false;

    private PhotoViewerViewModel? _viewModel;

    public PhotoViewerDialog()
    {
        InitializeComponent();
    }

    public void Load(PhotoViewerRequest request)
    {
        _viewModel = new PhotoViewerViewModel(request);
        DataContext = _viewModel;
    }
}
