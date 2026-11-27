using System;
using Sinan.Core.Models;
using Sinan.Data;
using Sinan.Data.Repositories;

namespace Sinan.App.Services;

/// <summary>
/// 当前总项目上下文：唯一决定"现在在看哪个项目"的地方。
///
/// 为什么需要它：多总项目后如果每个页面各自 <c>GetMain()</c>，切换项目时会出现
/// 总览已切、台账/报表还停在旧项目的串数据。这里把当前项目收口为一个服务：
/// - 选择持久化到 app_meta.current_project_id，重启后回到上次项目；
/// - 切换时发 Changed 事件，由 ShellViewModel 统一清缓存、重建页面。
///
/// 注意：这里只保存"选择"，不缓存业务数据；各仓储每次按项目 ID 实时查询。
/// </summary>
public sealed class ProjectContext
{
    private const string MetaKey = "current_project_id";

    private readonly Database _database;
    private readonly ProjectRepository _projects;

    public ProjectContext(Database database, ProjectRepository projects)
    {
        _database = database;
        _projects = projects;
    }

    /// <summary>当前项目；数据库里一个项目都没有时为 null（正常首启种子会保证有 P7391）。</summary>
    public Project? Current { get; private set; }

    public string? CurrentId => Current?.Id;

    /// <summary>项目切换完成后触发；订阅方负责刷新数据与界面。</summary>
    public event EventHandler<Project>? Changed;

    /// <summary>启动时读上次选择；记录失效（项目被删）则回退到第一个项目。</summary>
    public void Load()
    {
        string? savedId;
        using (var connection = _database.Open())
            savedId = Database.ReadMeta(connection, MetaKey);

        Current = _projects.Get(savedId) ?? _projects.GetMain();
        if (Current == null) return;
        Save(Current.Id);
    }

    /// <summary>切换项目；项目不存在或与原项目相同则返回 false，不触发页面重载。</summary>
    public bool Switch(string projectId)
    {
        var project = _projects.Get(projectId);
        if (project == null) return false;
        if (string.Equals(Current?.Id, project.Id, StringComparison.Ordinal)) return false;

        Current = project;
        Save(project.Id);
        Changed?.Invoke(this, project);
        return true;
    }

    /// <summary>项目信息（名称/客户）被修改后重新读取，不改变当前选择。</summary>
    public void RefreshCurrent()
    {
        if (Current == null) return;
        Current = _projects.Get(Current.Id) ?? _projects.GetMain();
        if (Current != null) Save(Current.Id);
    }

    private void Save(string projectId)
    {
        using var connection = _database.Open();
        Database.WriteMeta(connection, MetaKey, projectId);
    }
}
