using Sinan.Core.Models;

namespace Sinan.Core.Utils;

/// <summary>
/// 阶段/状态/倒挂/逾期规则。全部纯函数，ViewModel 与 Excel 通道共用，
/// 禁止在页面里再写一套判断，否则统计口径必然分叉。
/// </summary>
public static class StageRules
{
    /// <summary>
    /// 计划倒挂：设计下图计划日晚于装配调试计划日。
    /// 只比较语义日期；"已下图"、`/`、空白不参与（对应调研 5.2 的统计口径）。
    /// </summary>
    public static bool IsPlanInverted(Issue issue)
    {
        var design = issue.DesignStage?.DueDate;
        var assembly = issue.AssemblyStage?.DueDate;
        return design.HasValue && assembly.HasValue && design.Value > assembly.Value;
    }

    public static bool IsPlanInverted(DateOnly? designDue, DateOnly? assemblyDue) =>
        designDue.HasValue && assemblyDue.HasValue && designDue.Value > assemblyDue.Value;

    /// <summary>
    /// 倒挂天数：设计下图计划日晚于装配调试计划日多少天。
    /// 只在确实倒挂时为正数；任一日期缺失（含 `/`、`已下图`、空白）返回 0。
    /// 界面徽标与统计共用这一口径，禁止页面自己减日期。
    /// </summary>
    public static int InvertedDays(Issue issue)
    {
        var design = issue.DesignStage?.DueDate;
        var assembly = issue.AssemblyStage?.DueDate;
        if (!design.HasValue || !assembly.HasValue || design.Value <= assembly.Value) return 0;
        return design.Value.DayNumber - assembly.Value.DayNumber;
    }

    /// <summary>
    /// 逾期天数：未完成/非不适用阶段中，计划日早于今天的最长天数。
    /// 已关闭/已取消不算逾期；`/`、已下图（无 due_date）不参与。
    /// </summary>
    public static int OverdueDays(Issue issue)
    {
        if (IssueStatuses.IsTerminal(issue.Status) || issue.DeletedAt != null) return 0;
        var today = AppTime.Today;
        var max = 0;
        foreach (var stage in issue.Stages)
        {
            if (stage.State is StageState.Done or StageState.NotApplicable) continue;
            if (!stage.DueDate.HasValue) continue;
            var days = today.DayNumber - stage.DueDate.Value.DayNumber;
            if (days > max) max = days;
        }
        return max;
    }

    /// <summary>
    /// 由四阶段推导问题状态。规则：
    /// - 完成情况有文本 / 所有适用阶段完成 → 待验证（人工确认后才关闭）；
    /// - 取第一个未完成阶段映射状态；
    /// - 全无责任人且无计划 → 待分派。
    /// 允许人工覆盖，但覆盖要写审计。
    /// </summary>
    public static string DeriveStatus(IReadOnlyList<IssueStage> stages, string? completionText = null)
    {
        if (stages.Count == 0) return IssueStatuses.Unassigned;
        if (!string.IsNullOrWhiteSpace(completionText)) return IssueStatuses.Verifying;

        var applicable = stages.Where(s => s.State != StageState.NotApplicable).ToList();
        if (applicable.Count > 0 && applicable.All(s => s.State == StageState.Done))
            return IssueStatuses.Verifying;

        var current = stages
            .OrderBy(s => StageCodes.Index(s.Code))
            .FirstOrDefault(s => s.State is StageState.NotStarted or StageState.InProgress);

        if (current == null) return IssueStatuses.Verifying;

        // 只有"第一道工序都还没安排"才回到待分派。
        // 前一阶段已完成/已跳过时，即使下一阶段还没填责任人，也必须向前推进到
        // 下一阶段对应状态；否则方案保存完会从"方案中"倒退回"待分派"。
        var earlierHandled = stages.Any(s =>
            StageCodes.Index(s.Code) < StageCodes.Index(current.Code) &&
            s.State is StageState.Done or StageState.NotApplicable);

        if (!earlierHandled && !current.HasOwner && !current.HasPlan &&
            current.State == StageState.NotStarted)
            return IssueStatuses.Unassigned;

        return IssueStatuses.ForStage(current.Code);
    }

    /// <summary>
    /// 导入/编辑时由原文推导阶段状态：
    /// `/` → 不适用；"已下图" → 已完成；责任人和计划都空 → 未开始；否则进行中。
    /// </summary>
    public static StageState InferState(string? ownerText, string? dueRaw, bool actualDone = false)
    {
        if (RawDateParser.IsNotApplicable(dueRaw)) return StageState.NotApplicable;
        if (actualDone || RawDateParser.MeansDrawingIssued(dueRaw)) return StageState.Done;
        if (string.IsNullOrWhiteSpace(ownerText) && string.IsNullOrWhiteSpace(dueRaw))
            return StageState.NotStarted;
        return StageState.InProgress;
    }

    /// <summary>阶段"已填"的判定：责任人非空即算填了责任人；due_raw 非空即算填了计划。</summary>
    public static bool HasOwner(IssueStage stage) => !string.IsNullOrWhiteSpace(stage.OwnerText);
    public static bool HasPlan(IssueStage stage) =>
        !string.IsNullOrWhiteSpace(stage.DueRaw) || stage.DueDate.HasValue;

    public static string ProgressLabel(Issue issue)
    {
        var current = issue.CurrentStage;
        return current == null ? issue.Status : $"{current.Label} · {issue.ProgressText}";
    }
}

/// <summary>
/// 机台号 → 分项目归属。规则来自真实数据核对（见根 AGENTS.md 第 6 节）：
/// 1xx→01、2xx→02、3xx→03；格式异常（如 P73913118）返回 null，进"待确认"。
/// </summary>
public static class SubprojectRules
{
    public const string P01 = "P7391-01";
    public const string P02 = "P7391-02";
    public const string P03 = "P7391-03";

    public static string? Resolve(string? machineCode)
    {
        if (!TextUtil.IsStrictMachineCode(machineCode)) return null;
        var seq = TextUtil.MachineSeq(machineCode);
        if (seq == null) return null;
        return seq.Value switch
        {
            >= 100 and <= 199 => P01,
            >= 200 and <= 299 => P02,
            >= 300 and <= 399 => P03,
            _ => null
        };
    }

    public static string DisplayName(string code) => code switch
    {
        P01 => "机台 1xx 段",
        P02 => "机台 2xx 段",
        P03 => "机台 3xx 段",
        _ => "待确认"
    };
}
