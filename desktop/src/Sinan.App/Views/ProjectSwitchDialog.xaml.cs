using System.Windows.Controls;
using Kaleidoscope.UI.Views;
using Sinan.App.Services;
using Sinan.App.ViewModels;
using Sinan.Core.Models;

namespace Sinan.App.Views;

/// <summary>
/// 切换总项目弹窗：code-behind 只做 glue（DTO → VM、选择事件 → DialogResult）。
/// 关闭分层窗口，保留 ClearType（desktop/AGENTS.md 4.3）。
/// </summary>
public partial class ProjectSwitchDialog : ChromeDialogWindow
{
    protected override bool UseLayeredWindow => false;

    private ProjectSwitchDialogViewModel? _viewModel;

    public ProjectSwitchDialog()
    {
        InitializeComponent();
    }

    /// <summary>由 DialogService 在 ShowDialog 前调用。</summary>
    public void Load(ProjectSwitchRequest request)
    {
        _viewModel = new ProjectSwitchDialogViewModel(request);
        DataContext = _viewModel;

        _viewModel.Selected += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        _viewModel.Cancelled += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        _viewModel.CreateNewRequested += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }

    public ProjectSwitchResult? Result => _viewModel?.Result;

    /// <summary>点一行即切换：不再额外点"确定"，符合现场少一步操作的习惯。</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        if (sender is ListBox { SelectedItem: ProjectSummary summary })
            _viewModel.Select(summary);
    }
}
