using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Sinan.Core.Models;
using Sinan.Data.Sync;
using Sinan.Tests.Infrastructure;
using Xunit;

namespace Sinan.Tests.Sync;

/// <summary>
/// 局域网同步数据层：配对、上行、冲突、幂等、增量、附件。
/// 协议口径见 shared/api/sync-api.md；这些用例是"手机推得上来、桌面改得回去"的底线。
/// </summary>
public class SyncRepositoryTests
{
    private static DeviceInfo PairDevice(TempDb db, string name = "测试手机")
    {
        var code = db.Sync.CreatePairCode();
        var outcome = db.Sync.Pair(code.Code, name, "Pixel 7", "android", "engineer");
        Assert.True(outcome.Ok, outcome.Message);

        var device = db.Sync.Authenticate(outcome.Token);
        Assert.NotNull(device);
        return device!;
    }

    private static SyncOperation CreateOp(long clientOpId = 1, string entityId = "local-1") => new()
    {
        ClientOpId = clientOpId,
        Entity = "issue",
        EntityId = entityId,
        Op = "create",
        BaseVersion = 0,
        Payload = JsonSerializer.SerializeToElement(new
        {
            seq_no = "L001",
            machine_code = "P7391101",
            title = "手机新建：夹爪等待超时",
            solution = "调整气缸节流阀",
            status = "装配中",
            stage_code = "assembly",
            owner_text = "张工",
            due_raw = "11.28",
            state = "in_progress"
        })
    };

    [Fact]
    public void 配对码_生成后可换设备令牌_同码只能用一次()
    {
        using var db = new TempDb();
        var code = db.Sync.CreatePairCode();

        Assert.Equal(6, code.Code.Length);
        Assert.All(code.Code, c => Assert.InRange(c, '0', '9'));

        var first = db.Sync.Pair(code.Code, "现场手机", "Pixel 7", "android", "engineer");
        Assert.True(first.Ok, first.Message);
        Assert.False(string.IsNullOrWhiteSpace(first.Token));
        Assert.NotNull(db.Sync.Authenticate(first.Token));

        var again = db.Sync.Pair(code.Code, "第二台", "Mi 14", "android", "engineer");
        Assert.False(again.Ok);
        Assert.Equal("pair_code_invalid", again.Error);

        var wrong = db.Sync.Pair("000000", "第三台", "", "android", "");
        Assert.False(wrong.Ok);
    }

    [Fact]
    public void 手机新建问题_落主库并重编序号()
    {
        using var db = new TempDb();
        var device = PairDevice(db);

        var results = db.Sync.ApplyOperations(device, new[] { CreateOp() });

        Assert.Single(results);
        Assert.Equal("applied", results[0].Status);
        Assert.Equal("local-1", results[0].ServerId);

        var issue = db.Issues.Get("local-1");
        Assert.NotNull(issue);
        Assert.Equal("手机新建：夹爪等待超时", issue!.Title);
        Assert.Equal("P7391101", issue.MachineCode);
        Assert.Equal("张工", issue.AssemblyStage!.OwnerText);
        Assert.Equal("11.28", issue.AssemblyStage.DueRaw);
        Assert.Equal(3, issue.SeqNo.Length);
        Assert.NotEqual("L001", issue.SeqNo);
    }

    [Fact]
    public void 同一clientOpId重发_返回duplicate且不重复写()
    {
        using var db = new TempDb();
        var device = PairDevice(db);
        var op = CreateOp();

        var first = db.Sync.ApplyOperations(device, new[] { op });
        var second = db.Sync.ApplyOperations(device, new[] { op });

        Assert.Equal("applied", first[0].Status);
        Assert.Equal("duplicate", second[0].Status);
        Assert.Equal(37, db.Issues.All().Count);
    }

    [Fact]
    public void 阶段推进_版本一致写入_版本落后返回冲突()
    {
        using var db = new TempDb();
        var device = PairDevice(db);
        db.Sync.ApplyOperations(device, new[] { CreateOp() });

        var stageOp = new SyncOperation
        {
            ClientOpId = 2,
            Entity = "issue_stage",
            EntityId = "local-1:assembly",
            Op = "upsert",
            BaseVersion = 1,
            Payload = JsonSerializer.SerializeToElement(new
            {
                owner_text = "李工",
                due_raw = "11.30",
                state = "done",
                actual_date = "11-26",
                remark = "现场已复测"
            })
        };

        var applied = db.Sync.ApplyOperations(device, new[] { stageOp });
        Assert.Equal("applied", applied[0].Status);

        var issue = db.Issues.Get("local-1")!;
        Assert.Equal("李工", issue.AssemblyStage!.OwnerText);
        Assert.Equal(StageState.Done, issue.AssemblyStage.State);

        // 手机没拉到最新版本，仍用 baseVersion=1 重发 → 必须冲突并带回桌面当前值。
        var stale = stageOp with { ClientOpId = 3, BaseVersion = 1 };
        var conflict = db.Sync.ApplyOperations(device, new[] { stale });

        Assert.Equal("conflict", conflict[0].Status);
        Assert.NotNull(conflict[0].ServerPayload);
        Assert.Equal("李工", conflict[0].ServerPayload!.Value
            .GetProperty("ownerText").GetString());
    }

    [Fact]
    public void 状态操作_与桌面推导一致时视为已应用_落后版本才冲突()
    {
        using var db = new TempDb();
        var device = PairDevice(db);
        db.Sync.ApplyOperations(device, new[] { CreateOp() });

        // 手机 saveStage 会顺带入队状态；桌面 SaveStage 已推导出同一状态，不能判冲突。
        var same = new SyncOperation
        {
            ClientOpId = 2,
            Entity = "issue",
            EntityId = "local-1",
            Op = "status",
            BaseVersion = 1,
            Payload = JsonSerializer.SerializeToElement(new { status = "装配中" })
        };
        Assert.Equal("applied", db.Sync.ApplyOperations(device, new[] { same })[0].Status);

        var change = same with
        {
            ClientOpId = 3,
            Payload = JsonSerializer.SerializeToElement(new { status = "设计中" })
        };
        Assert.Equal("applied", db.Sync.ApplyOperations(device, new[] { change })[0].Status);

        // 换一个与当前不同的状态、但 baseVersion 已落后 → 必须冲突并带回桌面当前值。
        var stale = change with
        {
            ClientOpId = 4,
            BaseVersion = 1,
            Payload = JsonSerializer.SerializeToElement(new { status = "备料中" })
        };
        var result = db.Sync.ApplyOperations(device, new[] { stale })[0];
        Assert.Equal("conflict", result.Status);
        Assert.Equal("设计中", result.ServerPayload!.Value.GetProperty("status").GetString());
    }

    [Fact]
    public void 增量拉取_按游标返回_再拉为空()
    {
        using var db = new TempDb();
        var device = PairDevice(db);
        db.Sync.ApplyOperations(device, new[] { CreateOp() });

        var first = db.Sync.Pull(device.Id, 0, 200);
        Assert.True(first.Changes.Count >= 1);
        Assert.True(first.Cursor > 0);

        var change = first.Changes.First(c => c.EntityId == "local-1");
        Assert.Equal("issue", change.Entity);
        Assert.Equal("手机新建：夹爪等待超时",
            change.Payload.GetProperty("title").GetString());

        var second = db.Sync.Pull(device.Id, first.Cursor, 200);
        Assert.Empty(second.Changes);
        Assert.Equal(first.Cursor, second.Cursor);
    }

    [Fact]
    public void 附件上传_落盘幂等_校验sha()
    {
        using var db = new TempDb();
        var device = PairDevice(db);
        db.Sync.ApplyOperations(device, new[] { CreateOp() });

        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var first = db.Sync.SaveAttachment(device, "local-1", "att-1", "IMG_0001.jpg", bytes, sha);
        Assert.True(first.Ok, first.Message);
        Assert.True(File.Exists(db.Attachments.AbsolutePath(first.StoredPath)));

        var again = db.Sync.SaveAttachment(device, "local-1", "att-1", "IMG_0001.jpg", bytes, sha);
        Assert.True(again.Ok);
        Assert.Equal(first.StoredPath, again.StoredPath);

        var bad = db.Sync.SaveAttachment(device, "local-1", "att-2", "IMG_0002.jpg", bytes,
            "deadbeef");
        Assert.False(bad.Ok);
        Assert.Equal("sha256_mismatch", bad.Error);
    }

    [Fact]
    public void 移除设备后_旧令牌立即失效()
    {
        using var db = new TempDb();
        var code = db.Sync.CreatePairCode();
        var outcome = db.Sync.Pair(code.Code, "现场手机", "Pixel 7", "android", "engineer");
        var device = db.Sync.Authenticate(outcome.Token)!;

        Assert.True(db.Sync.RemoveDevice(device.Id));
        Assert.Null(db.Sync.Authenticate(outcome.Token));
    }
}
