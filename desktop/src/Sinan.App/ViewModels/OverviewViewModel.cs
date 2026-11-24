using System;
using System.Collections.Generic;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;

namespace Sinan.App.ViewModels;

/// <summary>
/// 项目总览（设计稿 p7 projects）：总项目头卡 → 分项目卡片 → 问题分布/阶段填写率/照片覆盖。
///
/// 所有数字来自 <see cref="Sinan.Data.Repositories.ProjectRepository.GetOverview"/>，
/// 页面不写死任何统计结果（AGENTS.md 第 2 节）。
/// 照片数固定带"演示值"标注：历史 Excel 没有附件，这 12 张是演示元数据。
/// </summary>
public sealed class OverviewViewModel : ObservableObject
{
    private readonly App _app;
    private readonly INavigationService _nav;
    private readonly ProjectOverview _data;
    private bool _isBusy;

    public OverviewViewModel(App app, INavigationService nav)
    {
        _app = app;
        _nav = nav;
        _data = app.Projects.GetOverview();

        OpenSubprojectCommand = new RelayCommand(p => _nav.NavigateToSubproject(p as string));
        OpenIssueCommand = new RelayCommand(p => _nav.NavigateToIssue(p as string ?? ""));
        GoDashboardCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageDashboard));
        GoIssuesCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageIssues));
        GoExchangeCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageExchange));
        GoReportCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageReport));
        GoSettingsCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageSettings));
    }

    public Project Project => _data.Project;
    public string ProjectCode => _data.Project.Code;
    public string ProjectName => _data.Project.Name;
    public string ProjectCustomer => string.IsNullOrWhiteSpace(_data.Project.Customer)
        ? "调试组自建" : _data.Project.Customer;
    public string StatusDisplay => _data.Project.StatusDisplay;

    public int SubprojectCount => _data.SubprojectCount;
    public int MachineCount => _data.MachineCount;
    public int IssueCount => _data.IssueCount;
    public int InvertedCount => _data.InvertedCount;
    public int PhotoCount => _data.PhotoCount;
    public int PhotoIssueCount => _data.PhotoIssueCount;
    public int UnassignedMachineCount => _data.UnassignedMachineCount;
    public int UnassignedIssueCount => _data.UnassignedIssueCount;
    public string LastImportDisplay => _data.LastImportDisplay;

    public string MachineIssueSummary => $"{SubprojectCount} 个分项目 · {MachineCount} 台设备 · {IssueCount} 条问题";
    public string PhotoSummary => $"照片 {PhotoCount} 张 · {PhotoIssueCount} 条问题有附件";
    public string UnassignedSummary => UnassignedIssueCount > 0
        ? $"{UnassignedMachineCount} 台机台号异常待归组"
        : "机台全部已归组";

    public List<SubprojectCard> Cards => _data.Cards;
    public List<StageFill> StageFills => _data.StageFills;
    public List<PhotoBucket> PhotoBuckets => _data.PhotoBuckets;

    /// <summary>
    /// 问题分布图例。WPF 没有 CSS conic-gradient，环形图用"分段堆叠条 + 图例"落地：
    /// 信息等价（每段条宽 = 该分项目问题占比），且不伪造第三方位图控件。
    /// </summary>
    public List<DistributionRow> Distribution => _data.Cards
        .Where(c => c.IssueCount > 0)
        .Select(c => new DistributionRow
        {
            Label = c.Code,
            Detail = $"{c.MachineCount} 台 · {c.IssueCount} 条",
            Percent = IssueCount == 0 ? 0 : c.IssueCount * 100d / IssueCount,
            IsUnassigned = c.IsUnassigned
        })
        .ToList();

    public string StageNote
    {
        get
        {
            var design = StageFills.FirstOrDefault(s => s.Code == StageCodes.Design);
            var done = StageFills.FirstOrDefault(s => s.Code == StageCodes.Assembly);
            return design == null
                ? ""
                : $"设计下图：责任人 {design.OwnerFilled}、计划 {design.DueFilled}。" +
                  $"完成情况 {done?.DoneCount ?? 0} / {IssueCount}，已闭环 {(IssueCount == 0 ? 0 : (done?.DoneCount ?? 0) * 100d / IssueCount):0.#}%。";
        }
    }

    public bool HasPhoto => PhotoCount > 0;
    public bool HasUnassigned => UnassignedIssueCount > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public RelayCommand OpenSubprojectCommand { get; }
    public RelayCommand OpenIssueCommand { get; }
    public RelayCommand GoDashboardCommand { get; }
    public RelayCommand GoIssuesCommand { get; }
    public RelayCommand GoExchangeCommand { get; }
    public RelayCommand GoReportCommand { get; }
    public RelayCommand GoSettingsCommand { get; }
}

public sealed class DistributionRow
{
    public string Label { get; set; } = "";
    public string Detail { get; set; } = "";
    public double Percent { get; set; }
    public bool IsUnassigned { get; set; }

    public string PercentText => $"{Percent:0.#}%";
    public string BarWidth => Percent.ToString("0.##");
    public string DetailWithShare => $"{Detail} · {Percent:0.#}%";
}
