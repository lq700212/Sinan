using Sinan.Core.Models;
using Sinan.Core.Utils;
using Xunit;

namespace Sinan.Tests.Core;

/// <summary>
/// 阶段/状态/倒挂/逾期规则（根 AGENTS.md 第 5 节 + desktop/AGENTS.md 第 6 节口径）。
/// 这些是统计与导出共用的唯一口径，页面里不允许再写第二套。
/// </summary>
public class StageRulesTests
{
    private static IssueStage Stage(string code, StageState state,
        string? dueRaw = null, DateOnly? due = null, string owner = "")
        => new()
        {
            Code = code,
            State = state,
            DueRaw = dueRaw ?? "",
            DueDate = due,
            OwnerText = owner
        };

    // ---------------- DeriveStatus ----------------

    [Fact]
    public void DeriveStatus_无阶段_返回待分派()
    {
        Assert.Equal(IssueStatuses.Unassigned, StageRules.DeriveStatus(Array.Empty<IssueStage>()));
    }

    [Fact]
    public void DeriveStatus_四阶段全部完成_返回待验证()
    {
        var stages = new[]
        {
            Stage(StageCodes.Plan, StageState.Done),
            Stage(StageCodes.Design, StageState.Done),
            Stage(StageCodes.Material, StageState.Done),
            Stage(StageCodes.Assembly, StageState.Done)
        };
        Assert.Equal(IssueStatuses.Verifying, StageRules.DeriveStatus(stages));
    }

    [Fact]
    public void DeriveStatus_全部不适用_视为待验证()
    {
        var stages = new[]
        {
            Stage(StageCodes.Plan, StageState.NotApplicable),
            Stage(StageCodes.Design, StageState.NotApplicable),
            Stage(StageCodes.Material, StageState.NotApplicable),
            Stage(StageCodes.Assembly, StageState.NotApplicable)
        };
        Assert.Equal(IssueStatuses.Verifying, StageRules.DeriveStatus(stages));
    }

    [Fact]
    public void DeriveStatus_完成情况有文本_直接待验证()
    {
        var stages = new[] { Stage(StageCodes.Plan, StageState.InProgress) };
        Assert.Equal(IssueStatuses.Verifying, StageRules.DeriveStatus(stages, "现场已确认"));
    }

    [Fact]
    public void DeriveStatus_首阶段未指派未计划_待分派()
    {
        var stages = new[]
        {
            Stage(StageCodes.Plan, StageState.NotStarted),
            Stage(StageCodes.Design, StageState.NotStarted)
        };
        Assert.Equal(IssueStatuses.Unassigned, StageRules.DeriveStatus(stages));
    }

    [Fact]
    public void DeriveStatus_方案已完成_设计未填_应推进到设计中而不是倒回待分派()
    {
        // AGENTS 明确要求的回归：方案保存完不能从"方案中"倒退回"待分派"。
        var stages = new[]
        {
            Stage(StageCodes.Plan, StageState.Done),
            Stage(StageCodes.Design, StageState.NotStarted),
            Stage(StageCodes.Material, StageState.NotStarted),
            Stage(StageCodes.Assembly, StageState.NotStarted)
        };
        Assert.Equal(IssueStatuses.Designing, StageRules.DeriveStatus(stages));
    }

    [Fact]
    public void DeriveStatus_首阶段进行中_返回方案中()
    {
        var stages = new[] { Stage(StageCodes.Plan, StageState.InProgress) };
        Assert.Equal(IssueStatuses.Planning, StageRules.DeriveStatus(stages));
    }

    [Fact]
    public void DeriveStatus_前一阶段不适用_下一阶段未填_也向前推进()
    {
        var stages = new[]
        {
            Stage(StageCodes.Plan, StageState.NotApplicable),
            Stage(StageCodes.Design, StageState.NotStarted)
        };
        Assert.Equal(IssueStatuses.Designing, StageRules.DeriveStatus(stages));
    }

    [Fact]
    public void DeriveStatus_首阶段未开始但已指派责任人_返回方案中()
    {
        var stages = new[] { Stage(StageCodes.Plan, StageState.NotStarted, owner: "张工") };
        Assert.Equal(IssueStatuses.Planning, StageRules.DeriveStatus(stages));
    }

    [Fact]
    public void DeriveStatus_首阶段未开始但有计划_返回方案中()
    {
        var stages = new[] { Stage(StageCodes.Plan, StageState.NotStarted, dueRaw: "11.5") };
        Assert.Equal(IssueStatuses.Planning, StageRules.DeriveStatus(stages));
    }

    // ---------------- 倒挂 ----------------

    [Fact]
    public void IsPlanInverted_设计晚于装配_为真()
    {
        var issue = new Issue
        {
            Stages =
            {
                Stage(StageCodes.Design, StageState.InProgress, due: new DateOnly(2026, 11, 10)),
                Stage(StageCodes.Assembly, StageState.InProgress, due: new DateOnly(2026, 11, 5))
            }
        };
        Assert.True(StageRules.IsPlanInverted(issue));
        Assert.Equal(5, StageRules.InvertedDays(issue));
        Assert.Equal("倒挂 5 天", issue.InvertedText);
    }

    [Fact]
    public void IsPlanInverted_同日或设计更早_为假()
    {
        Assert.False(StageRules.IsPlanInverted(
            new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5)));
        Assert.False(StageRules.IsPlanInverted(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5)));
    }

    [Fact]
    public void IsPlanInverted_任一日期缺失_为假()
    {
        Assert.False(StageRules.IsPlanInverted(null, new DateOnly(2026, 11, 5)));
        Assert.False(StageRules.IsPlanInverted(new DateOnly(2026, 11, 5), null));
        Assert.False(StageRules.IsPlanInverted(null, null));
    }

    [Fact]
    public void IsPlanInverted_已下图与斜杠不参与()
    {
        var issue = new Issue
        {
            Stages =
            {
                Stage(StageCodes.Design, StageState.Done, dueRaw: "已下图"),
                Stage(StageCodes.Assembly, StageState.InProgress, due: new DateOnly(2026, 11, 5))
            }
        };
        Assert.False(issue.IsInverted);
        Assert.Equal(0, StageRules.InvertedDays(issue));
    }

    // ---------------- 逾期 ----------------

    [Fact]
    public void OverdueDays_未完成且计划已过_按最长天数计()
    {
        var today = AppTime.Today;
        var issue = new Issue
        {
            Stages =
            {
                Stage(StageCodes.Plan, StageState.Done, due: today.AddDays(-90)),
                Stage(StageCodes.Design, StageState.InProgress, due: today.AddDays(-3)),
                Stage(StageCodes.Material, StageState.InProgress, due: today.AddDays(-7)),
                Stage(StageCodes.Assembly, StageState.NotStarted)
            }
        };
        Assert.Equal(7, StageRules.OverdueDays(issue));
    }

    [Fact]
    public void OverdueDays_已关闭或已取消_不算逾期()
    {
        var today = AppTime.Today;
        var closed = new Issue
        {
            Status = IssueStatuses.Closed,
            Stages = { Stage(StageCodes.Plan, StageState.Done, due: today.AddDays(-30)) }
        };
        var cancelled = new Issue
        {
            Status = IssueStatuses.Cancelled,
            Stages = { Stage(StageCodes.Plan, StageState.InProgress, due: today.AddDays(-30)) }
        };
        Assert.Equal(0, StageRules.OverdueDays(closed));
        Assert.Equal(0, StageRules.OverdueDays(cancelled));
    }

    [Fact]
    public void OverdueDays_软删除问题不算逾期()
    {
        var today = AppTime.Today;
        var issue = new Issue
        {
            DeletedAt = AppTime.UtcNowIso(),
            Stages = { Stage(StageCodes.Plan, StageState.InProgress, due: today.AddDays(-30)) }
        };
        Assert.Equal(0, StageRules.OverdueDays(issue));
    }

    [Fact]
    public void OverdueDays_不适用与已完成阶段不参与()
    {
        var today = AppTime.Today;
        var issue = new Issue
        {
            Stages =
            {
                Stage(StageCodes.Plan, StageState.NotApplicable, due: today.AddDays(-90)),
                Stage(StageCodes.Design, StageState.Done, due: today.AddDays(-60)),
                Stage(StageCodes.Material, StageState.InProgress) // 无 due_date
            }
        };
        Assert.Equal(0, StageRules.OverdueDays(issue));
    }

    [Fact]
    public void OverdueDays_计划为今天_不算逾期()
    {
        var issue = new Issue
        {
            Stages = { Stage(StageCodes.Plan, StageState.InProgress, due: AppTime.Today) }
        };
        Assert.Equal(0, StageRules.OverdueDays(issue));
    }

    // ---------------- InferState ----------------

    [Fact]
    public void InferState_斜杠_不适用()
    {
        Assert.Equal(StageState.NotApplicable, StageRules.InferState("张工", "/"));
        Assert.Equal(StageState.NotApplicable, StageRules.InferState("", "／"));
    }

    [Fact]
    public void InferState_已下图_已完成()
    {
        Assert.Equal(StageState.Done, StageRules.InferState("张工", "已下图"));
        Assert.Equal(StageState.Done, StageRules.InferState("", "", actualDone: true));
    }

    [Fact]
    public void InferState_责任人计划都空_未开始()
    {
        Assert.Equal(StageState.NotStarted, StageRules.InferState("", ""));
        Assert.Equal(StageState.NotStarted, StageRules.InferState("   ", null));
    }

    [Fact]
    public void InferState_有责任人或有计划_进行中()
    {
        Assert.Equal(StageState.InProgress, StageRules.InferState("张工", ""));
        Assert.Equal(StageState.InProgress, StageRules.InferState("", "11.5"));
    }

    [Theory]
    [InlineData("plan", IssueStatuses.Planning)]
    [InlineData("design", IssueStatuses.Designing)]
    [InlineData("material", IssueStatuses.Material)]
    [InlineData("assembly", IssueStatuses.Assembling)]
    [InlineData("unknown", IssueStatuses.Unassigned)]
    public void ForStage_映射到中文状态(string code, string expected)
    {
        Assert.Equal(expected, IssueStatuses.ForStage(code));
    }
}
