using System;
using System.Linq;

namespace Sinan.Core.Models;

/// <summary>总项目总览页的全部口径，一次查询装配，避免页面里散落 SQL。</summary>
public sealed class ProjectOverview
{
    public Project Project { get; set; } = new();
    public int SubprojectCount { get; set; }
    public int MachineCount { get; set; }
    public int IssueCount { get; set; }
    public int InvertedCount { get; set; }
    public int PhotoCount { get; set; }
    public int PhotoIssueCount { get; set; }
    public int UnassignedMachineCount { get; set; }
    public int UnassignedIssueCount { get; set; }
    public int StageTaskCount { get; set; }
    public List<SubprojectCard> Cards { get; set; } = new();
    public List<StageFill> StageFills { get; set; } = new();
    public List<PhotoBucket> PhotoBuckets { get; set; } = new();
    public string LastImportDisplay { get; set; } = "";
}

public sealed class SubprojectCard
{
    public string? SubprojectId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string MachineRange { get; set; } = "";
    public int MachineCount { get; set; }
    public int IssueCount { get; set; }
    public int InvertedCount { get; set; }
    public int PhotoCount { get; set; }
    public int OwnerCount { get; set; }
    public int FilledAssembly { get; set; }
    public bool IsUnassigned { get; set; }

    public string StatusDisplay => IsUnassigned ? "待确认" : "进行中";
    public string Summary => $"{MachineCount} 台 · {IssueCount} 条 · 照片 {PhotoCount}";
    public double Share => IssueCount;
}

public sealed class StageFill
{
    public string Code { get; set; } = "";
    public string Label => StageCodes.Label(Code);
    public int OwnerFilled { get; set; }
    public int DueFilled { get; set; }
    public int Total { get; set; }
    public int DoneCount { get; set; }
    public int NaCount { get; set; }

    public double OwnerPercent => Total == 0 ? 0 : OwnerFilled * 100d / Total;
    public double DuePercent => Total == 0 ? 0 : DueFilled * 100d / Total;

    public string Badge => OwnerFilled == 0 ? "空白" : OwnerFilled >= Total ? "齐全" : "部分";
    public string BadgeKind => OwnerFilled == 0 ? "plain" : OwnerFilled >= Total ? "ok" : "on";
    public string Detail => $"责任人 {OwnerFilled} · 计划 {DueFilled}";
}

public sealed class OwnerLoad
{
    public string Name { get; set; } = "";
    public int StageTaskCount { get; set; }
    public int IssueCount { get; set; }
    public int OverdueCount { get; set; }
    public double Ratio { get; set; }
}

public sealed class PhotoBucket
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public int IssueCount { get; set; }
    public double Ratio { get; set; }
}

public sealed class MachineSummary
{
    public string MachineId { get; set; } = "";
    public string Code { get; set; } = "";
    public string? SubprojectId { get; set; }
    public string SubprojectCode { get; set; } = "";
    public int IssueCount { get; set; }
    public int InvertedCount { get; set; }
    public int PhotoCount { get; set; }
    public string AssemblyOwners { get; set; } = "";
    public string Status { get; set; } = "active";

    /// <summary>有照片才显示绿色计数徽标；0 张时界面显示破折号，不显示"0"。</summary>
    public bool HasPhoto => PhotoCount > 0;

    /// <summary>有倒挂才显示红色徽标；0 条时整列不出现红点，避免满屏都是"0"。</summary>
    public bool HasInverted => InvertedCount > 0;

    /// <summary>装配责任人为空时界面给"未填"，不显示空白单元格。</summary>
    public string AssemblyOwnersDisplay =>
        string.IsNullOrWhiteSpace(AssemblyOwners) ? "未填" : AssemblyOwners;
}

public sealed class SubprojectDetail
{
    public Subproject? Subproject { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string MachineRange { get; set; } = "";
    public int MachineCount { get; set; }
    public int IssueCount { get; set; }
    public int InvertedCount { get; set; }
    public int PhotoCount { get; set; }
    public int OwnerCount { get; set; }

    /// <summary>装配阶段已填责任人的问题数；与总览卡片同口径（StageRules.HasOwner）。</summary>
    public int AssemblyFilled { get; set; }

    public bool IsUnassigned { get; set; }
    public List<MachineSummary> Machines { get; set; } = new();
}

public sealed class DashboardData
{
    public int TotalIssues { get; set; }
    public int TotalMachines { get; set; }
    public int TotalSubprojects { get; set; }
    public int InvertedCount { get; set; }
    public int PhotoCount { get; set; }
    public int PhotoIssueCount { get; set; }
    public int OverdueCount { get; set; }
    public int ClosedCount { get; set; }
    public int MyTodoCount { get; set; }
    public int PendingSyncCount { get; set; }
    public string DataAsOfDisplay { get; set; } = "";
    public List<StageFill> StageFills { get; set; } = new();
    public List<OwnerLoad> OwnerLoads { get; set; } = new();
    public List<Issue> Risks { get; set; } = new();
    public List<Issue> Recent { get; set; } = new();

    public Issue? FocusIssue => Risks.FirstOrDefault(i => i.IsInverted) ?? Risks.FirstOrDefault();
    public int StageTaskCount => StageFills.Sum(s => s.OwnerFilled);
}

public sealed class ReportData
{
    public int TotalIssues { get; set; }
    public int TotalMachines { get; set; }
    public int InvertedCount { get; set; }
    public int OverdueCount { get; set; }
    public int ClosedCount { get; set; }
    public int PhotoCount { get; set; }
    public int PhotoIssueCount { get; set; }
    public double PhotoLinkedRate { get; set; }

    /// <summary>真实分项目数（不含"待确认"虚拟分组）。</summary>
    public int SubprojectCount { get; set; }
    public string AsOfDisplay { get; set; } = "";
    public List<StageFill> StageFills { get; set; } = new();
    public List<OwnerLoad> OwnerLoads { get; set; } = new();
    public List<SubprojectCard> Subprojects { get; set; } = new();
    public List<PhotoBucket> PhotoBuckets { get; set; } = new();
    public List<CommonIssue> CommonIssues { get; set; } = new();

    /// <summary>分项目为空的机台 / 问题数（机台号异常、无法自动归组的那部分）。</summary>
    public int UnassignedMachineCount { get; set; }
    public int UnassignedIssueCount { get; set; }

    /// <summary>全部阶段任务数（有责任人的阶段条目合计），用于"共 N 个阶段任务"。</summary>
    public int StageTaskCount { get; set; }

    /// <summary>计划时间分布：按语义计划日聚合，取数量最多的前 5 个日期。</summary>
    public List<PlanDateBucket> PlanDateBuckets { get; set; } = new();

    /// <summary>计划列有原文但解析不成日期的数量（如"已下图"），不计入日期分布。</summary>
    public int TextPlanCount { get; set; }

    /// <summary>倒挂行的装配计划日期集中情况，一句人话（本期结论第 2 条）。</summary>
    public string InvertedPlanDateText { get; set; } = "";

    /// <summary>状态分布里数量最多的状态与条数（本期结论第 1 条）。</summary>
    public string TopStatusText { get; set; } = "";
}

/// <summary>计划时间分布的一根横条（设计稿 report 页"计划时间分布"）。</summary>
public sealed class PlanDateBucket
{
    public string DateText { get; set; } = "";
    public int Count { get; set; }
    public double Ratio { get; set; }
}

public sealed class CommonIssue
{
    public string Title { get; set; } = "";
    public int Count { get; set; }
    public string Machines { get; set; } = "";
}

public sealed class ImportPreview
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FileHash { get; set; } = "";
    public string ProjectCode { get; set; } = "";
    public int SourceRowCount { get; set; }
    public int Total { get; set; }
    public int InsertCount { get; set; }
    public int UpdateCount { get; set; }
    public int UnchangedCount { get; set; }
    public int ConflictCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }

    /// <summary>
    /// 倒挂提示行数：设计计划晚于装配计划的行。
    /// 导入确认弹窗顶部"倒挂提示"格用它，与 <see cref="StageRules.IsPlanInverted"/> 同一判据，
    /// 不在界面层另算一套。
    /// </summary>
    public int InvertedRowCount { get; set; }

    /// <summary>文本计划行数：计划原文解析不成日期（如"已下图"），原文照存但需人工看。</summary>
    public int TextPlanRowCount { get; set; }

    /// <summary>机台号异常行数：不像标准机台号或位数异常，归入待确认。</summary>
    public int MachineWarningCount { get; set; }

    /// <summary>冲突行（同 ID 已存在且系统内人工改过）；导入确认弹窗逐条让用户选覆盖还是保留。</summary>
    public List<ImportRowPreview> ConflictRows => Rows.Where(r => r.Kind == ImportRowKind.Conflict).ToList();

    public bool HasConflict => ConflictCount > 0;

    /// <summary>探测到的表头行号（1 基，0 表示未探测到）；第 2 步列映射直接展示这个值。</summary>
    public int HeaderRow { get; set; }

    /// <summary>表头布局说明：三行表头（阶段组在上 / 在下）或扁平表头，来自导入解析器。</summary>
    public string HeaderLayout { get; set; } = "";

    public DateTime ImportedAt { get; set; } = DateTime.Now;
    public List<ImportRowPreview> Rows { get; set; } = new();
    public List<string> Messages { get; set; } = new();

    public string Summary => $"新增 {InsertCount} · 更新 {UpdateCount} · 无变化 {UnchangedCount} · 冲突 {ConflictCount}";

    /// <summary>列映射页的"表头行"一行：把行号与布局说明合成一句人话。</summary>
    public string HeaderRowText
    {
        get
        {
            if (HeaderRow <= 0) return "未探测到（按首行处理）";
            var layout = string.IsNullOrWhiteSpace(HeaderLayout) ? "" : $" · {HeaderLayout}";
            return $"第 {HeaderRow} 行{layout}";
        }
    }
}

public sealed class ImportRowPreview
{
    public int SourceRow { get; set; }
    public string MachineCode { get; set; } = "";
    public string SeqNo { get; set; } = "";

    /// <summary>
    /// 冲突行对应库内记录的问题号（P7391-009）。新增行没有库内记录时为空，
    /// 界面按"机台号 + 序号"退化成临时标识——冲突列表必须让每条都能被一眼指认。
    ///
    /// 【为什么必须取库内真实 ID 而不是拼序号】库内 id 形如 P7391-006，而原表序号是 1：
    /// 两者不是一回事（seed 的 id 与 seq_no 顺序不同）。用序号拼会在冲突列表里显示出
    /// 一条库里并不存在的问题号，用户拿着它去台账搜会搜不到。
    /// </summary>
    public string IssueCode { get; set; } = "";

    public string Title { get; set; } = "";
    public string DesignOwner { get; set; } = "";
    public string AssemblyOwner { get; set; } = "";
    public string DesignDue { get; set; } = "";
    public string AssemblyDue { get; set; } = "";
    public ImportRowKind Kind { get; set; } = ImportRowKind.Insert;
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// 冲突行的逐条决策：true = 用原表值覆盖系统值，false = 保留系统值。
    ///
    /// 【为什么逐条而不是一个全局开关】"同 ID 已存在 + 系统内人工改过 + 原表也变了"这三件事
    /// 同时成立时，两边都有价值：系统里可能是现场核实后的修正，原表可能是客户刚发的新版。
    /// 一个"全量覆盖/全部跳过"的全局开关必然让其中一类被静默丢掉。设计稿 dlg-import-conflict
    /// 就是逐条勾选，默认取原表（用户此刻的意图就是"以这张表为准"），可单条改成保留系统值。
    /// </summary>
    public bool TakeExcelOnConflict { get; set; } = true;

    /// <summary>冲突行：系统内当前值的人话摘要（如"装配计划 11-15"）；非冲突行为空。</summary>
    public string ConflictSystemText { get; set; } = "";

    /// <summary>冲突行：原表值的人话摘要（如"11-01"）；非冲突行为空。</summary>
    public string ConflictExcelText { get; set; } = "";

    /// <summary>冲突行的处理结果文案，随 <see cref="TakeExcelOnConflict"/> 变化。</summary>
    public string ConflictDecisionText => TakeExcelOnConflict ? "用原表覆盖" : "保留系统值";

    /// <summary>
    /// 库内真实问题 ID（P7391-006）。冲突行一定对应库内已有记录，必须用它——
    /// 原表序号（seq_no=1）与库内 id 不是一回事，拿序号拼出来的问题号在台账里搜不到。
    /// </summary>
    public string ExistingIssueId { get; set; } = "";

    /// <summary>解析后的完整草稿：仅供导入向导内存传递，界面与同步都不使用。</summary>
    public IssueDraft? Draft { get; set; }

    public string KindDisplay => Kind switch
    {
        ImportRowKind.Update => "更新",
        ImportRowKind.Unchanged => "无变化",
        ImportRowKind.Conflict => "冲突",
        ImportRowKind.Error => "错误",
        _ => "新增"
    };

    public string KindKind => Kind switch
    {
        ImportRowKind.Update => "on",
        ImportRowKind.Conflict => "ng",
        ImportRowKind.Error => "ng",
        ImportRowKind.Unchanged => "plain",
        _ => "ok"
    };

    public string WarningText => string.Join("；", Warnings);
}

public enum ImportRowKind
{
    Insert,
    Update,
    Unchanged,
    Conflict,
    Error
}

public sealed class ImportResult
{
    public string BatchId { get; set; } = "";
    public int Total { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Conflicts { get; set; }
    public int Skipped { get; set; }
    public int Warnings { get; set; }
    public List<string> Messages { get; set; } = new();
    public string Summary => $"写入 {Inserted + Updated} 条 · 冲突 {Conflicts} · 跳过 {Skipped}";
}

public sealed class ExportRequest
{
    public string ProjectId { get; set; } = "";
    public string? SubprojectId { get; set; }

    /// <summary>
    /// 版式：original=客户原表版式（三行表头 + 四阶段分组）/ standard=标准明细表 /
    /// summary=统计汇总（责任人负载 + 阶段填写率 + 倒挂清单）。
    /// 保留 <see cref="UseOriginalLayout"/> 作为旧调用点的兼容入口（见其注释）。
    /// </summary>
    public string Layout { get; set; } = "original";

    public bool UseOriginalLayout
    {
        get => !string.Equals(Layout, "standard", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(Layout, "summary", StringComparison.OrdinalIgnoreCase);
        set => Layout = value ? "original" : "standard";
    }

    public bool IncludeAttachments { get; set; } = true;
    public bool IncludeChangeLog { get; set; } = true;
    public bool SelectAll { get; set; } = true;
    public List<string> IssueIds { get; set; } = new();

    /// <summary>导出范围：all=全部 / inverted=仅倒挂 / photos=仅有照片 / selected=仅选中。</summary>
    public string Scope { get; set; } = "all";

    /// <summary>
    /// 用户在导出确认弹窗里改过的文件名（含 .xlsx）。为空时按"项目号_版式_时间戳"自动命名。
    ///
    /// 【为什么允许界面指定】设计稿 dlg-export 的文件名是可编辑的：现场需要加批次号或客户简称，
    /// 事后才找得到。这里只接受纯文件名（后端会剥掉路径与非法字符），不允许借此把文件写到别处。
    /// </summary>
    public string PreferredFileName { get; set; } = "";

    public string LayoutLabel => Layout?.ToLowerInvariant() switch
    {
        "standard" => "标准明细表",
        "summary" => "统计汇总",
        _ => "原表版式"
    };
}

public sealed class ExportResult
{
    public string OutputPath { get; set; } = "";
    public int IssueCount { get; set; }
    public int AttachmentCount { get; set; }
    public long FileSize { get; set; }
    public string Summary => $"{IssueCount} 条问题 · 附件 {AttachmentCount} 个";
}
