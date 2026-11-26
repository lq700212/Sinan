using System.Windows;
using Kaleidoscope.UI.Views;
using Sinan.App.Services;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 删除问题确认弹窗。code-behind 只翻译 Confirmed/Cancelled 为 DialogResult；
/// "必须手抄问题号才可确认"的判断在 ViewModel 的 CanExecute 里。
/// </summary>
public partial class DeleteIssueDialog : ChromeDialogWindow
{
    /// <summary>
    /// 关掉分层窗口：WPF 分层窗会全局禁用 ClearType，弹窗里 9.5~11.5px 的中文
    /// （字段说明、错误提示、冲突明细）会糊。改走非分层 + SetWindowRgn 物理圆角。
    /// </summary>
    protected override bool UseLayeredWindow => false;

    private DeleteIssueDialogViewModel? _viewModel;

    public DeleteIssueDialog()
    {
        InitializeComponent();
    }

    public void Load(DeleteIssueRequest request)
    {
        _viewModel = new DeleteIssueDialogViewModel(request);
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
}
