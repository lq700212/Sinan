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
