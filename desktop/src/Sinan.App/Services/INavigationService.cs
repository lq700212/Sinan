namespace Sinan.App.Services;

/// <summary>
/// 页面导航契约。ViewModel 只能通过它切页，禁止自己 new Window（AGENTS.md 第 7 节）。
/// 实现方是 <c>ShellViewModel</c>；详情页返回也走 <see cref="Back"/>，
/// 由实现方维护简单导航栈（MVP 只需要"台账 → 详情 → 返回"一层）。
/// </summary>
public interface INavigationService
{
    /// <summary>切到主壳的六个固定页面之一（overview/dashboard/issues/exchange/report/settings）。</summary>
    void Navigate(string pageKey);

    /// <summary>打开某条问题的详情页。</summary>
    void NavigateToIssue(string issueId);

    /// <summary>打开问题台账并按分项目预筛；code 为空表示"全部"。</summary>
    void NavigateToIssues(string? subprojectCode);

    /// <summary>
    /// 打开"分项目 · 设备问题预览"页（设计稿 p1 project）：左侧设备清单、
    /// 右侧选中设备的问题点。总览页点分项目卡片走这里，不再直接跳台账。
    /// </summary>
    void NavigateToSubproject(string? subprojectCode);

    /// <summary>返回上一页；栈空时回工作台。</summary>
    void Back();

    /// <summary>当前页面 key，供侧栏高亮与"返回"判断。</summary>
    string CurrentKey { get; }

    /// <summary>
    /// 数据库发生写入（新建/推进/导入）后通知主壳刷新徽标与标题统计。
    /// 只更新壳层数字，不重建当前页面（详情页正在编辑时必须保住现场）。
    /// </summary>
    void NotifyDataChanged();
}
