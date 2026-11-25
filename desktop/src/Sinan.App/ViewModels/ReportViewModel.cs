using System;
using System.Collections.Generic;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;

namespace Sinan.App.ViewModels;

/// <summary>
/// 统计报表（设计稿 report 页）：4 张 KPI + 责任人任务量 + 计划时间分布
/// + 阶段填写率 / 照片分布 / 本期结论。
///
/// 全部数字来自 <see cref="Sinan.Data.Repositories.ProjectRepository.GetReport"/>；
/// 页面只负责排版，不在这里二次计算百分比，避免与总览/工作台出现两套口径。
/// 演示附件在界面上必须标注"演示值"，不能伪装成现场真实照片。
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
    public int SubprojectCount => _data.SubprojectCount;
    public int UnassignedMachineCount => _data.UnassignedMachineCount;
    public int UnassignedIssueCount => _data.UnassignedIssueCount;
    public int StageTaskCount => _data.StageTaskCount;
    public int TextPlanCount => _data.TextPlanCount;

    public string PhotoLinkedRateText => $"{_data.PhotoLinkedRate:0.#}%";
    public string AsOfDisplay => $"截至 {DateTime.Now:yyyy-MM-dd HH:mm}";
    public string AsOfDay => DateTime.Now.ToString("yyyy-MM-dd");

    public string ClosedRateText => $"{(TotalIssues == 0 ? 0 : ClosedCount * 100d / TotalIssues):0.#}%";
    public string InvertedRateText => $"{(TotalIssues == 0 ? 0 : InvertedCount * 100d / TotalIssues):0.#}%";
    public string PhotoPerIssueText => TotalIssues == 0 ? "0" : $"{PhotoCount * 1d / TotalIssues:0.0}";

    // ---- KPI 副标题（设计稿 k-s 一行） ----
    public string IssueSubtitle => $"{UnassignedIssueCount} 条待归组 · {SubprojectCount} 个分项目";
    public string MachineSubtitle => $"{TotalMachines} 台 · {UnassignedMachineCount} 台待归组";
    public string InvertedSubtitle => $"占 {InvertedRateText}";
    public string PhotoSubtitle => $"{PhotoIssueCount} 条问题 · 关联 {PhotoLinkedRateText}";

    public List<StageFill> StageFills => _data.StageFills;
    public List<OwnerLoad> OwnerLoads => _data.OwnerLoads;
    public List<SubprojectCard> Subprojects => _data.Subprojects;
    public List<PhotoBucket> PhotoBuckets => _data.PhotoBuckets;
    public List<CommonIssue> CommonIssues => _data.CommonIssues;
    public List<PlanDateBucket> PlanDateBars => _data.PlanDateBuckets;

    public bool HasCommonIssues => CommonIssues.Count > 0;
    public int OwnerTotal => OwnerLoads.Count;

    /// <summary>责任人负载条：阶段任务数换算成相对最高者的百分比（仓储 Ratio 即此值）。</summary>
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

    /// <summary>最高负载占比：用来判断"负载均衡 / 集中"，阈值取 30%（一人占全部阶段任务三成）。</summary>
    private double TopOwnerShare => StageTaskCount == 0 || OwnerLoads.Count == 0
        ? 0
        : OwnerLoads[0].StageTaskCount * 100d / StageTaskCount;

    public string OwnerBadgeText => TopOwnerShare >= 30 ? "负载集中" : "负载均衡";
    public string OwnerBadgeKind => TopOwnerShare >= 30 ? "warn" : "ok";

    public string OwnerSummaryText => OwnerLoads.Count == 0
        ? "还没有责任人数据。"
        : $"合计 {StageTaskCount} 个阶段任务 · {OwnerLoads[0].Name} {OwnerLoads[0].StageTaskCount} 项（{TopOwnerShare:0.#}%）";

    public string OwnerFooter => $"{OwnerTotal} 位责任人";

    /// <summary>阶段填写率卡底部补充：设计阶段的责任人 / 计划分开计数（设计稿同位置文案）。</summary>
    public string DesignStageDetail =>
        StageFills.FirstOrDefault(s => s.Code == "design")?.Detail ?? "设计阶段暂无数据";

    /// <summary>照片分布合计行（设计稿最后一行"合计"）。</summary>
    public PhotoBucket PhotoTotalRow => new()
    {
        Label = "合计",
        Count = PhotoCount,
        IssueCount = PhotoIssueCount,
        Ratio = PhotoCount == 0 ? 0 : 100
    };

    public string PhotoFooter => $"关联率 {PhotoIssueCount} / {TotalIssues} = {PhotoLinkedRateText}；历史 Excel 无附件";

    // ---- 本期结论（3 条，全部由数据拼出来，不写死） ----
    public string Conclusion1 => _data.TopStatusText;

    public string Conclusion2 => _data.InvertedPlanDateText;

    public string Conclusion3
    {
        get
        {
            var top = OwnerLoads.FirstOrDefault();
            if (top == null) return $"照片关联仅 {PhotoLinkedRateText}。";
            return $"{top.Name} {top.StageTaskCount} 项为最高负载（{TopOwnerShare:0.#}%）；照片关联仅 {PhotoLinkedRateText}。";
        }
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand GoExchangeCommand { get; }
    public RelayCommand GoIssuesCommand { get; }
    public RelayCommand OpenSubprojectCommand { get; }

    private void Refresh()
    {
        _data = _app.Projects.GetReport();
        OnPropertiesChanged(nameof(TotalIssues), nameof(TotalMachines), nameof(InvertedCount),
            nameof(OverdueCount), nameof(ClosedCount), nameof(PhotoCount), nameof(PhotoIssueCount),
            nameof(SubprojectCount), nameof(UnassignedMachineCount), nameof(UnassignedIssueCount),
            nameof(StageTaskCount), nameof(TextPlanCount),
            nameof(PhotoLinkedRateText), nameof(AsOfDisplay), nameof(AsOfDay), nameof(ClosedRateText),
            nameof(InvertedRateText), nameof(PhotoPerIssueText), nameof(IssueSubtitle),
            nameof(MachineSubtitle), nameof(InvertedSubtitle), nameof(PhotoSubtitle),
            nameof(StageFills), nameof(OwnerLoads), nameof(Subprojects), nameof(PhotoBuckets),
            nameof(CommonIssues), nameof(PlanDateBars), nameof(HasCommonIssues), nameof(OwnerTotal),
            nameof(OwnerBars), nameof(OwnerBadgeText), nameof(OwnerBadgeKind), nameof(OwnerSummaryText),
            nameof(OwnerFooter), nameof(DesignStageDetail), nameof(PhotoTotalRow), nameof(PhotoFooter),
            nameof(Conclusion1), nameof(Conclusion2), nameof(Conclusion3));
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
