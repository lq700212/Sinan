using System;
using System.Linq;
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
///
/// 2026-11-26 补反向联动：设置页比一屏长，用户手动滚动后左侧目录必须跟着高亮，
/// 否则会出现"内容停在提醒与逾期、左目录还亮着数据与备份"的错位（实机反馈）。
/// </summary>
public partial class SettingsPage : UserControl
{
    /// <summary>
    /// 点击目录触发的程序化滚动期间忽略 ScrollChanged 的选中回写。
    /// 例：点「关于与许可」时页面只能滚到底，视口顶部其实是「危险操作」，
    /// 不抑制的话目录会立刻跳回危险操作，用户以为点错了。
    /// </summary>
    private DateTime _suppressScrollSyncUntil = DateTime.MinValue;

    /// <summary>ScrollChanged 回写 SelectedSectionItem 时置位，阻止 OnSectionChanged 再把页面滚走。</summary>
    private bool _syncingFromScroll;

    public SettingsPage() => InitializeComponent();

    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 初次绑定/容器生成阶段也会触发 SelectionChanged，此时模板尚未加载，
        // 滚动会算到 0 甚至抛异常；只响应用户在已加载页面上的真实选择。
        if (!IsLoaded || _syncingFromScroll || DataContext is not SettingsViewModel vm) return;

        var target = ResolveSection(vm.SelectedSection);
        if (target == null) return;

        _suppressScrollSyncUntil = DateTime.UtcNow.AddMilliseconds(300);

        // 卡片顶部对齐内容区顶部，留 8px 呼吸；BringIntoView 只保证"可见"，
        // 会把卡片滚到屏幕最下面，看起来像点错了。
        // 必须用 StackPanel（滚动内容）坐标系：ScrollViewer 的 TransformToAncestor
        // 拿到的是内容坐标，不是视口坐标，直接拿它加当前 offset 会滚过头（实机踩过）。
        var y = target.TransformToAncestor(ContentStack).Transform(new Point(0, 0)).Y;
        ContentScroll.ScrollToVerticalOffset(Math.Max(0, y - 8));
    }

    /// <summary>
    /// 右侧内容滚动时反查当前卡片，左侧目录跟着高亮。
    /// 滚到顶/底直接给首尾项：最后一张「关于与许可」比视口矮，永远到不了顶部，
    /// 只按"卡片顶 &lt;= 16"判断会一直停在「危险操作」。
    /// </summary>
    private void OnContentScroll(object sender, ScrollChangedEventArgs e)
    {
        if (!IsLoaded || _syncingFromScroll || DataContext is not SettingsViewModel vm) return;
        if (DateTime.UtcNow < _suppressScrollSyncUntil) return;
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0) return;

        var key = ResolveCurrentSection();
        if (key == vm.SelectedSection) return;

        var item = vm.Sections.FirstOrDefault(s => s.Key == key);
        if (item == null) return;

        _syncingFromScroll = true;
        try
        {
            vm.SelectedSectionItem = item;
        }
        finally
        {
            _syncingFromScroll = false;
        }
    }

    private string ResolveCurrentSection()
    {
        var offset = ContentScroll.VerticalOffset;
        if (offset <= 0.5) return "data";
        if (ContentScroll.ScrollableHeight > 0 &&
            offset >= ContentScroll.ScrollableHeight - 1) return "about";

        var current = "data";
        foreach (var (key, element) in EnumerateSections())
        {
            // 内容坐标（相对 StackPanel）与滚动偏移比较；用 ScrollViewer 的
            // TranslatePoint 得到的是内容坐标，直接当视口坐标用会永远判成第一张卡。
            var y = element.TransformToAncestor(ContentStack).Transform(new Point(0, 0)).Y;
            if (y <= offset + 16) current = key;
            else break;
        }
        return current;
    }

    private FrameworkElement? ResolveSection(string key) => key switch
    {
        "data" => SectionData,
        "import" => SectionImport,
        "alert" => SectionAlert,
        "people" => SectionPeople,
        "mobile" => SectionMobile,
        "about" => SectionAbout,
        _ => null
    };

    /// <summary>页面内卡片的纵向顺序，必须与左侧目录的 6 项一一对应。</summary>
    private (string Key, FrameworkElement Element)[] EnumerateSections() => new (string, FrameworkElement)[]
    {
        ("data", SectionData),
        ("import", SectionImport),
        ("alert", SectionAlert),
        ("people", SectionPeople),
        ("mobile", SectionMobile),
        ("about", SectionAbout),
    };
}
