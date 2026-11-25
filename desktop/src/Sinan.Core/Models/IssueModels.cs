using Sinan.Core.Utils;

namespace Sinan.Core.Models;

/// <summary>
/// 一条问题及其四阶段。列表页与详情页共用同一模型（当前 36 条量级，无需再拆轻量 DTO），
/// 但仓储查询时可以选择是否加载 stages/attachments。
/// </summary>
public sealed class Issue
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string? SubprojectId { get; set; }
    public string MachineId { get; set; } = "";
    public string MachineCode { get; set; } = "";
    public string? SubprojectCode { get; set; }
    public string SeqNo { get; set; } = "";
    public string Title { get; set; } = "";
    public string Solution { get; set; } = "";
    public string Status { get; set; } = IssueStatuses.Unassigned;
    public string Priority { get; set; } = "中";
    public string Category { get; set; } = "";
    public string CompletionText { get; set; } = "";
    public string? ClosedAt { get; set; }
    public string? SourceBatchId { get; set; }
    public int? SourceRow { get; set; }
    public string CreatedBy { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public int Version { get; set; } = 1;
    public string? DeletedAt { get; set; }

    /// <summary>附件数量（列表查询时由 attachments 聚合填充，避免为每行加载附件实体）。</summary>
    public int AttachmentCount { get; set; }

    public List<IssueStage> Stages { get; set; } = new();
    public List<Attachment> Attachments { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
    public List<ActivityEntry> Activity { get; set; } = new();

    public IssueStage? Stage(string code) => Stages.FirstOrDefault(s => s.Code == code);
    public IssueStage? PlanStage => Stage(StageCodes.Plan);
    public IssueStage? DesignStage => Stage(StageCodes.Design);
    public IssueStage? MaterialStage => Stage(StageCodes.Material);
    public IssueStage? AssemblyStage => Stage(StageCodes.Assembly);

    public string DesignOwner => DesignStage?.OwnerText ?? "";
    public string AssemblyOwner => AssemblyStage?.OwnerText ?? "";
    public string DesignDueRaw => DesignStage?.DueRaw ?? "";
    public string AssemblyDueRaw => AssemblyStage?.DueRaw ?? "";
    public int PhotoCount => Attachments.Count > 0 ? Attachments.Count : AttachmentCount;

    public bool IsClosed => IssueStatuses.IsTerminal(Status);
    public bool IsInverted => StageRules.IsPlanInverted(this);
    public bool IsOverdue => StageRules.OverdueDays(this) > 0;

    /// <summary>逾期天数：取所有"未完成且有计划日"的阶段中最大者；关闭/取消不算逾期。</summary>
    public int OverdueDays => StageRules.OverdueDays(this);

    public string OverdueStageLabel
    {
        get
        {
            var today = AppTime.Today;
            var stage = Stages
                .Where(s => s.State is StageState.NotStarted or StageState.InProgress)
                .Where(s => s.DueDate.HasValue && s.DueDate.Value < today)
                .OrderBy(s => s.DueDate)
                .FirstOrDefault();
            return stage == null ? "" : StageCodes.Label(stage.Code);
        }
    }

    /// <summary>当前推进到的阶段（第一个未完成且非不适用的阶段）。</summary>
    public IssueStage? CurrentStage => Stages
        .OrderBy(s => StageCodes.Index(s.Code))
        .FirstOrDefault(s => s.State is StageState.NotStarted or StageState.InProgress);

    public int DoneStageCount => Stages.Count(s => s.State is StageState.Done or StageState.NotApplicable);
    public string ProgressText => $"{DoneStageCount}/4";

    public string MachineAndTitle => string.IsNullOrWhiteSpace(MachineCode)
        ? Title
        : $"{MachineCode} · {Title}";

    /// <summary>是否已挂照片（0 张时不显示"照片"徽标，避免整列都是 0）。</summary>
    public bool HasPhoto => PhotoCount > 0;

    /// <summary>问题点卡片上的照片徽标文本（演示元数据，与列表口径一致）。</summary>
    public string PhotoText => $"照片 {PhotoCount}";

    /// <summary>倒挂徽标文本：天数取最紧的那一阶段，没有倒挂时为空串。</summary>
    public string InvertedText => IsInverted ? $"倒挂 {StageRules.InvertedDays(this)} 天" : "";

    /// <summary>台账 ID 列：序号补足三位（原表允许缺口，不重排）。</summary>
    public string DisplaySeqNo => (SeqNo ?? "").Trim().PadLeft(3, '0');

    /// <summary>台账"设计 → 装配"第一行：空责任人显示"设计 /"，与设计稿的缺省写法一致。</summary>
    public string DesignOwnerDisplay =>
        string.IsNullOrWhiteSpace(DesignOwner) ? "设计 /" : DesignOwner;

    /// <summary>台账"设计 → 装配"第二行：装配责任人 · 装配日期，空值用破折号。</summary>
    public string AssemblyLine =>
        "→ " + (string.IsNullOrWhiteSpace(AssemblyOwner) ? "—" : AssemblyOwner) +
        " · " + (string.IsNullOrWhiteSpace(AssemblyDueRaw) ? "—" : AssemblyDueRaw);

    /// <summary>台账 7 列中的"设计 → 装配"合并列文本。</summary>
    public string OwnerFlow
    {
        get
        {
            var d = string.IsNullOrWhiteSpace(DesignOwner) ? "—" : DesignOwner;
            var a = string.IsNullOrWhiteSpace(AssemblyOwner) ? "—" : AssemblyOwner;
            return $"{d} → {a}";
        }
    }

    public string DueFlow
    {
        get
        {
            var d = string.IsNullOrWhiteSpace(DesignDueRaw) ? "—" : DesignDueRaw;
            var a = string.IsNullOrWhiteSpace(AssemblyDueRaw) ? "—" : AssemblyDueRaw;
            return $"{d} → {a}";
        }
    }

    /// <summary>
    /// 设备问题预览卡片上的"设计责任人 / 计划 · 装配计划"一行。
    /// 空值统一显示破折号：原始字段是空串而非 null，XAML 的 TargetNullValue 拦不住，
    /// 必须在这里转，否则界面会出现"设计  装配 "这种缺字的半截句子。
    /// 责任人缺失时省略"责任人"位，避免和缺计划的破折号连成"———"看不清谁缺什么。
    /// </summary>
    public string DesignPlanLine
    {
        get
        {
            var due = string.IsNullOrWhiteSpace(DesignDueRaw) ? "—" : DesignDueRaw;
            var asm = string.IsNullOrWhiteSpace(AssemblyDueRaw) ? "—" : AssemblyDueRaw;
            var head = string.IsNullOrWhiteSpace(DesignOwner)
                ? $"设计 {due}"
                : $"设计 {DesignOwner} {due}";
            return $"{head} · 装配 {asm}";
        }
    }
}

public sealed class IssueStage
{
    public string Id { get; set; } = "";
    public string IssueId { get; set; } = "";
    public string Code { get; set; } = "";
    public string OwnerText { get; set; } = "";
    public string OwnerIds { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string DueRaw { get; set; } = "";
    public StageState State { get; set; } = StageState.NotStarted;
    public DateOnly? ActualDate { get; set; }
    public string Remark { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public int Version { get; set; } = 1;

    public string Label => StageCodes.Label(Code);
    public string StateDisplay => StageStates.Display(State);
    public bool HasOwner => !string.IsNullOrWhiteSpace(OwnerText);
    public bool HasPlan => DueDate.HasValue || !string.IsNullOrWhiteSpace(DueRaw);

    public string DueDisplay => DueDate.HasValue
        ? DueDate.Value.ToString("MM-dd")
        : string.IsNullOrWhiteSpace(DueRaw) ? "—" : DueRaw;

    public string OwnerDisplay => string.IsNullOrWhiteSpace(OwnerText) ? "未指派" : OwnerText;

    public bool IsOverdue => State is StageState.NotStarted or StageState.InProgress
                             && DueDate.HasValue && DueDate.Value < AppTime.Today;

    public int OverdueDays => IsOverdue ? AppTime.Today.DayNumber - DueDate!.Value.DayNumber : 0;
}

public sealed class Attachment
{
    public string Id { get; set; } = "";
    public string IssueId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string StoredName { get; set; } = "";
    /// <summary>相对附件根目录，例如 <c>P7391-009/20261123_abc.jpg</c>。</summary>
    public string StoredPath { get; set; } = "";
    public string ThumbPath { get; set; } = "";
    public long Size { get; set; }
    public string Mime { get; set; } = "image/jpeg";
    public string Sha256 { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public bool IsDemo { get; set; }
    public string SyncState { get; set; } = "synced";

    public string SizeDisplay => Size switch
    {
        >= 1024 * 1024 => $"{Size / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{Size / 1024d:0} KB",
        _ => $"{Size} B"
    };

    public string CreatedDisplay => AppTime.Display(CreatedAt, "MM-dd HH:mm");
}

public sealed class Comment
{
    public string Id { get; set; } = "";
    public string IssueId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Content { get; set; } = "";
    public string CreatedAt { get; set; } = "";

    public string CreatedDisplay => AppTime.Display(CreatedAt, "MM-dd HH:mm");
}

public sealed class ActivityEntry
{
    public long Id { get; set; }
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Field { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserName { get; set; } = "";
    public string CreatedAt { get; set; } = "";

    public string CreatedDisplay => AppTime.Display(CreatedAt, "MM-dd HH:mm");

    public string Summary => Field switch
    {
        "status" => $"状态：{Empty(OldValue)} → {Empty(NewValue)}",
        "owner" => $"责任人：{Empty(OldValue)} → {Empty(NewValue)}",
        "due" => $"计划：{Empty(OldValue)} → {Empty(NewValue)}",
        "solution" => "更新了解决措施",
        "completion" => "更新了完成情况",
        _ => $"{Field}：{Empty(OldValue)} → {Empty(NewValue)}"
    };

    private static string Empty(string? value) => string.IsNullOrWhiteSpace(value) ? "空" : value;
}

public sealed class IssueFilter
{
    public string? ProjectId { get; set; }
    public string? SubprojectId { get; set; }
    public string? MachineId { get; set; }
    public string? Status { get; set; }
    public string? Owner { get; set; }
    public string? StageCode { get; set; }
    public string? Keyword { get; set; }
    public bool OnlyOverdue { get; set; }
    public bool OnlyInverted { get; set; }
    public bool OnlyWithPhoto { get; set; }
    public bool IncludeClosed { get; set; } = true;
    public int Limit { get; set; } = 500;
}

/// <summary>新建/编辑问题时的输入模型，避免 View 直接拼 Issue 实体。</summary>
public sealed class IssueDraft
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string? SubprojectId { get; set; }
    public string MachineId { get; set; } = "";
    public string MachineCode { get; set; } = "";
    public string SeqNo { get; set; } = "";
    public string Title { get; set; } = "";
    public string Solution { get; set; } = "";
    public string Priority { get; set; } = "中";
    public string Category { get; set; } = "";
    public string Status { get; set; } = IssueStatuses.Unassigned;
    public string CompletionText { get; set; } = "";
    public int Version { get; set; } = 1;
    /// <summary>Excel 导入批次与源行号；新建/编辑时保持 null。用于追溯"来自哪个文件第几行"。</summary>
    public string? SourceBatchId { get; set; }
    public int? SourceRow { get; set; }
    public StageDraft Plan { get; set; } = new() { Code = StageCodes.Plan };
    public StageDraft Design { get; set; } = new() { Code = StageCodes.Design };
    public StageDraft Material { get; set; } = new() { Code = StageCodes.Material };
    public StageDraft Assembly { get; set; } = new() { Code = StageCodes.Assembly };
}

public sealed class StageDraft
{
    public string Code { get; set; } = "";
    public string OwnerText { get; set; } = "";
    public string DueRaw { get; set; } = "";
    public StageState State { get; set; } = StageState.NotStarted;
    public string ActualDateRaw { get; set; } = "";
    public string Remark { get; set; } = "";
    public int Version { get; set; } = 1;

    /// <summary>
    /// 变更原因：只有改了计划原文时界面才强制填写（根 AGENTS.md 第 5 节第 9 条字段级审计）。
    /// 仓储把它拼进"计划"那条审计的 new 值，便于留痕里直接读出"为什么改"。
    /// </summary>
    public string ChangeReason { get; set; } = "";

    public string Label => StageCodes.Label(Code);
    public bool HasPlan => !string.IsNullOrWhiteSpace(DueRaw);
}
