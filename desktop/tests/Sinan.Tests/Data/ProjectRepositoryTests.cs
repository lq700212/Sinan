using Sinan.App.ViewModels;
using Sinan.Core.Models;
using Sinan.Core.Utils;
using Sinan.Tests.Infrastructure;
using Xunit;

namespace Sinan.Tests.Data;

/// <summary>
/// 项目/分项目/统计口径。总览、工作台、报表的数字必须全部从同一份 issues 实时算，
/// 禁止页面写死；这里用种子库做交叉验证。
/// </summary>
public class ProjectRepositoryTests
{
    [Theory]
    [InlineData("P7391101", "P7391-01")]
    [InlineData("P7391110", "P7391-01")]
    [InlineData("P7391201", "P7391-02")]
    [InlineData("P7391208", "P7391-02")]
    [InlineData("P7391301", "P7391-03")]
    [InlineData("P7391312", "P7391-03")]
    [InlineData("P73913118", null)] // 已知笔误不自动归组
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SubprojectRules_机台归属(string? code, string? expected)
    {
        Assert.Equal(expected, SubprojectRules.Resolve(code));
    }

    [Fact]
    public void GetOverview_与种子库一致()
    {
        using var db = new TempDb();
        var overview = db.Projects.GetOverview();

        Assert.Equal("P7391", overview.Project.Code);
        Assert.Equal(36, overview.IssueCount);
        Assert.Equal(31, overview.MachineCount);
        Assert.Equal(3, overview.SubprojectCount);
        Assert.Equal(1, overview.UnassignedMachineCount);
        Assert.Equal(1, overview.UnassignedIssueCount);
        Assert.Equal(4, overview.StageFills.Count);
        Assert.All(overview.StageFills, f => Assert.Equal(36, f.Total));
        Assert.Contains(overview.Cards, c => c.Code == "P7391-01");
        Assert.Equal(4, overview.Cards.Count); // 3 个分项目 + 待确认
    }

    [Fact]
    public void GetOverview_分项目卡片机台与问题数与详情一致()
    {
        using var db = new TempDb();
        var overview = db.Projects.GetOverview();

        foreach (var card in overview.Cards.Where(c => !c.IsUnassigned))
        {
            var detail = db.Projects.GetSubprojectDetail(card.Code);
            Assert.NotNull(detail);
            Assert.Equal(card.MachineCount, detail!.MachineCount);
            Assert.Equal(card.IssueCount, detail.IssueCount);
            Assert.Equal(card.InvertedCount, detail.InvertedCount);
        }
    }

    [Fact]
    public void GetOverview_照片计数_12张6条()
    {
        using var db = new TempDb();
        db.Attachments.EnsureDemoAttachments();

        var overview = db.Projects.GetOverview();
        Assert.Equal(12, overview.PhotoCount);
        Assert.Equal(6, overview.PhotoIssueCount);
        Assert.Equal(3, overview.PhotoBuckets.Count(b => b.Count > 0));
        Assert.Equal(12, overview.PhotoBuckets.Sum(b => b.Count));
    }

    [Fact]
    public void 分项目详情_P7391_01_10台15条()
    {
        using var db = new TempDb();
        var detail = db.Projects.GetSubprojectDetail("P7391-01");

        Assert.NotNull(detail);
        Assert.Equal("P7391-01", detail!.Code);
        Assert.Equal(10, detail.MachineCount);
        Assert.Equal(15, detail.IssueCount);
        Assert.False(detail.IsUnassigned);
        Assert.All(detail.Machines, m => Assert.Equal("P7391-01", m.SubprojectCode));
    }

    [Fact]
    public void 分项目详情_待确认_单台单条()
    {
        using var db = new TempDb();
        foreach (var code in new[] { "待确认", "", null })
        {
            var detail = db.Projects.GetSubprojectDetail(code);
            Assert.NotNull(detail);
            Assert.True(detail!.IsUnassigned);
            Assert.Equal(1, detail.MachineCount);
            Assert.Equal(1, detail.IssueCount);
            Assert.Equal("P73913118", detail.Machines.Single().Code);
        }
    }

    [Fact]
    public void 分项目详情_不存在返回null()
    {
        using var db = new TempDb();
        Assert.Null(db.Projects.GetSubprojectDetail("P7391-99"));
    }

    [Fact]
    public void GetDashboard_风险清单只含倒挂或逾期且不超过8条()
    {
        using var db = new TempDb();
        var data = db.Projects.GetDashboard(db.Engineer);

        Assert.Equal(36, data.TotalIssues);
        Assert.Equal(31, data.TotalMachines);
        Assert.Equal(3, data.TotalSubprojects);
        Assert.True(data.Risks.Count <= 8);
        Assert.All(data.Risks, i =>
        {
            Assert.False(i.IsClosed);
            Assert.True(i.IsInverted || i.OverdueDays > 0);
        });

        // 全库风险数与清单无关；清单是排序后的前 8 条
        var all = db.Issues.All();
        var expectedRisk = all.Count(i => !i.IsClosed && (i.IsInverted || i.OverdueDays > 0));
        Assert.Equal(Math.Min(8, expectedRisk), data.Risks.Count);
    }

    [Fact]
    public void GetDashboard_我的待办按登录人姓名匹配责任人()
    {
        using var db = new TempDb();
        var engineer = db.Engineer;
        var all = db.Issues.All();

        var expected = all.Count(i => !i.IsClosed && i.Stages.Any(s =>
            TextUtil.SplitOwners(s.OwnerText).Contains(engineer.Name, StringComparer.Ordinal)));
        var data = db.Projects.GetDashboard(engineer);

        Assert.Equal(expected, data.MyTodoCount);
    }

    [Fact]
    public void GetReport_四项总数实时且口径一致()
    {
        using var db = new TempDb();
        var report = db.Projects.GetReport();
        var all = db.Issues.All();

        Assert.Equal(all.Count, report.TotalIssues);
        Assert.Equal(all.Select(i => i.MachineId).Distinct().Count(), report.TotalMachines);
        Assert.Equal(all.Count(i => i.IsInverted), report.InvertedCount);
        Assert.Equal(all.Count(i => i.OverdueDays > 0), report.OverdueCount);
        Assert.Equal(all.Count(i => i.IsClosed), report.ClosedCount);
        Assert.Equal(4, report.StageFills.Count);
        Assert.Equal(4, report.Subprojects.Count); // 3 + 待确认
    }

    [Fact]
    public void GetReport_责任人负载去重且总数正确()
    {
        using var db = new TempDb();
        var report = db.Projects.GetReport();
        var all = db.Issues.All();

        // BuildOwnerLoads 的口径：拆多人 → 过滤 "/" → 按人分组；StageTaskCount 是任务数
        var expectedTasks = all
            .SelectMany(i => i.Stages)
            .SelectMany(s => TextUtil.SplitOwners(s.OwnerText))
            .Count(o => !RawDateParser.IsNotApplicable(o));

        Assert.Equal(expectedTasks, report.OwnerLoads.Sum(o => o.StageTaskCount));
        Assert.Equal(report.OwnerLoads.Count, report.OwnerLoads.Select(o => o.Name).Distinct().Count());
        Assert.All(report.OwnerLoads, o => Assert.True(o.IssueCount <= o.StageTaskCount));
    }

    [Fact]
    public void 新增逾期问题_总览与报表立即变化()
    {
        using var db = new TempDb();
        var beforeOverview = db.Projects.GetOverview();
        var beforeReport = db.Projects.GetReport();

        var project = db.Projects.GetMain()!;
        var machine = db.Projects.ListMachines(project.Id, null, true).First();
        var today = AppTime.Today;
        var created = db.Issues.Create(new IssueDraft
        {
            ProjectId = project.Id,
            MachineId = machine.Id,
            Title = "统计实时性回归：逾期问题",
            Solution = "测试",
            Plan = new StageDraft
            {
                Code = StageCodes.Plan,
                OwnerText = "张工",
                DueRaw = RawDateParser.FormatRaw(today.AddDays(-10)),
                State = StageState.InProgress
            }
        }, db.Engineer);

        Assert.True(db.Issues.Get(created.Id)!.OverdueDays > 0);

        var afterOverview = db.Projects.GetOverview();
        var afterReport = db.Projects.GetReport();

        Assert.Equal(beforeOverview.IssueCount + 1, afterOverview.IssueCount);
        Assert.Equal(beforeReport.OverdueCount + 1, afterReport.OverdueCount);
    }

    [Fact]
    public void 机台按编号自然排序()
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;
        var subs = db.Projects.ListSubprojects(project.Id);
        var machines = db.Projects.ListMachines(project.Id, subs[0].Id);

        var seqs = machines.Select(m => TextUtil.MachineSeq(m.Code) ?? int.MaxValue).ToList();
        Assert.Equal(seqs.OrderBy(s => s), seqs);
        Assert.Equal("P7391101", machines.First().Code);
    }

    [Fact]
    public void 连续新建总项目_自动建三个分项目且拦截重复与格式错误()
    {
        using var db = new TempDb();

        // "总项目能连续新增多个"：连建两个，项目号不冲突，各自都有 01/02/03。
        var first = db.Projects.CreateProject("P7392", "P7392 安装调试问题闭环", "演示客户", db.Engineer);
        var second = db.Projects.CreateProject("P7393", "P7393 安装调试问题闭环", "", db.Engineer);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(
            new[] { "P7391", "P7392", "P7393" },
            db.Projects.ListProjects().Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray());

        foreach (var project in new[] { first, second })
        {
            var subs = db.Projects.ListSubprojects(project.Id);
            Assert.Equal(
                new[] { project.Code + "-01", project.Code + "-02", project.Code + "-03" },
                subs.Select(s => s.Code).ToArray());
        }

        // 重复项目号必须拦在写库前；格式不合法的（少一位）同样拦截。
        Assert.Throws<InvalidOperationException>(() =>
            db.Projects.CreateProject("P7392", "重复项目", "", db.Engineer));
        Assert.Throws<InvalidOperationException>(() =>
            db.Projects.CreateProject("P739", "格式错误", "", db.Engineer));

        // 新建弹窗的建议号按最大值 +1，连建后自动变成 P7394。
        Assert.Equal("P7394", ProjectCreateDialogViewModel.NextCode(
            db.Projects.ListProjects().Select(p => p.Code)));
    }

    [Fact]
    public void GetOrCreateMachine_未知机台自动建档并归组()
    {
        using var db = new TempDb();
        var project = db.Projects.GetMain()!;

        var machine = db.Projects.GetOrCreateMachine(project.Id, "P7391120", AppTime.UtcNowIso());
        Assert.Equal("P7391120", machine.Code);
        Assert.NotNull(machine.SubprojectId);

        // 再调用不重复建档
        var again = db.Projects.GetOrCreateMachine(project.Id, "P7391120", AppTime.UtcNowIso());
        Assert.Equal(machine.Id, again.Id);

        var bad = db.Projects.GetOrCreateMachine(project.Id, "P73913199", AppTime.UtcNowIso());
        Assert.Null(bad.SubprojectId); // 格式异常 → 待确认
    }
}
