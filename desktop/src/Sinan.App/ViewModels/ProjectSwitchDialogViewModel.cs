using System;
using System.Collections.ObjectModel;
using System.Linq;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;

namespace Sinan.App.ViewModels;

/// <summary>
/// 切换总项目（总览头卡"进行中"右侧入口）。
///
/// 交互口径（用户明确要求）：弹出总项目列表，点某个项目即切换，不再多一步确认。
/// 每行显示实时规模（分项目 / 设备 / 问题），全部由 ProjectRepository.ListSummaries
/// 查库得到，弹窗不自己算数字。
/// </summary>
public sealed class ProjectSwitchDialogViewModel : ObservableObject
{
    private readonly ProjectSwitchRequest _request;

    public ProjectSwitchDialogViewModel(ProjectSwitchRequest request)
    {
        _request = request;
        Projects = new ObservableCollection<ProjectSummary>(request.Projects);

        SelectCommand = new RelayCommand(p => Select(p as ProjectSummary));
        CreateCommand = new RelayCommand(CreateNew);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? CreateNewRequested;
    public event EventHandler? Selected;
    public event EventHandler? Cancelled;

    public ObservableCollection<ProjectSummary> Projects { get; }

    public string Subtitle => $"{Projects.Count} 个总项目";

    public string Hint => Projects.Count == 0
        ? "库里还没有总项目，先新建一个。"
        : "点一行即切换；切换后台账、报表、导出全部跟随新项目。";

    public bool IsEmpty => Projects.Count == 0;

    public RelayCommand SelectCommand { get; }
    public RelayCommand CreateCommand { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>用户点中的项目；点"当前"这一行也允许，主壳会识别为无需切换并静默返回。</summary>
    public ProjectSwitchResult? Result { get; private set; }

    /// <summary>点行即切换：写入 Result 并发事件，由 code-behind 关闭窗口。</summary>
    public void Select(ProjectSummary? summary)
    {
        if (summary == null) return;
        Result = new ProjectSwitchResult { SelectedProjectId = summary.Id };
        Selected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>转去"新建总项目"：写入结果并发事件，由 code-behind 关闭本窗。</summary>
    public void CreateNew()
    {
        Result = new ProjectSwitchResult { CreateNew = true };
        CreateNewRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>供 code-behind 判断"是否真的换了项目"，用于关闭窗口。</summary>
    public string CurrentProjectId => _request.CurrentProjectId;

    public bool IsCurrent(ProjectSummary summary) =>
        string.Equals(summary.Id, _request.CurrentProjectId, StringComparison.Ordinal);
}
