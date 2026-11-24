using System.Windows.Controls;

namespace Sinan.App.Views;

/// <summary>问题详情页。全部交互走 <see cref="ViewModels.IssueDetailViewModel"/> 的命令。</summary>
public partial class IssueDetailPage : UserControl
{
    public IssueDetailPage() => InitializeComponent();
}
