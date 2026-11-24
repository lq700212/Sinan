using System.Windows;
using Kaleidoscope.UI.Views;
using Sinan.App.Services;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 开始导出弹窗（设计稿 dlg-export）。code-behind 只做 glue：
/// 请求 DTO 交给 ViewModel、事件翻成 DialogResult、把"浏览目录"回调接上。
/// </summary>
public partial class ExportConfirmDialog : ChromeDialogWindow
{
    private ExportConfirmDialogViewModel? _viewModel;

    public ExportConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>由 DialogService 在 ShowDialog 之前调用。</summary>
    public void Load(ExportConfirmRequest request)
    {
        _viewModel = new ExportConfirmDialogViewModel(request);
        DataContext = _viewModel;

        _viewModel.Confirmed += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        _viewModel.Cancelled += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
    }

    /// <summary>用户确认后的文件名与"是否打开文件夹"；取消时为 null。</summary>
    public ExportConfirmResult? Result => _viewModel?.BuildResult();
}
