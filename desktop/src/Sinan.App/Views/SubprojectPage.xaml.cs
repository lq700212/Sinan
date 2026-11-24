using System.Windows.Controls;

namespace Sinan.App.Views;

/// <summary>
/// 分项目 · 设备问题预览页（设计稿 p1 project）。
/// code-behind 只留空壳：设备选中、问题列表、照片条全部走 ViewModel 绑定，
/// 没有窗口拖动/关闭确认这类必须放 code-behind 的 glue。
/// </summary>
public partial class SubprojectPage : UserControl
{
    public SubprojectPage()
    {
        InitializeComponent();
    }
}
