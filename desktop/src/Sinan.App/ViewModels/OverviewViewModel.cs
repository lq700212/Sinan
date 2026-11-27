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
        _data = app.Projects.GetOverview(app.ProjectsContext.CurrentId);

        OpenSubprojectCommand = new RelayCommand(p => _nav.NavigateToSubproject(p as string));
        OpenIssueCommand = new RelayCommand(p => _nav.NavigateToIssue(p as string ?? ""));
        GoDashboardCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageDashboard));
        GoIssuesCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageIssues));
        GoExchangeCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageExchange));
        GoReportCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageReport));
        GoSettingsCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageSettings));
        // 头卡"进行中"右侧的总项目切换入口：用户在这里换项目或新建项目。
        SwitchProjectCommand = new RelayCommand(() => _nav.OpenProjectSwitch());
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

    public RelayCommand SwitchProjectCommand { get; }

    /// <summary>
    /// 问题分布图例 + 环形图数据。段色按分项目顺序取主题键（01 主橙 / 02 亮橙 /
    /// 03 深橙 / 待确认 灰），与设计稿 projects 页图例一一对应。
    ///
    /// 注意：这里只在 VM 里给"主题键字符串"，不引用 WPF Brush，
    /// 具体画刷由 DonutChart 在渲染时从当前主题资源解析，明暗主题自动跟随。
    /// </summary>
    public List<DistributionRow> Distribution
    {
        get
        {
            var keys = new[] { "Brush.Primary", "Brush.Primary2", "Brush.Warn", "Brush.Muted" };
            var rows = new List<DistributionRow>();
            for (int i = 0; i < _data.Cards.Count; i++)
            {
                var card = _data.Cards[i];
                if (card.IssueCount <= 0) continue;
                rows.Add(new DistributionRow
                {
                    Label = card.IsUnassigned ? "待确认" : card.Code,
                    Detail = $"{card.MachineCount} 台 · {card.IssueCount} 条",
                    Value = card.IssueCount,
                    Percent = IssueCount == 0 ? 0 : card.IssueCount * 100d / IssueCount,
                    PaletteKey = card.IsUnassigned ? keys[3] : keys[Math.Min(i, 2)],
                    IsUnassigned = card.IsUnassigned
                });
            }
            return rows;
        }
    }

    /// <summary>
    /// 阶段填写率卡片内的"现场照片（演示）"四行：条宽按最大分项目归一化，
    /// 与设计稿 pa-row 的视觉比例一致（不按总量算，否则第一行永远不到 60%）。
    /// </summary>
    public List<PhotoRow> PhotoRows
    {
        get
        {
            int max = _data.PhotoBuckets.Count == 0 ? 1 : Math.Max(1, _data.PhotoBuckets.Max(p => p.Count));
            var rows = _data.PhotoBuckets
                .Select(p => new PhotoRow
                {
                    Label = p.Label == "待确认" ? "待确认" : p.Label,
                    Count = p.Count,
                    BarPercent = p.Count * 100d / max
                })
                .ToList();

            // 设计稿固定列四行（含"待确认 0"）；仓储在待确认照片为 0 时会跳过该桶，
            // 这里补齐只为对齐设计稿的图例结构，不改变任何统计结果。
            if (rows.Count > 0 && !rows.Any(r => r.Label == "待确认"))
            {
                rows.Add(new PhotoRow { Label = "待确认", Count = 0, BarPercent = 0 });
            }
            return rows;
        }
    }

    public string TotalIssuesText => $"{IssueCount} 条";
    public string PhotoFooterText => $"照片 {PhotoCount} 张 · {PhotoIssueCount} 条问题有附件";
    public string PhotoStripSummary => $"{PhotoCount} 张 · {PhotoIssueCount} 条";
    public string ImportNote => $"最后导入 {LastImportDisplay}";

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
    public int Value { get; set; }
    public double Percent { get; set; }
    public string PaletteKey { get; set; } = "Brush.Primary";
    public bool IsUnassigned { get; set; }

    public string PercentText => $"{Percent:0.#}%";
    public string BarWidth => Percent.ToString("0.##");
    public string DetailWithShare => $"{Detail} · {Percent:0.#}%";
}

/// <summary>阶段卡片底部照片条的一行（条宽按最大分项目归一化）。</summary>
public sealed class PhotoRow
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public double BarPercent { get; set; }
}
