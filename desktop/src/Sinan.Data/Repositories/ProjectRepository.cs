using Microsoft.Data.Sqlite;
using Sinan.Core.Models;
using Sinan.Core.Utils;

namespace Sinan.Data.Repositories;

/// <summary>
/// 项目 / 分项目 / 机台查询，以及总览、工作台、报表的统计装配。
///
/// 统计口径只在这里实现一次：
/// - 全部基于 <see cref="IssueRepository.All"/> 的内存模型，不另写 SQL 聚合，
///   避免页面、导出、报表各算一套导致数字打架（当前演示库 36 条，量级完全够用）；
/// - "填写"只认语义字段：责任人用 owner_text，计划用 due_date；
///   `/`（不适用）、`已下图`、空白都不算计划已填，与调研 5.2 一致；
/// - 倒挂、逾期统一走 <see cref="StageRules"/>，不在这里重复判断。
/// </summary>
public sealed class ProjectRepository
{
    /// <summary>没有分项目归属的机台（如 P73913118）统一进这个虚拟分项目。</summary>
    public const string UnassignedCode = "待确认";

    private readonly Database _database;
    private readonly IssueRepository _issues;

    public ProjectRepository(Database database, IssueRepository issues)
    {
        _database = database;
        _issues = issues;
    }

    public Project? GetMain()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM projects ORDER BY created_at LIMIT 1";
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapProject(reader) : null;
    }

    public List<Subproject> ListSubprojects(string projectId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM subprojects
            WHERE project_id=$project
            ORDER BY sort_no, code
            """;
        command.Parameters.AddWithValue("$project", projectId);
        var list = new List<Subproject>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(MapSubproject(reader));
        return list;
    }

    /// <summary>分项目下的机台；subprojectId 传 null 表示"待确认"机台。</summary>
    public List<Machine> ListMachines(string projectId, string? subprojectId, bool unassigned = false)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = unassigned
            ? """
              SELECT * FROM machines
              WHERE project_id=$project AND subproject_id IS NULL
              ORDER BY seq_no IS NULL, seq_no, code
              """
            : """
              SELECT * FROM machines
              WHERE project_id=$project AND subproject_id=$sub
              ORDER BY seq_no IS NULL, seq_no, code
              """;
        command.Parameters.AddWithValue("$project", projectId);
        if (!unassigned) command.Parameters.AddWithValue("$sub", subprojectId ?? "");
        var list = new List<Machine>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(MapMachine(reader));
        return list;
    }

    /// <summary>总览页：项目卡片、阶段填写率、照片分桶、待确认汇总。</summary>
    public ProjectOverview GetOverview()
    {
        var project = GetMain() ?? new Project { Code = "P7391", Name = "P7391 安装调试问题闭环" };
        var issues = _issues.All();
        var subs = ListSubprojects(project.Id);
        var cards = BuildCards(issues, subs, project.Id);

        return new ProjectOverview
        {
            Project = project,
            SubprojectCount = cards.Count(c => !c.IsUnassigned),
            MachineCount = issues.Select(i => i.MachineId).Distinct().Count(),
            IssueCount = issues.Count,
            InvertedCount = issues.Count(i => i.IsInverted),
            PhotoCount = issues.Sum(i => i.PhotoCount),
            PhotoIssueCount = issues.Count(i => i.PhotoCount > 0),
            UnassignedMachineCount = cards.Where(c => c.IsUnassigned).Sum(c => c.MachineCount),
            UnassignedIssueCount = cards.Where(c => c.IsUnassigned).Sum(c => c.IssueCount),
            StageTaskCount = issues.Sum(i => i.Stages.Count(StageRules.HasOwner)),
            Cards = cards,
            StageFills = BuildStageFills(issues),
            PhotoBuckets = BuildPhotoBuckets(issues, subs),
            LastImportDisplay = LastImportDisplay()
        };
    }

    /// <summary>工作台：今日待办、风险清单、最近更新。</summary>
    public DashboardData GetDashboard(User user)
    {
        var issues = _issues.All();
        var loads = BuildOwnerLoads(issues);
        var name = user.Name?.Trim() ?? "";

        var risks = issues
            .Where(i => !i.IsClosed && (i.IsInverted || i.OverdueDays > 0))
            .OrderByDescending(i => i.IsInverted)
            .ThenByDescending(i => i.OverdueDays)
            .ThenBy(i => PriorityRank(i.Priority))
            .ThenBy(i => i.SourceRow ?? int.MaxValue)
            .Take(8)
            .ToList();

        var recent = issues
            .OrderByDescending(i => i.UpdatedAt, StringComparer.Ordinal)
            .ThenBy(i => i.SourceRow ?? int.MaxValue)
            .Take(8)
            .ToList();

        return new DashboardData
        {
            TotalIssues = issues.Count,
            TotalMachines = issues.Select(i => i.MachineId).Distinct().Count(),
            TotalSubprojects = issues.Where(i => i.SubprojectId != null).Select(i => i.SubprojectId).Distinct().Count(),
            InvertedCount = issues.Count(i => i.IsInverted),
            PhotoCount = issues.Sum(i => i.PhotoCount),
            PhotoIssueCount = issues.Count(i => i.PhotoCount > 0),
            OverdueCount = issues.Count(i => i.OverdueDays > 0),
            ClosedCount = issues.Count(i => i.IsClosed),
            MyTodoCount = name.Length == 0
                ? 0
                : issues.Count(i => !i.IsClosed && i.Stages.Any(s =>
                    TextUtil.SplitOwners(s.OwnerText).Contains(name, StringComparer.Ordinal))),
            PendingSyncCount = CountPendingSync(),
            DataAsOfDisplay = DateTime.Now.ToString("MM-dd HH:mm"),
            StageFills = BuildStageFills(issues),
            OwnerLoads = loads,
            Risks = risks,
            Recent = recent
        };
    }

    /// <summary>统计报表：阶段完成率、责任人负载、分项目对比、照片覆盖。</summary>
    public ReportData GetReport()
    {
        var project = GetMain() ?? new Project { Code = "P7391" };
        var issues = _issues.All();
        var subs = ListSubprojects(project.Id);

        // 计划时间分布：只吃语义日期，文字计划（如"已下图"）单独计数，不混进日期轴。
        var planDates = issues
            .SelectMany(i => i.Stages)
            .Where(s => s.DueDate.HasValue)
            .GroupBy(s => s.DueDate!.Value)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Date)
            .Take(5)
            .ToList();
        var maxPlanCount = planDates.Count == 0 ? 0 : planDates[0].Count;

        // 倒挂的装配计划集中情况：全部同一天 / 分散在多个日期，两种说法不能混。
        var invertedDates = issues
            .Where(i => i.IsInverted && i.AssemblyStage?.DueDate != null)
            .GroupBy(i => i.AssemblyStage!.DueDate!.Value)
            .OrderByDescending(g => g.Count())
            .ToList();
        var invertedDateText = invertedDates.Count switch
        {
            0 => "倒挂行没有可比较的装配计划日期",
            1 => $"倒挂的装配计划全部在 {invertedDates[0].Key:MM-dd}（{invertedDates[0].Count()} 条）",
            _ => $"倒挂的装配计划压在 {invertedDates.Count} 个日期，最多 {invertedDates[0].Key:MM-dd}（{invertedDates[0].Count()} 条）"
        };

        var topStatus = issues
            .GroupBy(i => i.Status)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        var completionFilled = issues.Count(i =>
            !string.IsNullOrWhiteSpace(i.CompletionText) && i.CompletionText.Trim() != "/");

        return new ReportData
        {
            TotalIssues = issues.Count,
            TotalMachines = issues.Select(i => i.MachineId).Distinct().Count(),
            InvertedCount = issues.Count(i => i.IsInverted),
            OverdueCount = issues.Count(i => i.OverdueDays > 0),
            ClosedCount = issues.Count(i => i.IsClosed),
            PhotoCount = issues.Sum(i => i.PhotoCount),
            PhotoIssueCount = issues.Count(i => i.PhotoCount > 0),
            PhotoLinkedRate = issues.Count == 0
                ? 0
                : issues.Count(i => i.PhotoCount > 0) * 100d / issues.Count,
            SubprojectCount = subs.Count,
            AsOfDisplay = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            StageFills = BuildStageFills(issues),
            OwnerLoads = BuildOwnerLoads(issues),
            Subprojects = BuildCards(issues, subs, project.Id),
            PhotoBuckets = BuildPhotoBuckets(issues, subs),
            CommonIssues = BuildCommonIssues(issues),
            UnassignedMachineCount = issues.Where(i => i.SubprojectId == null)
                .Select(i => i.MachineId).Distinct().Count(),
            UnassignedIssueCount = issues.Count(i => i.SubprojectId == null),
            StageTaskCount = issues.SelectMany(i => i.Stages).Count(s => StageRules.HasOwner(s)),
            PlanDateBuckets = planDates
                .Select(x => new PlanDateBucket
                {
                    DateText = x.Date.ToString("MM-dd"),
                    Count = x.Count,
                    Ratio = maxPlanCount == 0 ? 0 : x.Count * 100d / maxPlanCount
                })
                .ToList(),
            TextPlanCount = issues.SelectMany(i => i.Stages)
                .Count(s => !string.IsNullOrWhiteSpace(s.DueRaw)
                            && !RawDateParser.IsNotApplicable(s.DueRaw)
                            && !s.DueDate.HasValue),
            InvertedPlanDateText = invertedDateText,
            TopStatusText = topStatus == null
                ? "库里还没有问题"
                : $"「{topStatus.Key}」{topStatus.Count()} 条为最多；完成情况 {completionFilled} / {issues.Count}"
        };
    }

    /// <summary>分项目详情：机台清单及其问题/倒挂/照片汇总。</summary>
    public SubprojectDetail? GetSubprojectDetail(string? subprojectCode)
    {
        var project = GetMain();
        if (project == null) return null;

        var isUnassigned = string.IsNullOrWhiteSpace(subprojectCode) ||
                           subprojectCode == UnassignedCode;
        var sub = isUnassigned
            ? null
            : ListSubprojects(project.Id).FirstOrDefault(s => s.Code == subprojectCode);
        if (!isUnassigned && sub == null) return null;

        var machines = ListMachines(project.Id, sub?.Id, isUnassigned);
        var issues = _issues.All().Where(i =>
            isUnassigned ? i.SubprojectId == null : i.SubprojectId == sub!.Id).ToList();
        var byMachine = issues.GroupBy(i => i.MachineId).ToDictionary(g => g.Key, g => g.ToList());

        var summaries = new List<MachineSummary>();
        foreach (var machine in machines)
        {
            var list = byMachine.GetValueOrDefault(machine.Id) ?? new List<Issue>();
            var owners = list
                .Select(i => i.AssemblyStage?.OwnerText ?? "")
                .Where(o => !string.IsNullOrWhiteSpace(o) && !RawDateParser.IsNotApplicable(o))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            summaries.Add(new MachineSummary
            {
                MachineId = machine.Id,
                Code = machine.Code,
                SubprojectId = machine.SubprojectId,
                SubprojectCode = sub?.Code ?? UnassignedCode,
                IssueCount = list.Count,
                InvertedCount = list.Count(i => i.IsInverted),
                PhotoCount = list.Sum(i => i.PhotoCount),
                AssemblyOwners = string.Join(" / ", owners),
                Status = machine.Status
            });
        }

        return new SubprojectDetail
        {
            Subproject = sub,
            Code = sub?.Code ?? UnassignedCode,
            Name = sub?.Name ?? "待确认归属",
            MachineRange = sub?.MachineRange ?? "机台号格式异常，需人工确认",
            MachineCount = summaries.Count,
            IssueCount = issues.Count,
            InvertedCount = issues.Count(i => i.IsInverted),
            PhotoCount = issues.Sum(i => i.PhotoCount),
            OwnerCount = CountOwners(issues),
            AssemblyFilled = issues.Count(i => i.AssemblyStage != null &&
                                                StageRules.HasOwner(i.AssemblyStage)),
            IsUnassigned = isUnassigned,
            Machines = summaries
        };
    }

    /// <summary>责任人负载（按阶段任务数），供报表页画条形图。</summary>
    public List<OwnerLoad> BuildOwnerLoads(List<Issue> issues)
    {
        var stages = issues
            .SelectMany(i => i.Stages.Select(s => (Issue: i, Stage: s)))
            .SelectMany(x => TextUtil.SplitOwners(x.Stage.OwnerText)
                .Select(owner => (x.Issue, Owner: owner)))
            .Where(x => !RawDateParser.IsNotApplicable(x.Owner))
            .ToList();

        var max = stages.Count == 0
            ? 0
            : stages.GroupBy(x => x.Owner, StringComparer.Ordinal).Max(g => g.Count());

        return stages
            .GroupBy(x => x.Owner, StringComparer.Ordinal)
            .Select(g =>
            {
                var ids = g.Select(x => x.Issue.Id).Distinct(StringComparer.Ordinal).ToList();
                var overdue = g.Select(x => x.Issue)
                    .DistinctBy(i => i.Id)
                    .Count(i => i.OverdueDays > 0);
                return new OwnerLoad
                {
                    Name = g.Key,
                    StageTaskCount = g.Count(),
                    IssueCount = ids.Count,
                    OverdueCount = overdue,
                    Ratio = max == 0 ? 0 : g.Count() * 100d / max
                };
            })
            .OrderByDescending(o => o.StageTaskCount)
            .ThenBy(o => o.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>四阶段填写率。完成/不适用数单独给出，供报表做堆叠条。</summary>
    public List<StageFill> BuildStageFills(List<Issue> issues)
    {
        var result = new List<StageFill>();
        foreach (var code in StageCodes.All)
        {
            var stages = issues
                .Select(i => i.Stage(code))
                .Where(s => s != null)
                .Select(s => s!)
                .ToList();

            result.Add(new StageFill
            {
                Code = code,
                Total = issues.Count,
                OwnerFilled = stages.Count(s =>
                    !string.IsNullOrWhiteSpace(s.OwnerText) &&
                    !RawDateParser.IsNotApplicable(s.OwnerText)),
                DueFilled = stages.Count(s => s.DueDate.HasValue),
                DoneCount = stages.Count(s => s.State == StageState.Done),
                NaCount = stages.Count(s => s.State == StageState.NotApplicable)
            });
        }
        return result;
    }

    private List<SubprojectCard> BuildCards(List<Issue> issues, List<Subproject> subs, string projectId)
    {
        var groups = issues
            .GroupBy(IssueBucket)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var cards = new List<SubprojectCard>();

        foreach (var sub in subs.OrderBy(s => s.SortNo))
        {
            var list = groups.GetValueOrDefault(sub.Code) ?? new List<Issue>();
            cards.Add(new SubprojectCard
            {
                SubprojectId = sub.Id,
                Code = sub.Code,
                Name = sub.Name,
                MachineRange = sub.MachineRange,
                MachineCount = list.Select(i => i.MachineId).Distinct().Count(),
                IssueCount = list.Count,
                InvertedCount = list.Count(i => i.IsInverted),
                PhotoCount = list.Sum(i => i.PhotoCount),
                OwnerCount = CountOwners(list),
                FilledAssembly = list.Count(i => i.AssemblyStage != null &&
                                                  StageRules.HasOwner(i.AssemblyStage))
            });
        }

        var unknown = groups.GetValueOrDefault(UnassignedCode);
        if (unknown is { Count: > 0 })
        {
            var machineCount = ListMachines(projectId, null, unassigned: true).Count;
            cards.Add(new SubprojectCard
            {
                SubprojectId = null,
                Code = UnassignedCode,
                Name = "待确认归属",
                MachineRange = "机台号格式异常，需人工确认",
                MachineCount = machineCount == 0
                    ? unknown.Select(i => i.MachineId).Distinct().Count()
                    : machineCount,
                IssueCount = unknown.Count,
                InvertedCount = unknown.Count(i => i.IsInverted),
                PhotoCount = unknown.Sum(i => i.PhotoCount),
                OwnerCount = CountOwners(unknown),
                FilledAssembly = unknown.Count(i => i.AssemblyStage != null &&
                                                     StageRules.HasOwner(i.AssemblyStage)),
                IsUnassigned = true
            });
        }
        return cards;
    }

    private static List<PhotoBucket> BuildPhotoBuckets(List<Issue> issues, List<Subproject> subs)
    {
        var total = Math.Max(1, issues.Sum(i => i.PhotoCount));
        var buckets = new List<PhotoBucket>();
        foreach (var code in subs.OrderBy(s => s.SortNo).Select(s => s.Code)
                     .Concat(new[] { UnassignedCode }))
        {
            var list = issues.Where(i => IssueBucket(i) == code).ToList();
            var count = list.Sum(i => i.PhotoCount);
            if (code == UnassignedCode && count == 0) continue;
            buckets.Add(new PhotoBucket
            {
                Label = code,
                Count = count,
                IssueCount = list.Count(i => i.PhotoCount > 0),
                Ratio = count * 100d / total
            });
        }
        return buckets;
    }

    private static List<CommonIssue> BuildCommonIssues(List<Issue> issues) =>
        issues
            .Where(i => !string.IsNullOrWhiteSpace(i.Category))
            .GroupBy(i => i.Category, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new CommonIssue
            {
                Title = g.Key,
                Count = g.Count(),
                Machines = string.Join("、", g.Select(i => i.MachineCode)
                    .Distinct(StringComparer.Ordinal)
                    .Take(4))
            })
            .ToList();

    private static int CountOwners(List<Issue> issues) =>
        issues.SelectMany(i => i.Stages)
            .SelectMany(s => TextUtil.SplitOwners(s.OwnerText))
            .Where(o => !RawDateParser.IsNotApplicable(o))
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static string IssueBucket(Issue issue) =>
        string.IsNullOrWhiteSpace(issue.SubprojectCode) ? UnassignedCode : issue.SubprojectCode!;

    private static int PriorityRank(string? priority) => priority switch
    {
        "高" => 0,
        "低" => 2,
        _ => 1
    };

    private int CountPendingSync()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM attachments WHERE sync_state IN ('local','pending')";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private string LastImportDisplay()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT created_at FROM import_batches ORDER BY created_at DESC LIMIT 1";
        var value = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(value))
            value = Database.ReadMeta(connection, "seeded_at");
        var parsed = AppTime.ParseIso(value);
        return parsed.HasValue
            ? parsed.Value.ToLocalTime().ToString("MM-dd HH:mm")
            : "尚未导入";
    }

    /// <summary>按机台号查机台（不区分分项目）。</summary>
    public Machine? FindMachine(string projectId, string code)
    {
        var normalized = TextUtil.Normalize(code);
        if (normalized.Length == 0) return null;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM machines WHERE project_id=$project AND code=$code";
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$code", normalized);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapMachine(reader) : null;
    }

    /// <summary>
    /// 导入时遇到原表里的新机台：自动建档并按机台号规则归组；
    /// 格式异常（如 P73913118）不猜归属，直接进"待确认"。
    /// </summary>
    public Machine GetOrCreateMachine(string projectId, string code, string now)
    {
        var existing = FindMachine(projectId, code);
        if (existing != null) return existing;

        var normalized = TextUtil.Normalize(code);
        if (normalized.Length == 0)
            throw new InvalidOperationException("机台号不能为空。");

        var subprojectCode = SubprojectRules.Resolve(normalized);
        string? subprojectId = null;
        if (subprojectCode != null)
        {
            using var lookup = _database.Open();
            using var find = lookup.CreateCommand();
            find.CommandText = "SELECT id FROM subprojects WHERE project_id=$project AND code=$code";
            find.Parameters.AddWithValue("$project", projectId);
            find.Parameters.AddWithValue("$code", subprojectCode);
            subprojectId = find.ExecuteScalar() as string;
        }

        var machine = new Machine
        {
            Id = "MCH-" + normalized,
            ProjectId = projectId,
            SubprojectId = subprojectId,
            Code = normalized,
            Name = normalized,
            SeqNo = TextUtil.MachineSeq(normalized),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO machines
                (id, project_id, subproject_id, code, name, seq_no, status, created_at, updated_at, version)
            VALUES ($id, $project, $sub, $code, $name, $seq, 'active', $now, $now, 1)
            """;
        command.Parameters.AddWithValue("$id", machine.Id);
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.Add("$sub", (object?)machine.SubprojectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$code", machine.Code);
        command.Parameters.AddWithValue("$name", machine.Name);
        command.Parameters.Add("$seq", (object?)machine.SeqNo ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
        return machine;
    }

    private static Project MapProject(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        Code = reader.Str("code"),
        Name = reader.Str("name"),
        Customer = reader.Str("customer"),
        Status = reader.Str("status"),
        CreatedAt = reader.Str("created_at"),
        UpdatedAt = reader.Str("updated_at"),
        Version = reader.Int("version")
    };

    private static Subproject MapSubproject(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        ProjectId = reader.Str("project_id"),
        Code = reader.Str("code"),
        Name = reader.Str("name"),
        MachineRange = reader.Str("machine_range"),
        Status = reader.Str("status"),
        SortNo = reader.Int("sort_no"),
        CreatedAt = reader.Str("created_at"),
        UpdatedAt = reader.Str("updated_at"),
        Version = reader.Int("version")
    };

    private static Machine MapMachine(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        ProjectId = reader.Str("project_id"),
        SubprojectId = reader.StrOrNull("subproject_id"),
        Code = reader.Str("code"),
        Name = reader.Str("name"),
        SeqNo = reader.IntOrNull("seq_no"),
        Status = reader.Str("status"),
        CreatedAt = reader.Str("created_at"),
        UpdatedAt = reader.Str("updated_at"),
        Version = reader.Int("version")
    };
}
