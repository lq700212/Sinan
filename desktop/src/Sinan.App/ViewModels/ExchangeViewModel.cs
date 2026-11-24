using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;
using Sinan.Core.Utils;

namespace Sinan.App.ViewModels;

/// <summary>
/// 数据交换（设计稿 p3 import / export）：Excel 原表进出 + 移动端配对说明。
///
/// 纪律：
/// - 导入先在后台解析成预览（新增/更新/无变化/冲突/错误逐行可见），用户选策略后才写库；
/// - 写入逐行独立事务，单行失败不影响整体，失败原因回填 Messages；
/// - 导出统一落在数据根目录 archive\exports，界面只给路径与"打开目录"，不弹保存对话框
///   自选路径，避免现场把导出文件散落到桌面然后找不到。
/// - 移动端配对服务尚未实现（MVP 单机批），页面如实说明，不展示假配对码。
/// </summary>
public sealed class ExchangeViewModel : ObservableObject
{
    private readonly App _app;
    private readonly INavigationService _nav;

    private ImportPreview? _preview;
    private ImportResult? _importResult;
    private ExportResult? _exportResult;
    private StrategyOption _strategy;
    private bool _useOriginalLayout = true;
    private bool _includeAttachments = true;
    private bool _includeChangeLog = true;
    private bool _isBusy;
    private string _errorMessage = "";
    private string _noticeMessage = "";
    private string _filePath = "";
    private int _step = 1;
    private LayoutOption _layout;
    private ScopeOption _scope;

    public ExchangeViewModel(App app, INavigationService nav)
    {
        _app = app;
        _nav = nav;

        Strategies = new[]
        {
            new StrategyOption("merge", "合并（推荐）", "新增缺失行；覆盖来自 Excel 的字段；冲突行跳过并计数"),
            new StrategyOption("only_new", "只新增", "已存在的机台+序号一律跳过，不动库内数据"),
            new StrategyOption("overwrite", "以 Excel 为准", "新增 + 更新 + 冲突强制覆盖；风险最高，导入前请先导出备份")
        };
        _strategy = Strategies[0];

        Layouts = new[]
        {
            new LayoutOption("original", "客户原表版式",
                "保留合并表头、四阶段分组、列序与列宽；给客户回表用这个"),
            new LayoutOption("standard", "标准明细表",
                "一行一条问题，阶段拆成独立列，便于二次分析"),
            new LayoutOption("summary", "统计汇总",
                "责任人负载、阶段填写率、倒挂清单；协调会用这个")
        };
        _layout = Layouts[0];

        Scopes = new[]
        {
            new ScopeOption("all", "全部问题", "忽略筛选，导出库内全部在册问题"),
            new ScopeOption("inverted", "仅计划倒挂", "只导设计计划晚于装配计划的行"),
            new ScopeOption("overdue", "仅逾期", "只导已过计划日且未完成的行"),
            new ScopeOption("photos", "有现场照片的问题", "只导带附件的行，附照片清单")
        };
        _scope = Scopes[0];

        Rows = new ObservableCollection<ImportRowPreview>();

        PreviewCommand = new AsyncRelayCommand(PreviewAsync);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        ClearPreviewCommand = new RelayCommand(ClearPreview);
        OpenExportFolderCommand = new RelayCommand(OpenExportFolder);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        StepBackCommand = new RelayCommand(() => Step = Math.Max(1, Step - 1), () => CanGoBack);
        StepNextCommand = new RelayCommand(() => Step = Math.Min(4, Step + 1), () => CanGoNext);
        GoStepCommand = new RelayCommand(p => GoStep(p));

        LastImportDisplay = app.Projects.GetOverview().LastImportDisplay;
        ExportDirDisplay = Path.Combine(app.Database.Paths.ArchiveDir, "exports");
        DbPathDisplay = app.Database.Paths.DbPath;
        AttachmentsDirDisplay = app.Database.Paths.AttachmentsDir;
        SyncStatusText = "配对服务尚未启用（当前为离线单机版）";
    }

    public ObservableCollection<ImportRowPreview> Rows { get; }
    public IReadOnlyList<StrategyOption> Strategies { get; }
    public IReadOnlyList<LayoutOption> Layouts { get; }
    public IReadOnlyList<ScopeOption> Scopes { get; }

    /// <summary>
    /// 导入向导当前步骤（1 选择文件 → 2 列映射 → 3 校验预览 → 4 确认导入）。
    /// 设计稿要求"四步固定，当前步高亮，已完成的步骤可回看"：
    /// 这里只做状态机，回看靠 <see cref="GoStepCommand"/> 允许跳回已完成步。
    /// </summary>
    public int Step
    {
        get => _step;
        private set
        {
            if (!SetProperty(ref _step, value)) return;
            OnPropertiesChanged(nameof(StepTitle), nameof(CanGoBack), nameof(CanGoNext),
                nameof(StepHint), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4));
            StepBackCommand.RaiseCanExecuteChanged();
            StepNextCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;

    public string StepTitle => Step switch
    {
        1 => "第 1 步 · 选择文件",
        2 => "第 2 步 · 列映射",
        3 => "第 3 步 · 校验预览",
        _ => "第 4 步 · 确认导入"
    };

    public string StepHint => Step switch
    {
        1 => "选一个 Excel 原表，系统自动探测表头行与列映射；原表永不被修改。",
        2 => "确认系统识别出的列映射；识别不出的列会留空，不猜。",
        3 => "逐行看清将写入什么：新增 / 更新 / 无变化 / 冲突 / 错误。",
        _ => "确认策略与条数后写入；写入会记录批次与逐行来源，可整批回溯。"
    };

    public bool CanGoBack => Step > 1;
    public bool CanGoNext => Step switch
    {
        1 => HasPreview,
        2 => HasPreview,
        3 => HasPreview,
        _ => false
    };

    public LayoutOption Layout
    {
        get => _layout;
        set
        {
            if (value == null || !SetProperty(ref _layout, value)) return;
            // 版式决定"原表版式"开关，两者是同一件事的两种表达，必须同步。
            _useOriginalLayout = value.Value == "original";
            OnPropertiesChanged(nameof(UseOriginalLayout), nameof(ExportEstimateText));
        }
    }

    public ScopeOption Scope
    {
        get => _scope;
        set
        {
            if (value == null || !SetProperty(ref _scope, value)) return;
            OnPropertiesChanged(nameof(ExportEstimateText));
        }
    }

    /// <summary>导出前的估算文案：让用户在点按钮前就知道会导多少条。</summary>
    public string ExportEstimateText
    {
        get
        {
            var all = _app.Issues.All();
            int count = (Scope?.Value ?? "all") switch
            {
                "inverted" => all.Count(i => i.IsInverted),
                "overdue" => all.Count(i => i.OverdueDays > 0),
                "photos" => all.Count(i => i.PhotoCount > 0),
                _ => all.Count
            };
            return $"将导出 {count} 条 · 版式「{Layout?.Label ?? "原表版式"}」";
        }
    }

    public string LastImportDisplay { get; }
    public string ExportDirDisplay { get; }
    public string DbPathDisplay { get; }
    public string AttachmentsDirDisplay { get; }
    public string SyncStatusText { get; }
    public string SyncPortDisplay => "规划端口 5180 · 仅局域网 · 当前未监听";

    public StrategyOption Strategy
    {
        get => _strategy;
        set
        {
            if (value != null) SetProperty(ref _strategy, value);
        }
    }

    public bool UseOriginalLayout
    {
        get => _useOriginalLayout;
        set
        {
            if (!SetProperty(ref _useOriginalLayout, value)) return;
            // 旧开关与新版式是同一件事：勾上=原表版式，取消=标准明细。
            // 只在两者确实不一致时才改 Layout，避免把"统计汇总"冲掉。
            var expected = value ? "original" : "standard";
            if (_layout.Value != expected)
                Layout = Layouts.FirstOrDefault(l => l.Value == expected) ?? _layout;
        }
    }

    public bool IncludeAttachments
    {
        get => _includeAttachments;
        set => SetProperty(ref _includeAttachments, value);
    }

    public bool IncludeChangeLog
    {
        get => _includeChangeLog;
        set => SetProperty(ref _includeChangeLog, value);
    }

    public string FilePath
    {
        get => _filePath;
        private set => SetProperty(ref _filePath, value);
    }

    public ImportPreview? Preview
    {
        get => _preview;
        private set
        {
            if (!SetProperty(ref _preview, value)) return;
            OnPropertiesChanged(nameof(HasPreview), nameof(PreviewSummary), nameof(CanApply));
        }
    }

    public bool HasPreview => Preview != null;
    public bool CanApply => Preview != null && (Preview.InsertCount + Preview.UpdateCount > 0);
    public string PreviewSummary => Preview == null ? "" : Preview.Summary;

    public ImportResult? ImportResult
    {
        get => _importResult;
        private set
        {
            if (!SetProperty(ref _importResult, value)) return;
            OnPropertiesChanged(nameof(HasImportResult), nameof(ImportResultSummary), nameof(ImportMessages));
        }
    }

    public bool HasImportResult => ImportResult != null;
    public string ImportResultSummary => ImportResult?.Summary ?? "";
    public string ImportMessages => ImportResult == null ? "" : string.Join("\n", ImportResult.Messages);

    public ExportResult? ExportResult
    {
        get => _exportResult;
        private set
        {
            if (!SetProperty(ref _exportResult, value)) return;
            OnPropertiesChanged(nameof(HasExportResult), nameof(ExportResultSummary));
        }
    }

    public bool HasExportResult => ExportResult != null;
    public string ExportResultSummary => ExportResult == null
        ? ""
        : $"{ExportResult.Summary} · {FormatSize(ExportResult.FileSize)}";

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
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

    public string NoticeMessage
    {
        get => _noticeMessage;
        private set
        {
            if (!SetProperty(ref _noticeMessage, value)) return;
            OnPropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeMessage);

    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
    public RelayCommand ClearPreviewCommand { get; }
    public RelayCommand OpenExportFolderCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand StepBackCommand { get; }
    public RelayCommand StepNextCommand { get; }
    public RelayCommand GoStepCommand { get; }

    /// <summary>
    /// 跳到指定步：只允许跳到"已完成"的步（≤ 当前进度）。
    /// 允许回头改策略/看映射，但不允许跳过解析直接进第 3/4 步——
    /// 那会让用户以为"确认导入"能在没有预览的情况下工作。
    /// </summary>
    private void GoStep(object? parameter)
    {
        if (parameter is not string text || !int.TryParse(text, out int target)) return;
        if (target < 1 || target > 4) return;
        if (target > Step) return;
        Step = target;
    }

    private async Task PreviewAsync()
    {
        ErrorMessage = "";
        NoticeMessage = "";

        var path = _app.Dialogs.PickOpenFile(
            "Excel 原表|*.xls;*.xlsx|Excel 97-2003|*.xls|Excel 工作簿|*.xlsx|所有文件|*.*",
            "选择要导入的 Excel 原表");
        if (string.IsNullOrWhiteSpace(path)) return;

        FilePath = path;
        IsBusy = true;
        try
        {
            var preview = await Task.Run(() => _app.Import.Preview(path));

            Preview = preview;
            Rows.Clear();
            foreach (var row in preview.Rows) Rows.Add(row);

            ImportResult = null;
            if (preview.ErrorCount > 0)
                ErrorMessage = $"解析完成，但有 {preview.ErrorCount} 行错误，写入时将逐行跳过。";
            else
                NoticeMessage = $"解析完成：共 {preview.Total} 行，可写入 {preview.InsertCount + preview.UpdateCount} 行。";

            // 解析成功 → 直接进第 3 步（校验预览）。第 2 步（列映射）由 StepBack 回看，
            // 因为映射结果本身就是解析产物，用户第一步要看的是"将要写什么"。
            Step = 3;
        }
        catch (Exception ex)
        {
            AppLog.Error("Excel 预览失败", ex);
            ErrorMessage = $"解析失败：{ex.Message}";
            Preview = null;
            Rows.Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 导入确认弹窗的请求快照：把预览里的真实计数与冲突行原样带过去，
    /// 弹窗只负责"让人看见 + 逐条决策"，不做第二次解析。
    /// </summary>
    public ImportConfirmRequest BuildConfirmRequest()
    {
        var preview = Preview ?? throw new InvalidOperationException("还没有解析结果。");
        var request = new ImportConfirmRequest
        {
            FileName = preview.FileName,
            Total = preview.Total,
            InsertCount = preview.InsertCount,
            UpdateCount = preview.UpdateCount,
            UnchangedCount = preview.UnchangedCount,
            ConflictCount = preview.ConflictCount,
            ErrorCount = preview.ErrorCount,
            InvertedCount = preview.InvertedRowCount,
            TextPlanCount = preview.TextPlanRowCount,
            MachineWarningCount = preview.MachineWarningCount,
            Strategy = Strategy.Value
        };

        foreach (var row in preview.ConflictRows)
        {
            request.Conflicts.Add(new ImportConflictRow
            {
                SourceRow = row.SourceRow,
                // 冲突行必然对应库内已有记录，用它的真实 ID（P7391-006），
                // 而不是原表序号拼出来的 P7391-001——后者在台账里搜不到。
                IssueCode = string.IsNullOrWhiteSpace(row.ExistingIssueId)
                    ? FormatIssueCode(row.SeqNo)
                    : row.ExistingIssueId,
                Title = row.Title,
                SystemText = row.ConflictSystemText,
                ExcelText = row.ConflictExcelText,
                TakeExcel = row.TakeExcelOnConflict,
                // 把行对象直接带回去，弹窗改完勾选后由调用方写回，避免靠 SourceRow 二次匹配。
                Tag = row
            });
        }

        return request;
    }

    /// <summary>
    /// 问题号展示口径与详情页一致：`P7391-` + 三位序号。序号本身是原表文本，
    /// 能解析成数字就补零，解析不了（"已下图"这类脏值）原样拼——绝不因格式化改原文。
    /// </summary>
    private static string FormatIssueCode(string seqNo)
    {
        var seq = (seqNo ?? "").Trim();
        if (seq.Length == 0) return "P7391-?";
        return int.TryParse(seq, out var n) ? $"P7391-{n:D3}" : $"P7391-{seq}";
    }

    private async Task ApplyAsync()
    {
        var preview = Preview;
        if (preview == null) return;

        ErrorMessage = "";
        NoticeMessage = "";

        // 写库前必须过一道"看得见"的确认：条数、倒挂/文本计划/机台号异常，
        // 以及冲突行逐条选覆盖还是保留。原来只弹一个系统 MessageBox，
        // 冲突行只能靠"策略"整体处理——两边都有价值时必然丢一边。
        var request = BuildConfirmRequest();
        var decision = _app.Dialogs.ConfirmImport(request);
        if (decision == null) return;

        // 把用户在弹窗里的逐条决定写回预览行，Apply 才能按行处置。
        foreach (var conflict in request.Conflicts)
        {
            if (conflict.Tag is ImportRowPreview row)
                row.TakeExcelOnConflict = conflict.TakeExcel;
        }

        if (!decision.Proceed) return;

        var user = _app.Session.User;
        if (user == null)
        {
            ErrorMessage = "登录已失效，请重新登录。";
            return;
        }

        IsBusy = true;
        try
        {
            var strategy = Strategy.Value;
            var result = await Task.Run(() => _app.Import.Apply(preview, strategy, user));

            ImportResult = result;
            NoticeMessage = $"导入完成：{result.Summary}。";
            if (result.Conflicts > 0)
                ErrorMessage = $"{result.Conflicts} 行因冲突或错误被跳过，详见下方消息。";
            else if (result.Skipped > 0)
                NoticeMessage += $" 跳过 {result.Skipped} 行。";

            Step = 4;
            _nav.NotifyDataChanged();
        }
        catch (Exception ex)
        {
            AppLog.Error("Excel 写入失败", ex);
            ErrorMessage = $"写入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAsync()
    {
        ErrorMessage = "";
        NoticeMessage = "";

        var user = _app.Session.User;
        if (user == null)
        {
            ErrorMessage = "登录已失效，请重新登录。";
            return;
        }

        var project = _app.Projects.GetMain();
        if (project == null)
        {
            ErrorMessage = "数据库中没有项目。";
            return;
        }

        // 先算这次要导多少条、有多少异常行，再弹确认框：
        // 弹窗里的"将包含"必须与真正写盘的清单一致，不能先弹空壳再算。
        var all = _app.Issues.All();
        var scopeValue = Scope?.Value ?? "all";
        var scoped = scopeValue switch
        {
            "inverted" => all.Where(i => i.IsInverted).ToList(),
            "overdue" => all.Where(i => i.OverdueDays > 0).ToList(),
            "photos" => all.Where(i => i.PhotoCount > 0).ToList(),
            _ => all
        };

        var layoutValue = Layout?.Value ?? "original";
        var request = new ExportConfirmRequest
        {
            FileName = BuildDefaultFileName(project.Code, layoutValue),
            Directory = ExportDirDisplay,
            LayoutLabel = Layout?.Label ?? "客户原表版式",
            ScopeLabel = Scope?.Label ?? "全部问题",
            IssueCount = scoped.Count,
            InvertedCount = scoped.Count(i => i.IsInverted),
            TextPlanCount = scoped.Count(CountTextPlans),
            PhotoCount = scoped.Sum(i => i.PhotoCount),
            IncludeAttachments = IncludeAttachments,
            IncludeChangeLog = IncludeChangeLog
        };

        // 工作表清单按后端实际写入顺序拼：主表 → 附件清单 →（可选）变更记录 → 导出说明。
        request.Sheets.Add(layoutValue switch
        {
            "standard" => "问题台账",
            "summary" => "统计汇总",
            _ => "问题清单"
        });
        request.Sheets.Add("附件清单");
        if (IncludeChangeLog) request.Sheets.Add("变更记录");
        request.Sheets.Add("导出说明");

        var decision = _app.Dialogs.ConfirmExport(request);
        if (decision == null) return;

        IsBusy = true;
        try
        {
            var exportRequest = new ExportRequest
            {
                ProjectId = project.Id,
                Layout = layoutValue,
                Scope = scopeValue,
                IncludeAttachments = IncludeAttachments,
                IncludeChangeLog = IncludeChangeLog,
                SelectAll = true,
                // 用户在确认弹窗里改过的文件名优先；空则回落到后端默认命名。
                PreferredFileName = decision.FileName
            };

            var result = await Task.Run(() => _app.Export.Export(exportRequest, user));
            ExportResult = result;
            NoticeMessage = $"导出完成：{result.Summary}。文件已放入 {ExportDirDisplay}";

            if (decision.OpenFolderAfterExport) OpenExportFolder();
        }
        catch (Exception ex)
        {
            AppLog.Error("Excel 导出失败", ex);
            ErrorMessage = $"导出失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 默认文件名：项目号_版式_时间戳，与后端命名口径一致（后端撞名时追加 _2/_3）。
    /// 放在界面侧生成是为了让用户在弹窗里看得见、改得动，而不是写盘后才发现叫什么。
    /// </summary>
    private static string BuildDefaultFileName(string projectCode, string layout) =>
        $"{projectCode}_{layout switch
        {
            "standard" => "标准明细",
            "summary" => "统计汇总",
            _ => "原表版式"
        }}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

    /// <summary>该问题是否有"文本计划"（如"已下图"）：与导入侧同一判据，导出前如实计数。</summary>
    private static bool CountTextPlans(Issue issue) =>
        issue.Stages.Any(s =>
            !string.IsNullOrWhiteSpace(s.DueRaw) &&
            !RawDateParser.IsNotApplicable(s.DueRaw) &&
            RawDateParser.MeansDrawingIssued(s.DueRaw));

    private void ClearPreview()
    {
        Preview = null;
        ImportResult = null;
        Rows.Clear();
        FilePath = "";
        ErrorMessage = "";
        NoticeMessage = "";
    }

    private void OpenExportFolder() => OpenFolder(ExportDirDisplay);

    private void OpenDataFolder() => OpenFolder(_app.Database.Paths.Root);

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"打开目录失败：{ex.Message}");
            ErrorMessage = $"无法打开目录：{path}";
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B"
    };
}

public sealed class StrategyOption
{
    public StrategyOption(string value, string label, string hint)
    {
        Value = value;
        Label = label;
        Hint = hint;
    }

    public string Value { get; }
    public string Label { get; }
    public string Hint { get; }
}

/// <summary>导出格式（设计稿 export 页「输出格式」三选一）。</summary>
public sealed class LayoutOption
{
    public LayoutOption(string value, string label, string hint)
    {
        Value = value;
        Label = label;
        Hint = hint;
    }

    public string Value { get; }
    public string Label { get; }
    public string Hint { get; }

    /// <summary>
    /// 无障碍名：列表项默认名是类型名，三项全叫 "LayoutOption"，
    /// 读屏与自动化都分不清选的是哪个版式。
    /// </summary>
    public string AccessibleName => $"版式 {Label}";

    public override string ToString() => Label;
}

/// <summary>导出范围（设计稿 export 页「导出范围」）。</summary>
public sealed class ScopeOption
{
    public ScopeOption(string value, string label, string hint)
    {
        Value = value;
        Label = label;
        Hint = hint;
    }

    public string Value { get; }
    public string Label { get; }
    public string Hint { get; }

    public string AccessibleName => $"范围 {Label}";

    public override string ToString() => Label;
}
