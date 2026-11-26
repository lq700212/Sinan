using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sinan.Data.Repositories;
using Sinan.Data.Sync;

namespace Sinan.App.Services;

/// <summary>
/// 局域网同步服务（协议唯一真源：shared/api/sync-api.md）。
///
/// 设计取舍：
/// - 用 Kestrel（.NET 共享框架自带，不是第三方包）而不是 HttpListener：
///   HttpListener 走 Windows HTTP.sys，非管理员绑定局域网 IP 需要先写 urlacl，
///   现场装完直接双击运行会起不来；Kestrel 用普通 socket，不需要管理员权限。
/// - 端口固定 5180，监听所有网卡（含回环，便于本机自测）；跨网段/公网访问不属于 MVP，
///   由 Windows 防火墙控制入站范围。
/// - 启动失败不抛到 UI：设置页如实显示"未运行 + 原因"，主程序照常可用。
/// </summary>
public sealed class SyncServer : IDisposable
{
    public const int Port = 5180;
    public const int Protocol = 1;

    private readonly SyncRepository _sync;
    private readonly AttachmentRepository _attachments;
    private readonly int _port;
    private WebApplication? _app;
    private bool _disposed;

    /// <summary>
    /// <paramref name="port"/> 默认 5180（现场固定端口）；测试可注入空闲端口，
    /// 避免与正在运行的司南实例抢端口。
    /// </summary>
    public SyncServer(SyncRepository sync, AttachmentRepository attachments, int port = Port)
    {
        _sync = sync;
        _attachments = attachments;
        _port = port;
    }

    public bool IsRunning => _app != null;
    public IReadOnlyList<string> Urls { get; private set; } = Array.Empty<string>();
    public string LastError { get; private set; } = "";

    /// <summary>设置页状态文案：运行中给地址，失败给原因，绝不显示"已连接"糊弄。</summary>
    public string StatusText => IsRunning
        ? "配对服务运行中 · 仅监听局域网"
        : string.IsNullOrWhiteSpace(LastError)
            ? "配对服务未运行"
            : $"配对服务未运行：{LastError}";

    public void Start()
    {
        if (_app != null) return;
        LastError = "";

        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.ClearProviders();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(_port);
                // 照片上限 8 MB；留一点余量给 multipart/头部，Kestrel 提前拒绝超大请求。
                options.Limits.MaxRequestBodySize = SyncRepository.MaxAttachmentBytes + 1024 * 1024;
            });

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.PropertyNameCaseInsensitive = true;
            });

            var app = builder.Build();
            MapRoutes(app);
            app.StartAsync().GetAwaiter().GetResult();
            _app = app;

            var ips = LocalIPv4();
            Urls = ips.Select(ip => $"http://{ip}:{_port}").ToList();
            AppLog.Info(Urls.Count > 0
                ? $"同步服务已启动：{string.Join(", ", Urls)}"
                : $"同步服务已启动，但未找到局域网 IPv4（端口 {_port}）");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Urls = Array.Empty<string>();
            AppLog.Warn($"同步服务启动失败（端口 {_port}）：{ex.Message}");
        }
    }

    public void Stop()
    {
        var app = _app;
        _app = null;
        if (app == null) return;

        try
        {
            app.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"同步服务停止时出错：{ex.Message}");
        }
        Urls = Array.Empty<string>();
    }

    private void MapRoutes(WebApplication app)
    {
        app.MapGet("/api/ping", () => Results.Json(new
        {
            ok = true,
            app = "sinan",
            protocol = Protocol,
            serverName = "司南 · 桌面端",
            desktopVersion = AppInfo.Version,
            time = DateTime.UtcNow.ToString("O")
        }));

        app.MapPost("/api/pair", async (HttpRequest request) =>
        {
            var body = await ReadJsonAsync<PairRequest>(request);
            if (body == null)
                return Error(400, "bad_request", "配对请求格式不正确。");

            var outcome = _sync.Pair(body.Code, body.DeviceName, body.Model,
                body.Platform, body.EmployeeNo);
            if (!outcome.Ok)
            {
                var status = outcome.Error == "device_limit" ? 409 : 400;
                return Error(status, outcome.Error, outcome.Message);
            }

            return Results.Json(new
            {
                ok = true,
                deviceId = outcome.DeviceId,
                token = outcome.Token,
                serverName = "司南 · 桌面端",
                protocol = Protocol,
                pairedAt = outcome.PairedAt
            });
        });

        app.MapPost("/api/sync/push", async (HttpRequest request) =>
        {
            var device = Authenticate(request);
            if (device == null) return Unauthorized();

            var body = await ReadJsonAsync<PushRequest>(request);
            if (body == null)
                return Error(400, "bad_request", "同步请求格式不正确。");

            _sync.TouchDevice(device.Id);
            var results = _sync.ApplyOperations(device, body.Operations);
            return Results.Json(new { ok = true, results });
        });

        app.MapGet("/api/sync/pull", (HttpRequest request) =>
        {
            var device = Authenticate(request);
            if (device == null) return Unauthorized();

            _sync.TouchDevice(device.Id);
            var cursor = ParseLong(request.Query["cursor"], 0);
            var limit = (int)ParseLong(request.Query["limit"], 200);
            var result = _sync.Pull(device.Id, cursor, limit);
            return Results.Json(new
            {
                ok = true,
                cursor = result.Cursor,
                hasMore = result.HasMore,
                changes = result.Changes
            });
        });

        app.MapPost("/api/attachments/{issueId}/{attachmentId}",
            async (HttpRequest request, string issueId, string attachmentId) =>
            {
                var device = Authenticate(request);
                if (device == null) return Unauthorized();

                _sync.TouchDevice(device.Id);
                if (request.ContentLength > SyncRepository.MaxAttachmentBytes + 1024)
                    return Error(413, "attachment_too_large", "单张照片不能超过 8 MB。");

                using var buffer = new MemoryStream();
                await request.Body.CopyToAsync(buffer);
                var result = _sync.SaveAttachment(device, issueId, attachmentId,
                    request.Query["fileName"].ToString(),
                    buffer.ToArray(),
                    request.Query["sha256"].ToString());

                if (!result.Ok)
                {
                    var status = result.Error switch
                    {
                        "attachment_too_large" => 413,
                        "issue_not_found" => 404,
                        _ => 400
                    };
                    return Error(status, result.Error, result.Message);
                }

                return Results.Json(new
                {
                    ok = true,
                    attachmentId = result.AttachmentId,
                    storedPath = result.StoredPath,
                    size = result.Size
                });
            });

        app.MapGet("/api/attachments/{attachmentId}", (HttpRequest request, string attachmentId) =>
        {
            if (Authenticate(request) == null) return Unauthorized();

            var attachment = _attachments.Get(attachmentId);
            if (attachment == null || !_attachments.FileExists(attachment))
                return Error(404, "not_found", "附件不存在。");

            var file = _attachments.AbsolutePath(attachment.StoredPath);
            return Results.File(file, attachment.Mime, attachment.FileName);
        });
    }

    // ---------------------------------------------------------------------
    // HTTP 辅助
    // ---------------------------------------------------------------------

    private DeviceInfo? Authenticate(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..].Trim()
            : "";
        return token.Length == 0 ? null : _sync.Authenticate(token);
    }

    private static IResult Unauthorized() =>
        Error(401, "unauthorized", "设备未授权，请重新配对。");

    private static IResult Error(int status, string error, string message) =>
        Results.Json(new { ok = false, error, message }, statusCode: status);

    private static long ParseLong(string? text, long fallback) =>
        long.TryParse(text, out var value) ? value : fallback;

    private static async Task<T?> ReadJsonAsync<T>(HttpRequest request) where T : class
    {
        try
        {
            return await request.ReadFromJsonAsync<T>();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (BadHttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// 本机可被手机访问的 IPv4：跳过回环、隧道、未启用网卡与 169.254 自动地址。
    /// 手机与桌面端必须在同一局域网；跨网段/外网访问不属于 MVP。
    /// </summary>
    public static List<string> LocalIPv4()
    {
        var list = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel) continue;

                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = address.Address.ToString();
                    if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                    if (!list.Contains(ip)) list.Add(ip);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"枚举局域网地址失败：{ex.Message}");
        }
        return list;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
