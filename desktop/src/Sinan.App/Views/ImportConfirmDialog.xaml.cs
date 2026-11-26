using System.Windows;
using Kaleidoscope.UI.Views;
using Sinan.App.Services;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 导入确认 / 冲突处置弹窗（设计稿 dlg-import-conflict）。
/// code-behind 只做两件 glue：把请求 DTO 交给 ViewModel、把事件翻成 DialogResult；
/// 计数、冲突逐条决策、写入范围全部在 ViewModel。
/// </summary>
public partial class ImportConfirmDialog : ChromeDialogWindow
{
    /// <summary>
    /// 关掉分层窗口：WPF 分层窗会全局禁用 ClearType，弹窗里 9.5~11.5px 的中文
    /// （字段说明、错误提示、冲突明细）会糊。改走非分层 + SetWindowRgn 物理圆角。
    /// </summary>
    protected override bool UseLayeredWindow => false;

    private ImportConfirmDialogViewModel? _viewModel;

    public ImportConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>由 DialogService 在 ShowDialog 之前调用。</summary>
    public void Load(ImportConfirmRequest request)
    {
        _viewModel = new ImportConfirmDialogViewModel(request);
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

    /// <summary>用户点"确认写入"后的结果；"返回校验"时为 null。</summary>
    public ImportConfirmResult? Result => _viewModel?.BuildResult();
}
