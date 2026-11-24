using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 问题台账页。code-behind 只做"双击行"这一件纯 UI 事件的转发，
/// 筛选/排序/打开详情的业务全部在 <see cref="IssueListViewModel"/>。
/// </summary>
public partial class IssueListPage : UserControl
{
    public IssueListPage() => InitializeComponent();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 双击列头或空白区域时不能误开详情：只在命中 ListViewItem 时转发。
        if (sender is not ListView list) return;
        if (list.SelectedItem is not Core.Models.Issue issue) return;
        if (ItemsControl.ContainerFromElement(list, (DependencyObject)e.OriginalSource) is not ListViewItem)
            return;

        if (DataContext is IssueListViewModel vm)
            vm.OpenIssueCommand.Execute(issue.Id);
    }
}
