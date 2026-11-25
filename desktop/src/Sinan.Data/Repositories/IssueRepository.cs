using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sinan.Core.Models;
using Sinan.Core.Utils;

namespace Sinan.Data.Repositories;

/// <summary>
/// 问题仓储。列表/详情/新建/阶段推进/评论全在这里，ViewModel 不接触 SQL。
///
/// 关键口径：
/// - due_date 与 due_raw 双写：录入时解析成功才写 due_date，原文永远照存；
/// - stage 状态由"责任人 + 原文 + 完成情况"推导，也允许人工选择；
/// - 每次写业务字段都写 activity_log（字段级 old/new）与 change_log（同步增量）；
/// - 更新带 version 乐观锁，影响 0 行即抛冲突，禁止静默覆盖。
/// </summary>
public sealed class IssueRepository
{
    private static readonly JsonSerializerOptions ChangeJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Database _database;

    public IssueRepository(Database database) => _database = database;

    public List<Issue> List(IssueFilter filter)
    {
        using var connection = _database.Open();
        var sql = """
            SELECT i.*, m.code AS machine_code, sp.code AS subproject_code
            FROM issues i
            JOIN machines m ON m.id = i.machine_id
            LEFT JOIN subprojects sp ON sp.id = i.subproject_id
            WHERE i.deleted_at IS NULL
            """;
        var conditions = new List<string>();
        void Param(SqliteParameterCollection p)
        {
            if (!string.IsNullOrWhiteSpace(filter.ProjectId))
            {
                conditions.Add("i.project_id=$project");
                p.AddWithValue("$project", filter.ProjectId);
            }
            if (!string.IsNullOrWhiteSpace(filter.SubprojectId))
            {
                conditions.Add("i.subproject_id=$sub");
                p.AddWithValue("$sub", filter.SubprojectId);
            }
            if (!string.IsNullOrWhiteSpace(filter.MachineId))
            {
                conditions.Add("i.machine_id=$machine");
                p.AddWithValue("$machine", filter.MachineId);
            }
            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                conditions.Add("i.status=$status");
                p.AddWithValue("$status", filter.Status);
            }
            if (!filter.IncludeClosed)
            {
                conditions.Add("i.status NOT IN ('已关闭','已取消')");
            }
            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                conditions.Add("""
                    (i.title LIKE $kw OR i.solution LIKE $kw OR m.code LIKE $kw OR i.seq_no LIKE $kw)
                    """);
                p.AddWithValue("$kw", $"%{filter.Keyword.Trim()}%");
            }
        }

        var issues = LoadIssues(connection, null, sql, conditions, Param, filter.Limit);
        if (ShouldFilterInMemory(filter))
        {
            issues = issues.Where(i => MatchesInMemory(i, filter)).ToList();
        }
        return issues
            .OrderBy(i => i.SourceRow ?? int.MaxValue)
            .ThenBy(i => i.SeqNo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<Issue> All()
    {
        using var connection = _database.Open();
        return LoadIssues(connection, null, """
            SELECT i.*, m.code AS machine_code, sp.code AS subproject_code
            FROM issues i
            JOIN machines m ON m.id = i.machine_id
            LEFT JOIN subprojects sp ON sp.id = i.subproject_id
            WHERE i.deleted_at IS NULL
            """, new List<string>(), null, 0)
            .OrderBy(i => i.SourceRow ?? int.MaxValue)
            .ToList();
    }

    public Issue? Get(string id)
    {
        using var connection = _database.Open();
        var issues = LoadIssues(connection, null, """
            SELECT i.*, m.code AS machine_code, sp.code AS subproject_code
            FROM issues i
            JOIN machines m ON m.id = i.machine_id
            LEFT JOIN subprojects sp ON sp.id = i.subproject_id
            WHERE i.id=$id AND i.deleted_at IS NULL
            """, new List<string> { "i.id=$id" },
            p => p.AddWithValue("$id", id), 1);
        var issue = issues.FirstOrDefault();
        if (issue == null) return null;

        LoadAttachments(connection, null, issue);
        LoadComments(connection, issue);
        LoadActivity(connection, issue);
        return issue;
    }

    public Issue Create(IssueDraft draft, User user)
    {
        if (string.IsNullOrWhiteSpace(draft.Title))
            throw new InvalidOperationException("问题描述不能为空。");
        if (string.IsNullOrWhiteSpace(draft.MachineId))
            throw new InvalidOperationException("请选择机台。");

        var id = string.IsNullOrWhiteSpace(draft.Id) ? IdGen.NewIssueId() : draft.Id.Trim();
        var now = AppTime.UtcNowIso();
        var year = AppTime.Today.Year;

        var stages = BuildStages(draft, year);

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO issues
                    (id, project_id, subproject_id, machine_id, seq_no, title, solution, status, priority,
                     category, completion_text, closed_at, source_batch_id, source_row, created_by, updated_by,
                     created_at, updated_at, version, deleted_at)
                VALUES
                    ($id, $project, $sub, $machine, $seq, $title, $solution, $status, $priority,
                     $category, $completion, NULL, $batch, $row, $user, $user, $now, $now, 1, NULL)
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$project", draft.ProjectId);
            command.Parameters.Add("$sub", draft.SubprojectId);
            command.Parameters.AddWithValue("$machine", draft.MachineId);
            command.Parameters.AddWithValue("$seq", draft.SeqNo ?? "");
            command.Parameters.AddWithValue("$title", draft.Title.Trim());
            command.Parameters.AddWithValue("$solution", draft.Solution ?? "");
            command.Parameters.AddWithValue("$status", IssueStatuses.Normalize(draft.Status));
            command.Parameters.AddWithValue("$priority", string.IsNullOrWhiteSpace(draft.Priority) ? "中" : draft.Priority);
            command.Parameters.AddWithValue("$category", draft.Category ?? "");
            command.Parameters.AddWithValue("$completion", draft.CompletionText ?? "");
            command.Parameters.Add("$batch", (object?)draft.SourceBatchId ?? DBNull.Value);
            command.Parameters.Add("$row", (object?)draft.SourceRow ?? DBNull.Value);
            command.Parameters.AddWithValue("$user", user.Id);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }

        foreach (var stage in stages)
        {
            InsertStage(connection, transaction, id, stage, now);
        }

        Log(connection, transaction, "issue", id, "新建问题", "", draft.Title.Trim(), user.Id, now);
        transaction.Commit();

        var created = Get(id) ?? throw new InvalidOperationException("问题创建后读取失败。");
        WriteChange(created, "upsert");
        return created;
    }

    /// <summary>
    /// 保存单个阶段（推进弹窗）。传入 StageDraft.Code 指定阶段。
    /// 状态推导：已关闭/已取消的问题不被阶段编辑强行拉回；其余按四阶段重新推导。
    /// </summary>
    public Issue SaveStage(string issueId, StageDraft draft, User user)
    {
        var now = AppTime.UtcNowIso();
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var issue = LoadIssues(connection, transaction, """
            SELECT i.*, m.code AS machine_code, sp.code AS subproject_code
            FROM issues i
            JOIN machines m ON m.id = i.machine_id
            LEFT JOIN subprojects sp ON sp.id = i.subproject_id
            WHERE i.id=$id AND i.deleted_at IS NULL
            """, new List<string> { "i.id=$id" },
            p => p.AddWithValue("$id", issueId), 1).FirstOrDefault()
            ?? throw new InvalidOperationException("问题不存在或已删除。");

        var stage = issue.Stage(draft.Code)
            ?? throw new InvalidOperationException($"阶段 {draft.Code} 不存在。");

        var dueRaw = draft.DueRaw?.Trim() ?? "";
        var dueDate = RawDateParser.Parse(dueRaw, AppTime.Today.Year);
        var state = draft.State;
        var owner = draft.OwnerText?.Trim() ?? "";

        // 变更原因（界面在"改计划日"时强制填写）：写进计划变更那条审计的备注，
        // 让"为什么把 11-15 改成 11-01"能直接从留痕里读出来，而不是只剩两个日期。
        var reason = draft.ChangeReason?.Trim() ?? "";
        var dueNote = string.IsNullOrWhiteSpace(reason) ? dueRaw : $"{dueRaw}（原因：{reason}）";

        if (stage.OwnerText != owner)
            Log(connection, transaction, "issue", issueId, $"{stage.Label}责任人", stage.OwnerText, owner, user.Id, now);
        if (stage.DueRaw != dueRaw)
            Log(connection, transaction, "issue", issueId, $"{stage.Label}计划", stage.DueRaw, dueNote, user.Id, now);
        if (stage.State != state)
            Log(connection, transaction, "issue", issueId, $"{stage.Label}状态", stage.StateDisplay, StageStates.Display(state), user.Id, now);

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE issue_stages
                SET owner_text=$owner, due_raw=$raw, due_date=$due, state=$state,
                    actual_date=$actual, remark=$remark, updated_at=$now, version=version+1
                WHERE issue_id=$issue AND stage_code=$code AND version=$version
                """;
            command.Parameters.AddWithValue("$owner", owner);
            command.Parameters.AddWithValue("$raw", dueRaw);
            command.Parameters.Add("$due", AppTime.ToDb(dueDate));
            command.Parameters.AddWithValue("$state", StageStates.ToDb(state));
            command.Parameters.Add("$actual", ParseActualDate(draft.ActualDateRaw));
            command.Parameters.AddWithValue("$remark", draft.Remark ?? "");
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$issue", issueId);
            command.Parameters.AddWithValue("$code", draft.Code);
            command.Parameters.AddWithValue("$version", draft.Version);
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException("该阶段已被其他窗口修改，请刷新后再试。");
        }

        // 用新值覆盖内存副本后推导问题状态，保证与库内一致。
        stage.OwnerText = owner;
        stage.DueRaw = dueRaw;
        stage.DueDate = dueDate;
        stage.State = state;
        stage.Remark = draft.Remark ?? "";

        if (!IssueStatuses.IsTerminal(issue.Status))
        {
            var newStatus = StageRules.DeriveStatus(issue.Stages, issue.CompletionText);
            if (newStatus != issue.Status)
            {
                Log(connection, transaction, "issue", issueId, "状态", issue.Status, newStatus, user.Id, now);
                using var statusCommand = connection.CreateCommand();
                statusCommand.Transaction = transaction;
                statusCommand.CommandText = """
                    UPDATE issues SET status=$status, updated_by=$user, updated_at=$now, version=version+1
                    WHERE id=$id
                    """;
                statusCommand.Parameters.AddWithValue("$status", newStatus);
                statusCommand.Parameters.AddWithValue("$user", user.Id);
                statusCommand.Parameters.AddWithValue("$now", now);
                statusCommand.Parameters.AddWithValue("$id", issueId);
                statusCommand.ExecuteNonQuery();
            }
            else
            {
                TouchIssue(connection, transaction, issueId, user.Id, now);
            }
        }
        else
        {
            TouchIssue(connection, transaction, issueId, user.Id, now);
        }

        transaction.Commit();
        var updated = Get(issueId) ?? throw new InvalidOperationException("保存后读取问题失败。");
        WriteChange(updated, "upsert");
        return updated;
    }

    /// <summary>
    /// Excel 增量导入：按 Excel 值覆盖主表文本与四阶段。
    ///
    /// 与界面编辑的差别：
    /// - 主表只覆盖 title / solution / completion_text，优先级、分类、状态不来自原表；
    /// - 阶段原文没变时保留库内状态（人工推进的 done 不会被导入打回）；
    /// - 原文变了才按原表语义重算状态（`/` → 不适用，"已下图" → 已完成）；
    /// - 全字段带 version 校验，导入与界面同时改会抛冲突而不是静默覆盖。
    /// </summary>
    public Issue ApplyImported(IssueDraft draft, User user)
    {
        if (string.IsNullOrWhiteSpace(draft.Id))
            throw new InvalidOperationException("导入更新缺少问题 ID。");

        var now = AppTime.UtcNowIso();
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var issue = LoadIssues(connection, transaction, """
            SELECT i.*, m.code AS machine_code, sp.code AS subproject_code
            FROM issues i
            JOIN machines m ON m.id = i.machine_id
            LEFT JOIN subprojects sp ON sp.id = i.subproject_id
            WHERE i.id=$id AND i.deleted_at IS NULL
            """, new List<string> { "i.id=$id" },
            p => p.AddWithValue("$id", draft.Id), 1).FirstOrDefault()
            ?? throw new InvalidOperationException("问题不存在或已删除。");

        var title = draft.Title?.Trim() ?? "";
        if (title.Length == 0) throw new InvalidOperationException("问题描述不能为空。");

        if (issue.Title != title)
            Log(connection, transaction, "issue", issue.Id, "问题描述", issue.Title, title, user.Id, now);
        if (issue.Solution != draft.Solution)
            Log(connection, transaction, "issue", issue.Id, "解决措施", issue.Solution, draft.Solution, user.Id, now);
        if (issue.CompletionText != draft.CompletionText)
            Log(connection, transaction, "issue", issue.Id, "完成情况", issue.CompletionText, draft.CompletionText, user.Id, now);

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE issues
                SET title=$title, solution=$solution, completion_text=$completion,
                    source_batch_id=COALESCE($batch, source_batch_id),
                    source_row=COALESCE($row, source_row),
                    updated_by=$user, updated_at=$now, version=version+1
                WHERE id=$id
                """;
            update.Parameters.AddWithValue("$title", title);
            update.Parameters.AddWithValue("$solution", draft.Solution ?? "");
            update.Parameters.AddWithValue("$completion", draft.CompletionText ?? "");
            update.Parameters.Add("$batch", (object?)draft.SourceBatchId ?? DBNull.Value);
            update.Parameters.Add("$row", (object?)draft.SourceRow ?? DBNull.Value);
            update.Parameters.AddWithValue("$user", user.Id);
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$id", issue.Id);
            update.ExecuteNonQuery();
        }

        foreach (var code in StageCodes.All)
        {
            var incoming = code switch
            {
                StageCodes.Plan => draft.Plan,
                StageCodes.Design => draft.Design,
                StageCodes.Material => draft.Material,
                _ => draft.Assembly
            };
            var stage = issue.Stage(code)
                ?? throw new InvalidOperationException($"问题缺少阶段 {code}。");
            var owner = incoming.OwnerText?.Trim() ?? "";
            var dueRaw = incoming.DueRaw?.Trim() ?? "";
            if (stage.OwnerText == owner && stage.DueRaw == dueRaw) continue;

            var dueDate = RawDateParser.Parse(dueRaw, AppTime.Today.Year);
            var state = StageRules.InferState(owner, dueRaw);

            if (stage.OwnerText != owner)
                Log(connection, transaction, "issue", issue.Id, $"{stage.Label}责任人", stage.OwnerText, owner, user.Id, now);
            if (stage.DueRaw != dueRaw)
                Log(connection, transaction, "issue", issue.Id, $"{stage.Label}计划", stage.DueRaw, dueRaw, user.Id, now);
            if (stage.State != state)
                Log(connection, transaction, "issue", issue.Id, $"{stage.Label}状态", stage.StateDisplay, StageStates.Display(state), user.Id, now);

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE issue_stages
                SET owner_text=$owner, due_raw=$raw, due_date=$due, state=$state, updated_at=$now, version=version+1
                WHERE issue_id=$issue AND stage_code=$code AND version=$version
                """;
            command.Parameters.AddWithValue("$owner", owner);
            command.Parameters.AddWithValue("$raw", dueRaw);
            command.Parameters.Add("$due", AppTime.ToDb(dueDate));
            command.Parameters.AddWithValue("$state", StageStates.ToDb(state));
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$issue", issue.Id);
            command.Parameters.AddWithValue("$code", code);
            command.Parameters.AddWithValue("$version", stage.Version);
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException($"{stage.Label}已被其他窗口修改，导入已停止该行。");

            stage.OwnerText = owner;
            stage.DueRaw = dueRaw;
            stage.DueDate = dueDate;
            stage.State = state;
        }

        if (!IssueStatuses.IsTerminal(issue.Status))
        {
            var newStatus = StageRules.DeriveStatus(issue.Stages, draft.CompletionText);
            if (newStatus != issue.Status)
            {
                Log(connection, transaction, "issue", issue.Id, "状态", issue.Status, newStatus, user.Id, now);
                using var statusCommand = connection.CreateCommand();
                statusCommand.Transaction = transaction;
                statusCommand.CommandText = """
                    UPDATE issues SET status=$status, updated_by=$user, updated_at=$now, version=version+1
                    WHERE id=$id
                    """;
                statusCommand.Parameters.AddWithValue("$status", newStatus);
                statusCommand.Parameters.AddWithValue("$user", user.Id);
                statusCommand.Parameters.AddWithValue("$now", now);
                statusCommand.Parameters.AddWithValue("$id", issue.Id);
                statusCommand.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        var updated = Get(issue.Id) ?? throw new InvalidOperationException("导入更新后读取问题失败。");
        WriteChange(updated, "upsert");
        return updated;
    }

    public Issue UpdateDetails(string issueId, string title, string solution, string completionText,
        string priority, string category, User user)
    {
        var now = AppTime.UtcNowIso();
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        var before = LoadIssues(connection, transaction, """
            SELECT i.*, m.code AS machine_code, sp.code AS subproject_code
            FROM issues i JOIN machines m ON m.id=i.machine_id
            LEFT JOIN subprojects sp ON sp.id=i.subproject_id
            WHERE i.id=$id AND i.deleted_at IS NULL
            """, new List<string> { "i.id=$id" }, p => p.AddWithValue("$id", issueId), 1)
            .FirstOrDefault() ?? throw new InvalidOperationException("问题不存在或已删除。");

        if (before.Title != title)
            Log(connection, transaction, "issue", issueId, "问题描述", before.Title, title, user.Id, now);
        if (before.Solution != solution)
            Log(connection, transaction, "issue", issueId, "解决措施", before.Solution, solution, user.Id, now);
        if (before.CompletionText != completionText)
            Log(connection, transaction, "issue", issueId, "完成情况", before.CompletionText, completionText, user.Id, now);

        var status = before.Status;
        if (!IssueStatuses.IsTerminal(status) && !string.IsNullOrWhiteSpace(completionText))
            status = IssueStatuses.Verifying;

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE issues SET title=$title, solution=$solution, completion_text=$completion,
                    priority=$priority, category=$category, status=$status,
                    updated_by=$user, updated_at=$now, version=version+1
                WHERE id=$id AND version=$version
                """;
            command.Parameters.AddWithValue("$title", title.Trim());
            command.Parameters.AddWithValue("$solution", solution ?? "");
            command.Parameters.AddWithValue("$completion", completionText ?? "");
            command.Parameters.AddWithValue("$priority", priority ?? "中");
            command.Parameters.AddWithValue("$category", category ?? "");
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$user", user.Id);
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$id", issueId);
            command.Parameters.AddWithValue("$version", before.Version);
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException("该问题已被其他窗口修改，请刷新后再试。");
        }

        transaction.Commit();
        var updated = Get(issueId) ?? throw new InvalidOperationException("保存后读取问题失败。");
        WriteChange(updated, "upsert");
        return updated;
    }

    public Issue SetStatus(string issueId, string status, User user)
    {
        status = IssueStatuses.Normalize(status);
        var now = AppTime.UtcNowIso();
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        var before = LoadIssues(connection, transaction, """
            SELECT i.*, m.code AS machine_code, sp.code AS subproject_code
            FROM issues i JOIN machines m ON m.id=i.machine_id
            LEFT JOIN subprojects sp ON sp.id=i.subproject_id
            WHERE i.id=$id AND i.deleted_at IS NULL
            """, new List<string> { "i.id=$id" }, p => p.AddWithValue("$id", issueId), 1)
            .FirstOrDefault() ?? throw new InvalidOperationException("问题不存在或已删除。");

        Log(connection, transaction, "issue", issueId, "状态", before.Status, status, user.Id, now);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE issues SET status=$status,
                    closed_at=CASE WHEN $status IN ('已关闭','已取消') THEN $now ELSE NULL END,
                    updated_by=$user, updated_at=$now, version=version+1
                WHERE id=$id AND version=$version
                """;
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$user", user.Id);
            command.Parameters.AddWithValue("$id", issueId);
            command.Parameters.AddWithValue("$version", before.Version);
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException("该问题已被其他窗口修改，请刷新后再试。");
        }

        transaction.Commit();
        var updated = Get(issueId) ?? throw new InvalidOperationException("状态变更后读取失败。");
        WriteChange(updated, "upsert");
        return updated;
    }

    public Comment AddComment(string issueId, string content, User user)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("评论内容不能为空。");
        var now = AppTime.UtcNowIso();
        var comment = new Comment
        {
            Id = IdGen.NewCommentId(),
            IssueId = issueId,
            UserId = user.Id,
            UserName = user.Name,
            Content = content.Trim(),
            CreatedAt = now
        };

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO comments(id, issue_id, user_id, content, created_at)
                VALUES ($id, $issue, $user, $content, $now)
                """;
            command.Parameters.AddWithValue("$id", comment.Id);
            command.Parameters.AddWithValue("$issue", issueId);
            command.Parameters.AddWithValue("$user", user.Id);
            command.Parameters.AddWithValue("$content", comment.Content);
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }
        Log(connection, transaction, "issue", issueId, "评论", "", comment.Content, user.Id, now);
        TouchIssue(connection, transaction, issueId, user.Id, now);
        transaction.Commit();
        return comment;
    }

    public void Delete(string issueId, User user)
    {
        var now = AppTime.UtcNowIso();
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE issues SET deleted_at=$now, updated_by=$user, updated_at=$now, version=version+1
                WHERE id=$id AND deleted_at IS NULL
                """;
            command.Parameters.AddWithValue("$now", now);
            command.Parameters.AddWithValue("$user", user.Id);
            command.Parameters.AddWithValue("$id", issueId);
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException("问题不存在或已删除。");
        }
        Log(connection, transaction, "issue", issueId, "删除问题", "", "软删除", user.Id, now);
        transaction.Commit();
        WriteChange(new Issue { Id = issueId, DeletedAt = now }, "delete");
    }

    // ---------------------------------------------------------------------
    // 查询装配
    // ---------------------------------------------------------------------

    private static List<Issue> LoadIssues(SqliteConnection connection, SqliteTransaction? transaction,
        string baseSql, List<string> conditions, Action<SqliteParameterCollection>? addParams, int limit)
    {
        // 必须先让 addParams 填充 conditions，再拼 SQL：
        // 早期版本先判断 conditions.Count 再调用 addParams，导致 List() 里动态添加的
        // 状态/关键字/分项目条件全部失效（SQL 只带基础 WHERE，返回全量后被内存过滤掩盖）。
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        addParams?.Invoke(command.Parameters);

        var sql = baseSql;
        if (conditions.Count > 0)
            sql += " AND " + string.Join(" AND ", conditions);
        sql += " ORDER BY i.source_row IS NULL, i.source_row, i.seq_no";
        if (limit > 0) sql += $" LIMIT {limit}";

        var issues = new List<Issue>();
        var index = new Dictionary<string, Issue>(StringComparer.Ordinal);

        command.CommandText = sql;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var issue = MapIssue(reader);
                issues.Add(issue);
                index[issue.Id] = issue;
            }
        }

        if (issues.Count == 0) return issues;
        LoadStages(connection, transaction, issues, index);
        LoadAttachmentCounts(connection, transaction, issues);
        return issues;
    }

    private static Issue MapIssue(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        ProjectId = reader.Str("project_id"),
        SubprojectId = reader.StrOrNull("subproject_id"),
        MachineId = reader.Str("machine_id"),
        MachineCode = reader.Str("machine_code"),
        SubprojectCode = reader.StrOrNull("subproject_code"),
        SeqNo = reader.Str("seq_no"),
        Title = reader.Str("title"),
        Solution = reader.Str("solution"),
        Status = IssueStatuses.Normalize(reader.Str("status")),
        Priority = reader.Str("priority"),
        Category = reader.Str("category"),
        CompletionText = reader.Str("completion_text"),
        ClosedAt = reader.StrOrNull("closed_at"),
        SourceBatchId = reader.StrOrNull("source_batch_id"),
        SourceRow = reader.IntOrNull("source_row"),
        CreatedBy = reader.Str("created_by"),
        UpdatedBy = reader.Str("updated_by"),
        CreatedAt = reader.Str("created_at"),
        UpdatedAt = reader.Str("updated_at"),
        Version = reader.Int("version"),
        DeletedAt = reader.StrOrNull("deleted_at")
    };

    private static void LoadStages(SqliteConnection connection, SqliteTransaction? transaction,
        List<Issue> issues, Dictionary<string, Issue> index)
    {
        var ids = issues.Select(i => i.Id).ToList();
        var parameters = ids.Select((_, i) => "$id" + i).ToList();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT * FROM issue_stages
            WHERE issue_id IN ({string.Join(",", parameters)})
            ORDER BY CASE stage_code
                WHEN 'plan' THEN 0 WHEN 'design' THEN 1 WHEN 'material' THEN 2 ELSE 3 END
            """;
        for (var i = 0; i < ids.Count; i++) command.Parameters.AddWithValue(parameters[i], ids[i]);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var issueId = reader.Str("issue_id");
            if (!index.TryGetValue(issueId, out var issue)) continue;
            issue.Stages.Add(MapStage(reader));
        }
    }

    private static IssueStage MapStage(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        IssueId = reader.Str("issue_id"),
        Code = reader.Str("stage_code"),
        OwnerText = reader.Str("owner_text"),
        OwnerIds = reader.Str("owner_ids"),
        DueDate = reader.DateOnlyOrNull("due_date"),
        DueRaw = reader.Str("due_raw"),
        State = StageStates.Parse(reader.Str("state")),
        ActualDate = reader.DateOnlyOrNull("actual_date"),
        Remark = reader.Str("remark"),
        UpdatedAt = reader.Str("updated_at"),
        Version = reader.Int("version")
    };

    private static void LoadAttachmentCounts(SqliteConnection connection, SqliteTransaction? transaction,
        List<Issue> issues)
    {
        var ids = issues.Select(i => i.Id).ToList();
        var parameters = ids.Select((_, i) => "$id" + i).ToList();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT issue_id, COUNT(*) AS cnt FROM attachments
            WHERE issue_id IN ({string.Join(",", parameters)})
            GROUP BY issue_id
            """;
        for (var i = 0; i < ids.Count; i++) command.Parameters.AddWithValue(parameters[i], ids[i]);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) counts[reader.Str("issue_id")] = reader.Int("cnt");
        }
        foreach (var issue in issues)
            issue.AttachmentCount = counts.GetValueOrDefault(issue.Id);
    }

    private static void LoadAttachments(SqliteConnection connection, SqliteTransaction? transaction, Issue issue)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM attachments WHERE issue_id=$id ORDER BY created_at";
        command.Parameters.AddWithValue("$id", issue.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            issue.Attachments.Add(new Attachment
            {
                Id = reader.Str("id"),
                IssueId = reader.Str("issue_id"),
                FileName = reader.Str("file_name"),
                StoredName = reader.Str("stored_name"),
                StoredPath = reader.Str("stored_path"),
                ThumbPath = reader.Str("thumb_path"),
                Size = reader.Long("size"),
                Mime = reader.Str("mime"),
                Sha256 = reader.Str("sha256"),
                UploadedBy = reader.Str("uploaded_by"),
                CreatedAt = reader.Str("created_at"),
                IsDemo = reader.Bool("is_demo"),
                SyncState = reader.Str("sync_state")
            });
        }
    }

    private static void LoadComments(SqliteConnection connection, Issue issue)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.*, u.name AS user_name FROM comments c
            LEFT JOIN users u ON u.id=c.user_id
            WHERE c.issue_id=$id ORDER BY c.created_at DESC
            """;
        command.Parameters.AddWithValue("$id", issue.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            issue.Comments.Add(new Comment
            {
                Id = reader.Str("id"),
                IssueId = reader.Str("issue_id"),
                UserId = reader.Str("user_id"),
                UserName = reader.Str("user_name"),
                Content = reader.Str("content"),
                CreatedAt = reader.Str("created_at")
            });
        }
    }

    private static void LoadActivity(SqliteConnection connection, Issue issue)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.*, u.name AS user_name FROM activity_log a
            LEFT JOIN users u ON u.id=a.user_id
            WHERE a.entity='issue' AND a.entity_id=$id
            ORDER BY a.id DESC LIMIT 200
            """;
        command.Parameters.AddWithValue("$id", issue.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            issue.Activity.Add(new ActivityEntry
            {
                Id = reader.Long("id"),
                Entity = reader.Str("entity"),
                EntityId = reader.Str("entity_id"),
                Field = reader.Str("field"),
                OldValue = reader.Str("old_value"),
                NewValue = reader.Str("new_value"),
                UserId = reader.Str("user_id"),
                UserName = reader.Str("user_name"),
                CreatedAt = reader.Str("created_at")
            });
        }
    }

    // ---------------------------------------------------------------------
    // 写入辅助
    // ---------------------------------------------------------------------

    private static List<StageDraft> BuildStages(IssueDraft draft, int year)
    {
        var list = new List<StageDraft> { draft.Plan, draft.Design, draft.Material, draft.Assembly };
        foreach (var stage in list)
        {
            stage.Code = string.IsNullOrWhiteSpace(stage.Code) ? "plan" : stage.Code;
            stage.DueRaw = stage.DueRaw?.Trim() ?? "";
            stage.OwnerText = stage.OwnerText?.Trim() ?? "";
            if (stage.State == StageState.InProgress && !stage.HasPlan)
            {
                var parsed = RawDateParser.Parse(stage.DueRaw, year);
                if (parsed == null) stage.State = StageState.NotStarted;
            }
        }
        return list;
    }

    private static void InsertStage(SqliteConnection connection, SqliteTransaction transaction,
        string issueId, StageDraft draft, string now)
    {
        var dueDate = RawDateParser.Parse(draft.DueRaw, AppTime.Today.Year);
        var state = draft.State;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO issue_stages
                (id, issue_id, stage_code, owner_text, owner_ids, due_date, due_raw, state, actual_date, remark, updated_at, version)
            VALUES ($id, $issue, $code, $owner, '', $due, $raw, $state, $actual, $remark, $now, 1)
            """;
        command.Parameters.AddWithValue("$id", $"{issueId}-{draft.Code}");
        command.Parameters.AddWithValue("$issue", issueId);
        command.Parameters.AddWithValue("$code", draft.Code);
        command.Parameters.AddWithValue("$owner", draft.OwnerText ?? "");
        command.Parameters.Add("$due", AppTime.ToDb(dueDate));
        command.Parameters.AddWithValue("$raw", draft.DueRaw ?? "");
        command.Parameters.AddWithValue("$state", StageStates.ToDb(state));
        command.Parameters.Add("$actual", ParseActualDate(draft.ActualDateRaw));
        command.Parameters.AddWithValue("$remark", draft.Remark ?? "");
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static string? ParseActualDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var date = RawDateParser.Parse(raw, AppTime.Today.Year);
        return AppTime.ToDb(date);
    }

    private static void TouchIssue(SqliteConnection connection, SqliteTransaction transaction,
        string issueId, string userId, string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE issues SET updated_by=$user, updated_at=$now, version=version+1 WHERE id=$id
            """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$now", now);
        command.Parameters.AddWithValue("$id", issueId);
        command.ExecuteNonQuery();
    }

    private static void Log(SqliteConnection connection, SqliteTransaction transaction,
        string entity, string entityId, string field, string? oldValue, string? newValue,
        string userId, string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO activity_log(entity, entity_id, field, old_value, new_value, user_id, created_at)
            VALUES ($entity, $id, $field, $old, $new, $user, $now)
            """;
        command.Parameters.AddWithValue("$entity", entity);
        command.Parameters.AddWithValue("$id", entityId);
        command.Parameters.AddWithValue("$field", field);
        command.Parameters.AddWithValue("$old", oldValue ?? "");
        command.Parameters.AddWithValue("$new", newValue ?? "");
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private void WriteChange(Issue issue, string op)
    {
        try
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO change_log(entity, entity_id, op, payload, device_id, created_at)
                VALUES ('issue', $id, $op, $payload, 'desktop', $now)
                """;
            command.Parameters.AddWithValue("$id", issue.Id);
            command.Parameters.AddWithValue("$op", op);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(issue, ChangeJson));
            command.Parameters.AddWithValue("$now", AppTime.UtcNowIso());
            command.ExecuteNonQuery();
        }
        catch
        {
            // 增量日志失败不能阻断业务保存：下次全量同步仍能自愈。
        }
    }

    private static bool ShouldFilterInMemory(IssueFilter filter) =>
        filter.OnlyOverdue || filter.OnlyInverted || filter.OnlyWithPhoto ||
        !string.IsNullOrWhiteSpace(filter.Owner) || !string.IsNullOrWhiteSpace(filter.StageCode);

    private static bool MatchesInMemory(Issue issue, IssueFilter filter)
    {
        if (filter.OnlyOverdue && !issue.IsOverdue) return false;
        if (filter.OnlyInverted && !issue.IsInverted) return false;
        if (filter.OnlyWithPhoto && issue.PhotoCount == 0) return false;
        if (!filter.IncludeClosed && issue.IsClosed) return false;

        if (!string.IsNullOrWhiteSpace(filter.Owner))
        {
            var owner = filter.Owner.Trim();
            if (!issue.Stages.Any(s => s.OwnerText.Contains(owner, StringComparison.Ordinal))) return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.StageCode))
        {
            var stage = issue.Stage(filter.StageCode);
            if (stage == null) return false;
            if (filter.OnlyOverdue && !stage.IsOverdue) return false;
            if (!stage.HasOwner && !stage.HasPlan &&
                stage.State is StageState.NotStarted or StageState.NotApplicable) return false;
        }
        return true;
    }
}
