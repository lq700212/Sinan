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
