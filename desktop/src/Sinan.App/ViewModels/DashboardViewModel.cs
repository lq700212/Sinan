using System;
using System.Collections.Generic;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;
using Sinan.Core.Utils;

namespace Sinan.App.ViewModels;

/// <summary>
/// 工作台（设计稿 p1 main）：一屏回答三个问题——有多少问题、卡在哪个阶段、谁的活最重。
///
/// 布局纪律：
/// - 顶部焦点卡只放"今天必须看的一件事"（第一条倒挂，没有倒挂则取最严重逾期）；
/// - KPI 全部来自 <see cref="Sinan.Data.Repositories.ProjectRepository.GetDashboard"/>；
/// - 风险清单可点，直接进对应问题详情。
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    private readonly App _app;
    private readonly INavigationService _nav;
    private readonly DashboardData _data;

    public DashboardViewModel(App app, INavigationService nav)
    {
        _app = app;
        _nav = nav;
        _data = app.Projects.GetDashboard(app.Session.User ?? new User());

        var allIssues = app.Issues.All();
        var unassigned = allIssues.Where(i => i.SubprojectId == null).ToList();
        UnassignedIssueCount = unassigned.Count;
        UnassignedMachineCount = unassigned.Select(i => i.MachineId).Distinct().Count();

        OpenIssueCommand = new RelayCommand(p => _nav.NavigateToIssue(p as string ?? ""));
        GoIssuesCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageIssues));
        GoReportCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageReport));
        GoExchangeCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageExchange));
        GoSettingsCommand = new RelayCommand(() => _nav.Navigate(ShellViewModel.PageSettings));
        OpenOverdueCommand = new RelayCommand(() => _nav.NavigateToIssues(null));
    }

    public int TotalIssues => _data.TotalIssues;
    public int TotalMachines => _data.TotalMachines;
    public int TotalSubprojects => _data.TotalSubprojects;
    public int InvertedCount => _data.InvertedCount;
    public int PhotoCount => _data.PhotoCount;
    public int PhotoIssueCount => _data.PhotoIssueCount;
    public int OverdueCount => _data.OverdueCount;
    public int ClosedCount => _data.ClosedCount;
    public int MyTodoCount => _data.MyTodoCount;
    public int PendingSyncCount => _data.PendingSyncCount;
    public string DataAsOfDisplay => _data.DataAsOfDisplay;

    public List<StageFill> StageFills => _data.StageFills;
    public List<OwnerLoad> OwnerLoads => _data.OwnerLoads.Take(11).ToList();
    public List<Issue> Risks => _data.Risks;
    public List<Issue> Recent => _data.Recent;
    public int OwnerTotal => _data.OwnerLoads.Count;
    public int StageTaskTotal => _data.StageTaskCount;
    public Issue? FocusIssue => _data.FocusIssue;

    /// <summary>未归入分项目的问题/机台数（KPI 副标题与"数据待处理"共用）。</summary>
    public int UnassignedIssueCount { get; }
    public int UnassignedMachineCount { get; }

    public string FocusTitle => InvertedCount > 0
        ? $"计划倒挂 {InvertedCount} 条"
        : OverdueCount > 0 ? $"逾期 {OverdueCount} 条" : "当前没有倒挂与逾期";

    public string FocusTag => InvertedCount > 0 ? "待校正" : OverdueCount > 0 ? "待跟进" : "状态正常";
    public string FocusTagKind => InvertedCount > 0 ? "ng" : OverdueCount > 0 ? "warn" : "ok";

    public string FocusDescription
    {
        get
        {
            var issue = FocusIssue;
            if (issue == null) return "台账为空或全部已闭环，可以去数据交换导入 Excel 原表。";
            if (issue.IsInverted)
            {
                return $"最早 {issue.SubprojectCode ?? "待确认"} / {issue.MachineCode}：设计 " +
                       $"{DueText(issue.DesignStage)} → 装配 {DueText(issue.AssemblyStage)}。" +
                       "设计计划晚于装配计划，按现有计划无法按期交付。";
            }
            return $"{issue.MachineCode} · {issue.Title}（逾期 {issue.OverdueDays} 天，最晚阶段 {issue.OverdueStageLabel}）。";
        }
    }

    public string StageFooter => $"完成情况 {ClosedCount} / {TotalIssues}，已闭环 " +
        $"{(TotalIssues == 0 ? 0 : ClosedCount * 100d / TotalIssues):0.#}%。";

    public string OwnerFooter
    {
        get
        {
            var top = _data.OwnerLoads.FirstOrDefault();
            if (top == null) return "还没有责任人数据。";
            var total = _data.OwnerLoads.Sum(o => o.StageTaskCount);
            var share = total == 0 ? 0 : top.StageTaskCount * 100d / total;
            return $"{top.Name} {top.StageTaskCount} 项（{share:0.#}%），共 {OwnerTotal} 位责任人参与。";
        }
    }

    /// <summary>数据体检清单（设计稿底部四条），每条都可跳转到处理位置。</summary>
    public List<HealthRow> HealthRows
    {
        get
        {
            var rows = new List<HealthRow>();
            if (InvertedCount > 0)
                rows.Add(new HealthRow
                {
                    Tag = "倒挂", Kind = "ng",
                    Title = $"{InvertedCount} 条计划时间倒挂",
                    Hint = string.Join(" / ", _data.Risks.Where(i => i.IsInverted)
                        .Select(i => i.SubprojectCode?.Replace("P7391-", "") ?? "??").Distinct().Take(6)),
                    Action = "去校正", Command = GoIssuesCommand
                });

            // 计划列写成"已下图"等非日期文本：设计稿第二条"文本"。
            // 只数原文等于"已下图"的计划单元格，与导入解析的 TextUtil 判据一致。
            var textAnomalies = _app.Issues.All()
                .SelectMany(i => i.Stages)
                .Count(s => (s.DueRaw ?? "").Trim() == "已下图");
            if (textAnomalies > 0)
                rows.Add(new HealthRow
                {
                    Tag = "文本", Kind = "warn",
                    Title = $"{textAnomalies} 处计划列写成“已下图”",
                    Hint = string.Join(" / ", _app.Issues.All()
                        .Where(i => i.Stages.Any(s => (s.DueRaw ?? "").Trim() == "已下图"))
                        .Select(i => i.SeqNo.PadLeft(3, '0')).Distinct().Take(6)),
                    Action = "去转换", Command = GoExchangeCommand
                });

            if (OverdueCount > 0)
                rows.Add(new HealthRow
                {
                    Tag = "逾期", Kind = "warn",
                    Title = $"{OverdueCount} 条问题已超过计划日期",
                    Hint = $"未关闭且有计划日；最早 {_data.Risks.FirstOrDefault(i => i.OverdueDays > 0)?.OverdueDays ?? 0} 天",
                    Action = "看清单", Command = OpenOverdueCommand
                });

            var material = StageFills.FirstOrDefault(s => s.Code == StageCodes.Material);
            if (material is { OwnerFilled: 0 })
                rows.Add(new HealthRow
                {
                    Tag = "缺口", Kind = "warn",
                    Title = "物料准备阶段整列未填责任人",
                    Hint = $"{material.Total} 条问题该阶段为空",
                    Action = "定规则", Command = GoSettingsCommand
                });

            var unassigned = _app.Issues.All().Count(i => i.SubprojectId == null);
            if (unassigned > 0)
                rows.Add(new HealthRow
                {
                    Tag = "归组", Kind = "warn",
                    Title = $"{unassigned} 条问题机台号未归入分项目",
                    Hint = string.Join("、", _app.Issues.All()
                        .Where(i => i.SubprojectId == null)
                        .Select(i => i.MachineCode).Distinct().Take(3)),
                    Action = "去归组", Command = GoIssuesCommand
                });

            if (rows.Count == 0)
                rows.Add(new HealthRow
                {
                    Tag = "正常", Kind = "ok", Title = "数据体检未发现异常",
                    Hint = "倒挂/逾期/空白阶段/未归组均为 0", Action = "看报表", Command = GoReportCommand
                });

            return rows;
        }
    }

    private static string IssueCountText(int count) => $"{count}";

    /// <summary>焦点卡里的计划显示：优先原表原文（11.5 / 已下图），没有才格式化语义日期。</summary>
    private static string DueText(IssueStage? stage)
    {
        if (stage == null) return "未排";
        if (!string.IsNullOrWhiteSpace(stage.DueRaw)) return stage.DueRaw;
        return stage.DueDate.HasValue ? stage.DueDate.Value.ToString("MM-dd") : "未排";
    }

    public RelayCommand OpenIssueCommand { get; }
    public RelayCommand GoIssuesCommand { get; }
    public RelayCommand GoReportCommand { get; }
    public RelayCommand GoExchangeCommand { get; }
    public RelayCommand GoSettingsCommand { get; }
    public RelayCommand OpenOverdueCommand { get; }
}

public sealed class HealthRow
{
    public string Tag { get; set; } = "";
    public string Kind { get; set; } = "warn";
    public string Title { get; set; } = "";
    public string Hint { get; set; } = "";
    public string Action { get; set; } = "查看";
    public System.Windows.Input.ICommand? Command { get; set; }
}
