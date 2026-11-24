using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;
using Sinan.Core.Utils;

namespace Sinan.App.ViewModels;

/// <summary>
/// 新建问题弹窗（设计稿 p1/p2 的「＋ 新建问题」）。
///
/// 规则：
/// - 机台号严格校验 P7391\d{3}(-\d)?；格式不对直接就地报错，不自动"猜"归属；
/// - 机台不存在时在保存时自动建档，并按 1xx/2xx/3xx 规则归入分项目；
/// - 四阶段统一使用原文口径（due_raw），`/` 表示不适用、"已下图"表示已完成，
///   状态由 <see cref="StageRules.InferState"/> 推导，保存前再把整条问题状态推导一次。
/// </summary>
public sealed class NewIssueViewModel : ObservableObject
{
    private readonly App _app;
    private readonly string _projectId;

    private string _machineCode = "";
    private string _seqNo = "";
    private string _title = "";
    private string _solution = "";
    private string _priority = "中";
    private string _category = "";
    private string _errorMessage = "";
    private bool _isBusy;

    public NewIssueViewModel(App app)
    {
        _app = app;
        var project = app.Projects.GetMain()
            ?? throw new InvalidOperationException("数据库中没有项目，无法新建问题。");
        _projectId = project.Id;

        var issues = app.Issues.All();
        MachineOptions = issues
            .Select(i => i.MachineCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        MachineCode = MachineOptions.FirstOrDefault() ?? "P7391101";
        SeqNo = NextSeqNo(issues);
        PriorityOptions = new[] { "高", "中", "低" };

        Stages = new List<StageInput>
        {
            new(StageCodes.Plan, "方案确定"),
            new(StageCodes.Design, "设计下图"),
            new(StageCodes.Material, "物料准备"),
            new(StageCodes.Assembly, "装配调试")
        };

        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler<string>? Saved;
    public event EventHandler? Cancelled;

    public IReadOnlyList<string> MachineOptions { get; }
    public IReadOnlyList<string> PriorityOptions { get; }
    public IReadOnlyList<StageInput> Stages { get; }

    public string MachineCode
    {
        get => _machineCode;
        set
        {
            if (SetProperty(ref _machineCode, value)) ErrorMessage = "";
        }
    }

    public string SeqNo
    {
        get => _seqNo;
        set => SetProperty(ref _seqNo, value);
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value)) ErrorMessage = "";
        }
    }

    public string Solution
    {
        get => _solution;
        set => SetProperty(ref _solution, value);
    }

    public string Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, value);
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value)) return;
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    private async Task SaveAsync()
    {
        if (IsBusy) return;
        ErrorMessage = "";

        var machineCode = TextUtil.Normalize(MachineCode);
        if (!TextUtil.IsStrictMachineCode(machineCode))
        {
            ErrorMessage = "机台号格式不对：应为 P7391 + 3 位数字（可带 -序号），例如 P7391101。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "问题描述不能为空，现场至少要写清「哪里不对」。";
            return;
        }

        var user = _app.Session.User;
        if (user == null)
        {
            ErrorMessage = "登录已失效，请重新登录。";
            return;
        }

        IsBusy = true;
        try
        {
            var now = AppTime.UtcNowIso();
            var machine = _app.Projects.FindMachine(_projectId, machineCode)
                          ?? _app.Projects.GetOrCreateMachine(_projectId, machineCode, now);

            var drafts = Stages.ToDictionary(s => s.Code, s => s.ToDraft());
            var draft = new IssueDraft
            {
                ProjectId = _projectId,
                SubprojectId = machine.SubprojectId,
                MachineId = machine.Id,
                MachineCode = machine.Code,
                SeqNo = SeqNo?.Trim() ?? "",
                Title = Title.Trim(),
                Solution = Solution?.Trim() ?? "",
                Priority = Priority,
                Category = Category?.Trim() ?? "",
                Plan = drafts[StageCodes.Plan],
                Design = drafts[StageCodes.Design],
                Material = drafts[StageCodes.Material],
                Assembly = drafts[StageCodes.Assembly]
            };

            // 状态按阶段推导（与详情页/导入同一套规则），不信任界面默认值。
            var derivedStages = new List<IssueStage>
            {
                ToStage(draft.Plan), ToStage(draft.Design), ToStage(draft.Material), ToStage(draft.Assembly)
            };
            draft.Status = StageRules.DeriveStatus(derivedStages, draft.CompletionText);

            var issue = await Task.Run(() => _app.Issues.Create(draft, user));
            AppLog.Info($"新建问题：{issue.MachineCode} 第 {issue.SeqNo} 项 · {issue.Title}");
            Saved?.Invoke(this, issue.Id);
        }
        catch (Exception ex)
        {
            AppLog.Error("新建问题失败", ex);
            ErrorMessage = $"新建失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static IssueStage ToStage(StageDraft draft) => new()
    {
        Code = draft.Code,
        OwnerText = draft.OwnerText,
        DueRaw = draft.DueRaw,
        State = draft.State
    };

    private static string NextSeqNo(List<Issue> issues)
    {
        var max = issues
            .Select(i => int.TryParse(i.SeqNo, out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 1).ToString();
    }
}

/// <summary>新建弹窗里的一个阶段输入行。</summary>
public sealed class StageInput : ObservableObject
{
    private string _ownerText = "";
    private string _dueRaw = "";

    public StageInput(string code, string label)
    {
        Code = code;
        Label = label;
    }

    public string Code { get; }
    public string Label { get; }

    public string OwnerText
    {
        get => _ownerText;
        set => SetProperty(ref _ownerText, value);
    }

    /// <summary>计划原文：冒号里的 11.5 / 已下图 / / 都照原样保存。</summary>
    public string DueRaw
    {
        get => _dueRaw;
        set => SetProperty(ref _dueRaw, value);
    }

    public StageDraft ToDraft() => new()
    {
        Code = Code,
        OwnerText = OwnerText?.Trim() ?? "",
        DueRaw = DueRaw?.Trim() ?? "",
        State = StageRules.InferState(OwnerText, DueRaw)
    };
}
