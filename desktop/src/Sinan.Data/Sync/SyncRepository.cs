using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sinan.Core.Models;
using Sinan.Core.Utils;
using Sinan.Data.Repositories;

namespace Sinan.Data.Sync;

/// <summary>
/// 局域网同步数据层（协议见 <c>shared/api/sync-api.md</c>）。
///
/// 只做四件事，HTTP 收发放到 Sinan.App/Services/SyncServer：
/// 1. 配对码与设备令牌（明文 token 只在配对响应里出现一次，库里只存 SHA-256）；
/// 2. 上行操作：手机 outbox 的 create / upsert / status / comment 落到主库；
/// 3. 下行增量：按 change_log.seq 拉取，排除本设备自己推的变更；
/// 4. 附件落盘：按 attachmentId 幂等，重复上传不产生第二张照片。
///
/// 冲突口径与根 AGENTS.md 一致：桌面端是主库，先写先赢；手机端带 baseVersion，
/// 落后就返回 conflict + 桌面当前值，由手机端人工选择，绝不静默覆盖。
/// </summary>
public sealed class SyncRepository
{
    public const int MaxDevices = 8;
    public const int PairCodeMinutes = 10;
    public const long MaxAttachmentBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions CamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Database _database;
    private readonly IssueRepository _issues;
    private readonly ProjectRepository _projects;
    private readonly UserRepository _users;

    public SyncRepository(Database database, IssueRepository issues,
        ProjectRepository projects, UserRepository users)
    {
        _database = database;
        _issues = issues;
        _projects = projects;
        _users = users;
    }

    // ---------------------------------------------------------------------
    // 配对码
    // ---------------------------------------------------------------------

    /// <summary>
    /// 生成一次性 6 位配对码（10 分钟有效）。顺手清掉已用/过期的旧码，
    /// 避免设置页二维码越积越多；同一时刻只保留最近生成的一个未使用码。
    /// </summary>
    public PairCodeInfo CreatePairCode()
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(PairCodeMinutes);
        var code = IdGen.NewPairCode();

        using var connection = _database.Open();
        using (var cleanup = connection.CreateCommand())
        {
            cleanup.CommandText = """
                DELETE FROM pair_codes
                WHERE used=1 OR expires_at < $now
                """;
            cleanup.Parameters.AddWithValue("$now", now.ToString("O"));
            cleanup.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO pair_codes(code, expires_at, created_at, used)
                VALUES($code, $expires, $now, 0)
                """;
            insert.Parameters.AddWithValue("$code", code);
            insert.Parameters.AddWithValue("$expires", expires.ToString("O"));
            insert.Parameters.AddWithValue("$now", now.ToString("O"));
            insert.ExecuteNonQuery();
        }

        return new PairCodeInfo { Code = code, ExpiresAt = expires };
    }

    /// <summary>当前仍可用的配对码（设置页刷新二维码时用）。</summary>
    public List<PairCodeInfo> ListActivePairCodes()
    {
        var now = DateTimeOffset.UtcNow;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT code, expires_at FROM pair_codes
            WHERE used=0 AND expires_at >= $now
            ORDER BY created_at DESC
            """;
        command.Parameters.AddWithValue("$now", now.ToString("O"));

        var list = new List<PairCodeInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!DateTimeOffset.TryParse(reader.Str("expires_at"), out var expires)) continue;
            list.Add(new PairCodeInfo { Code = reader.Str("code"), ExpiresAt = expires });
        }
        return list;
    }

    // ---------------------------------------------------------------------
    // 设备
    // ---------------------------------------------------------------------

    public List<DeviceInfo> ListDevices()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, model, platform, user_id, paired_at, last_sync_at, active
            FROM devices ORDER BY active DESC, paired_at DESC
            """;
        var list = new List<DeviceInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(MapDevice(reader));
        return list;
    }

    /// <summary>移除设备只置 active=0，历史同步记录与操作人仍可追溯。</summary>
    public bool RemoveDevice(string deviceId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE devices SET active=0 WHERE id=$id AND active=1";
        command.Parameters.AddWithValue("$id", deviceId);
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// 按 Bearer token 找设备。库里只有 SHA-256，token 本身不落盘；
    /// 停用设备立即失效，不等 token 过期（当前 token 无过期时间，靠 active 控制）。
    /// </summary>
    public DeviceInfo? Authenticate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = Sha256Hex(token.Trim());

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, model, platform, user_id, paired_at, last_sync_at, active
            FROM devices WHERE token_hash=$hash AND active=1
            """;
        command.Parameters.AddWithValue("$hash", hash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapDevice(reader) : null;
    }

    /// <summary>每次成功同步刷新"最近同步"，在线状态按这个时间实时算，不伪造心跳。</summary>
    public void TouchDevice(string deviceId)
    {
        var now = AppTime.UtcNowIso();
        using var connection = _database.Open();
        using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE devices SET last_sync_at=$now WHERE id=$id";
            update.Parameters.AddWithValue("$now", now);
            update.Parameters.AddWithValue("$id", deviceId);
            update.ExecuteNonQuery();
        }
        using var state = connection.CreateCommand();
        state.CommandText = """
            INSERT INTO sync_state(device_id, last_cursor, last_sync_at)
            VALUES($id, 0, $now)
            ON CONFLICT(device_id) DO UPDATE SET last_sync_at=excluded.last_sync_at
            """;
        state.Parameters.AddWithValue("$id", deviceId);
        state.Parameters.AddWithValue("$now", now);
        state.ExecuteNonQuery();
    }

    /// <summary>
    /// 用配对码换设备身份。成功返回明文 token（仅此一次），失败给可读错误码。
    /// 设备上限 8 台：现场手机换得勤，旧设备应在设置页先移除，不能无限堆。
    /// </summary>
    public PairOutcome Pair(string code, string deviceName, string model,
        string platform, string employeeNo)
    {
        code = (code ?? "").Trim();
        if (code.Length == 0)
            return Fail("pair_code_invalid", "请输入 6 位配对码。");

        using var connection = _database.Open();

        string? expiresAt = null;
        var used = false;
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT expires_at, used FROM pair_codes WHERE code=$code";
            query.Parameters.AddWithValue("$code", code);
            using var reader = query.ExecuteReader();
            if (reader.Read())
            {
                expiresAt = reader.Str("expires_at");
                used = reader.GetInt32(reader.GetOrdinal("used")) == 1;
            }
        }

        if (expiresAt == null)
            return Fail("pair_code_invalid", "配对码不存在，请在桌面端重新生成。");
        if (used)
            return Fail("pair_code_invalid", "配对码已被使用，请重新生成。");
        if (!DateTimeOffset.TryParse(expiresAt, out var expires) ||
            expires < DateTimeOffset.UtcNow)
            return Fail("pair_code_expired", "配对码已过期，请在桌面端重新生成。");

        using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM devices WHERE active=1";
            if (Convert.ToInt32(count.ExecuteScalar()) >= MaxDevices)
                return Fail("device_limit", $"已配对 {MaxDevices} 台设备，请先在桌面端移除旧设备。");
        }

        var deviceId = IdGen.NewDeviceId();
        var token = "v1_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = AppTime.UtcNowIso();
        var userId = _users.ListActive()
            .FirstOrDefault(u => string.Equals(u.EmployeeNo, (employeeNo ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase))?.Id ?? "";

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO devices(id, name, model, platform, user_id, token_hash, paired_at, last_sync_at, active)
                VALUES($id, $name, $model, $platform, $user, $hash, $now, NULL, 1)
                """;
            insert.Parameters.AddWithValue("$id", deviceId);
            insert.Parameters.AddWithValue("$name", Trim(deviceName, 40, "Android 现场端"));
            insert.Parameters.AddWithValue("$model", Trim(model, 60, ""));
            insert.Parameters.AddWithValue("$platform", Trim(platform, 16, "android"));
            insert.Parameters.AddWithValue("$user", userId);
            insert.Parameters.AddWithValue("$hash", Sha256Hex(token));
            insert.Parameters.AddWithValue("$now", now);
            insert.ExecuteNonQuery();
        }

        using (var consume = connection.CreateCommand())
        {
            consume.CommandText = "UPDATE pair_codes SET used=1 WHERE code=$code";
            consume.Parameters.AddWithValue("$code", code);
            consume.ExecuteNonQuery();
        }

        return new PairOutcome
        {
            Ok = true,
            DeviceId = deviceId,
            Token = token,
            PairedAt = now
        };
    }

    // ---------------------------------------------------------------------
    // 上行
    // ---------------------------------------------------------------------

    /// <summary>
    /// 应用一批手机操作。逐条处理：一条失败不影响同批其他条；
    /// applied / conflict 写幂等回执，rejected 不写（用户修正后可重发同一 opId）。
    /// </summary>
    public List<SyncOperationResult> ApplyOperations(DeviceInfo device,
        IReadOnlyList<SyncOperation> operations)
    {
        var results = new List<SyncOperationResult>();
        foreach (var op in operations)
        {
            var receipt = ReadReceipt(device.Id, op.ClientOpId);
            if (receipt != null)
            {
                results.Add(CopyAs(receipt, "duplicate"));
                continue;
            }

            SyncOperationResult result;
            try
            {
                result = ApplyOne(device, op);
            }
            catch (Exception ex)
            {
                result = new SyncOperationResult
                {
                    ClientOpId = op.ClientOpId,
                    Status = "rejected",
                    Message = ex.Message
                };
            }

            if (result.Status != "rejected")
                WriteReceipt(device.Id, op.ClientOpId, result);
            results.Add(result);
        }
        return results;
    }

    private SyncOperationResult ApplyOne(DeviceInfo device, SyncOperation op)
    {
        var entity = (op.Entity ?? "").Trim().ToLowerInvariant();
        var action = (op.Op ?? "").Trim().ToLowerInvariant();
        var entityId = (op.EntityId ?? "").Trim();
        if (entityId.Length == 0)
            return Rejected(op, "缺少 entityId。");

        return (entity, action) switch
        {
            ("issue", "create") => ApplyCreate(device, op, entityId),
            ("issue", "status") => ApplyStatus(device, op, entityId),
            ("issue", "comment") => ApplyComment(device, op, entityId),
            ("issue_stage", "upsert") => ApplyStage(device, op, entityId),
            ("attachment", "upload") =>
                Rejected(op, "照片请走 /api/attachments 上传接口，不放进 push。"),
            _ => Rejected(op, $"不支持的同步操作：{entity}/{action}。")
        };
    }

    private SyncOperationResult ApplyCreate(DeviceInfo device, SyncOperation op, string entityId)
    {
        // 幂等兜底：回执丢了但业务已写入时，不重复建第二条。
        var existing = _issues.Get(entityId);
        if (existing != null) return Applied(op, existing);

        var payload = op.Payload;
        var machineCode = GetString(payload, "machine_code");
        if (machineCode.Length == 0)
            return Rejected(op, "机台号不能为空。");

        // 项目归属优先级：payload.project_id（手机明确指定）→ 机台号前缀推导 → 主项目兜底。
        // 多总项目后不能无脑写进第一个项目，否则 P7392 的现场问题会串到 P7391。
        var project = _projects.Get(GetString(payload, "project_id"))
            ?? _projects.ListProjects().FirstOrDefault(p =>
                string.Equals(p.Code, TextUtil.ProjectCodeOfMachine(machineCode),
                    StringComparison.OrdinalIgnoreCase))
            ?? _projects.GetMain()
            ?? throw new InvalidOperationException("主项目不存在，无法接收手机新建问题。");
        var title = GetString(payload, "title").Trim();
        if (title.Length == 0)
            return Rejected(op, "问题描述不能为空。");

        var machine = _projects.GetOrCreateMachine(project.Id, machineCode, AppTime.UtcNowIso());
        var draft = new IssueDraft
        {
            Id = entityId,
            ProjectId = project.Id,
            SubprojectId = machine.SubprojectId,
            MachineId = machine.Id,
            SeqNo = NextSeqNo(project.Id),
            Title = title,
            Solution = GetString(payload, "solution"),
            Status = GetString(payload, "status"),
            Priority = GetString(payload, "priority"),
            Category = GetString(payload, "category"),
            CompletionText = GetString(payload, "completion_text")
        };

        var stageCode = GetString(payload, "stage_code").Trim().ToLowerInvariant();
        if (StageCodes.All.Contains(stageCode))
        {
            var stage = StageDraftFor(draft, stageCode);
            stage.OwnerText = GetString(payload, "owner_text").Trim();
            stage.DueRaw = GetString(payload, "due_raw").Trim();
            var stateText = GetString(payload, "state").Trim();
            stage.State = stateText.Length > 0
                ? StageStates.Parse(stateText)
                : StageState.InProgress;
        }

        var created = _issues.Create(draft, UserFor(device, null));
        return Applied(op, created);
    }

    private SyncOperationResult ApplyStage(DeviceInfo device, SyncOperation op, string entityId)
    {
        var parts = entityId.Split(':', 2);
        if (parts.Length != 2)
            return Rejected(op, "阶段操作 entityId 必须是 <issueId>:<stageCode>。");

        var issue = _issues.Get(parts[0]);
        if (issue == null) return Rejected(op, "问题不存在或已删除。");
        var stage = issue.Stage(parts[1]);
        if (stage == null) return Rejected(op, $"阶段 {parts[1]} 不存在。");

        if (op.BaseVersion != stage.Version)
            return Conflict(op, stage.Version, stage);

        var payload = op.Payload;
        var draft = new StageDraft
        {
            Code = stage.Code,
            OwnerText = GetString(payload, "owner_text").Trim(),
            DueRaw = GetString(payload, "due_raw").Trim(),
            State = StageStates.Parse(GetString(payload, "state")),
            ActualDateRaw = GetString(payload, "actual_date").Trim(),
            Remark = GetString(payload, "remark"),
            Version = op.BaseVersion
        };

        var updated = _issues.SaveStage(issue.Id, draft, UserFor(device, null));
        return Applied(op, updated);
    }

    private SyncOperationResult ApplyStatus(DeviceInfo device, SyncOperation op, string entityId)
    {
        var issue = _issues.Get(entityId);
        if (issue == null) return Rejected(op, "问题不存在或已删除。");

        var status = IssueStatuses.Normalize(GetString(op.Payload, "status"));

        // 手机端 saveStage 会同时入队 stage + status 两条：桌面端 SaveStage 已按
        // 同一套 StageRules 推导出相同状态，此时 status 操作是 no-op，不能判冲突。
        if (status == issue.Status) return Applied(op, issue);

        if (op.BaseVersion != issue.Version)
            return Conflict(op, issue.Version, issue);

        var updated = _issues.SetStatus(issue.Id, status, UserFor(device, null));
        return Applied(op, updated);
    }

    private SyncOperationResult ApplyComment(DeviceInfo device, SyncOperation op, string entityId)
    {
        var issue = _issues.Get(entityId);
        if (issue == null) return Rejected(op, "问题不存在或已删除。");

        var body = GetString(op.Payload, "body").Trim();
        if (body.Length == 0) return Rejected(op, "评论内容不能为空。");

        var author = GetString(op.Payload, "author").Trim();
        _issues.AddComment(issue.Id, body, UserFor(device, author));
        var after = _issues.Get(issue.Id) ?? issue;
        return Applied(op, after);
    }

    // ---------------------------------------------------------------------
    // 下行
    // ---------------------------------------------------------------------

    /// <summary>
    /// 按 change_log.seq 拉增量。排除本设备自己推送产生的变更（当前业务写入统一
    /// 记在 'desktop' 名下，所以实际不会命中；保留 device_id 条件是为将来多端铺路）。
    /// </summary>
    public SyncPullResult Pull(string deviceId, long cursor, int limit)
    {
        limit = Math.Clamp(limit, 1, 500);
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT seq, entity, entity_id, op, payload, created_at
            FROM change_log
            WHERE seq > $cursor AND (device_id IS NULL OR device_id <> $device)
            ORDER BY seq
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$cursor", cursor);
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$limit", limit);

        var changes = new List<SyncChange>();
        var last = cursor;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var seq = reader.GetInt64(reader.GetOrdinal("seq"));
            last = seq;
            changes.Add(new SyncChange
            {
                Seq = seq,
                Entity = reader.Str("entity"),
                EntityId = reader.Str("entity_id"),
                Op = reader.Str("op"),
                CreatedAt = reader.Str("created_at"),
                Payload = ParsePayload(reader.Str("payload"))
            });
        }

        return new SyncPullResult
        {
            Cursor = last,
            HasMore = changes.Count >= limit,
            Changes = changes
        };
    }

    // ---------------------------------------------------------------------
    // 附件
    // ---------------------------------------------------------------------

    /// <summary>
    /// 手机照片落盘 + 登记。attachmentId 由手机生成，重复上传直接返回第一次结果。
    /// 文件按 attachments/{issueId}/{attachmentId}{ext} 存，与桌面端附件口径一致。
    /// </summary>
    public AttachmentUploadResult SaveAttachment(DeviceInfo device, string issueId,
        string attachmentId, string fileName, byte[] bytes, string? expectedSha256)
    {
        issueId = (issueId ?? "").Trim();
        attachmentId = (attachmentId ?? "").Trim();
        if (issueId.Length == 0 || attachmentId.Length == 0)
            return AttachmentFail("bad_request", "缺少问题 ID 或附件 ID。");
        if (_issues.Get(issueId) == null)
            return AttachmentFail("issue_not_found", "问题不存在或已删除，请先同步问题本身。");
        if (bytes.Length == 0)
            return AttachmentFail("bad_request", "附件内容为空。");
        if (bytes.Length > MaxAttachmentBytes)
            return AttachmentFail("attachment_too_large",
                $"单张照片不能超过 {MaxAttachmentBytes / 1024 / 1024} MB。");

        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(expectedSha256) &&
            !string.Equals(sha, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            return AttachmentFail("sha256_mismatch", "照片校验失败，请重新上传。");

        using var connection = _database.Open();

        // 幂等：同一 attachmentId 只落一次盘。
        using (var existing = connection.CreateCommand())
        {
            existing.CommandText = """
                SELECT id, stored_path, size FROM attachments WHERE id=$id
                """;
            existing.Parameters.AddWithValue("$id", attachmentId);
            using var reader = existing.ExecuteReader();
            if (reader.Read())
            {
                return new AttachmentUploadResult
                {
                    Ok = true,
                    AttachmentId = reader.Str("id"),
                    StoredPath = reader.Str("stored_path"),
                    Size = reader.GetInt64(reader.GetOrdinal("size"))
                };
            }
        }

        var ext = NormalizeImageExtension(fileName);
        var storedName = $"{attachmentId}{ext}";
        var relative = $"{issueId}/{storedName}";
        var target = Path.Combine(_database.Paths.AttachmentsDir,
            relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, bytes);

        var now = AppTime.UtcNowIso();
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO attachments
                    (id, issue_id, file_name, stored_name, stored_path, thumb_path, size, mime,
                     sha256, uploaded_by, created_at, is_demo, sync_state)
                VALUES
                    ($id, $issue, $name, $stored, $path, '', $size, $mime,
                     $sha, $user, $now, 0, 'synced')
                """;
            insert.Parameters.AddWithValue("$id", attachmentId);
            insert.Parameters.AddWithValue("$issue", issueId);
            insert.Parameters.AddWithValue("$name", Trim(fileName, 120, storedName));
            insert.Parameters.AddWithValue("$stored", storedName);
            insert.Parameters.AddWithValue("$path", relative);
            insert.Parameters.AddWithValue("$size", bytes.Length);
            insert.Parameters.AddWithValue("$mime", MimeOf(ext));
            insert.Parameters.AddWithValue("$sha", sha);
            insert.Parameters.AddWithValue("$user", device.UserId);
            insert.Parameters.AddWithValue("$now", now);
            insert.ExecuteNonQuery();
        }

        // 附件变更也进增量日志：其他手机/桌面重开时能刷新照片计数。
        using (var change = connection.CreateCommand())
        {
            change.CommandText = """
                INSERT INTO change_log(entity, entity_id, op, payload, device_id, created_at)
                VALUES('attachment', $id, 'upsert', $payload, $device, $now)
                """;
            change.Parameters.AddWithValue("$id", attachmentId);
            change.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(new
            {
                id = attachmentId,
                issueId,
                fileName = Trim(fileName, 120, storedName),
                storedPath = relative,
                size = bytes.Length,
                mime = MimeOf(ext),
                sha256 = sha,
                createdAt = now
            }, CamelJson));
            change.Parameters.AddWithValue("$device", device.Id);
            change.Parameters.AddWithValue("$now", now);
            change.ExecuteNonQuery();
        }

        return new AttachmentUploadResult
        {
            Ok = true,
            AttachmentId = attachmentId,
            StoredPath = relative,
            Size = bytes.Length
        };
    }

    // ---------------------------------------------------------------------
    // 回执与辅助
    // ---------------------------------------------------------------------

    private SyncOperationResult? ReadReceipt(string deviceId, long clientOpId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status, result_json FROM sync_receipts
            WHERE device_id=$device AND client_op_id=$op
            """;
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$op", clientOpId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        try
        {
            var result = JsonSerializer.Deserialize<SyncOperationResult>(
                reader.Str("result_json"),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result;
        }
        catch
        {
            return null;
        }
    }

    private void WriteReceipt(string deviceId, long clientOpId, SyncOperationResult result)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO sync_receipts(device_id, client_op_id, status, result_json, created_at)
            VALUES($device, $op, $status, $json, $now)
            """;
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$op", clientOpId);
        command.Parameters.AddWithValue("$status", result.Status);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(result, CamelJson));
        command.Parameters.AddWithValue("$now", AppTime.UtcNowIso());
        command.ExecuteNonQuery();
    }

    private static SyncOperationResult CopyAs(SyncOperationResult source, string status) => new()
    {
        ClientOpId = source.ClientOpId,
        Status = status,
        ServerId = source.ServerId,
        ServerVersion = source.ServerVersion,
        SeqNo = source.SeqNo,
        ServerPayload = source.ServerPayload,
        Message = source.Message
    };

    private static SyncOperationResult Applied(SyncOperation op, Issue issue) => new()
    {
        ClientOpId = op.ClientOpId,
        Status = "applied",
        ServerId = issue.Id,
        ServerVersion = issue.Version,
        SeqNo = issue.SeqNo
    };

    private static SyncOperationResult Conflict(SyncOperation op, int serverVersion, object payload) => new()
    {
        ClientOpId = op.ClientOpId,
        Status = "conflict",
        ServerVersion = serverVersion,
        ServerPayload = JsonSerializer.SerializeToElement(payload, CamelJson),
        Message = "该记录已被桌面端修改，请选择保留手机或保留桌面。"
    };

    private static SyncOperationResult Rejected(SyncOperation op, string message) => new()
    {
        ClientOpId = op.ClientOpId,
        Status = "rejected",
        Message = message
    };

    /// <summary>
    /// 手机端新建问题不占原表序号：在**当前总项目内**取数字序号最大值 +1，格式三位。
    /// 多总项目后必须按 project_id 过滤：P7392 的第一条从 001 开始，
    /// 不能延续 P7391 的 040（否则新项目的问题号看起来像从半截开始）。
    /// </summary>
    private string NextSeqNo(string projectId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(MAX(CAST(seq_no AS INTEGER)), 0)
            FROM issues
            WHERE project_id = $project
              AND seq_no <> '' AND seq_no NOT GLOB '*[^0-9]*'
            """;
        command.Parameters.AddWithValue("$project", projectId);
        var max = Convert.ToInt32(command.ExecuteScalar());
        return (max + 1).ToString("D3");
    }

    private static StageDraft StageDraftFor(IssueDraft draft, string code) => code switch
    {
        StageCodes.Plan => draft.Plan,
        StageCodes.Design => draft.Design,
        StageCodes.Material => draft.Material,
        _ => draft.Assembly
    };

    /// <summary>
    /// 手机操作人：优先用配对账号；配对账号被停用时退回设备名，
    /// 审计里仍能看出"哪台手机改的"，不会变成空用户。
    /// </summary>
    private User UserFor(DeviceInfo device, string? authorName)
    {
        var user = string.IsNullOrWhiteSpace(device.UserId)
            ? null
            : _users.GetById(device.UserId);
        if (user != null && string.IsNullOrWhiteSpace(authorName)) return user;

        return new User
        {
            Id = string.IsNullOrWhiteSpace(device.UserId) ? $"device:{device.Id}" : device.UserId,
            EmployeeNo = device.UserId,
            Name = string.IsNullOrWhiteSpace(authorName) ? device.Name : authorName.Trim(),
            Role = UserRole.Engineer,
            Active = true
        };
    }

    private static DeviceInfo MapDevice(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        Name = reader.Str("name"),
        Model = reader.Str("model"),
        Platform = reader.Str("platform"),
        UserId = reader.Str("user_id"),
        PairedAt = reader.Str("paired_at"),
        LastSyncAt = reader.StrOrNull("last_sync_at"),
        Active = reader.GetInt32(reader.GetOrdinal("active")) == 1
    };

    private static PairOutcome Fail(string error, string message) =>
        new() { Ok = false, Error = error, Message = message };

    private static AttachmentUploadResult AttachmentFail(string error, string message) =>
        new() { Ok = false, Error = error, Message = message };

    private static string GetString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object) return "";
        if (!payload.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static JsonElement ParsePayload(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch
        {
            return JsonSerializer.SerializeToElement(new { });
        }
    }

    private static string NormalizeImageExtension(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" => ext,
            _ => ".jpg"
        };
    }

    private static string MimeOf(string ext) => ext switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    private static string Trim(string? value, int max, string fallback)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return fallback;
        return text.Length <= max ? text : text[..max];
    }

    private static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
