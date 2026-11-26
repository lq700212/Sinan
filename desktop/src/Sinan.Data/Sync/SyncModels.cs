using System.Text.Json;

namespace Sinan.Data.Sync;

/// <summary>
/// 同步领域模型：配对码、设备、上行操作、下行变更。
///
/// 这些类型只描述局域网同步协议（shared/api/sync-api.md），
/// 不含任何界面逻辑；HTTP 层（Sinan.App/Services/SyncServer.cs）只做收发与鉴权。
/// </summary>
public sealed class PairCodeInfo
{
    public string Code { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public string ExpiresDisplay => ExpiresAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
}

public sealed class DeviceInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Model { get; init; } = "";
    public string Platform { get; init; } = "";
    public string UserId { get; init; } = "";
    public string PairedAt { get; init; } = "";
    public string? LastSyncAt { get; init; }
    public bool Active { get; init; } = true;

    /// <summary>最近 2 分钟内有同步请求才算在线；没有心跳就如实显示离线。</summary>
    public bool IsOnline => Active && !string.IsNullOrWhiteSpace(LastSyncAt) &&
                            DateTimeOffset.TryParse(LastSyncAt, out var t) &&
                            DateTimeOffset.UtcNow - t.ToUniversalTime() < TimeSpan.FromMinutes(2);

    public string PairedDisplay => Format(PairedAt);
    public string LastSyncDisplay => string.IsNullOrWhiteSpace(LastSyncAt)
        ? "尚未同步"
        : Format(LastSyncAt!);

    private static string Format(string iso) =>
        DateTimeOffset.TryParse(iso, out var t)
            ? t.ToLocalTime().ToString("MM-dd HH:mm")
            : iso;
}

public sealed record SyncOperation
{
    public long ClientOpId { get; init; }
    public string Entity { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string Op { get; init; } = "";
    public int BaseVersion { get; init; }
    public string ClientTime { get; init; } = "";
    public JsonElement Payload { get; init; }
}

public sealed class SyncOperationResult
{
    public long ClientOpId { get; init; }

    /// <summary>applied / conflict / rejected / duplicate。</summary>
    public string Status { get; init; } = "applied";

    public string ServerId { get; init; } = "";
    public int ServerVersion { get; init; }
    public string SeqNo { get; init; } = "";
    public JsonElement? ServerPayload { get; init; }
    public string Message { get; init; } = "";
}

public sealed class SyncChange
{
    public long Seq { get; init; }
    public string Entity { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string Op { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public JsonElement Payload { get; init; }
}

public sealed class SyncPullResult
{
    public long Cursor { get; init; }
    public bool HasMore { get; init; }
    public List<SyncChange> Changes { get; init; } = new();
}

/// <summary>照片上传结果：成功给落盘相对路径，失败给错误码（手机端据此决定是否留队列）。</summary>
public sealed class AttachmentUploadResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public string Message { get; init; } = "";
    public string AttachmentId { get; init; } = "";
    public string StoredPath { get; init; } = "";
    public long Size { get; init; }
}

/// <summary>配对请求（POST /api/pair）。</summary>
public sealed class PairRequest
{
    public string Code { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Model { get; set; } = "";
    public string Platform { get; set; } = "android";
    public string EmployeeNo { get; set; } = "";
}

/// <summary>上行请求（POST /api/sync/push）。</summary>
public sealed class PushRequest
{
    public List<SyncOperation> Operations { get; set; } = new();
}

/// <summary>配对结果：成功给 token，失败给可读错误码与提示。</summary>
public sealed class PairOutcome
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public string Message { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string Token { get; init; } = "";
    public string PairedAt { get; init; } = "";
}
