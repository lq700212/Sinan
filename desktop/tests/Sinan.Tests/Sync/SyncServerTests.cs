using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Sinan.App.Services;
using Sinan.Tests.Infrastructure;
using Xunit;

namespace Sinan.Tests.Sync;

/// <summary>
/// 同步服务 HTTP 层：真实起 Kestrel、真实发请求，验证路由、鉴权、状态码与 JSON 字段
/// 和 `shared/api/sync-api.md` 一致。
///
/// 与 `SyncRepositoryTests` 的分工：那边测数据层语义（冲突/幂等/序号重编），
/// 这里只保证"手机照契约发请求能走通、错令牌进不来"。端口注入空闲端口，
/// 不与正在运行的司南实例（5180）抢端口。
/// </summary>
public class SyncServerTests
{
    /// <summary>向系统要一个当前空闲的端口；拿到后立刻释放，Kestrel 再绑定。</summary>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task HTTP层_协议版本_鉴权_配对_上行下行_附件()
    {
        using var db = new TempDb();
        var port = FreePort();
        using var server = new SyncServer(db.Sync, db.Attachments, port);
        server.Start();
        Assert.True(server.IsRunning, server.LastError);

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        // 1. ping：协议版本与产品标识，手机据此判断"对面是不是司南"。
        using (var ping = await ReadJsonAsync(await http.GetAsync("/api/ping")))
        {
            var root = ping.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal("sinan", root.GetProperty("app").GetString());
            Assert.Equal(1, root.GetProperty("protocol").GetInt32());
        }

        // 2. 未带令牌一律 401，不能泄露主库数据。
        var unauthorized = await http.GetAsync("/api/sync/pull?cursor=0");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        // 3. 无效配对码返回 400 + pair_code_invalid。
        var badPair = await http.PostAsJsonAsync("/api/pair", new
        {
            code = "000000",
            deviceName = "无效码",
            model = "",
            platform = "android",
            employeeNo = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, badPair.StatusCode);
        using (var badJson = await ReadJsonAsync(badPair))
        {
            Assert.Equal("pair_code_invalid", badJson.RootElement.GetProperty("error").GetString());
        }

        // 4. 正常配对：拿设备令牌，之后所有请求带 Bearer。
        var code = db.Sync.CreatePairCode();
        var pair = await http.PostAsJsonAsync("/api/pair", new
        {
            code = code.Code,
            deviceName = "HTTP 集成手机",
            model = "Pixel 7",
            platform = "android",
            employeeNo = "engineer"
        });
        Assert.Equal(HttpStatusCode.OK, pair.StatusCode);

        string token;
        using (var pairJson = await ReadJsonAsync(pair))
        {
            Assert.True(pairJson.RootElement.GetProperty("ok").GetBoolean());
            token = pairJson.RootElement.GetProperty("token").GetString() ?? "";
        }
        Assert.False(string.IsNullOrWhiteSpace(token));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 5. 上行新建问题：保留手机 local-* 主键，桌面端重编三位序号。
        var pushBody = new
        {
            operations = new[]
            {
                new
                {
                    clientOpId = 9001L,
                    entity = "issue",
                    entityId = "local-http-1",
                    op = "create",
                    baseVersion = 0,
                    clientTime = "2026-11-26T18:00:00Z",
                    payload = new
                    {
                        seq_no = "L001",
                        machine_code = "P7391102",
                        title = "HTTP 集成：传感器误触发",
                        solution = "更换屏蔽线并复测",
                        status = "装配中",
                        stage_code = "assembly",
                        owner_text = "王工",
                        due_raw = "12.01",
                        state = "in_progress"
                    }
                }
            }
        };

        var push = await http.PostAsJsonAsync("/api/sync/push", pushBody);
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        using (var pushJson = await ReadJsonAsync(push))
        {
            var result = pushJson.RootElement.GetProperty("results")[0];
            Assert.Equal("applied", result.GetProperty("status").GetString());
            Assert.Equal("local-http-1", result.GetProperty("serverId").GetString());
            Assert.Equal(3, result.GetProperty("seqNo").GetString()!.Length);
        }

        // 6. 同 clientOpId 重发：回执幂等，返回 duplicate，不重复落库。
        var resend = await http.PostAsJsonAsync("/api/sync/push", pushBody);
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        using (var resendJson = await ReadJsonAsync(resend))
        {
            Assert.Equal("duplicate",
                resendJson.RootElement.GetProperty("results")[0].GetProperty("status").GetString());
        }

        // 7. 下行：游标 0 能拉到刚上传的问题完整快照。
        var pull = await http.GetAsync("/api/sync/pull?cursor=0&limit=500");
        Assert.Equal(HttpStatusCode.OK, pull.StatusCode);
        long cursor;
        using (var pullJson = await ReadJsonAsync(pull))
        {
            var root = pullJson.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());
            cursor = root.GetProperty("cursor").GetInt64();

            var found = false;
            foreach (var change in root.GetProperty("changes").EnumerateArray())
            {
                if (change.GetProperty("entity").GetString() != "issue" ||
                    change.GetProperty("entityId").GetString() != "local-http-1")
                    continue;
                found = true;
                Assert.Equal("HTTP 集成：传感器误触发",
                    change.GetProperty("payload").GetProperty("title").GetString());
            }
            Assert.True(found, "下行结果里没有刚上行的问题");
        }

        // 8. 游标推进后再拉应为空（增量而不是全量）。
        using (var emptyJson = await ReadJsonAsync(
                   await http.GetAsync($"/api/sync/pull?cursor={cursor}&limit=500")))
        {
            Assert.Equal(0, emptyJson.RootElement.GetProperty("changes").GetArrayLength());
        }

        // 9. 附件上传：按 attachmentId 幂等，重复上传返回同一存储路径。
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22, 0x33, 0xFF, 0xD9 };
        async Task<HttpResponseMessage> UploadAsync()
        {
            var content = new ByteArrayContent(jpg);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return await http.PostAsync(
                "/api/attachments/local-http-1/att-http-1?fileName=IMG_0001.jpg", content);
        }

        string storedPath;
        using (var uploadJson = await ReadJsonAsync(await UploadAsync()))
        {
            Assert.True(uploadJson.RootElement.GetProperty("ok").GetBoolean());
            storedPath = uploadJson.RootElement.GetProperty("storedPath").GetString() ?? "";
        }
        Assert.False(string.IsNullOrWhiteSpace(storedPath));

        using (var againJson = await ReadJsonAsync(await UploadAsync()))
        {
            Assert.Equal(storedPath, againJson.RootElement.GetProperty("storedPath").GetString());
        }

        // 10. 附件下载：字节与上传一致。
        var download = await http.GetAsync("/api/attachments/att-http-1");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(jpg, await download.Content.ReadAsByteArrayAsync());

        server.Stop();
        Assert.False(server.IsRunning);
    }
}
