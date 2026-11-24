using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;

namespace Sinan.App.ViewModels;

/// <summary>
/// 主壳：顶栏 + 侧栏 6 项 + 页面容器 + 底部状态条。
///
/// 导航规则（AGENTS.md 第 8 节）：
/// - 侧栏固定 6 项，一屏只保留 1 个主操作（顶栏"＋ 新建问题"）；
/// - 页面 VM 缓存复用，切走再切回保留筛选/滚动；
/// - 详情页不进缓存（每次按 issueId 重新读取，避免看到过期数据）。
///
/// 开窗（登录/退出/新建弹窗）不在 VM 里 new Window，统一发事件给 code-behind。
/// </summary>
public sealed class ShellViewModel : ObservableObject, INavigationService
{
    public const string PageOverview = "overview";
    public const string PageDashboard = "dashboard";
    public const string PageIssues = "issues";
    public const string PageExchange = "exchange";
    public const string PageReport = "report";
    public const string PageSettings = "settings";

    private readonly App _app;
    private readonly Dictionary<string, object> _cache = new(StringComparer.Ordinal);
    private object? _currentPage;
    private string _currentKey = PageOverview;
    private string _title = "项目总览";
    private string _subtitle = "";
    private bool _canGoBack;

    public ShellViewModel(App app)
    {
        _app = app;

        var user = app.Session.User;
        UserName = user?.Name ?? "未登录";
        UserInitial = user?.Initial ?? "?";
        RoleDisplay = user?.RoleDisplay ?? "";
        IsAdmin = user?.IsAdmin == true;

        var issues = app.Issues.All();
        _invertedCount = issues.Count(i => i.IsInverted);
        _issueTotal = issues.Count;
        _photoCount = issues.Sum(i => i.PhotoCount);
        _machineCount = issues.Select(i => i.MachineId).Distinct().Count();
        _subprojectCount = issues.Where(i => i.SubprojectId != null)
            .Select(i => i.SubprojectId).Distinct().Count();

        NavItems = new ObservableCollection<NavItem>
        {
            new(PageOverview, "⌂", "项目总览"),
            new(PageDashboard, "◈", "工作台"),
            new(PageIssues, "▤", "问题台账") { Badge = _issueTotal },
            new(PageExchange, "⇥", "数据交换"),
            new(PageReport, "▦", "统计报表"),
            new(PageSettings, "⚙", "系统设置")
        };

        ProjectCode = app.Projects.GetMain()?.Code ?? "P7391";
        ProjectSubtitle = $"{ProjectCode} · {_subprojectCount} 个分项目 · {_machineCount} 台机台";
        DataAsOf = $"数据截至 {DateTime.Now:MM-dd HH:mm}";
        VersionText = $"v0.0.1 · 离线模式 · 库 {_app.Database.Paths.DbPath}";

        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? PageDashboard));
        RefreshCommand = new RelayCommand(RefreshCurrentPage);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        SignOutCommand = new RelayCommand(() => SignOutRequested?.Invoke(this, EventArgs.Empty));
        NewIssueCommand = new RelayCommand(() => NewIssueRequested?.Invoke(this, EventArgs.Empty));
        BackCommand = new RelayCommand(Back);

        // 设置页也能切主题：主题变化后顶栏图标同步（静态事件生命周期=应用，无需退订）。
        // 设置页自己的"当前主题/切换到…"文案也在这里一并刷新——该 VM 被 _cache 缓存复用，
        // 只在 ToggleTheme 里刷自己会漏掉"顶栏切主题"这条路径。
        ThemeManager.Changed += (_, _) =>
        {
            OnPropertiesChanged(nameof(ThemeIcon), nameof(ThemeTip));
            if (_cache.TryGetValue(PageSettings, out var cached) && cached is SettingsViewModel settings)
                settings.RefreshTheme();
        };

        Navigate(PageOverview);
    }

    public event EventHandler? SignOutRequested;
    public event EventHandler? NewIssueRequested;

    public ObservableCollection<NavItem> NavItems { get; }
    public string UserName { get; }
    public string UserInitial { get; }
    public string RoleDisplay { get; }
    public bool IsAdmin { get; }
    public string ProjectCode { get; }
    public string ProjectSubtitle { get; }
    public string DataAsOf { get; private set; }
    public string VersionText { get; }
    public int IssueTotal => _issueTotal;
    public int InvertedCount => _invertedCount;
    public int PhotoCount => _photoCount;
    public int SubprojectCount => _subprojectCount;
    public int MachineCount => _machineCount;

    private int _issueTotal;
    private int _invertedCount;
    private int _photoCount;
    private int _subprojectCount;
    private int _machineCount;

    public string InvertedBadge => $"{InvertedCount} 条计划倒挂";
    public bool HasInverted => InvertedCount > 0;

    public string ThemeIcon => ThemeManager.Current == ThemeKind.Dark ? "☀" : "☾";
    public string ThemeTip => ThemeManager.Current == ThemeKind.Dark ? "切换到浅色主题" : "切换到深色主题";

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        private set => SetProperty(ref _canGoBack, value);
    }

    public string CurrentKey => _currentKey;

    public RelayCommand NavigateCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand NewIssueCommand { get; }
    public RelayCommand BackCommand { get; }

    private readonly Stack<string> _history = new();

    public void Navigate(string pageKey)
    {
        if (!IsShellPage(pageKey)) pageKey = PageDashboard;
        if (_currentKey == pageKey && _currentPage != null && _history.Count == 0) return;

        if (_currentPage != null && _history.Count == 0) _history.Clear();
        OpenPage(pageKey, pushHistory: false);
    }

    public void NavigateToIssue(string issueId)
    {
        if (string.IsNullOrWhiteSpace(issueId)) return;
        var issue = _app.Issues.Get(issueId);
        if (issue == null)
        {
            _app.Dialogs.Alert("问题不存在", "该问题可能已被删除，请刷新台账。");
            return;
        }

        _history.Push(_currentKey);
        CanGoBack = true;
        SetActive(null);
        CurrentPage = new IssueDetailViewModel(_app, this, issue);
        Title = "问题详情";
        Subtitle = $"{issue.MachineCode} · 第 {issue.SeqNo} 项";
    }

    public void NavigateToIssues(string? subprojectCode)
    {
        if (_cache.TryGetValue(PageIssues, out var cached) && cached is IssueListViewModel list)
            list.SetSubprojectFilter(subprojectCode);
        Navigate(PageIssues);
    }

    /// <summary>
    /// 打开分项目设备问题预览页。不走 _cache：这一页强绑定某个分项目代码，
    /// 缓存会让"P7391-01 → P7391-02"复用旧 VM 显示错误数据。
    /// </summary>
    public void NavigateToSubproject(string? subprojectCode)
    {
        SubprojectViewModel page;
        try
        {
            page = new SubprojectViewModel(_app, this, subprojectCode);
        }
        catch (InvalidOperationException)
        {
            // 分项目代码对不上（例如种子被清空）：如实告知并留在总览页，不静默跳空页。
            _app.Dialogs.Alert("分项目不存在", $"找不到分项目「{subprojectCode}」，可能数据已变更，请刷新总览。");
            Navigate(PageOverview);
            return;
        }

        _history.Push(_currentKey);
        CanGoBack = true;
        SetActive(PageOverview);
        CurrentPage = page;
        Title = $"{page.Code} 分项目";
        Subtitle = $"{page.MachineCount} 台设备 · {page.IssueCount} 条问题 · 设备问题预览";
    }

    public void Back()
    {
        if (_history.Count == 0)
        {
            Navigate(PageIssues);
            return;
        }
        var key = _history.Pop();
        CanGoBack = _history.Count > 0;
        OpenPage(key, pushHistory: false);
    }

    /// <summary>数据变更（新建/推进/导入）后强制重建当前页，让统计立刻刷新。</summary>
    public void RefreshCurrentPage()
    {
        if (CurrentPage is IssueDetailViewModel)
        {
            Back();
            return;
        }
        _cache.Remove(_currentKey);
        OpenPage(_currentKey, pushHistory: false);
        OnPropertiesChanged(nameof(DataAsOf));
    }

    /// <summary>
    /// 壳层数字刷新：只在数据库写入后调用，不重建页面。
    /// 台账页标题里的"36 条 · 倒挂 5 条"也会跟着更新。
    /// </summary>
    public void NotifyDataChanged()
    {
        var issues = _app.Issues.All();
        _issueTotal = issues.Count;
        _invertedCount = issues.Count(i => i.IsInverted);
        _photoCount = issues.Sum(i => i.PhotoCount);
        _machineCount = issues.Select(i => i.MachineId).Distinct().Count();
        _subprojectCount = issues.Where(i => i.SubprojectId != null)
            .Select(i => i.SubprojectId).Distinct().Count();

        DataAsOf = $"数据截至 {DateTime.Now:MM-dd HH:mm}";
        OnPropertiesChanged(nameof(IssueTotal), nameof(InvertedCount), nameof(PhotoCount),
            nameof(MachineCount), nameof(SubprojectCount), nameof(InvertedBadge),
            nameof(HasInverted), nameof(ProjectSubtitle), nameof(DataAsOf));

        var badge = NavItems.FirstOrDefault(i => i.Key == PageIssues);
        if (badge != null) badge.Badge = _issueTotal;

        // 台账缓存里是旧的 Issue 快照（状态、照片数、阶段文字都会过期）。
        // 写操作后必须重读库，同时保留用户当前的搜索词与筛选条件。
        if (_cache.TryGetValue(PageIssues, out var cachedList) && cachedList is IssueListViewModel list)
            list.Reload();

        // 统计类页面一次性读库，缓存直接作废，下次进入时按新数据重建。
        _cache.Remove(PageOverview);
        _cache.Remove(PageDashboard);
        _cache.Remove(PageReport);

        // 详情页也挂在"问题台账"这个入口下，但它的标题是"问题详情"。
        // 保存阶段后 Reload 会走到这里，不能把详情页标题改成台账标题。
        if (_currentKey == PageIssues && CurrentPage is IssueListViewModel)
        {
            var (title, subtitle) = Describe(PageIssues);
            Title = title;
            Subtitle = subtitle;
        }
    }

    private void OpenPage(string pageKey, bool pushHistory)
    {
        if (pushHistory)
        {
            _history.Push(_currentKey);
            CanGoBack = true;
        }

        if (!_cache.TryGetValue(pageKey, out var page))
        {
            page = CreatePage(pageKey);
            _cache[pageKey] = page;
        }

        _currentKey = pageKey;
        CurrentPage = page;
        SetActive(pageKey);

        var (title, subtitle) = Describe(pageKey);
        Title = title;
        Subtitle = subtitle;
        DataAsOf = $"数据截至 {DateTime.Now:MM-dd HH:mm}";
    }

    private object CreatePage(string key) => key switch
    {
        PageOverview => new OverviewViewModel(_app, this),
        PageIssues => new IssueListViewModel(_app, this),
        PageExchange => new ExchangeViewModel(_app, this),
        PageReport => new ReportViewModel(_app, this),
        PageSettings => new SettingsViewModel(_app, this),
        _ => new DashboardViewModel(_app, this)
    };

    private (string Title, string Subtitle) Describe(string key) => key switch
    {
        PageOverview => ("项目总览", ProjectSubtitle),
        PageDashboard => ("工作台", $"{ProjectCode} · 今天必须看的一件事"),
        PageIssues => ("问题台账", $"{IssueTotal} 条 · 计划倒挂 {InvertedCount} 条"),
        PageExchange => ("数据交换", "Excel 原表进出 · 移动端配对"),
        PageReport => ("统计报表", "阶段完成率 · 责任人负载 · 照片覆盖"),
        PageSettings => ("系统设置", "本地数据 · 账号 · 关于"),
        _ => ("工作台", ProjectSubtitle)
    };

    private void SetActive(string? key)
    {
        foreach (var item in NavItems)
            item.IsActive = item.Key == key;
    }

    private void ToggleTheme()
    {
        ThemeManager.Toggle();
        OnPropertiesChanged(nameof(ThemeIcon), nameof(ThemeTip));
    }

    private static bool IsShellPage(string key) =>
        key is PageOverview or PageDashboard or PageIssues or PageExchange or PageReport or PageSettings;
}
