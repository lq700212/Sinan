using System.IO;
using Microsoft.Data.Sqlite;
using Sinan.Core.Models;
using Sinan.Core.Utils;
using Sinan.Tests.Infrastructure;
using Xunit;

namespace Sinan.Tests.Data;

/// <summary>
/// 问题仓储：新建、阶段推进、状态机、乐观锁、软删除、评论、审计。
/// 每条都对应根 AGENTS.md 第 5 节的数据红线，改实现时这些用例必须先过。
/// </summary>
public class IssueRepositoryTests
{
    private static IssueDraft NewDraft(TempDb db, string machineId, string masterId = "")
    {
        var project = db.Projects.GetMain()!;
        return new IssueDraft
        {
            ProjectId = project.Id,
            MachineId = machineId,
            Title = "测试新增：联轴器异响",
            Solution = "更换弹性垫并复测",
            Priority = "高",
            Category = "测试"
        };
    }

    [Fact]
    public void All_种子36条_按来源行排序()
    {
        using var db = new TempDb();
        var issues = db.Issues.All();

        Assert.Equal(36, issues.Count);
        var rows = issues.Select(i => i.SourceRow ?? int.MaxValue).ToList();
        Assert.Equal(rows.OrderBy(r => r), rows);
    }

    [Fact]
    public void Get_加载四阶段与审计()
    {
        using var db = new TempDb();
        var issue = db.Issues.Get("P7391-015");

        Assert.NotNull(issue);
        Assert.Equal(4, issue!.Stages.Count);
        Assert.Equal(StageCodes.All, issue.Stages.Select(s => s.Code));
        Assert.Equal("11.5", issue.PlanStage!.DueRaw);        // 原文保留
        Assert.Equal(new DateOnly(2026, 11, 5), issue.PlanStage.DueDate); // 语义值
    }

    [Fact]
    public void Create_成功_四阶段与审计齐备()
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;
        var machine = db.Projects.ListMachines(project.Id, null, unassigned: false)
            .FirstOrDefault() ?? db.Projects.ListMachines(project.Id, null, true).First();

        var created = db.Issues.Create(NewDraft(db, machine.Id), db.Engineer);

        Assert.Equal(36 + 1, db.Issues.All().Count);
        Assert.Equal(4, created.Stages.Count);
        Assert.Equal(1, created.Version);
        Assert.Equal(db.Engineer.Id, created.CreatedBy);
        Assert.Contains(created.Activity, a => a.Field == "新建问题");
        Assert.Equal(machine.Id, created.MachineId);
    }

    [Theory]
    [InlineData("", "机台")]
    [InlineData("   ", "机台")]
    public void Create_缺标题_抛异常(string title, string _)
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;
        var machine = db.Projects.ListMachines(project.Id, null, true).First();
        var draft = NewDraft(db, machine.Id);
        draft.Title = title;

        Assert.Throws<InvalidOperationException>(() => db.Issues.Create(draft, db.Engineer));
    }

    [Fact]
    public void Create_缺机台_抛异常()
    {
        using var db = new TempDb();
        var draft = NewDraft(db, "");
        Assert.Throws<InvalidOperationException>(() => db.Issues.Create(draft, db.Engineer));
    }

    [Fact]
    public void SaveStage_填责任人_状态推进到方案中()
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;
        var machine = db.Projects.ListMachines(project.Id, null, true).First();
        var created = db.Issues.Create(NewDraft(db, machine.Id), db.Engineer);
        Assert.Equal(IssueStatuses.Unassigned, created.Status);

        var plan = created.PlanStage!;
        var updated = db.Issues.SaveStage(created.Id, new StageDraft
        {
            Code = StageCodes.Plan,
            OwnerText = "张工",
            DueRaw = "11.5",
            State = StageState.InProgress,
            Version = plan.Version
        }, db.Engineer);

        Assert.Equal(IssueStatuses.Planning, updated.Status);
        Assert.Equal("张工", updated.PlanStage!.OwnerText);
        Assert.Equal("11.5", updated.PlanStage.DueRaw);
        Assert.Equal(new DateOnly(2026, 11, 5), updated.PlanStage.DueDate);
        Assert.Contains(updated.Activity, a => a.Field == "方案确定责任人" && a.NewValue == "张工");
        Assert.Contains(updated.Activity, a => a.Field == "方案确定计划");
    }

    [Fact]
    public void SaveStage_方案完成_状态到设计中而不是倒退()
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;
        var machine = db.Projects.ListMachines(project.Id, null, true).First();
        var created = db.Issues.Create(NewDraft(db, machine.Id), db.Engineer);

        var updated = db.Issues.SaveStage(created.Id, new StageDraft
        {
            Code = StageCodes.Plan,
            OwnerText = "张工",
            DueRaw = "11.5",
            State = StageState.Done,
            Version = created.PlanStage!.Version
        }, db.Engineer);

        Assert.Equal(IssueStatuses.Designing, updated.Status);
    }

    [Fact]
    public void SaveStage_已下图_原文保留但语义日期为空()
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;
        var machine = db.Projects.ListMachines(project.Id, null, true).First();
        var created = db.Issues.Create(NewDraft(db, machine.Id), db.Engineer);

        var updated = db.Issues.SaveStage(created.Id, new StageDraft
        {
            Code = StageCodes.Design,
            OwnerText = "李工",
            DueRaw = "已下图",
            State = StageState.Done,
            Version = created.DesignStage!.Version
        }, db.Engineer);

        Assert.Equal("已下图", updated.DesignStage!.DueRaw);
        Assert.Null(updated.DesignStage.DueDate);
    }

    [Fact]
    public void SaveStage_旧版本_乐观锁冲突()
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;
        var machine = db.Projects.ListMachines(project.Id, null, true).First();
        var created = db.Issues.Create(NewDraft(db, machine.Id), db.Engineer);
        var stale = created.PlanStage!; // version = 1

        db.Issues.SaveStage(created.Id, new StageDraft
        {
            Code = StageCodes.Plan,
            OwnerText = "张工",
            DueRaw = "11.5",
            State = StageState.InProgress,
            Version = stale.Version
        }, db.Engineer);

        // 第二个窗口仍拿旧 version 提交 → 必须报冲突，不能静默覆盖
        var ex = Assert.Throws<InvalidOperationException>(() => db.Issues.SaveStage(created.Id,
            new StageDraft
            {
                Code = StageCodes.Plan,
                OwnerText = "王工",
                DueRaw = "12.1",
                State = StageState.InProgress,
                Version = stale.Version
            }, db.Engineer));
        Assert.Contains("刷新", ex.Message);
    }

    [Fact]
    public void SetStatus_关闭后写关闭时间_阶段编辑不拉回()
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;
        var machine = db.Projects.ListMachines(project.Id, null, true).First();
        var created = db.Issues.Create(NewDraft(db, machine.Id), db.Engineer);

        var closed = db.Issues.SetStatus(created.Id, IssueStatuses.Closed, db.Engineer);
        Assert.Equal(IssueStatuses.Closed, closed.Status);
        Assert.False(string.IsNullOrWhiteSpace(closed.ClosedAt));
        Assert.Contains(closed.Activity, a => a.Field == "状态" && a.NewValue == IssueStatuses.Closed);

        // 终态保护：再改阶段不应把已关闭拉回进行中
        var after = db.Issues.SaveStage(created.Id, new StageDraft
        {
            Code = StageCodes.Design,
            OwnerText = "李工",
            DueRaw = "11.20",
            State = StageState.InProgress,
            Version = closed.DesignStage!.Version
        }, db.Engineer);
        Assert.Equal(IssueStatuses.Closed, after.Status);
    }

    [Fact]
    public void UpdateDetails_改完成情况_状态到待验证()
    {
        using var db = new TempDb();
        var issue = db.Issues.Get("P7391-001")!; // 待分派种子

        var updated = db.Issues.UpdateDetails(issue.Id, issue.Title, issue.Solution,
            "现场已确认处理完成", "高", "机械", db.Engineer);

        Assert.Equal("现场已确认处理完成", updated.CompletionText);
        Assert.Equal(IssueStatuses.Verifying, updated.Status);
        Assert.Equal("高", updated.Priority);
        Assert.Contains(updated.Activity, a => a.Field == "完成情况");
    }

    [Fact]
    public void AddComment_落库并写审计()
    {
        using var db = new TempDb();
        var comment = db.Issues.AddComment("P7391-015", "  已联系供应商  ", db.Engineer);

        Assert.Equal("已联系供应商", comment.Content);
        var issue = db.Issues.Get("P7391-015")!;
        Assert.Contains(issue.Comments, c => c.Id == comment.Id);
        Assert.Contains(issue.Activity, a => a.Field == "评论" && a.NewValue == "已联系供应商");
    }

    [Fact]
    public void AddComment_空内容_抛异常()
    {
        using var db = new TempDb();
        Assert.Throws<InvalidOperationException>(() =>
            db.Issues.AddComment("P7391-015", "   ", db.Engineer));
    }

    [Fact]
    public void Delete_软删除_列表与详情都不再返回()
    {
        using var db = new TempDb();
        db.Issues.Delete("P7391-001", db.Engineer);

        Assert.Equal(35, db.Issues.All().Count);
        Assert.Null(db.Issues.Get("P7391-001"));

        // 重复删除应报错，而不是静默成功
        Assert.Throws<InvalidOperationException>(() => db.Issues.Delete("P7391-001", db.Engineer));
    }

    [Fact]
    public void Delete_不删附件文件_等归档策略处理()
    {
        using var db = new TempDb();
        var source = Path.Combine(Path.GetTempPath(), $"sinan-del-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(source, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        try
        {
            var attachment = db.Attachments.Add("P7391-001", source, db.Engineer.Id);
            var abs = db.Attachments.AbsolutePath(attachment.StoredPath);
            Assert.True(File.Exists(abs));

            db.Issues.Delete("P7391-001", db.Engineer);

            Assert.True(File.Exists(abs)); // 问题删除不删附件
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public void List_按状态过滤_与全量口径一致()
    {
        using var db = new TempDb();
        var all = db.Issues.All();
        foreach (var status in IssueStatuses.All)
        {
            var expected = all.Count(i => i.Status == status);
            var actual = db.Issues.List(new IssueFilter { Status = status }).Count;
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void List_关键词过滤_命中标题与机台号()
    {
        using var db = new TempDb();
        var target = db.Issues.All().First(i => i.Title.Length >= 2);
        var hit = db.Issues.List(new IssueFilter { Keyword = target.Title[..2] });
        Assert.Contains(hit, i => i.Id == target.Id);

        var byMachine = db.Issues.List(new IssueFilter { Keyword = target.MachineCode });
        Assert.Contains(byMachine, i => i.Id == target.Id);
    }

    [Fact]
    public void List_逾期与倒挂_与StageRules口径一致()
    {
        using var db = new TempDb();
        var all = db.Issues.All();

        var overdue = db.Issues.List(new IssueFilter { OnlyOverdue = true });
        Assert.Equal(all.Count(i => StageRules.OverdueDays(i) > 0), overdue.Count);
        Assert.All(overdue, i => Assert.True(i.OverdueDays > 0));

        var inverted = db.Issues.List(new IssueFilter { OnlyInverted = true });
        Assert.Equal(all.Count(i => i.IsInverted), inverted.Count);
        Assert.All(inverted, i => Assert.True(i.IsInverted));
    }

    [Fact]
    public void List_仅看有照片_与照片计数一致()
    {
        using var db = new TempDb();
        db.Attachments.EnsureDemoAttachments();

        var expected = db.Issues.All().Count(i => i.PhotoCount > 0);
        var actual = db.Issues.List(new IssueFilter { OnlyWithPhoto = true });
        Assert.Equal(expected, actual.Count);
        Assert.All(actual, i => Assert.True(i.PhotoCount > 0));
    }

    [Fact]
    public void List_排除已关闭_不返回已关闭与已取消()
    {
        using var db = new TempDb();
        var open = db.Issues.List(new IssueFilter { IncludeClosed = false });
        Assert.DoesNotContain(open, i => IssueStatuses.IsTerminal(i.Status));
    }

    [Fact]
    public void ApplyImported_阶段原文未变_保留系统人工推进状态()
    {
        using var db = new TempDb();
        var issue = db.Issues.Get("P7391-001")!;
        var beforeState = issue.PlanStage!.State;

        var draft = new IssueDraft
        {
            Id = issue.Id,
            Title = issue.Title,
            Solution = "导入覆盖后的措施",
            CompletionText = issue.CompletionText,
            Plan = new StageDraft
            {
                Code = StageCodes.Plan,
                OwnerText = issue.PlanStage.OwnerText,
                DueRaw = issue.PlanStage.DueRaw,
                State = issue.PlanStage.State,
                Version = issue.PlanStage.Version
            },
            Design = new StageDraft
            {
                Code = StageCodes.Design,
                OwnerText = issue.DesignStage!.OwnerText,
                DueRaw = issue.DesignStage.DueRaw,
                State = issue.DesignStage.State,
                Version = issue.DesignStage.Version
            },
            Material = new StageDraft
            {
                Code = StageCodes.Material,
                OwnerText = issue.MaterialStage!.OwnerText,
                DueRaw = issue.MaterialStage.DueRaw,
                State = issue.MaterialStage.State,
                Version = issue.MaterialStage.Version
            },
            Assembly = new StageDraft
            {
                Code = StageCodes.Assembly,
                OwnerText = issue.AssemblyStage!.OwnerText,
                DueRaw = issue.AssemblyStage.DueRaw,
                State = issue.AssemblyStage.State,
                Version = issue.AssemblyStage.Version
            }
        };

        var updated = db.Issues.ApplyImported(draft, db.Engineer);

        Assert.Equal("导入覆盖后的措施", updated.Solution);
        Assert.Equal(beforeState, updated.PlanStage!.State);
    }

    [Fact]
    public void ApplyImported_阶段原文变化_按原表语义重算状态()
    {
        using var db = new TempDb();
        var issue = db.Issues.Get("P7391-016")!;
        var design = issue.DesignStage!;
        Assert.Equal(StageState.Done, design.State); // 种子里是"已下图"

        var updated = db.Issues.ApplyImported(new IssueDraft
        {
            Id = issue.Id,
            Title = issue.Title,
            Solution = issue.Solution,
            CompletionText = issue.CompletionText,
            Plan = Copy(issue.PlanStage!),
            Design = new StageDraft
            {
                Code = StageCodes.Design,
                OwnerText = design.OwnerText,
                DueRaw = "12.1", // 已下图 → 具体日期
                State = design.State,
                Version = design.Version
            },
            Material = Copy(issue.MaterialStage!),
            Assembly = Copy(issue.AssemblyStage!)
        }, db.Engineer);

        Assert.Equal("12.1", updated.DesignStage!.DueRaw);
        Assert.Equal(StageState.InProgress, updated.DesignStage.State);
    }

    private static StageDraft Copy(IssueStage stage) => new()
    {
        Code = stage.Code,
        OwnerText = stage.OwnerText,
        DueRaw = stage.DueRaw,
        State = stage.State,
        Version = stage.Version
    };
}
