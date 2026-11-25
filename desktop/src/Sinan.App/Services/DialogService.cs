using System.Windows;
using Microsoft.Win32;
using Sinan.App.Views;

namespace Sinan.App.Services;

/// <summary>
/// 弹窗服务：ViewModel 不 new Window，也不直接 MessageBox（desktop/AGENTS.md 第 2 节）。
///
/// 约定：业务校验错误必须就地显示在页面上，不走这里；
/// 这里只负责"确认类"与"系统文件选择"两类无法内联的交互。
/// </summary>
public interface IDialogService
{
    bool Confirm(string title, string message, string okText = "确定", string cancelText = "取消");
    void Alert(string title, string message);
    string? PickOpenFile(string filter, string title);
    string? PickSaveFile(string filter, string title, string defaultFileName);

    /// <summary>
    /// 更新阶段弹窗（设计稿 dlg-stage）：阶段切换 + 阶段结果 + 实际完成日 + 责任人 +
    /// 调整计划日 + 变更原因 + 倒挂提示。返回 null 表示用户取消。
    /// </summary>
    StageDialogResult? EditStage(StageDialogRequest request);

    /// <summary>
    /// 删除问题二次确认（设计稿 dlg-del）：必须手抄问题号才能确认，
    /// 避免现场误触把整条问题连同照片一起删掉。返回 true 才真正删除。
    /// </summary>
    bool ConfirmDeleteIssue(DeleteIssueRequest request);

    /// <summary>查看现场照片（设计稿 dlg-photo）：大图 + 元数据 + 上一张/下一张。</summary>
    void ViewPhotos(PhotoViewerRequest request);

    /// <summary>
    /// 导入确认（设计稿 dlg-import-conflict）：五格真实计数 + 冲突行逐条选择
    /// "用原表覆盖 / 保留系统值" + 写入范围。返回 null 表示用户返回校验。
    /// </summary>
    ImportConfirmResult? ConfirmImport(ImportConfirmRequest request);

    /// <summary>
    /// 导出确认（设计稿 dlg-export）：文件名、保存目录、工作表清单、"将包含"逐条。
    /// 返回 null 表示取消。
    /// </summary>
    ExportConfirmResult? ConfirmExport(ExportConfirmRequest request);
}

public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message, string okText = "确定", string cancelText = "取消")
    {
        var owner = Application.Current?.MainWindow;
        var result = owner is { IsVisible: true }
            ? MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        // WPF MessageBox 不能改按钮文案；标题栏已带操作语义，按钮保持系统默认即可。
        _ = okText;
        _ = cancelText;
        return result == MessageBoxResult.OK;
    }

    public void Alert(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is { IsVisible: true })
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public string? PickOpenFile(string filter, string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string filter, string title, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// 三个业务弹窗都走 ChromeDialogWindow，与新建问题弹窗同一套窗口壳
    /// （标题栏、圆角、投影、主题键都来自 Kaleidoscope.UI，不在页面里重复实现）。
    /// Owner 一律取主窗，保证弹窗居中于主窗而不是屏幕。
    /// </summary>
    public StageDialogResult? EditStage(StageDialogRequest request)
    {
        var dialog = new StageDialog { Owner = Application.Current?.MainWindow };
        dialog.Load(request);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public bool ConfirmDeleteIssue(DeleteIssueRequest request)
    {
        var dialog = new DeleteIssueDialog { Owner = Application.Current?.MainWindow };
        dialog.Load(request);
        return dialog.ShowDialog() == true;
    }

    public void ViewPhotos(PhotoViewerRequest request)
    {
        var dialog = new PhotoViewerDialog { Owner = Application.Current?.MainWindow };
        dialog.Load(request);
        dialog.ShowDialog();
    }

    public ImportConfirmResult? ConfirmImport(ImportConfirmRequest request)
    {
        var dialog = new ImportConfirmDialog { Owner = Application.Current?.MainWindow };
        dialog.Load(request);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public ExportConfirmResult? ConfirmExport(ExportConfirmRequest request)
    {
        var dialog = new ExportConfirmDialog { Owner = Application.Current?.MainWindow };
        dialog.Load(request);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
