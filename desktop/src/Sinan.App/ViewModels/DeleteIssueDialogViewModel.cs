using System;
using Sinan.App.Mvvm;
using Sinan.App.Services;

namespace Sinan.App.ViewModels;

/// <summary>
/// 删除问题二次确认（设计稿 dlg-del）。
///
/// 【为什么必须手抄问题号】删除是软删除，但界面上"这一条连同照片、评论、审计一起消失"
/// 对现场是不可逆的感知；设计稿把"输入 ID"做成第二道闸，就是为了拦住误触。
/// 输入框内容与问题号逐字比对（忽略大小写与首尾空白），不匹配则确认按钮不可用。
///
/// 【为什么不提供"同时删除照片文件"】根 AGENTS.md 第 6 节（照片/附件）：删除问题不删附件原文件，
/// 等归档策略统一处理。界面给出这句说明，而不是给一个做不到的勾选框。
/// </summary>
public sealed class DeleteIssueDialogViewModel : ObservableObject
{
    private readonly DeleteIssueRequest _request;
    private string _typedCode = "";

    public DeleteIssueDialogViewModel(DeleteIssueRequest request)
    {
        _request = request;
        ConfirmCommand = new RelayCommand(() => Confirmed?.Invoke(this, EventArgs.Empty), () => Matched);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;

    public string IssueCode => _request.IssueCode;
    public string IssueTitle => _request.IssueTitle;

    public string WarningText =>
        $"确定删除 {_request.IssueCode}「{_request.IssueTitle}」？";

    /// <summary>影响面说明：条数与照片数都是真实计数，不是占位。</summary>
    public string ImpactText
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_request.ActivityCount > 0) parts.Add($"{_request.ActivityCount} 条修改留痕");
            if (_request.PhotoCount > 0) parts.Add($"{_request.PhotoCount} 张现场照片");
            var head = parts.Count == 0 ? "该问题还没有留痕与照片" : $"该问题有 {string.Join("、", parts)}";
            return $"{head}。删除后统计、导出与手机端同步都会移除它。";
        }
    }

    public string TypedCode
    {
        get => _typedCode;
        set
        {
            if (!SetProperty(ref _typedCode, value)) return;
            OnPropertiesChanged(nameof(Matched), nameof(MatchTag), nameof(MatchText));
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>逐字匹配（忽略大小写与首尾空白）；不匹配时确认按钮置灰。</summary>
    public bool Matched =>
        string.Equals((_typedCode ?? "").Trim(), (_request.IssueCode ?? "").Trim(),
            StringComparison.OrdinalIgnoreCase);

    public string MatchTag => Matched ? "匹配" : "待输入";
    public string MatchText => Matched ? "已匹配，可以删除" : $"请输入 {_request.IssueCode} 以确认";

    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CancelCommand { get; }
}
