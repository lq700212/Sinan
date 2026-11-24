using System;
using System.Collections.ObjectModel;
using Sinan.App.Mvvm;
using Sinan.App.Services;

namespace Sinan.App.ViewModels;

/// <summary>
/// 导出确认（设计稿 dlg-export）：点"开始导出"后先让人看清"这次到底导出什么"。
///
/// 【为什么导出还要再确认一次】导出看起来无害，实际有两个真实代价：
///   1) 文件落在固定目录，同一分钟内重导会撞名（后端已追加 _2/_3），
///      用户需要先看到"这次生成的文件名是什么"，否则事后在目录里找不到；
///   2) 原表版式带倒挂标红/文本计划标黄，是直接发给客户的成品——
///      发出去之前必须知道这一批里有多少条异常行。
/// 所以这个弹窗不是"确认一下"，而是"导出前的事实清单"。
/// </summary>
public sealed class ExportConfirmDialogViewModel : ObservableObject
{
    private readonly ExportConfirmRequest _request;
    private string _fileName;
    private bool _openFolderAfterExport = true;

    public ExportConfirmDialogViewModel(ExportConfirmRequest request)
    {
        _request = request;
        _fileName = request.FileName;

        // "将包含"逐条按真实数据生成：没有照片就不出现照片那条，
        // 没有倒挂就不写"0 条倒挂标红"（那句话会让客户以为表里有问题）。
        Items = new ObservableCollection<ExportIncludeItem>();
        Items.Add(new ExportIncludeItem(true, $"{request.IssueCount} 条问题，列序与原表一致"));
        if (request.InvertedCount > 0)
            Items.Add(new ExportIncludeItem(true, $"{request.InvertedCount} 条倒挂行标红，需协调会确认"));
        if (request.TextPlanCount > 0)
            Items.Add(new ExportIncludeItem(true, $"{request.TextPlanCount} 处文本计划标黄（如“已下图”），原文保留"));
        Items.Add(new ExportIncludeItem(true, "“/”与空白分别保留，不统一成空"));
        if (request.PhotoCount > 0 && request.IncludeAttachments)
            Items.Add(new ExportIncludeItem(true, $"{request.PhotoCount} 张现场照片随包附带（manifest.csv 索引）"));
        Items.Add(new ExportIncludeItem(false, "不包含：人员权限、操作日志明细"));

        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;

    public string HeaderSubtitle => $"{_request.LayoutLabel} · {_request.IssueCount} 条";

    /// <summary>文件名可改：默认按"项目_版式_时间戳"生成，用户想加批次号时自己补。</summary>
    public string FileName
    {
        get => _fileName;
        set
        {
            if (!SetProperty(ref _fileName, value)) return;
            OnPropertiesChanged(nameof(FileNameHint), nameof(CanConfirm));
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanConfirm => !string.IsNullOrWhiteSpace(_fileName);

    public string FileNameHint => string.IsNullOrWhiteSpace(_fileName)
        ? "文件名不能为空"
        : $"将生成 {EnsureXlsx(_fileName)}";

    /// <summary>保存目录：真实归档路径（%LOCALAPPDATA%\Sinan\archive\exports），不是设计稿的演示路径。</summary>
    public string Directory => _request.Directory;

    public string LayoutLabel => _request.LayoutLabel;
    public string ScopeLabel => _request.ScopeLabel;

    /// <summary>工作表清单：本次实际会写哪几个 sheet，与后端写入顺序一致。</summary>
    public string SheetListText
    {
        get
        {
            if (_request.Sheets.Count == 0) return "—";
            return string.Join(" / ", _request.Sheets);
        }
    }

    public ObservableCollection<ExportIncludeItem> Items { get; }

    public bool OpenFolderAfterExport
    {
        get => _openFolderAfterExport;
        set => SetProperty(ref _openFolderAfterExport, value);
    }

    public string FooterHint => "不会修改数据库";

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>导出结果由调用方负责（后台任务），这里只把用户的选择带回去。</summary>
    public ExportConfirmResult BuildResult() => new()
    {
        FileName = EnsureXlsx(_fileName),
        OpenFolderAfterExport = _openFolderAfterExport
    };

    /// <summary>补扩展名：用户可能只改了主名，后端按 .xlsx 写盘，界面必须显示真实文件名。</summary>
    private static string EnsureXlsx(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "";
        return trimmed.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".xlsx";
    }

    private void Confirm() => Confirmed?.Invoke(this, EventArgs.Empty);

    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}

/// <summary>"将包含"清单里的一条：<see cref="Included"/> 决定用勾还是圈。</summary>
public sealed class ExportIncludeItem
{
    public ExportIncludeItem(bool included, string text)
    {
        Included = included;
        Text = text;
    }

    public bool Included { get; }
    public string Text { get; }

    /// <summary>勾=包含（绿），圈=不包含（灰）；模板按这个切换徽标色。</summary>
    public string TagKind => Included ? "ok" : "plain";
    public string Mark => Included ? "✓" : "○";
}
