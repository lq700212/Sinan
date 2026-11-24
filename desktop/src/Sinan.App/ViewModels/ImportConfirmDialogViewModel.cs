using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;

namespace Sinan.App.ViewModels;

/// <summary>
/// 导入确认（设计稿 dlg-import-conflict）：写库前最后一道"看得见"的闸。
///
/// 【为什么要有这个弹窗，而不是一个 MessageBox】原来的确认框只能回答"要不要写"，
/// 而真正需要人判断的是三件事：
///   1) 这一批到底动多少行（新增/更新/冲突/倒挂/文本计划/机台号异常）；
///   2) 冲突行（系统内人工改过、原表也变了）逐条是覆盖还是保留——两边都有价值，
///      全局"全量覆盖/全部跳过"必然静默丢掉一边；
///   3) 写入范围（新增+更新 / 仅新增 / 以 Excel 为准）。
/// 设计稿把这三点放在一个 680 宽的弹窗里，本类照做。
///
/// 【为什么不提供"先清空再导入"】设计稿里有这个选项，但它意味着整库删除重建，
/// 与"数据脱敏/审计留痕/软删除"三条红线冲突，且本系统没有实现整批清空能力。
/// 界面不摆一个点了会报错或悄悄什么都不做的按钮，改为等价的"以 Excel 为准"（冲突也覆盖），
/// 并在提示里说明它会覆盖人工修改。
/// </summary>
public sealed class ImportConfirmDialogViewModel : ObservableObject
{
    private readonly ImportConfirmRequest _request;
    private StrategyChoice _strategy;

    public ImportConfirmDialogViewModel(ImportConfirmRequest request)
    {
        _request = request;

        Strategies = new[]
        {
            new StrategyChoice("merge", "新增 + 更新", "按 ID 匹配，冲突逐条处置"),
            new StrategyChoice("only_new", "仅新增", "已存在的行一律跳过"),
            new StrategyChoice("overwrite", "以 Excel 为准", "冲突也覆盖，会改掉人工修改")
        };
        _strategy = Strategies.FirstOrDefault(s => s.Value == request.Strategy) ?? Strategies[0];

        Conflicts = new ObservableCollection<ImportConflictRowVm>(
            request.Conflicts.Select(r => new ImportConflictRowVm(r, OnConflictChanged)));

        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;

    public string HeaderSubtitle => string.IsNullOrWhiteSpace(_request.FileName)
        ? "写库前最后一步"
        : _request.FileName;

    // ---------------- 五格计数 ----------------

    /// <summary>
    /// 将写入：新增 + 更新 + 当前勾成"用原表覆盖"的冲突行。
    /// "仅新增"策略下冲突行一律跳过，不能算进"将写入"——否则按钮写着"确认写入 4 条"，
    /// 实际只写 2 条，用户会以为丢数据。
    /// </summary>
    public int WillWriteCount
    {
        get
        {
            var conflictsToWrite = _strategy.Value == "only_new"
                ? 0
                : Conflicts.Count(c => c.TakeExcel);
            var updates = _strategy.Value == "only_new" ? 0 : _request.UpdateCount;
            return _request.InsertCount + updates + conflictsToWrite;
        }
    }

    /// <summary>按 ID 更新：只有"更新 + 覆盖的冲突"才算真正改已有行，新增不算。</summary>
    public int WillUpdateCount
    {
        get
        {
            if (_strategy.Value == "only_new") return 0;
            return _request.UpdateCount + Conflicts.Count(c => c.TakeExcel);
        }
    }

    public int InvertedCount => _request.InvertedCount;
    public int TextPlanCount => _request.TextPlanCount;
    public int MachineWarningCount => _request.MachineWarningCount;

    public string WillWriteText => WillWriteCount.ToString();
    public string WillUpdateText => WillUpdateCount.ToString();
    public string InvertedText => InvertedCount.ToString();
    public string TextPlanText => TextPlanCount.ToString();
    public string MachineWarningText => MachineWarningCount.ToString();

    /// <summary>倒挂/文本计划是"要人看一眼"的，格子里转黄；为 0 时不转。</summary>
    public bool HasInverted => InvertedCount > 0;
    public bool HasTextPlan => TextPlanCount > 0;
    public bool HasMachineWarning => MachineWarningCount > 0;

    // ---------------- 冲突 ----------------

    public ObservableCollection<ImportConflictRowVm> Conflicts { get; }
    public bool HasConflict => Conflicts.Count > 0;
    public bool HasNoConflict => Conflicts.Count == 0;

    public string ConflictHeadText => $"{Conflicts.Count} 条";

    public string ConflictHint =>
        "默认策略：以 Excel 原表为准；若系统内已有手工修改，取消勾选即保留系统值。";

    /// <summary>冲突区标题下的来源说明：说明这些行是"同 ID 已存在"而不是新增。</summary>
    public string ConflictSourceText => "同 ID 已存在，且系统内改过、原表也变了";

    // ---------------- 写入范围 ----------------

    public IReadOnlyList<StrategyChoice> Strategies { get; }

    public StrategyChoice Strategy
    {
        get => _strategy;
        set
        {
            if (value == null || !SetProperty(ref _strategy, value)) return;
            OnPropertiesChanged(nameof(IsOverwriteWarningVisible), nameof(IsOnlyNewHintVisible),
                nameof(ConfirmButtonText), nameof(WillWriteCount), nameof(WillUpdateCount),
                nameof(WillWriteText), nameof(WillUpdateText));
        }
    }

    /// <summary>"以 Excel 为准"会覆盖人工修改，必须给红字警示（不阻断，但要看见）。</summary>
    public bool IsOverwriteWarningVisible => _strategy.Value == "overwrite";

    /// <summary>
    /// "仅新增"策略下冲突行一律跳过，逐条勾选不生效——必须明说，
    /// 否则用户在这里一条条勾完，写库后发现冲突行全被跳过，会以为勾选坏了。
    /// </summary>
    public bool IsOnlyNewHintVisible => _strategy.Value == "only_new";

    public string ConfirmButtonText => $"确认写入 {WillWriteCount} 条";

    // ---------------- 底部 ----------------

    public string FooterHint => "写库前最后一步 · 原 Excel 不会被修改";

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>逐条勾选或切换策略时刷新汇总数字与按钮文案。</summary>
    private void OnConflictChanged()
    {
        OnPropertiesChanged(nameof(WillWriteCount), nameof(WillUpdateCount),
            nameof(WillWriteText), nameof(WillUpdateText), nameof(ConfirmButtonText));
    }

    private void Confirm()
    {
        // 把逐条勾选写回请求，供调用方回填到预览行。
        foreach (var vm in Conflicts) vm.Apply();
        _request.Strategy = _strategy.Value;
        Confirmed?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    /// <summary>把逐条决策带回调用方；由 DialogService 在 ShowDialog 返回后取。</summary>
    public ImportConfirmResult BuildResult() => new()
    {
        Proceed = true,
        Strategy = _strategy.Value
    };
}

/// <summary>写入范围选项（三选一）。纯数据：选中态由 ListBox 的 SelectedItem 管。</summary>
public sealed class StrategyChoice
{
    public StrategyChoice(string value, string label, string hint)
    {
        Value = value;
        Label = label;
        Hint = hint;
    }

    public string Value { get; }
    public string Label { get; }
    public string Hint { get; }

    public string AccessibleName => $"写入范围 {Label}";

    public override string ToString() => Label;
}

/// <summary>
/// 冲突列表里的一行（可勾选）。
///
/// 【为什么包一层而不是直接绑 DTO】勾选状态变化要立刻刷新弹窗顶部的"将写入/按 ID 更新"
/// 两个数字，DTO 是纯数据、不带通知；这一层只做"通知 + 写回"两件事。
/// </summary>
public sealed class ImportConflictRowVm : ObservableObject
{
    private readonly ImportConflictRow _row;
    private readonly Action _changed;
    private bool _takeExcel;

    public ImportConflictRowVm(ImportConflictRow row, Action changed)
    {
        _row = row;
        _changed = changed;
        _takeExcel = row.TakeExcel;
    }

    public string IssueCode => _row.IssueCode;
    public string Title => _row.Title;
    public string SystemText => _row.SystemText;
    public string ExcelText => _row.ExcelText;

    /// <summary>true = 用原表覆盖；false = 保留系统值。</summary>
    public bool TakeExcel
    {
        get => _takeExcel;
        set
        {
            if (!SetProperty(ref _takeExcel, value)) return;
            OnPropertiesChanged(nameof(DecisionText), nameof(DecisionHint), nameof(AccessibleName));
            _changed();
        }
    }

    public string DecisionText => _takeExcel ? "用原表覆盖" : "保留系统值";

    public string DecisionHint => _takeExcel
        ? $"原表 {ExcelText}"
        : $"保留 {SystemText}";

    /// <summary>无障碍名：同类多行默认都叫类型名，读屏与自动化分不清是哪一条。</summary>
    public string AccessibleName => $"{IssueCode} {DecisionText}";

    /// <summary>把当前勾选写回 DTO，供调用方回填预览行。</summary>
    public void Apply() => _row.TakeExcel = _takeExcel;
}
