using Kaleidoscope.UI.Views;
using Sinan.App.Services;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>新建总项目弹窗：code-behind 只把 VM 事件翻成 DialogResult。</summary>
public partial class ProjectCreateDialog : ChromeDialogWindow
{
    protected override bool UseLayeredWindow => false;

    private ProjectCreateDialogViewModel? _viewModel;

    public ProjectCreateDialog()
    {
        InitializeComponent();
    }

    /// <summary>由 DialogService 在 ShowDialog 前调用。</summary>
    public void Load(ProjectCreateRequest request)
    {
        _viewModel = new ProjectCreateDialogViewModel(request);
        DataContext = _viewModel;

        _viewModel.Created += (_, _) =>
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

    public ProjectCreateResult? Result => _viewModel?.Result;
}
