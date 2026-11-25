using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;
using Sinan.Core.Utils;

namespace Sinan.App.ViewModels;

/// <summary>
/// 更新阶段弹窗（设计稿 dlg-stage）：在一个窗口里完成"选阶段 → 填结果 → 填实际完成日 →
/// 调计划日 → 写变更原因"，比在详情页四张卡片里翻找更贴近现场"一次说清这一段"的习惯。
///
/// 【与详情页四阶段卡的关系】两者写的是同一批 StageDraft、同一套仓储与乐观锁；
/// 弹窗只是把"当前这一段"放大，保存后详情页整条重读（见 IssueDetailViewModel.Reload）。
///
/// 【三条业务纪律，全部来自根 AGENTS.md 第 5 节与设计稿 dlg-stage】
/// 1. 改计划日必须填变更原因（字段级审计要能回答"为什么改"），只推进阶段可留空；
/// 2. 倒挂只告警不阻断：装配计划日早于设计计划日时给出红条，但仍允许保存；
/// 3. 阶段结果与计划原文是两个字段：`/` 表示不适用，空白表示未填，不能混。
/// </summary>
public sealed class StageDialogViewModel : ObservableObject
{
    private readonly StageDialogRequest _request;

    private StageDialogChoice _current;
    private StageState _state;
    private string _ownerText = "";
    private string _dueRaw = "";
    private string _actualDateRaw = "";
    private string _remark = "";
    private string _changeReason = "";
    private string _errorMessage = "";

    public StageDialogViewModel(StageDialogRequest request)
    {
        _request = request;

        // 默认选中：调用方指定 > 第一个有实际完成日/进行中的 > 第一个。
        // 现场点"填写阶段"时最可能要继续推进当前在做的这一段。
        _current = request.Stages.FirstOrDefault(s => s.Code == request.SelectedCode)
                   ?? request.Stages.FirstOrDefault()
                   ?? throw new InvalidOperationException("该问题没有可编辑的阶段。");

        StageTabs = new ObservableCollection<StageTab>(
            request.Stages.Select(s => new StageTab(s.Code, s.Label)));

        SelectStageCommand = new RelayCommand(p => SelectStage(p as string));
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));

        LoadFrom(_current);
        SyncTabs();
    }

    /// <summary>用户确认保存：code-behind 用它取结果并关闭弹窗。</summary>
    public StageDialogResult? Result { get; private set; }

    public event EventHandler? Saved;
    public event EventHandler? Cancelled;

    public string IssueCode => _request.IssueCode;
    public string HeaderSubtitle => $"{_request.IssueCode} · {_request.IssueTitle}";

    /// <summary>顶部阶段切换条：当前段由 <see cref="StageTab.IsCurrent"/> 高亮。</summary>
    public ObservableCollection<StageTab> StageTabs { get; }

    public string CurrentLabel => _current.Label;

    /// <summary>阶段结果四选一。用 ListBox 单选承载（设计稿是三个 chk，四个状态都能落到界面）。</summary>
    public IReadOnlyList<StageStateOption> StateOptions { get; } = new[]
    {
        new StageStateOption(StageState.NotStarted),
        new StageStateOption(StageState.InProgress),
        new StageStateOption(StageState.Done),
        new StageStateOption(StageState.NotApplicable)
    };

    /// <summary>ListBox 的选中项 ↔ StageState 双向绑定（避免在 XAML 里写比较转换器）。</summary>
    public StageStateOption SelectedStateOption
    {
        get => StateOptions.First(o => o.State == _state);
        set
        {
            if (value == null || value.State == _state) return;
            State = value.State;
        }
    }

    public StageState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;

            // 选"不适用"时计划日应显式写 `/`（根 AGENTS.md 第 5 节第 2 条），
            // 否则导出时"跳过"与"没填"分不清。
            if (value == StageState.NotApplicable && string.IsNullOrWhiteSpace(_dueRaw)) _dueRaw = "/";

            OnPropertiesChanged(nameof(SelectedStateOption), nameof(StateDisplay), nameof(DueRaw),
                nameof(DueHint), nameof(ActualHint), nameof(InvertedHint), nameof(HasInvertedHint),
                nameof(PlanChangedTag), nameof(CanSaveText));
        }
    }

    public string StateDisplay => StageStates.Display(State);

    public string OwnerText
    {
        get => _ownerText;
        set => SetProperty(ref _ownerText, value);
    }

    public string DueRaw
    {
        get => _dueRaw;
        set
        {
            if (!SetProperty(ref _dueRaw, value)) return;
            OnPropertiesChanged(nameof(DueHint), nameof(PlanChangedTag), nameof(PlanChangedHint),
                nameof(NeedReason), nameof(InvertedHint), nameof(HasInvertedHint));
        }
    }

    public string ActualDateRaw
    {
        get => _actualDateRaw;
        set
        {
            if (!SetProperty(ref _actualDateRaw, value)) return;
            OnPropertiesChanged(nameof(InvertedHint), nameof(HasInvertedHint), nameof(ActualHint));
        }
    }

    public string Remark
    {
        get => _remark;
        set => SetProperty(ref _remark, value);
    }

    public string ChangeReason
    {
        get => _changeReason;
        set
        {
            if (!SetProperty(ref _changeReason, value)) return;
            OnPropertiesChanged(nameof(NeedReason));
        }
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

    /// <summary>计划日相对原值是否改过：决定"变更原因"是否必填。</summary>
    public bool PlanChanged =>
        !string.Equals((_dueRaw ?? "").Trim(), (_current.DueRaw ?? "").Trim(), StringComparison.Ordinal);

    public bool NeedReason => PlanChanged;

    /// <summary>计划日旁的徽标文本（设计稿 dlg-stage 的「未改动」tag）。</summary>
    public string PlanChangedTag => PlanChanged ? "已修改" : "未改动";

    public string PlanChangedHint => PlanChanged
        ? $"原计划「{BlankAs(_current.DueRaw)}」，改计划日必须写原因"
        : "与导入时的原文一致";

    /// <summary>计划日旁的辅助说明：能解析成日期就给出"计划 11-05"。</summary>
    public string DueHint
    {
        get
        {
            if (State == StageState.NotApplicable) return "标 / 表示本阶段不适用";
            var raw = (DueRaw ?? "").Trim();
            if (raw.Length == 0) return "留空表示未填计划";
            var parsed = RawDateParser.Parse(raw, AppTime.Today.Year);
            return parsed.HasValue
                ? $"计划 {parsed.Value:MM-dd}"
                : "原文照存（无法解析成日期，不参与逾期与倒挂计算）";
        }
    }

    /// <summary>实际完成日提示：填了日期但阶段结果还不是"已完成"时提醒一句。</summary>
    public string ActualHint
    {
        get
        {
            var raw = (ActualDateRaw ?? "").Trim();
            if (raw.Length == 0) return "留空表示尚未完成";
            if (!RawDateParser.Parse(raw, AppTime.Today.Year).HasValue)
                return "建议写 2026-11-05 这种完整日期";
            return State == StageState.Done
                ? "与阶段结果一致"
                : $"阶段结果仍是「{StateDisplay}」，确认无误再保存";
        }
    }

    /// <summary>
    /// 倒挂提示（设计稿 dlg-stage 底部红条）：用"保存后的计划"重新判断，
    /// 让用户在点保存前就知道会不会产生倒挂，而不是保存完才在详情页看到红标。
    /// 判断走 <see cref="StageRules.IsPlanInverted(DateOnly?, DateOnly?)"/>，与统计同一口径。
    /// </summary>
    public string InvertedHint
    {
        get
        {
            var (designDue, assemblyDue) = ResolvePlanAfterEdit();
            if (!designDue.HasValue || !assemblyDue.HasValue) return "";
            if (designDue.Value <= assemblyDue.Value) return "";

            var days = designDue.Value.DayNumber - assemblyDue.Value.DayNumber;
            return $"设计计划 {designDue.Value:MM-dd}，装配计划 {assemblyDue.Value:MM-dd}，相差 {days} 天。"
                 + "保存后倒挂标记不会消失，需同时调整装配计划。";
        }
    }

    public bool HasInvertedHint => !string.IsNullOrWhiteSpace(InvertedHint);

    public string CanSaveText => "保存并留痕";

    public RelayCommand SelectStageCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>切换阶段：把当前输入写回内存快照，再载入目标阶段（未保存不落库）。</summary>
    private void SelectStage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code == _current.Code) return;

        var target = _request.Stages.FirstOrDefault(s => s.Code == code);
        if (target == null) return;

        StashCurrent();
        _current = target;
        LoadFrom(_current);
        SyncTabs();

        OnPropertiesChanged(nameof(CurrentLabel), nameof(State), nameof(SelectedStateOption),
            nameof(OwnerText), nameof(DueRaw), nameof(ActualDateRaw), nameof(Remark),
            nameof(StateDisplay), nameof(DueHint), nameof(ActualHint), nameof(PlanChangedTag),
            nameof(PlanChangedHint), nameof(NeedReason), nameof(InvertedHint), nameof(HasInvertedHint));
    }

    /// <summary>把当前编辑值回写到快照，切换阶段再切回来时不会丢未保存的输入。</summary>
    private void StashCurrent()
    {
        _current.OwnerText = OwnerText ?? "";
        _current.DueRaw = DueRaw ?? "";
        _current.ActualDateRaw = ActualDateRaw ?? "";
        _current.Remark = Remark ?? "";
    }

    private void SyncTabs()
    {
        foreach (var tab in StageTabs) tab.IsCurrent = tab.Code == _current.Code;
    }

    private void LoadFrom(StageDialogChoice choice)
    {
        _ownerText = choice.OwnerText;
        _dueRaw = choice.DueRaw;
        _actualDateRaw = choice.ActualDateRaw;
        _remark = choice.Remark;

        // 阶段结果由"实际完成日 + 计划原文"反推一个初值：
        // 已填实际完成日 → 已完成；计划是 `/` → 不适用；否则未开始（用户可改）。
        _state = !string.IsNullOrWhiteSpace(choice.ActualDateRaw) ? StageState.Done
               : string.Equals((choice.DueRaw ?? "").Trim(), "/", StringComparison.Ordinal) ? StageState.NotApplicable
               : StageState.NotStarted;

        _changeReason = "";
        ErrorMessage = "";
    }

    /// <summary>
    /// 计算"保存后"的设计/装配计划日：当前阶段用输入框里的值，其他阶段用快照。
    /// 只解析语义日期，`已下图`、`/`、空白都不参与倒挂判断（根 AGENTS.md 第 6 节统计口径）。
    /// </summary>
    private (DateOnly? Design, DateOnly? Assembly) ResolvePlanAfterEdit()
    {
        DateOnly? Resolve(string? raw)
        {
            var text = (raw ?? "").Trim();
            if (text.Length == 0) return null;
            return RawDateParser.Parse(text, AppTime.Today.Year);
        }

        DateOnly? design = null;
        DateOnly? assembly = null;
        foreach (var stage in _request.Stages)
        {
            bool isCurrent = stage.Code == _current.Code;
            var parsed = Resolve(isCurrent ? DueRaw : stage.DueRaw);
            if (stage.Code == StageCodes.Design) design = parsed;
            else if (stage.Code == StageCodes.Assembly) assembly = parsed;
        }
        return (design, assembly);
    }

    private void Save()
    {
        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(OwnerText))
        {
            ErrorMessage = "责任人不能为空：导出的原表需要这个名字，留空会让台账多一条「未填责任人」。";
            return;
        }

        if (State == StageState.Done && string.IsNullOrWhiteSpace(ActualDateRaw))
        {
            ErrorMessage = "阶段结果为「已完成」时必须填实际完成日，否则逾期与阶段填写率都算不准。";
            return;
        }

        if (NeedReason && string.IsNullOrWhiteSpace(ChangeReason))
        {
            ErrorMessage = "改过计划日就必须写变更原因，审计要能回答「为什么改」。";
            return;
        }

        Result = new StageDialogResult
        {
            Code = _current.Code,
            OwnerText = OwnerText?.Trim() ?? "",
            DueRaw = DueRaw?.Trim() ?? "",
            ActualDateRaw = ActualDateRaw?.Trim() ?? "",
            Remark = Remark?.Trim() ?? "",
            ChangeReason = ChangeReason?.Trim() ?? "",
            Version = _current.Version,
            StateDb = StageStates.ToDb(State)
        };

        Saved?.Invoke(this, EventArgs.Empty);
    }

    private static string BlankAs(string? value) => string.IsNullOrWhiteSpace(value) ? "空白" : value;
}

/// <summary>阶段切换条上的一个按钮；IsCurrent 变化时按钮高亮跟随。</summary>
public sealed class StageTab : ObservableObject
{
    private bool _isCurrent;

    public StageTab(string code, string label)
    {
        Code = code;
        Label = label;
    }

    public string Code { get; }
    public string Label { get; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}
