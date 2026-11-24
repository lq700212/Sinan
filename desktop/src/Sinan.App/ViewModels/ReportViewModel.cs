using System;
using System.Collections.Generic;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;

namespace Sinan.App.ViewModels;

/// <summary>
/// 统计报表（设计稿 p4 report）：阶段完成率、责任人负载、分项目对比、照片覆盖。
///
/// 全部数字来自 <see cref="Sinan.Data.Repositories.ProjectRepository.GetReport"/>；
/// 页面只负责排版，不在这里二次计算百分比，避免与总览/工作台出现两套口径。
/// </summary>
public sealed class ReportViewModel : ObservableObject
{
    private readonly App _app;
    private readonly INavigationService _nav;
    private ReportData _data;

    public ReportViewModel(App app, INavigationService nav)
    {
        _app = app;
        _nav = nav;
        _data = app.Projects.GetReport();

        RefreshCommand = new RelayCommand(Refresh);
        GoExchangeCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageExchange));
        GoIssuesCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageIssues));
        OpenSubprojectCommand = new RelayCommand(p => _nav.NavigateToIssues(p as string));
    }

    public int TotalIssues => _data.TotalIssues;
    public int TotalMachines => _data.TotalMachines;
    public int InvertedCount => _data.InvertedCount;
    public int OverdueCount => _data.OverdueCount;
    public int ClosedCount => _data.ClosedCount;
    public int PhotoCount => _data.PhotoCount;
    public int PhotoIssueCount => _data.PhotoIssueCount;
    public string PhotoLinkedRateText => $"{_data.PhotoLinkedRate:0.#}%";
    public string AsOfDisplay => $"统计时间 {_data.AsOfDisplay}";

    public string ClosedRateText => $"{(TotalIssues == 0 ? 0 : ClosedCount * 100d / TotalIssues):0.#}%";
    public string InvertedRateText => $"{(TotalIssues == 0 ? 0 : InvertedCount * 100d / TotalIssues):0.#}%";
    public string PhotoPerIssueText => TotalIssues == 0 ? "0" : $"{PhotoCount * 1d / TotalIssues:0.0}";

    public List<StageFill> StageFills => _data.StageFills;
    public List<OwnerLoad> OwnerLoads => _data.OwnerLoads;
    public List<SubprojectCard> Subprojects => _data.Subprojects;
    public List<PhotoBucket> PhotoBuckets => _data.PhotoBuckets;
    public List<CommonIssue> CommonIssues => _data.CommonIssues;

    public bool HasCommonIssues => CommonIssues.Count > 0;
    public int OwnerTotal => OwnerLoads.Count;

    /// <summary>
    /// 责任人负载条：把阶段任务数换算成相对最高者的百分比（仓储 Ratio 即此值）。
    /// 直接用于 ProgressBar 的 Value，最大 100。
    /// </summary>
    public List<OwnerBar> OwnerBars => OwnerLoads
        .Take(12)
        .Select(o => new OwnerBar
        {
            Name = o.Name,
            TaskCount = o.StageTaskCount,
            IssueCount = o.IssueCount,
            OverdueCount = o.OverdueCount,
            Ratio = o.Ratio,
            Detail = $"{o.IssueCount} 条问题" + (o.OverdueCount > 0 ? $" · 逾期 {o.OverdueCount}" : "")
        })
        .ToList();

    public string OwnerFooter => OwnerLoads.Count == 0
        ? "还没有责任人数据。"
        : $"共 {OwnerTotal} 位责任人；最长条 = {OwnerLoads[0].StageTaskCount} 项，条形按相对最高者换算。";

    public RelayCommand RefreshCommand { get; }
    public RelayCommand GoExchangeCommand { get; }
    public RelayCommand GoIssuesCommand { get; }
    public RelayCommand OpenSubprojectCommand { get; }

    private void Refresh()
    {
        _data = _app.Projects.GetReport();
        OnPropertiesChanged(nameof(TotalIssues), nameof(TotalMachines), nameof(InvertedCount),
            nameof(OverdueCount), nameof(ClosedCount), nameof(PhotoCount), nameof(PhotoIssueCount),
            nameof(PhotoLinkedRateText), nameof(AsOfDisplay), nameof(ClosedRateText),
            nameof(InvertedRateText), nameof(PhotoPerIssueText), nameof(StageFills),
            nameof(OwnerLoads), nameof(Subprojects), nameof(PhotoBuckets), nameof(CommonIssues),
            nameof(HasCommonIssues), nameof(OwnerTotal), nameof(OwnerBars), nameof(OwnerFooter));
    }
}

public sealed class OwnerBar
{
    public string Name { get; set; } = "";
    public int TaskCount { get; set; }
    public int IssueCount { get; set; }
    public int OverdueCount { get; set; }
    public double Ratio { get; set; }
    public string Detail { get; set; } = "";
    public string TaskText => $"{TaskCount} 项";
}
