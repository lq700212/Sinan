using System.Text.Json;
using System.Text.Json.Serialization;
using Sinan.Core.Models;
using Sinan.Core.Utils;
using Microsoft.Data.Sqlite;

namespace Sinan.Data;

/// <summary>
/// 首启种子。数据来自客户原表解析产物 shared/db/seed/issues.seed.json（57 条），
/// 机台按真实编号规则归入 P7391-01/02/03；P73913118 保持"待确认"（subproject_id=NULL）。
///
/// 幂等规则：users 用 INSERT OR IGNORE 补齐；业务数据只在 issues 为空时种入，
/// 不覆盖现场后来改动。seed 标记写 app_meta，便于以后做"恢复样例库"。
/// </summary>
public static class SeedBootstrapper
{
    private const string SeedResource = "Sinan.Data.Seed.issues.seed.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void Seed(SqliteConnection connection)
    {
        SeedUsers(connection);

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM issues";
        var existing = Convert.ToInt32(countCommand.ExecuteScalar());
        if (existing > 0) return;

        var rows = LoadSeedRows();
        if (rows.Count == 0) return;

        using var transaction = connection.BeginTransaction();
        var now = AppTime.UtcNowIso();
        var adminId = FindUserId(connection, transaction, "admin") ?? "admin";

        var projectId = "PRJ-P7391";
        InsertProject(connection, transaction, projectId, now);

        var subprojectIds = new Dictionary<string, string>
        {
            [SubprojectRules.P01] = "SUB-P7391-01",
            [SubprojectRules.P02] = "SUB-P7391-02",
            [SubprojectRules.P03] = "SUB-P7391-03"
        };
        foreach (var (code, id) in subprojectIds)
        {
            var sort = code switch
            {
                SubprojectRules.P01 => 1,
                SubprojectRules.P02 => 2,
                _ => 3
            };
            var range = code switch
            {
                SubprojectRules.P01 => "P7391101 – P7391110",
                SubprojectRules.P02 => "P7391201 – P7391208",
                _ => "P7391301 – P7391312"
            };
            InsertSubproject(connection, transaction, id, projectId, code,
                SubprojectRules.DisplayName(code), range, sort, now);
        }

        var machines = rows
            .Select(r => TextUtil.Normalize(r.Machine))
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => TextUtil.MachineSeq(code) ?? int.MaxValue)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var machineCode in machines)
        {
            var subprojectCode = SubprojectRules.Resolve(machineCode);
            var subprojectId = subprojectCode != null && subprojectIds.TryGetValue(subprojectCode, out var sid)
                ? sid
                : null;
            InsertMachine(connection, transaction, machineCode, projectId, subprojectId,
                TextUtil.MachineSeq(machineCode), now);
        }

        foreach (var row in rows)
        {
            InsertIssue(connection, transaction, row, projectId, adminId, now);
        }

        Database.WriteMeta(connection, "seed_version", "1");
        Database.WriteMeta(connection, "seeded_at", now);
        transaction.Commit();
    }

    private static List<SeedIssue> LoadSeedRows()
    {
        var assembly = typeof(SeedBootstrapper).Assembly;
        using var stream = assembly.GetManifestResourceStream(SeedResource)
            ?? throw new InvalidOperationException($"缺少嵌入资源 {SeedResource}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<List<SeedIssue>>(json, JsonOptions) ?? new List<SeedIssue>();
    }

    private static void SeedUsers(SqliteConnection connection)
    {
        var now = AppTime.UtcNowIso();
        InsertUser(connection, "USR-admin", "admin", "系统管理员", "admin", "信息部", now);
        InsertUser(connection, "USR-engineer", "engineer", "张工", "engineer", "工程部", now);
    }

    private static void InsertUser(SqliteConnection connection, string id, string employeeNo,
        string name, string role, string dept, string now)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO users
                (id, employee_no, name, password_hash, role, dept, phone, active, created_at, updated_at)
            VALUES ($id, $no, $name, $hash, $role, $dept, '', 1, $now, $now)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$no", employeeNo);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$hash", PasswordHasher.Hash("123456"));
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$dept", dept);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static string? FindUserId(SqliteConnection connection, SqliteTransaction transaction, string employeeNo)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM users WHERE employee_no=$no";
        command.Parameters.AddWithValue("$no", employeeNo);
        return command.ExecuteScalar() as string;
    }

    private static void InsertProject(SqliteConnection connection, SqliteTransaction transaction,
        string id, string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO projects(id, code, name, customer, status, created_at, updated_at, version)
            VALUES ($id, 'P7391', 'P7391 安装调试问题闭环', '', 'active', $now, $now, 1)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static void InsertSubproject(SqliteConnection connection, SqliteTransaction transaction,
        string id, string projectId, string code, string name, string range, int sort, string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO subprojects(id, project_id, code, name, machine_range, status, sort_no, created_at, updated_at, version)
            VALUES ($id, $project, $code, $name, $range, 'active', $sort, $now, $now, 1)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$range", range);
        command.Parameters.AddWithValue("$sort", sort);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static void InsertMachine(SqliteConnection connection, SqliteTransaction transaction,
        string code, string projectId, string? subprojectId, int? seq, string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO machines(id, project_id, subproject_id, code, name, seq_no, status, created_at, updated_at, version)
            VALUES ($id, $project, $sub, $code, $code, $seq, 'active', $now, $now, 1)
            """;
        command.Parameters.AddWithValue("$id", "MCH-" + code);
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$sub", (object?)subprojectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$seq", (object?)seq ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static void InsertIssue(SqliteConnection connection, SqliteTransaction transaction,
        SeedIssue row, string projectId, string adminId, string now)
    {
        var machineCode = TextUtil.Normalize(row.Machine);
        var machineId = "MCH-" + machineCode;
        var subprojectCode = SubprojectRules.Resolve(machineCode);
        var subprojectId = subprojectCode switch
        {
            SubprojectRules.P01 => "SUB-P7391-01",
            SubprojectRules.P02 => "SUB-P7391-02",
            SubprojectRules.P03 => "SUB-P7391-03",
            _ => null
        };

        var status = IssueStatuses.Normalize(row.Status);

        using var issueCommand = connection.CreateCommand();
        issueCommand.Transaction = transaction;
        issueCommand.CommandText = """
            INSERT INTO issues
                (id, project_id, subproject_id, machine_id, seq_no, title, solution, status, priority,
                 category, completion_text, closed_at, source_batch_id, source_row, created_by, updated_by,
                 created_at, updated_at, version, deleted_at)
            VALUES
                ($id, $project, $sub, $machine, $seq, $title, $solution, $status, '中',
                 '', $completion, NULL, 'seed', $row, $admin, $admin, $now, $now, 1, NULL)
            """;
        issueCommand.Parameters.AddWithValue("$id", row.Id);
        issueCommand.Parameters.AddWithValue("$project", projectId);
        issueCommand.Parameters.AddWithValue("$sub", (object?)subprojectId ?? DBNull.Value);
        issueCommand.Parameters.AddWithValue("$machine", machineId);
        issueCommand.Parameters.AddWithValue("$seq", TextUtil.Normalize(row.Seq));
        issueCommand.Parameters.AddWithValue("$title", row.Title ?? "");
        issueCommand.Parameters.AddWithValue("$solution", row.Solution ?? "");
        issueCommand.Parameters.AddWithValue("$status", status);
        issueCommand.Parameters.AddWithValue("$completion", row.Completion ?? "");
        issueCommand.Parameters.AddWithValue("$row", (object?)row.SourceRow ?? DBNull.Value);
        issueCommand.Parameters.AddWithValue("$admin", adminId);
        issueCommand.Parameters.AddWithValue("$now", now);
        issueCommand.ExecuteNonQuery();

        foreach (var code in StageCodes.All)
        {
            row.Stages.TryGetValue(code, out var stage);
            var owner = stage?.Owner?.Trim() ?? "";
            var dueRaw = stage?.RawDue?.Trim() ?? "";
            var dueDate = stage?.Due;
            var state = string.IsNullOrWhiteSpace(stage?.State)
                ? StageRules.InferState(owner, dueRaw)
                : StageStates.Parse(stage.State);
            var note = stage?.Note ?? "";

            using var stageCommand = connection.CreateCommand();
            stageCommand.Transaction = transaction;
            stageCommand.CommandText = """
                INSERT INTO issue_stages
                    (id, issue_id, stage_code, owner_text, owner_ids, due_date, due_raw, state, actual_date, remark, updated_at, version)
                VALUES ($id, $issue, $code, $owner, '', $due, $raw, $state, NULL, $note, $now, 1)
                """;
            stageCommand.Parameters.AddWithValue("$id", $"{row.Id}-{code}");
            stageCommand.Parameters.AddWithValue("$issue", row.Id);
            stageCommand.Parameters.AddWithValue("$code", code);
            stageCommand.Parameters.AddWithValue("$owner", owner);
            stageCommand.Parameters.AddWithValue("$due", (object?)AppTime.ToDb(dueDate) ?? DBNull.Value);
            stageCommand.Parameters.AddWithValue("$raw", dueRaw);
            stageCommand.Parameters.AddWithValue("$state", StageStates.ToDb(state));
            stageCommand.Parameters.AddWithValue("$note", note);
            stageCommand.Parameters.AddWithValue("$now", now);
            stageCommand.ExecuteNonQuery();
        }
    }

    private sealed class SeedIssue
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("seq")] public string Seq { get; set; } = "";
        [JsonPropertyName("machine")] public string Machine { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("solution")] public string Solution { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("completion")] public string Completion { get; set; } = "";
        [JsonPropertyName("source_row")] public int? SourceRow { get; set; }

        [JsonPropertyName("stages")]
        public Dictionary<string, SeedStage> Stages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SeedStage
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("owner")] public string Owner { get; set; } = "";
        [JsonPropertyName("due")] public DateOnly? Due { get; set; }
        [JsonPropertyName("raw_due")] public string RawDue { get; set; } = "";
        [JsonPropertyName("note")] public string Note { get; set; } = "";
        [JsonPropertyName("state")] public string State { get; set; } = "";
    }
}
