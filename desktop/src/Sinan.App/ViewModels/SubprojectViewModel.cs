using System;
using System.Collections.Generic;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;
using Sinan.Data.Repositories;

namespace Sinan.App.ViewModels;

/// <summary>
/// 分项目 · 设备问题预览（设计稿 p1 project）：左侧逐台设备列问题数与倒挂，
/// 右侧固定展示选中设备的全部问题点与现场照片。
///
/// 【为什么单独一页】设计稿把"设备清单 + 该设备问题点"放在同屏，避免现场
/// "进台账 → 再筛机台 → 再点回来"的来回跳。总览页点分项目卡片先到这里，
/// 看清"这台机器有哪几条问题"后再决定进台账或进详情。
///
/// 【数据口径】设备清单/问题数/倒挂/照片/装配责任人全部来自
/// <see cref="ProjectRepository.GetSubprojectDetail"/>；问题明细按机台号从
/// <see cref="Sinan.Data.Repositories.IssueRepository"/> 取一次快照缓存，
/// 切行不重复查库（同屏切 10 台设备不该产生 10 次全表查询）。
/// 照片是演示元数据（历史 Excel 无附件），界面必须带"演示值"标注。
/// </summary>
public sealed class SubprojectViewModel : ObservableObject
{
    private readonly INavigationService _nav;
    private readonly SubprojectDetail _data;

    /// <summary>按机台号分组的全量问题快照：构造时查一次，切行只做字典查找。</summary>
    private readonly Dictionary<string, List<Issue>> _issuesByMachine;

    private MachineSummary? _selectedMachine;
    private List<Issue> _selectedIssues = new();
    private List<PhotoThumb> _selectedPhotos = new();
    private bool _isBusy;

    public SubprojectViewModel(App app, INavigationService nav, string? subprojectCode)
    {
        _nav = nav;

        _data = app.Projects.GetSubprojectDetail(subprojectCode)
                ?? throw new InvalidOperationException($"分项目不存在：{subprojectCode}");

        // 一次全表快照 + 分组，切设备时零查询。
        // 排序口径与台账默认一致：倒挂优先 → 源表序号，两处看到同一顺序。
        _issuesByMachine = app.Issues.All()
            .GroupBy(i => i.MachineId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(i => i.IsInverted).ThenBy(i => i.SeqNo, SeqNoComparer.Instance).ToList());

        BackCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageOverview));
        OpenIssuesCommand = new RelayCommand(() =>
            _nav.NavigateToIssues(_data.IsUnassigned ? null : _data.Code));
        OpenIssueCommand = new RelayCommand(p => _nav.NavigateToIssue(p as string ?? ""));
        ExportCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageExchange));

        // 默认选中第一条有问题的设备：右侧不留空白，直接给出"这台机器的问题"。
        // 全都没问题时退回第一台，保持左右两栏都有内容可读。
        SelectedMachine = Machines.FirstOrDefault(m => m.IssueCount > 0) ?? Machines.FirstOrDefault();
    }

    public string Code => _data.Code;
    public string Name => _data.Name;
    public string MachineRange => _data.MachineRange;
    public int MachineCount => _data.MachineCount;
    public int IssueCount => _data.IssueCount;
    public int InvertedCount => _data.InvertedCount;
    public int PhotoCount => _data.PhotoCount;
    public int OwnerCount => _data.OwnerCount;
    public bool IsUnassigned => _data.IsUnassigned;

    /// <summary>
    /// 状态徽标文本：待确认分项目不能显示"进行中"（那是自相矛盾的），
    /// 与总览页 SubprojectCard.StatusDisplay 同口径。
    /// </summary>
    public string StatusDisplay => IsUnassigned ? "待确认" : "进行中";

    /// <summary>待确认用红色徽标，正常分项目用绿色，与总览页卡片一致。</summary>
    public string StatusChipTag => IsUnassigned ? "ng" : "ok";

    /// <summary>倒挂 &gt; 0 才显示红标，避免"倒挂 0 条"白占一行位置。</summary>
    public bool HasInverted => InvertedCount > 0;

    public string SummaryText => $"{MachineCount} 台设备 · {IssueCount} 条问题 · {OwnerCount} 位责任人";
    public string PhotoNote => $"照片 {PhotoCount} 张（演示值）";

    /// <summary>
    /// 头卡印章里的短码：设计稿 seal 只放分项目序号（"01"），不放整串 "P7391-01"，
    /// 46×46 的印章塞全串会被截成 "P7391…"。取连字符后的部分，待确认显示"待定"。
    /// </summary>
    public string SealText
    {
        get
        {
            if (IsUnassigned) return "待定";
            int dash = Code.LastIndexOf('-');
            return dash >= 0 && dash < Code.Length - 1 ? Code[(dash + 1)..] : Code;
        }
    }

    /// <summary>头卡红标文本；倒挂为 0 时该徽标整体隐藏（见 <see cref="HasInverted"/>）。</summary>
    public string InvertedText => $"倒挂 {InvertedCount} 条";

    public List<MachineSummary> Machines => _data.Machines;

    /// <summary>当前选中设备；切行时右侧标题/责任人/问题列表/照片条一起换。</summary>
    public MachineSummary? SelectedMachine
    {
        get => _selectedMachine;
        set
        {
            if (!SetProperty(ref _selectedMachine, value)) return;

            // 切行时重算三块内容并一次性通知，避免 XAML 里三次读属性各算一遍。
            _selectedIssues = value == null
                ? new List<Issue>()
                : _issuesByMachine.GetValueOrDefault(value.MachineId) ?? new List<Issue>();
            _selectedPhotos = BuildPhotos(_selectedIssues);

            OnPropertiesChanged(
                nameof(SelectedIssues), nameof(SelectedPhotos),
                nameof(HasSelectedMachine), nameof(SelectedTitle), nameof(SelectedIssueCountText),
                nameof(SelectedOwnerValue),
                nameof(SelectedPhotoText), nameof(SelectedInvertedText),
                nameof(SelectedHasInverted), nameof(SelectedHasIssues), nameof(SelectedHasPhotos),
                nameof(SelectedEmptyHint));
        }
    }

    public bool HasSelectedMachine => _selectedMachine != null;

    public string SelectedTitle => _selectedMachine == null
        ? "未选择设备"
        : _selectedMachine.Code;

    public string SelectedIssueCountText => _selectedMachine == null
        ? ""
        : $"{_selectedMachine.IssueCount} 条问题";

    /// <summary>装配责任人显示值：空时给"未填"，避免摘要条出现"装配责任人 "后面空一截。</summary>
    public string SelectedOwnerValue => string.IsNullOrWhiteSpace(_selectedMachine?.AssemblyOwners)
        ? "未填"
        : _selectedMachine!.AssemblyOwners;

    public string SelectedPhotoText => _selectedMachine == null
        ? ""
        : $"照片 {_selectedMachine.PhotoCount} 张（演示值）";

    public string SelectedInvertedText => _selectedMachine == null
        ? ""
        : $"倒挂 {_selectedMachine.InvertedCount} 条";

    public bool SelectedHasInverted => _selectedMachine?.InvertedCount > 0;
    public bool SelectedHasIssues => _selectedIssues.Count > 0;
    public bool SelectedHasPhotos => _selectedPhotos.Count > 0;

    /// <summary>设备存在但一条问题都没有时的提示语（空状态必须给话，不能留白）。</summary>
    public string SelectedEmptyHint => _selectedMachine == null
        ? "点左侧任意设备查看该机台的问题点"
        : "这台设备暂无问题记录，可直接新建问题";

    public IReadOnlyList<Issue> SelectedIssues => _selectedIssues;

    /// <summary>照片条（演示占位）。按"有照片的问题"展开，每条带问题序号便于对应。</summary>
    public IReadOnlyList<PhotoThumb> SelectedPhotos => _selectedPhotos;

    private static List<PhotoThumb> BuildPhotos(List<Issue> issues)
    {
        var result = new List<PhotoThumb>();
        foreach (var issue in issues.Where(i => i.PhotoCount > 0))
        {
            for (int n = 1; n <= issue.PhotoCount; n++)
            {
                result.Add(new PhotoThumb
                {
                    IssueId = issue.Id,
                    // 序号来自源表（文本），这里只做展示，不参与排序或计算。
                    SeqLabel = $"第 {issue.SeqNo} 项",
                    Index = $"{n}/{issue.PhotoCount}"
                });
            }
        }
        return result;
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand OpenIssuesCommand { get; }
    public RelayCommand OpenIssueCommand { get; }
    public RelayCommand ExportCommand { get; }
}

/// <summary>照片条里的一张（演示占位，不带真实图片文件）。</summary>
public sealed class PhotoThumb
{
    public string IssueId { get; set; } = "";
    public string SeqLabel { get; set; } = "";
    public string Index { get; set; } = "";
}

/// <summary>
/// 源表序号比较器：序号是文本（可能为空、可能是 "4" / "18" / "已下图"），
/// 能解析成整数的按数值排，解析不了的排后面且保持稳定，绝不因排序丢条目。
/// </summary>
public sealed class SeqNoComparer : IComparer<string>
{
    public static readonly SeqNoComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        bool xOk = int.TryParse(x, out int xi);
        bool yOk = int.TryParse(y, out int yi);
        if (xOk && yOk) return xi.CompareTo(yi);
        if (xOk) return -1;
        if (yOk) return 1;
        return string.CompareOrdinal(x ?? "", y ?? "");
    }
}
