using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;

namespace Sinan.App.ViewModels;

/// <summary>
/// 问题台账（设计稿 p2 issues）：筛选条 + 8 列清单 + 空状态。
///
/// 数据量：演示库 36 条，一次读入内存后本地筛选即可（不逐次打 SQL，
/// 键盘输入关键字不会造成界面卡顿）。筛选与排序规则集中在本类，
/// 保证列表顺序与工作台风险清单口径一致：倒挂优先 → 逾期天数降序 → 源表行号。
///
/// 分项目筛选用 Code 而不是 Id：卡片点击、导航服务、导入预览都拿 Code，
/// "待确认"没有真实 Id，用字符串可统一表达。
/// </summary>
public sealed class IssueListViewModel : ObservableObject
{
    public const string AllSubprojects = "全部分项目";
    public const string UnassignedSubproject = "待确认";
    public const string AllStatuses = "全部状态";
    public const string AllOwners = "全部责任人";

    private readonly App _app;
    private readonly INavigationService _nav;
    private readonly List<Issue> _source;

    private string _keyword = "";
    private string _selectedSubproject = AllSubprojects;
    private string _selectedStatus = AllStatuses;
    private string _selectedOwner = AllOwners;
    private bool _onlyInverted;
    private bool _onlyOverdue;
    private bool _onlyWithPhoto;
    private bool _includeClosed = true;
    private bool _showMoreFilters;
    private Issue? _selectedIssue;

    public IssueListViewModel(App app, INavigationService nav)
    {
        _app = app;
        _nav = nav;
        _source = app.Issues.All();

        var project = app.Projects.GetMain();
        var codes = project == null
            ? new List<string>()
            : app.Projects.ListSubprojects(project.Id).Select(s => s.Code).ToList();
        var subprojects = new List<string> { AllSubprojects };
        subprojects.AddRange(codes);
        if (_source.Any(i => i.SubprojectId == null)) subprojects.Add(UnassignedSubproject);
        SubprojectOptions = subprojects;

        StatusOptions = new[] { AllStatuses }.Concat(IssueStatuses.All).ToArray();

        var owners = _source
            .SelectMany(i => new[] { i.DesignOwner, i.AssemblyOwner })
            .SelectMany(TextSplit)
            .Where(n => n.Length > 0 && n != "/")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        OwnerOptions = new[] { AllOwners }.Concat(owners).ToArray();

        OpenIssueCommand = new RelayCommand(p => OpenIssue(p as string ?? _selectedIssue?.Id));
        ResetCommand = new RelayCommand(ResetFilters);
        RefreshCommand = new RelayCommand(Reload);
        ToggleMoreFiltersCommand = new RelayCommand(() => ShowMoreFilters = !ShowMoreFilters);
        SetTabCommand = new RelayCommand(SetTab);

        ApplyFilters();
    }

    public ObservableCollection<Issue> Items { get; } = new();
    public IReadOnlyList<string> SubprojectOptions { get; }
    public IReadOnlyList<string> StatusOptions { get; }
    public IReadOnlyList<string> OwnerOptions { get; }

    public string Keyword
    {
        get => _keyword;
        set
        {
            if (SetProperty(ref _keyword, value)) ApplyFilters();
        }
    }

    public string SelectedSubproject
    {
        get => _selectedSubproject;
        set
        {
            if (SetProperty(ref _selectedSubproject, value ?? AllSubprojects)) ApplyFilters();
        }
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetProperty(ref _selectedStatus, value ?? AllStatuses)) ApplyFilters();
        }
    }

    public string SelectedOwner
    {
        get => _selectedOwner;
        set
        {
            if (SetProperty(ref _selectedOwner, value ?? AllOwners)) ApplyFilters();
        }
    }

    public bool ShowMoreFilters
    {
        get => _showMoreFilters;
        set => SetProperty(ref _showMoreFilters, value);
    }

    public bool OnlyInverted
    {
        get => _onlyInverted;
        set
        {
            if (SetProperty(ref _onlyInverted, value)) ApplyFilters();
        }
    }

    public bool OnlyOverdue
    {
        get => _onlyOverdue;
        set
        {
            if (SetProperty(ref _onlyOverdue, value)) ApplyFilters();
        }
    }

    public bool OnlyWithPhoto
    {
        get => _onlyWithPhoto;
        set
        {
            if (SetProperty(ref _onlyWithPhoto, value)) ApplyFilters();
        }
    }

    public bool IncludeClosed
    {
        get => _includeClosed;
        set
        {
            if (SetProperty(ref _includeClosed, value)) ApplyFilters();
        }
    }

    public Issue? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (!SetProperty(ref _selectedIssue, value)) return;
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedIssue != null;

    public int TotalCount => _source.Count;
    public int VisibleCount => Items.Count;
    public int InvertedCount => _source.Count(i => i.IsInverted);
    public int PhotoIssueCount => _source.Count(i => i.PhotoCount > 0);
    public string ProjectCode => _app.Projects.GetMain()?.Code ?? "P7391";
    public string CountSummary => $"显示 {VisibleCount} / {TotalCount} 条";

    /// <summary>筛选卡面包屑：当前分项目（含"全部分项目"/"待确认"）。</summary>
    public string BreadcrumbSubproject => SelectedSubproject;

    /// <summary>胶囊 tab 的选中态；重置或改下拉时同步刷新。</summary>
    public bool IsAllTab => !OnlyInverted && !OnlyOverdue && !OnlyWithPhoto && !HasMoreFilters;
    public bool IsInvertedTab => OnlyInverted;
    public bool IsPhotoTab => OnlyWithPhoto;

    private bool HasMoreFilters =>
        SelectedSubproject != AllSubprojects ||
        SelectedStatus != AllStatuses ||
        SelectedOwner != AllOwners ||
        !IncludeClosed || !string.IsNullOrWhiteSpace(Keyword);

    public bool HasActiveFilter =>
        !string.IsNullOrWhiteSpace(Keyword) ||
        SelectedSubproject != AllSubprojects ||
        SelectedStatus != AllStatuses ||
        SelectedOwner != AllOwners ||
        OnlyInverted || OnlyOverdue || OnlyWithPhoto || !IncludeClosed;

    public string FilterHint => HasActiveFilter ? "已按条件筛选" : "显示全部在册问题";

    public bool IsEmpty => Items.Count == 0;
    public bool IsLibraryEmpty => _source.Count == 0;

    public string EmptyTitle => IsLibraryEmpty ? "台账还是空的" : "没有符合条件的问题";
    public string EmptyHint => IsLibraryEmpty
        ? "去「数据交换」导入 Excel 原表，或在顶栏新建第一条问题。"
        : "可以放宽筛选条件，或点「重置」回到全部问题。";

    public RelayCommand OpenIssueCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleMoreFiltersCommand { get; }
    public RelayCommand SetTabCommand { get; }

    /// <summary>胶囊 tab：all / inverted / photo。三个互斥，等价于只打开对应筛选。</summary>
    private void SetTab(object? parameter)
    {
        var tab = parameter as string ?? "all";
        _onlyInverted = tab == "inverted";
        _onlyOverdue = false;
        _onlyWithPhoto = tab == "photo";
        OnPropertiesChanged(nameof(OnlyInverted), nameof(OnlyOverdue), nameof(OnlyWithPhoto));
        ApplyFilters();
    }

    /// <summary>供导航服务按分项目预筛（总览卡片点击、待确认卡片点击）。</summary>
    public void SetSubprojectFilter(string? subprojectCode)
    {
        _selectedSubproject = string.IsNullOrWhiteSpace(subprojectCode)
            ? AllSubprojects
            : subprojectCode.Trim();
        OnPropertyChanged(nameof(SelectedSubproject));
        ApplyFilters();
    }

    public void Reload()
    {
        _source.Clear();
        _source.AddRange(_app.Issues.All());
        OnPropertyChanged(nameof(TotalCount));
        ApplyFilters();
    }

    private void OpenIssue(string? issueId)
    {
        if (!string.IsNullOrWhiteSpace(issueId)) _nav.NavigateToIssue(issueId);
    }

    private void ResetFilters()
    {
        _keyword = "";
        _selectedSubproject = AllSubprojects;
        _selectedStatus = AllStatuses;
        _selectedOwner = AllOwners;
        _onlyInverted = false;
        _onlyOverdue = false;
        _onlyWithPhoto = false;
        _includeClosed = true;
        _showMoreFilters = false;
        OnPropertiesChanged(nameof(Keyword), nameof(SelectedSubproject), nameof(SelectedStatus),
            nameof(SelectedOwner), nameof(OnlyInverted), nameof(OnlyOverdue), nameof(OnlyWithPhoto),
            nameof(IncludeClosed), nameof(ShowMoreFilters));
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        IEnumerable<Issue> query = _source;

        if (!string.IsNullOrWhiteSpace(_keyword))
        {
            var keyword = _keyword.Trim();
            query = query.Where(i =>
                Contains(i.Title, keyword) ||
                Contains(i.Solution, keyword) ||
                Contains(i.MachineCode, keyword) ||
                Contains(i.SeqNo, keyword) ||
                Contains(i.DesignOwner, keyword) ||
                Contains(i.AssemblyOwner, keyword));
        }

        if (SelectedSubproject == UnassignedSubproject)
        {
            query = query.Where(i => i.SubprojectId == null);
        }
        else if (SelectedSubproject != AllSubprojects)
        {
            query = query.Where(i => i.SubprojectCode == SelectedSubproject);
        }

        if (SelectedStatus != AllStatuses)
            query = query.Where(i => i.Status == SelectedStatus);

        if (SelectedOwner != AllOwners)
        {
            query = query.Where(i =>
                TextSplit(i.DesignOwner).Contains(SelectedOwner, StringComparer.Ordinal) ||
                TextSplit(i.AssemblyOwner).Contains(SelectedOwner, StringComparer.Ordinal));
        }

        if (OnlyInverted) query = query.Where(i => i.IsInverted);
        if (OnlyOverdue) query = query.Where(i => i.OverdueDays > 0);
        if (OnlyWithPhoto) query = query.Where(i => i.PhotoCount > 0);
        if (!IncludeClosed) query = query.Where(i => !i.IsClosed);

        // 排序口径与工作台风险清单一致：倒挂最危险 → 逾期最久 → 源表顺序。
        var list = query
            .OrderByDescending(i => i.IsInverted)
            .ThenByDescending(i => i.OverdueDays)
            .ThenBy(i => i.SourceRow ?? int.MaxValue)
            .ThenBy(i => i.SeqNo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Items.Clear();
        foreach (var issue in list) Items.Add(issue);

        SelectedIssue = Items.FirstOrDefault();
        OnPropertiesChanged(nameof(VisibleCount), nameof(CountSummary), nameof(HasActiveFilter),
            nameof(FilterHint), nameof(IsEmpty), nameof(IsLibraryEmpty), nameof(EmptyTitle),
            nameof(EmptyHint), nameof(BreadcrumbSubproject), nameof(IsAllTab), nameof(IsInvertedTab),
            nameof(IsPhotoTab));
    }

    private static IEnumerable<string> TextSplit(string? owners) =>
        (owners ?? "")
            .Split(new[] { '/', '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim())
            .Where(n => n.Length > 0);

    private static bool Contains(string? source, string keyword) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
