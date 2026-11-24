namespace Sinan.Core.Models;

/// <summary>账号角色。MVP 只做页面门禁，不做细粒度字段权限。</summary>
public enum UserRole
{
    Admin,
    Engineer,
    Viewer
}

public static class RoleNames
{
    public const string Admin = "admin";
    public const string Engineer = "engineer";
    public const string Viewer = "viewer";

    public static UserRole Parse(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        Admin => UserRole.Admin,
        Viewer => UserRole.Viewer,
        _ => UserRole.Engineer
    };

    public static string ToDb(UserRole role) => role switch
    {
        UserRole.Admin => Admin,
        UserRole.Viewer => Viewer,
        _ => Engineer
    };

    public static string Display(UserRole role) => role switch
    {
        UserRole.Admin => "管理员",
        UserRole.Viewer => "只读",
        _ => "工程师"
    };
}

/// <summary>四阶段编码。顺序即业务推进顺序，界面与导出都依赖它。</summary>
public static class StageCodes
{
    public const string Plan = "plan";
    public const string Design = "design";
    public const string Material = "material";
    public const string Assembly = "assembly";

    public static readonly string[] All = { Plan, Design, Material, Assembly };

    public static string Label(string? code) => code switch
    {
        Plan => "方案确定",
        Design => "设计下图",
        Material => "物料准备",
        Assembly => "装配调试",
        _ => code ?? ""
    };

    public static int Index(string? code) => Array.IndexOf(All, code);
}

/// <summary>阶段状态。逾期不落库，按 due_date 实时推导，避免"过期状态"。</summary>
public enum StageState
{
    NotApplicable,
    NotStarted,
    InProgress,
    Done
}

public static class StageStates
{
    public static string ToDb(StageState state) => state switch
    {
        StageState.NotApplicable => "na",
        StageState.InProgress => "in_progress",
        StageState.Done => "done",
        _ => "not_started"
    };

    public static StageState Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "na" => StageState.NotApplicable,
        "in_progress" => StageState.InProgress,
        "done" => StageState.Done,
        _ => StageState.NotStarted
    };

    public static string Display(StageState state) => state switch
    {
        StageState.NotApplicable => "不适用",
        StageState.InProgress => "进行中",
        StageState.Done => "已完成",
        _ => "未开始"
    };
}

/// <summary>问题状态（中文入库，导出与 UI 直接用）。</summary>
public static class IssueStatuses
{
    public const string Unassigned = "待分派";
    public const string Planning = "方案中";
    public const string Designing = "设计中";
    public const string Material = "备料中";
    public const string Assembling = "装配中";
    public const string Verifying = "待验证";
    public const string Closed = "已关闭";
    public const string Cancelled = "已取消";

    public static readonly string[] All =
    {
        Unassigned, Planning, Designing, Material, Assembling, Verifying, Closed, Cancelled
    };

    public static string ForStage(string stageCode) => stageCode switch
    {
        StageCodes.Plan => Planning,
        StageCodes.Design => Designing,
        StageCodes.Material => Material,
        StageCodes.Assembly => Assembling,
        _ => Unassigned
    };

    public static bool IsTerminal(string? status) =>
        status is Closed or Cancelled;

    public static string Normalize(string? status)
    {
        var value = status?.Trim() ?? "";
        return All.Contains(value) ? value : Unassigned;
    }
}
