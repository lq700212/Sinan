namespace Sinan.Core.Models;

/// <summary>登录会话中的用户。密码哈希不进入 ViewModel。</summary>
public sealed class User
{
    public string Id { get; set; } = "";
    public string EmployeeNo { get; set; } = "";
    public string Name { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Engineer;
    public string Dept { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool Active { get; set; } = true;
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";

    public string RoleDisplay => RoleNames.Display(Role);
    public bool IsAdmin => Role == UserRole.Admin;
    public bool CanEdit => Role != UserRole.Viewer;
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1];
    public string Badge => $"{Initial} {Name}";
}

public sealed class Project
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Status { get; set; } = "active";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public int Version { get; set; } = 1;

    public string StatusDisplay => Status switch
    {
        "done" => "已完成",
        "paused" => "已暂停",
        _ => "进行中"
    };

    /// <summary>下拉/弹窗里的一行标题：P7391 · 安装调试问题闭环。</summary>
    public string CodeAndName => string.IsNullOrWhiteSpace(Name) ? Code : $"{Code} · {Name}";

    /// <summary>机台号前缀即项目号（P7391 + 3 位机台号），新增项目时不在库里重复存。</summary>
    public string MachinePrefix => Code;
}

/// <summary>
/// 切换总项目弹窗的一行：项目本体 + 实时规模统计。
/// 数字全部由 ProjectRepository 查询数据库得到，不允许在界面写死。
/// </summary>
public sealed class ProjectSummary
{
    public Project Project { get; set; } = new();
    public int SubprojectCount { get; set; }
    public int MachineCount { get; set; }
    public int IssueCount { get; set; }
    public int InvertedCount { get; set; }
    public bool IsCurrent { get; set; }

    public string Id => Project.Id;
    public string Code => Project.Code;
    public string Name => Project.Name;
    public string StatusDisplay => Project.StatusDisplay;
    public string MetricText =>
        $"{SubprojectCount} 个分项目 · {MachineCount} 台设备 · {IssueCount} 条问题";

    public string CurrentText => IsCurrent ? "当前" : "";

    /// <summary>
    /// 读屏/自动化用的整行名称：项目号 + 名称 + 规模 + 是否当前。
    /// 列表项默认名是类型名（多行同名），不显式给 AccessibleName 会导致
    /// 读屏只能念 "ProjectSummary"，自动化也点不中指定行（桌面端弹窗规范第 6 条）。
    /// </summary>
    public string AccessibleName =>
        $"{Code} {Name} · {MetricText}" + (IsCurrent ? " · 当前" : "");
}

public sealed class Subproject
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string MachineRange { get; set; } = "";
    public string Status { get; set; } = "active";
    public int SortNo { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public int Version { get; set; } = 1;

    public string StatusDisplay => Status == "done" ? "已完成" : "进行中";
}

public sealed class Machine
{
    public string Id { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string? SubprojectId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int? SeqNo { get; set; }
    public string Status { get; set; } = "active";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public int Version { get; set; } = 1;
}
