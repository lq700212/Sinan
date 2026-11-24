using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Sinan.Core.Models;
using Sinan.Core.Utils;

namespace Sinan.Data.Repositories;

/// <summary>
/// 附件仓储。文件按 <c>attachments/{issueId}/{uuid}{ext}</c> 存，数据库只存相对路径；
/// 删除问题不删附件文件（等归档策略处理），删除附件才删文件。
///
/// 演示附件（is_demo=1）只登记元数据，不伪造现场照片；界面用占位图 + "演示"角标，
/// 导出 ZIP 时写说明文件而不是塞假图。
/// </summary>
public sealed class AttachmentRepository
{
    public const long MaxFileSize = 20 * 1024 * 1024;

    private readonly Database _database;

    public AttachmentRepository(Database database) => _database = database;

    public List<Attachment> List(string issueId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM attachments WHERE issue_id=$issue ORDER BY created_at";
        command.Parameters.AddWithValue("$issue", issueId);
        var list = new List<Attachment>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public Attachment? Get(string id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM attachments WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>把本地文件复制进附件库并登记。调用方负责选择文件；这里只校验大小。</summary>
    public Attachment Add(string issueId, string sourcePath, string uploadedBy)
    {
        var file = new FileInfo(sourcePath);
        if (!file.Exists) throw new FileNotFoundException("附件文件不存在。", sourcePath);
        if (file.Length > MaxFileSize)
            throw new InvalidOperationException($"附件超过 {MaxFileSize / 1024 / 1024} MB 限制。");

        var now = AppTime.UtcNowIso();
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        var storedName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
        var relative = $"{issueId}/{storedName}";
        var target = AbsolutePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file.FullName, target, overwrite: false);

        var attachment = new Attachment
        {
            Id = IdGen.NewAttachmentId(),
            IssueId = issueId,
            FileName = file.Name,
            StoredName = storedName,
            StoredPath = relative,
            ThumbPath = "",
            Size = file.Length,
            Mime = MimeOf(ext),
            Sha256 = Sha256Of(target),
            UploadedBy = uploadedBy,
            CreatedAt = now,
            IsDemo = false,
            SyncState = "local"
        };

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO attachments
                (id, issue_id, file_name, stored_name, stored_path, thumb_path, size, mime,
                 sha256, uploaded_by, created_at, is_demo, sync_state)
            VALUES
                ($id, $issue, $name, $stored, $path, '', $size, $mime,
                 $sha, $user, $now, 0, 'local')
            """;
        command.Parameters.AddWithValue("$id", attachment.Id);
        command.Parameters.AddWithValue("$issue", attachment.IssueId);
        command.Parameters.AddWithValue("$name", attachment.FileName);
        command.Parameters.AddWithValue("$stored", attachment.StoredName);
        command.Parameters.AddWithValue("$path", attachment.StoredPath);
        command.Parameters.AddWithValue("$size", attachment.Size);
        command.Parameters.AddWithValue("$mime", attachment.Mime);
        command.Parameters.AddWithValue("$sha", attachment.Sha256);
        command.Parameters.AddWithValue("$user", attachment.UploadedBy);
        command.Parameters.AddWithValue("$now", attachment.CreatedAt);
        command.ExecuteNonQuery();
        return attachment;
    }

    public void Delete(string id)
    {
        var attachment = Get(id) ?? throw new InvalidOperationException("附件不存在。");
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM attachments WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();

        if (!attachment.IsDemo)
        {
            var path = AbsolutePath(attachment.StoredPath);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>数据库相对路径 → 本机绝对路径。目录穿越直接拒绝。</summary>
    public string AbsolutePath(string storedPath)
    {
        var normalized = storedPath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_database.Paths.AttachmentsDir, normalized));
        var root = Path.GetFullPath(_database.Paths.AttachmentsDir) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("附件路径越界。");
        return full;
    }

    public bool FileExists(Attachment attachment) =>
        !attachment.IsDemo && File.Exists(AbsolutePath(attachment.StoredPath));

    /// <summary>
    /// 首次启动补 12 张演示附件（6 条问题）：01 分项目 3 条 7 张、02 分项目 1 条 3 张、
    /// 03 分项目 2 条 2 张。仅登记元数据，界面标"演示"，不伪造现场照片。
    /// 已有任何附件时直接跳过，避免重复种入。
    /// </summary>
    public int EnsureDemoAttachments()
    {
        using var connection = _database.Open();
        using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = "SELECT COUNT(*) FROM attachments";
            if (Convert.ToInt32(countCommand.ExecuteScalar()) > 0) return 0;
        }

        var bySubproject = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var query = connection.CreateCommand())
        {
            query.CommandText = """
                SELECT i.id, COALESCE(sp.code, '') AS sub
                FROM issues i
                LEFT JOIN subprojects sp ON sp.id = i.subproject_id
                WHERE i.deleted_at IS NULL
                ORDER BY i.source_row IS NULL, i.source_row, i.seq_no
                """;
            using var reader = query.ExecuteReader();
            while (reader.Read())
            {
                var sub = reader.Str("sub");
                if (!bySubproject.TryGetValue(sub, out var ids))
                    bySubproject[sub] = ids = new List<string>();
                ids.Add(reader.Str("id"));
            }
        }

        // 与设计稿一致的演示分布：01 → 3 条 7 张，02 → 1 条 3 张，03 → 2 条 2 张。
        var plan = new (string Code, int IssueIndex, int Count)[]
        {
            (SubprojectRules.P01, 0, 3), (SubprojectRules.P01, 1, 2), (SubprojectRules.P01, 2, 2),
            (SubprojectRules.P02, 0, 3),
            (SubprojectRules.P03, 0, 1), (SubprojectRules.P03, 1, 1)
        };

        var now = AppTime.UtcNowIso();
        var inserted = 0;
        using var transaction = connection.BeginTransaction();
        foreach (var (code, index, count) in plan)
        {
            if (!bySubproject.TryGetValue(code, out var ids) || ids.Count <= index) continue;
            var issueId = ids[index];
            for (var n = 1; n <= count; n++)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO attachments
                        (id, issue_id, file_name, stored_name, stored_path, thumb_path, size, mime,
                         sha256, uploaded_by, created_at, is_demo, sync_state)
                    VALUES
                        ($id, $issue, $name, $stored, $path, '', $size, 'image/jpeg',
                         '', '', $now, 1, 'synced')
                    """;
                insert.Parameters.AddWithValue("$id", IdGen.NewAttachmentId());
                insert.Parameters.AddWithValue("$issue", issueId);
                insert.Parameters.AddWithValue("$name", $"演示照片_{code}_{n}.jpg");
                insert.Parameters.AddWithValue("$stored", $"demo_{n}.jpg");
                insert.Parameters.AddWithValue("$path", $"demo/{issueId}/demo_{n}.jpg");
                insert.Parameters.AddWithValue("$size", 320 * 1024 + n * 48 * 1024);
                insert.Parameters.AddWithValue("$now", now);
                insert.ExecuteNonQuery();
                inserted++;
            }
        }
        transaction.Commit();
        return inserted;
    }

    private static Attachment Map(SqliteDataReader reader) => new()
    {
        Id = reader.Str("id"),
        IssueId = reader.Str("issue_id"),
        FileName = reader.Str("file_name"),
        StoredName = reader.Str("stored_name"),
        StoredPath = reader.Str("stored_path"),
        ThumbPath = reader.Str("thumb_path"),
        Size = reader.GetInt64(reader.GetOrdinal("size")),
        Mime = reader.Str("mime"),
        Sha256 = reader.Str("sha256"),
        UploadedBy = reader.Str("uploaded_by"),
        CreatedAt = reader.Str("created_at"),
        IsDemo = reader.Int("is_demo") == 1,
        SyncState = reader.Str("sync_state")
    };

    private static string MimeOf(string ext) => ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
