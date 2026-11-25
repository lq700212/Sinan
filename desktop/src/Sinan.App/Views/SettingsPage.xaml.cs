using System.Windows;
using System.Windows.Controls;
using Sinan.App.ViewModels;

namespace Sinan.App.Views;

/// <summary>
/// 系统设置页。路径、人员、主题全部来自 <see cref="SettingsViewModel"/>。
///
/// 这里是唯一的 View glue：左侧目录是同一页的滚动定位锚点，
/// 点「移动端配对」等目录项时把右侧对应卡片滚到内容区顶部，
/// 而不是隐藏其它卡片（设计稿所有卡片都在一页里纵向排列）。
/// </summary>
public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 初次绑定/容器生成阶段也会触发 SelectionChanged，此时模板尚未加载，
        // 滚动会算到 0 甚至抛异常；只响应用户在已加载页面上的真实选择。
        if (!IsLoaded || DataContext is not SettingsViewModel vm) return;

        var target = vm.SelectedSection switch
        {
            "data" => SectionData,
            "import" => SectionImport,
            "alert" => SectionAlert,
            "people" => SectionPeople,
            "mobile" => SectionMobile,
            "about" => SectionAbout,
            _ => null
        };
        if (target == null) return;

        // 卡片顶部对齐内容区顶部，留 8px 呼吸；BringIntoView 只保证"可见"，
        // 会把卡片滚到屏幕最下面，看起来像点错了。
        var y = target.TransformToAncestor(ContentScroll).Transform(new Point(0, 0)).Y;
        ContentScroll.ScrollToVerticalOffset(ContentScroll.VerticalOffset + y - 8);
    }
}
