using System.Windows;
using Kaleidoscope.UI.Views;
using Sinan.App.Services;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 更新阶段弹窗。code-behind 只做两件 glue：
/// 1) 把请求 DTO 交给 ViewModel，把 ViewModel 的 Saved/Cancelled 翻成 DialogResult；
/// 2) 暴露 <see cref="Result"/> 给 DialogService 取回结果。
/// 业务判断（必填、倒挂提示、审计原因）全部在 ViewModel。
///
/// 【为什么不给 ViewModel 加无参构造】请求里带阶段快照与问题号，缺了无法工作；
/// 宁可让 DialogService 显式调用 <see cref="Load"/>，也不做一个"半初始化"的构造。
/// </summary>
public partial class StageDialog : ChromeDialogWindow
{
    private StageDialogViewModel? _viewModel;

    /// <summary>
    /// 关掉分层窗口：WPF 分层窗会全局禁用 ClearType，弹窗里 9.5~11.5px 的中文
    /// （字段说明、错误提示、冲突明细）会糊。改走非分层 + SetWindowRgn 物理圆角。
    /// </summary>
    protected override bool UseLayeredWindow => false;

    public StageDialog()
    {
        InitializeComponent();
    }

    /// <summary>由 DialogService 在 ShowDialog 之前调用。</summary>
    public void Load(StageDialogRequest request)
    {
        _viewModel = new StageDialogViewModel(request);
        DataContext = _viewModel;

        _viewModel.Saved += OnSaved;
        _viewModel.Cancelled += OnCancelled;
    }

    /// <summary>用户确认保存后的阶段结果；取消时为 null。</summary>
    public StageDialogResult? Result => _viewModel?.Result;

    private void OnSaved(object? sender, System.EventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelled(object? sender, System.EventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
