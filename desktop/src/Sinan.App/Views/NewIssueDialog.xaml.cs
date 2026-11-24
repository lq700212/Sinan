using System.Windows;
using Kaleidoscope.UI.Views;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 新建问题弹窗。code-behind 只把 ViewModel 的 Saved/Cancelled 事件翻译成
/// DialogResult，让主壳知道"是否要跳到新问题详情"。
/// </summary>
public partial class NewIssueDialog : ChromeDialogWindow
{
    private readonly NewIssueViewModel _viewModel;

    public NewIssueDialog()
    {
        InitializeComponent();

        _viewModel = new NewIssueViewModel(App.Instance);
        DataContext = _viewModel;

        _viewModel.Saved += OnSaved;
        _viewModel.Cancelled += OnCancelled;

        Loaded += (_, _) => FocusFirstInput();
    }

    /// <summary>创建成功的问题 ID；DialogResult=true 时主壳用它直接打开详情。</summary>
    public string? CreatedIssueId { get; private set; }

    private void OnSaved(object? sender, string issueId)
    {
        CreatedIssueId = issueId;
        DialogResult = true;
        Close();
    }

    private void OnCancelled(object? sender, System.EventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void FocusFirstInput()
    {
        // 焦点交给问题描述以外的第一项：机台号已预填，直接让用户写问题更顺手。
        // 用 MoveFocus 避免在 code-behind 里持有控件字段引用。
        MoveFocus(new System.Windows.Input.TraversalRequest(
            System.Windows.Input.FocusNavigationDirection.Next));
    }
}
