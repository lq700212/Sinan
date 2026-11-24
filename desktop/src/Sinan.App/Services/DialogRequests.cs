using System.Collections.Generic;

namespace Sinan.App.Services;

/// <summary>
/// 自定义弹窗的数据契约。
///
/// 【为什么要 DTO 而不是直接传 ViewModel】AGENTS.md 第 7 节要求 ViewModel 不 new Window，
/// 且服务层不得反向依赖 ViewModel 类型。这里用纯数据请求/结果穿过 <see cref="IDialogService"/>，
/// 弹窗自己负责把结果翻译成对象；ViewModel 只认 DTO，换实现（改成内联面板）不影响业务代码。
/// </summary>

/// <summary>更新阶段弹窗的请求。</summary>
public sealed class StageDialogRequest
{
    /// <summary>问题号（P7391-009），弹窗标题副行显示。</summary>
    public string IssueCode { get; set; } = "";

    public string IssueTitle { get; set; } = "";

    /// <summary>四个阶段的可选项，顺序即业务顺序。</summary>
    public List<StageDialogChoice> Stages { get; set; } = new();

    /// <summary>默认选中的阶段编码；为空时选第一个未完成的阶段。</summary>
    public string SelectedCode { get; set; } = "";
}

/// <summary>更新阶段弹窗里的一个阶段标签（只读快照，用于切换与倒挂提示）。</summary>
public sealed class StageDialogChoice
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public string OwnerText { get; set; } = "";
    public string DueRaw { get; set; } = "";
    public string ActualDateRaw { get; set; } = "";
    public string Remark { get; set; } = "";
    public int Version { get; set; } = 1;

    /// <summary>语义计划日（解析失败为 null）；只用于弹窗内的倒挂提示，不落库。</summary>
    public string DueDateIso { get; set; } = "";
}

/// <summary>更新阶段弹窗的结果；取消时返回 null。</summary>
public sealed class StageDialogResult
{
    public string Code { get; set; } = "";
    public string OwnerText { get; set; } = "";
    public string DueRaw { get; set; } = "";
    public string ActualDateRaw { get; set; } = "";
    public string Remark { get; set; } = "";
    public int Version { get; set; } = 1;

    /// <summary>阶段结果（对应 StageState 的库值：not_started / in_progress / done / na）。</summary>
    public string StateDb { get; set; } = "not_started";

    /// <summary>变更原因：只有改了计划原文才必填（AGENTS 第 6 节第 9 条：字段级审计）。</summary>
    public string ChangeReason { get; set; } = "";
}

/// <summary>删除问题弹窗的请求。</summary>
public sealed class DeleteIssueRequest
{
    public string IssueCode { get; set; } = "";
    public string IssueTitle { get; set; } = "";
    public int ActivityCount { get; set; }
    public int PhotoCount { get; set; }
}

/// <summary>查看现场照片弹窗的请求。</summary>
public sealed class PhotoViewerRequest
{
    public string IssueCode { get; set; } = "";
    public string IssueTitle { get; set; } = "";
    public string MachineCode { get; set; } = "";
    public string SubprojectCode { get; set; } = "";
    public List<PhotoViewerItem> Photos { get; set; } = new();
    public int StartIndex { get; set; }
}

/// <summary>照片弹窗里的一张（文件路径由附件服务解析成绝对路径）。</summary>
public sealed class PhotoViewerItem
{
    public string FileName { get; set; } = "";
    public string Caption { get; set; } = "";
    public string TakenAtDisplay { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public string SizeDisplay { get; set; } = "";
    public string DimensionText { get; set; } = "";

    /// <summary>绝对路径；空表示只有元数据（演示附件或尚未同步到本机）。</summary>
    public string AbsolutePath { get; set; } = "";
}

/// <summary>
/// 导入确认弹窗的请求（设计稿 dlg-import-conflict）。
/// 五格计数与冲突行全部来自 <c>ImportPreview</c> 的真实统计，弹窗不自己再算一遍。
/// </summary>
public sealed class ImportConfirmRequest
{
    public string FileName { get; set; } = "";

    public int Total { get; set; }
    public int InsertCount { get; set; }
    public int UpdateCount { get; set; }
    public int UnchangedCount { get; set; }
    public int ConflictCount { get; set; }
    public int ErrorCount { get; set; }

    /// <summary>倒挂提示行数（设计计划晚于装配计划），与 StageRules 同一判据。</summary>
    public int InvertedCount { get; set; }

    /// <summary>文本计划行数（"已下图"这类解析不成日期的原文）。</summary>
    public int TextPlanCount { get; set; }

    /// <summary>机台号异常行数（不像标准机台号 / 位数异常）。</summary>
    public int MachineWarningCount { get; set; }

    /// <summary>当前向导里选的策略值（merge / only_new / overwrite），弹窗据此决定"写入范围"默认项。</summary>
    public string Strategy { get; set; } = "merge";

    public List<ImportConflictRow> Conflicts { get; set; } = new();
}

/// <summary>导入冲突列表里的一行：同 ID 已存在，且系统内人工改过、原表也变了。</summary>
public sealed class ImportConflictRow
{
    public int SourceRow { get; set; }
    public string IssueCode { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>系统内当前值摘要（如"装配计划 11-15"）。</summary>
    public string SystemText { get; set; } = "";

    /// <summary>原表值摘要（如"装配计划 11-01"）。</summary>
    public string ExcelText { get; set; } = "";

    /// <summary>true = 用原表覆盖；false = 保留系统值。默认取原表（用户此刻的意图就是"以这张表为准"）。</summary>
    public bool TakeExcel { get; set; } = true;

    /// <summary>回指源对象（<c>ImportRowPreview</c>），由调用方写回逐条决策；弹窗只做展示与勾选。</summary>
    public object? Tag { get; set; }
}

/// <summary>导入确认弹窗的结果；用户点"返回校验"时返回 null。</summary>
public sealed class ImportConfirmResult
{
    /// <summary>true 才真正写库；false 表示用户改了勾选但要求返回上一屏再看一眼。</summary>
    public bool Proceed { get; set; }

    /// <summary>逐条决策已由弹窗写回 <see cref="ImportConfirmRequest.Conflicts"/>，这里只带写入范围。</summary>
    public string Strategy { get; set; } = "merge";
}

/// <summary>
/// 导出确认弹窗的请求（设计稿 dlg-export）。
/// 文件名、保存目录、工作表清单与"将包含"逐条都由真实数据生成，
/// 不用设计稿里的占位文案（"D:\Sinan\outbox\" 是演示路径）。
/// </summary>
public sealed class ExportConfirmRequest
{
    public string FileName { get; set; } = "";
    public string Directory { get; set; } = "";

    /// <summary>版式中文名（原表版式 / 标准明细表 / 统计汇总）。</summary>
    public string LayoutLabel { get; set; } = "";

    /// <summary>范围中文名（全部问题 / 仅计划倒挂 / …）。</summary>
    public string ScopeLabel { get; set; } = "";

    public int IssueCount { get; set; }

    /// <summary>倒挂条数（"将包含"里"5 条倒挂行标红"的数字）。</summary>
    public int InvertedCount { get; set; }

    /// <summary>文本计划条数（"已下图"这类，标黄）。</summary>
    public int TextPlanCount { get; set; }

    /// <summary>附件张数；为 0 时"将包含"不出现照片那一条。</summary>
    public int PhotoCount { get; set; }

    /// <summary>将生成的工作表清单（问题台账 / 附件清单 / 变更记录 / 导出说明）。</summary>
    public List<string> Sheets { get; set; } = new();

    /// <summary>是否包含附件（决定是否会额外打包 ZIP）。</summary>
    public bool IncludeAttachments { get; set; } = true;

    /// <summary>是否包含变更记录。</summary>
    public bool IncludeChangeLog { get; set; } = true;
}

/// <summary>导出确认弹窗的结果；取消返回 null。</summary>
public sealed class ExportConfirmResult
{
    /// <summary>用户选择的文件名（可改）；空表示沿用默认名。</summary>
    public string FileName { get; set; } = "";

    /// <summary>导出完成后是否打开文件夹。</summary>
    public bool OpenFolderAfterExport { get; set; } = true;
}
