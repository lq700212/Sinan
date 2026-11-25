using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Sinan.App.Mvvm;
using Sinan.App.Services;
using Sinan.Core.Models;
using Sinan.Core.Utils;

namespace Sinan.App.ViewModels;

/// <summary>
/// 问题详情（设计稿 p2 detail）：左侧主信息 + 四阶段推进，右侧附件 / 评论 / 审计。
///
/// 设计取舍：
/// - 阶段"编辑值与库值"分离：界面改完不自动落库，必须点每段的「保存本阶段」，
///   现场用手机拍照回桌面的场景不会因为误触把责任人或计划改掉；
/// - 每次保存都带 version，服务端式乐观锁直接用在本地库，冲突提示"刷新后再试"；
/// - 保存成功后整条问题重新读取，状态推导、审计、附件计数一次性刷新。
/// </summary>
public sealed class IssueDetailViewModel : ObservableObject
{
    private readonly App _app;
    private readonly INavigationService _nav;

    private Issue _issue;
    private string _title = "";
    private string _solution = "";
    private string _completionText = "";
    private string _priority = "中";
    private string _category = "";
    private string _status = "";
    private string _commentDraft = "";
    private string _errorMessage = "";
    private string _noticeMessage = "";
    private bool _isBusy;
    private bool _isEditingDetails;
    private bool _isManagingAttachments;

    public IssueDetailViewModel(App app, INavigationService nav, Issue issue)
    {
        _app = app;
        _nav = nav;
        _issue = issue;

        StageEditors = new ObservableCollection<StageEditor>();
        Attachments = new ObservableCollection<Attachment>();
        Activity = new ObservableCollection<ActivityEntry>();
        Comments = new ObservableCollection<Comment>();

        PriorityOptions = new[] { "高", "中", "低" };
        StatusOptions = IssueStatuses.All;

        SaveDetailsCommand = new AsyncRelayCommand(SaveDetailsAsync);
        SetStatusCommand = new AsyncRelayCommand(SetStatusAsync);
        AddCommentCommand = new AsyncRelayCommand(AddCommentAsync);
        AddAttachmentCommand = new AsyncRelayCommand(AddAttachmentAsync);
        OpenAttachmentCommand = new RelayCommand(OpenAttachment);
        DeleteAttachmentCommand = new AsyncRelayCommand(DeleteAttachmentAsync);
        BackCommand = new RelayCommand(() => _nav.Back());
        RefreshCommand = new RelayCommand(Reload);
        ToggleDetailsEditCommand = new RelayCommand(() => IsEditingDetails = !IsEditingDetails);
        ToggleAttachmentsCommand = new RelayCommand(() => IsManagingAttachments = !IsManagingAttachments);

        // 设计稿 dlg-stage / dlg-del / dlg-photo 三个弹窗入口。
        EditStageCommand = new AsyncRelayCommand(EditStageAsync);
        DeleteIssueCommand = new AsyncRelayCommand(DeleteIssueAsync);
        ViewPhotosCommand = new RelayCommand(ViewPhotos);

        LoadEditableFields();
        RebuildCollections();
    }

    public Issue Issue => _issue;
    public string IssueId => _issue.Id;
    public string MachineCode => _issue.MachineCode;
    public string SubprojectDisplay => string.IsNullOrWhiteSpace(_issue.SubprojectCode) ? "待确认" : _issue.SubprojectCode!;
    public string SeqDisplay => _issue.SeqNo;
    public string DisplaySeqNo => _issue.DisplaySeqNo;

    /// <summary>摘要卡副标题（设计稿 .detail-sub）：机台 · 序号 · 源表行 · 创建时间。</summary>
    public string HeaderSubtitle =>
        $"{MachineCode} · 序号 {SeqDisplay} · {SourceRowText} · 创建于 {CreatedShort}";

    /// <summary>摘要卡左侧大号序号（设计稿 .detail-mark）：取序号后两位，纯装饰。</summary>
    public string SeqBadge
    {
        get
        {
            var seq = (_issue.SeqNo ?? "").Trim();
            return seq.Length <= 2 ? seq : seq[^2..];
        }
    }

    public string DesignOwnerDisplay =>
        string.IsNullOrWhiteSpace(_issue.DesignOwner) ? "—" : _issue.DesignOwner;
    public string DesignDueDisplay =>
        string.IsNullOrWhiteSpace(_issue.DesignDueRaw) ? "—" : _issue.DesignDueRaw;
    public string AssemblyOwnerDisplay =>
        string.IsNullOrWhiteSpace(_issue.AssemblyOwner) ? "—" : _issue.AssemblyOwner;
    public string AssemblyDueDisplay =>
        string.IsNullOrWhiteSpace(_issue.AssemblyDueRaw) ? "—" : _issue.AssemblyDueRaw;

    /// <summary>摘要卡右侧两行计划：阶段名 + 责任人 + 日期。</summary>
    public string DesignPlanLine => $"设计下图 · {DesignOwnerDisplay} · {DesignDueDisplay}";
    public string AssemblyPlanLine => $"装配调试 · {AssemblyOwnerDisplay} · {AssemblyDueDisplay}";

    /// <summary>解决措施（设计稿 .detail-measure 的 .dm-v）；空值给破折号，不留半截。</summary>
    public string MeasureDisplay =>
        string.IsNullOrWhiteSpace(_issue.Solution) ? "—" : _issue.Solution;

    public string SourceRowDisplay => _issue.SourceRow?.ToString() ?? "—";
    public string SourceRowText => _issue.SourceRow.HasValue
        ? $"源表第 {_issue.SourceRow} 行"
        : "无源表行";
    public string CreatedShort => AppTimeDisplay(_issue.CreatedAt, "MM-dd HH:mm");
    public string UpdatedShort => AppTimeDisplay(_issue.UpdatedAt, "MM-dd HH:mm");

    /// <summary>倒挂天数与说明（设计稿右栏"计划倒挂"卡）。</summary>
    public int InvertedDays => _issue.IsInverted ? StageRules.InvertedDays(_issue) : 0;
    public string InvertedDaysText => InvertedDays > 0 ? $"{InvertedDays} 天" : "无";
    public bool IsNotInverted => !_issue.IsInverted;

    /// <summary>倒挂卡正文用的短日期（设计稿写 11-05 / 11-01，不带年份）。</summary>
    public string DesignDueShort => ShortDue(_issue.DesignStage);
    public string AssemblyDueShort => ShortDue(_issue.AssemblyStage);

    public string InvertedDescription =>
        $"设计下图计划 {DesignDueShort}，装配调试计划 {AssemblyDueShort}。\n" +
        (InvertedDays > 0
            ? "按现计划，装配开始时设计图尚未下发。"
            : "当前计划顺序正常，未触发倒挂。");

    /// <summary>倒挂排名（设计稿"5 条之一"）：全库倒挂按天数降序排，给当前条的位置。</summary>
    public string InvertedRankText
    {
        get
        {
            if (!_issue.IsInverted) return "";
            var all = _app.Issues.All()
                .Where(i => i.IsInverted)
                .OrderByDescending(i => StageRules.InvertedDays(i))
                .ThenBy(i => i.SeqNo, StringComparer.Ordinal)
                .ToList();
            var index = all.FindIndex(i => i.Id == _issue.Id);
            return index < 0 ? "" : $"第 {index + 1} 条 / 共 {all.Count} 条";
        }
    }

    /// <summary>四阶段卡底部"最近更新 xx · 谁"：优先取最新一条留痕的人名。</summary>
    public string LastUpdatedLine
    {
        get
        {
            var actor = _issue.Activity
                .OrderByDescending(a => a.Id)
                .Select(a => a.UserName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (string.IsNullOrWhiteSpace(actor))
                actor = _issue.Comments.OrderByDescending(c => c.CreatedAt)
                    .Select(c => c.UserName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            return string.IsNullOrWhiteSpace(actor)
                ? $"最近更新 {UpdatedShort}"
                : $"最近更新 {UpdatedShort} · {actor}";
        }
    }

    /// <summary>阶段备注（设计稿 .stage-note）：取最近更新过且有备注的那一段。</summary>
    private IssueStage? LatestRemarkStage => _issue.Stages
        .Where(s => !string.IsNullOrWhiteSpace(s.Remark))
        .OrderByDescending(s => s.UpdatedAt)
        .ThenByDescending(s => StageCodes.Index(s.Code))
        .FirstOrDefault();

    public bool HasStageNote => LatestRemarkStage != null;
    public string StageNoteLabel => LatestRemarkStage == null
        ? "阶段备注"
        : $"阶段备注 · {LatestRemarkStage.Label}";
    public string StageNoteText => LatestRemarkStage?.Remark ?? "";
    public string StageNoteTime => LatestRemarkStage == null
        ? ""
        : AppTimeDisplay(LatestRemarkStage.UpdatedAt, "MM-dd HH:mm");

    /// <summary>照片标签：有照片写张数，没照片写"暂无照片"，避免出现"现场照片 0 张"。</summary>
    public string StageNotePhotoText => PhotoCount > 0 ? $"现场照片 {PhotoCount} 张" : "暂无照片";

    /// <summary>同机台问题摘要：条数 + 序号列表（最多 6 个），用于"机台档案"。</summary>
    public string SameMachineIssueText
    {
        get
        {
            var siblings = _app.Issues.All()
                .Where(i => i.MachineId == _issue.MachineId)
                .OrderBy(i => i.SourceRow ?? int.MaxValue)
                .ThenBy(i => i.SeqNo, StringComparer.Ordinal)
                .ToList();
            if (siblings.Count == 0) return "—";
            var seqs = string.Join(" / ", siblings.Take(6).Select(i => i.DisplaySeqNo));
            return $"{siblings.Count} 条（{seqs}）";
        }
    }

    /// <summary>主要责任人：优先装配责任人，空则设计责任人。</summary>
    public string PrimaryOwnerText =>
        !string.IsNullOrWhiteSpace(_issue.AssemblyOwner) ? _issue.AssemblyOwner :
        !string.IsNullOrWhiteSpace(_issue.DesignOwner) ? _issue.DesignOwner : "—";

    /// <summary>
    /// 问题号（P7391-027）：弹窗标题与"手抄问题号才可删除"都用它。
    /// 设计稿里问题号统一是 `P7391-` + 三位序号（P7391-009），序号本身是原表文本，
    /// 能解析成数字就补零到三位；解析不了（"已下图"这类脏值）就原样拼，
    /// 绝不因为格式化把原文改掉。
    /// </summary>
    public string IssueCodeDisplay
    {
        get
        {
            var seq = (_issue.SeqNo ?? "").Trim();
            var formatted = int.TryParse(seq, out int n) ? n.ToString("D3") : seq;
            return $"P7391-{formatted}";
        }
    }
    public string StatusDisplay => _issue.Status;
    public string ProgressText => _issue.ProgressText;
    public string OwnerFlow => _issue.OwnerFlow;
    public string DueFlow => _issue.DueFlow;
    public int PhotoCount => _issue.PhotoCount;
    public int OverdueDays => _issue.OverdueDays;
    public bool IsInverted => _issue.IsInverted;
    public bool HasInverted => _issue.IsInverted;
    public bool HasOverdue => _issue.OverdueDays > 0;
    public bool IsClosed => _issue.IsClosed;

    public string SourceTrace => _issue.SourceRow.HasValue
        ? $"来源：Excel 第 {_issue.SourceRow} 行 · 批次 {ShortBatch(_issue.SourceBatchId)}"
        : "来源：手工新建";

    public string CreatedDisplay => AppTimeDisplay(_issue.CreatedAt);
    public string UpdatedDisplay => AppTimeDisplay(_issue.UpdatedAt);

    public ObservableCollection<StageEditor> StageEditors { get; }
    public ObservableCollection<Attachment> Attachments { get; }
    public ObservableCollection<Comment> Comments { get; }
    public ObservableCollection<ActivityEntry> Activity { get; }

    public IReadOnlyList<string> PriorityOptions { get; }
    public IReadOnlyList<string> StatusOptions { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Solution
    {
        get => _solution;
        set => SetProperty(ref _solution, value);
    }

    public string CompletionText
    {
        get => _completionText;
        set => SetProperty(ref _completionText, value);
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

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string CommentDraft
    {
        get => _commentDraft;
        set => SetProperty(ref _commentDraft, value);
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

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>摘要卡下的"编辑主信息"折叠面板；设计稿默认只读，编辑是低频操作。</summary>
    public bool IsEditingDetails
    {
        get => _isEditingDetails;
        set => SetProperty(ref _isEditingDetails, value);
    }

    /// <summary>阶段备注里的"附件明细"折叠：默认收起，点开才显示文件名与删除入口。</summary>
    public bool IsManagingAttachments
    {
        get => _isManagingAttachments;
        set => SetProperty(ref _isManagingAttachments, value);
    }

    public bool HasAttachments => Attachments.Count > 0;
    public bool HasComments => Comments.Count > 0;
    public bool HasActivity => Activity.Count > 0;
    public int CommentCount => Comments.Count;
    public int ActivityCount => Activity.Count;

    public AsyncRelayCommand SaveDetailsCommand { get; }
    public AsyncRelayCommand SetStatusCommand { get; }
    public AsyncRelayCommand AddCommentCommand { get; }
    public AsyncRelayCommand AddAttachmentCommand { get; }
    public RelayCommand OpenAttachmentCommand { get; }
    public AsyncRelayCommand DeleteAttachmentCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleDetailsEditCommand { get; }
    public RelayCommand ToggleAttachmentsCommand { get; }

    /// <summary>打开「更新阶段」弹窗（设计稿 dlg-stage）。</summary>
    public AsyncRelayCommand EditStageCommand { get; }

    /// <summary>打开「删除问题」二次确认（设计稿 dlg-del）。</summary>
    public AsyncRelayCommand DeleteIssueCommand { get; }

    /// <summary>打开「查看现场照片」弹窗（设计稿 dlg-photo）。</summary>
    public RelayCommand ViewPhotosCommand { get; }

    private User CurrentUser => _app.Session.User
        ?? throw new InvalidOperationException("登录已失效，请重新登录。");

    /// <summary>保存后由阶段编辑器回调：重新读库并刷新所有集合。</summary>
    internal void OnSaved(string message)
    {
        ErrorMessage = "";
        NoticeMessage = message;
        Reload();
    }

    internal void OnFailed(string message)
    {
        NoticeMessage = "";
        ErrorMessage = message;
    }

    /// <summary>重新读取整条问题（阶段保存、附件增删、状态变更后统一走这里）。</summary>
    public void Reload()
    {
        var fresh = _app.Issues.Get(_issue.Id);
        if (fresh == null)
        {
            ErrorMessage = "该问题已被删除，请返回台账刷新。";
            return;
        }

        _issue = fresh;
        LoadEditableFields();
        RebuildCollections();
        _nav.NotifyDataChanged();
    }

    private void LoadEditableFields()
    {
        Title = _issue.Title;
        Solution = _issue.Solution;
        CompletionText = _issue.CompletionText;
        Priority = string.IsNullOrWhiteSpace(_issue.Priority) ? "中" : _issue.Priority;
        Category = _issue.Category;
        Status = _issue.Status;
    }

    private void RebuildCollections()
    {
        StageEditors.Clear();
        foreach (var stage in _issue.Stages.OrderBy(s => StageCodes.Index(s.Code)))
            StageEditors.Add(new StageEditor(_app, _issue.Id, stage, this));

        Attachments.Clear();
        foreach (var attachment in _issue.Attachments) Attachments.Add(attachment);

        Comments.Clear();
        foreach (var comment in _issue.Comments.OrderByDescending(c => c.CreatedAt)) Comments.Add(comment);

        Activity.Clear();
        foreach (var entry in _issue.Activity.OrderByDescending(a => a.Id).Take(8)) Activity.Add(entry);

        OnPropertiesChanged(nameof(Issue), nameof(StatusDisplay), nameof(ProgressText),
            nameof(OwnerFlow), nameof(DueFlow), nameof(PhotoCount), nameof(OverdueDays),
            nameof(IsInverted), nameof(HasInverted), nameof(HasOverdue), nameof(IsClosed),
            nameof(SourceTrace),
            nameof(CreatedDisplay), nameof(UpdatedDisplay), nameof(HasAttachments),
            nameof(HasComments), nameof(HasActivity), nameof(HeaderSubtitle),
            nameof(DisplaySeqNo), nameof(SeqBadge), nameof(DesignOwnerDisplay),
            nameof(DesignDueDisplay), nameof(AssemblyOwnerDisplay), nameof(AssemblyDueDisplay),
            nameof(DesignPlanLine), nameof(AssemblyPlanLine), nameof(SourceRowDisplay),
            nameof(CreatedShort), nameof(UpdatedShort), nameof(InvertedDays),
            nameof(InvertedDaysText), nameof(InvertedDescription), nameof(SameMachineIssueText),
            nameof(PrimaryOwnerText), nameof(MeasureDisplay), nameof(SourceRowText),
            nameof(IsNotInverted), nameof(DesignDueShort), nameof(AssemblyDueShort),
            nameof(InvertedRankText), nameof(LastUpdatedLine), nameof(HasStageNote),
            nameof(StageNoteLabel), nameof(StageNoteText), nameof(StageNoteTime),
            nameof(StageNotePhotoText), nameof(CommentCount), nameof(ActivityCount));
    }

    private async Task SaveDetailsAsync()
    {
        ErrorMessage = "";
        NoticeMessage = "";
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "问题描述不能为空。";
            return;
        }

        IsBusy = true;
        try
        {
            var user = CurrentUser;
            var id = _issue.Id;
            var title = Title.Trim();
            var solution = Solution?.Trim() ?? "";
            var completion = CompletionText?.Trim() ?? "";
            var priority = Priority;
            var category = Category?.Trim() ?? "";

            await Task.Run(() => _app.Issues.UpdateDetails(id, title, solution, completion,
                priority, category, user));

            OnSaved("主信息已保存。");
        }
        catch (Exception ex)
        {
            AppLog.Error("保存问题主信息失败", ex);
            OnFailed($"保存失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SetStatusAsync()
    {
        ErrorMessage = "";
        NoticeMessage = "";
        IsBusy = true;
        try
        {
            var user = CurrentUser;
            var id = _issue.Id;
            var status = Status;
            await Task.Run(() => _app.Issues.SetStatus(id, status, user));
            OnSaved($"状态已改为「{status}」。");
        }
        catch (Exception ex)
        {
            AppLog.Error("修改问题状态失败", ex);
            OnFailed($"修改状态失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddCommentAsync()
    {
        if (string.IsNullOrWhiteSpace(CommentDraft)) return;

        ErrorMessage = "";
        NoticeMessage = "";
        IsBusy = true;
        try
        {
            var user = CurrentUser;
            var id = _issue.Id;
            var content = CommentDraft.Trim();
            await Task.Run(() => _app.Issues.AddComment(id, content, user));
            CommentDraft = "";
            OnSaved("评论已添加。");
        }
        catch (Exception ex)
        {
            AppLog.Error("添加评论失败", ex);
            OnFailed($"添加评论失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddAttachmentAsync()
    {
        ErrorMessage = "";
        NoticeMessage = "";

        var path = _app.Dialogs.PickOpenFile(
            "图片文件|*.jpg;*.jpeg;*.png;*.webp;*.bmp|所有文件|*.*", "选择要附加到该问题的图片");
        if (string.IsNullOrWhiteSpace(path)) return;

        IsBusy = true;
        try
        {
            var user = CurrentUser;
            var id = _issue.Id;
            await Task.Run(() => _app.Attachments.Add(id, path, user.Name));
            OnSaved("附件已添加。");
        }
        catch (Exception ex)
        {
            AppLog.Error("添加附件失败", ex);
            OnFailed($"添加附件失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenAttachment(object? parameter)
    {
        if (parameter is not Attachment attachment) return;

        try
        {
            if (!_app.Attachments.FileExists(attachment))
            {
                ErrorMessage = attachment.IsDemo
                    ? "这是演示附件，只有元数据、没有真实文件。"
                    : "附件文件不在本机，可能尚未从手机同步。";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _app.Attachments.AbsolutePath(attachment.StoredPath),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("打开附件失败", ex);
            OnFailed($"打开附件失败：{ex.Message}");
        }
    }

    private async Task DeleteAttachmentAsync(object? parameter)
    {
        if (parameter is not Attachment attachment) return;
        if (!_app.Dialogs.Confirm("删除附件", $"确定删除「{attachment.FileName}」？\n\n删除只移除记录，不删除磁盘原文件。"))
            return;

        ErrorMessage = "";
        NoticeMessage = "";
        IsBusy = true;
        try
        {
            var id = attachment.Id;
            await Task.Run(() => _app.Attachments.Delete(id));
            OnSaved("附件已删除。");
        }
        catch (Exception ex)
        {
            AppLog.Error("删除附件失败", ex);
            OnFailed($"删除附件失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 打开「更新阶段」弹窗：把四个阶段快照交给弹窗，用户保存后按 StageDraft 落库。
    /// 保存走与详情页阶段卡同一个仓储方法，乐观锁 version 一致。
    /// </summary>
    private async Task EditStageAsync()
    {
        ErrorMessage = "";
        NoticeMessage = "";

        var request = new StageDialogRequest
        {
            IssueCode = IssueCodeDisplay,
            IssueTitle = _issue.Title,
            SelectedCode = CurrentStageCode(),
            Stages = _issue.Stages
                .OrderBy(s => StageCodes.Index(s.Code))
                .Select(s => new StageDialogChoice
                {
                    Code = s.Code,
                    Label = s.Label,
                    OwnerText = s.OwnerText,
                    DueRaw = s.DueRaw,
                    ActualDateRaw = s.ActualDate?.ToString("yyyy-MM-dd") ?? "",
                    Remark = s.Remark,
                    Version = s.Version
                })
                .ToList()
        };

        var result = _app.Dialogs.EditStage(request);
        if (result == null) return;

        var draft = new StageDraft
        {
            Code = result.Code,
            OwnerText = result.OwnerText,
            DueRaw = result.DueRaw,
            ActualDateRaw = result.ActualDateRaw,
            Remark = result.Remark,
            ChangeReason = result.ChangeReason,
            Version = result.Version,
            State = StageStates.Parse(result.StateDb)
        };

        IsBusy = true;
        try
        {
            var user = CurrentUser;
            var id = _issue.Id;
            var label = StageCodes.Label(result.Code);
            await Task.Run(() => _app.Issues.SaveStage(id, draft, user));
            OnSaved($"{label}已保存。");
        }
        catch (Exception ex)
        {
            AppLog.Error("弹窗保存阶段失败", ex);
            OnFailed($"保存阶段失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>弹窗默认选中哪一段：第一个"进行中"，否则第一个未开始，否则最后一段。</summary>
    private string CurrentStageCode()
    {
        var ordered = _issue.Stages.OrderBy(s => StageCodes.Index(s.Code)).ToList();
        var inProgress = ordered.FirstOrDefault(s => s.State == StageState.InProgress);
        if (inProgress != null) return inProgress.Code;

        var pending = ordered.FirstOrDefault(s => s.State == StageState.NotStarted);
        if (pending != null) return pending.Code;

        return ordered.LastOrDefault()?.Code ?? StageCodes.Plan;
    }

    /// <summary>
    /// 打开「删除问题」二次确认：必须手抄问题号才允许删。
    /// 删除是软删除（写 deleted_at），删完回台账，不留在已作废的详情页上。
    /// </summary>
    private async Task DeleteIssueAsync()
    {
        ErrorMessage = "";
        NoticeMessage = "";

        var request = new DeleteIssueRequest
        {
            IssueCode = IssueCodeDisplay,
            IssueTitle = _issue.Title,
            ActivityCount = _issue.Activity.Count,
            PhotoCount = _issue.PhotoCount
        };

        if (!_app.Dialogs.ConfirmDeleteIssue(request)) return;

        IsBusy = true;
        try
        {
            var user = CurrentUser;
            var id = _issue.Id;
            await Task.Run(() => _app.Issues.Delete(id, user));

            AppLog.Info($"删除问题：{request.IssueCode}（{_issue.MachineCode}）");
            _nav.NotifyDataChanged();
            // 回台账：详情页对应的记录已经作废，继续停在这里只会显示一条已删数据。
            _nav.Navigate(ShellViewModel.PageIssues);
        }
        catch (Exception ex)
        {
            AppLog.Error("删除问题失败", ex);
            OnFailed($"删除失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 打开「查看现场照片」弹窗：把附件元数据翻译成弹窗项，路径交给附件服务解析。
    /// 没有照片时也打开弹窗——里面会说明"这条问题还没有现场照片"，
    /// 比按钮点了没反应更清楚。
    /// </summary>
    private void ViewPhotos(object? parameter)
    {
        var ordered = _issue.Attachments
            .OrderBy(a => a.CreatedAt, StringComparer.Ordinal)
            .ToList();

        var photos = ordered.Select(a => new PhotoViewerItem
        {
            FileName = a.FileName,
            Caption = a.IsDemo ? "演示附件（只有元数据）" : "",
            TakenAtDisplay = AppTimeDisplay(a.CreatedAt),
            UploadedBy = a.UploadedBy,
            SizeDisplay = a.SizeDisplay,
            AbsolutePath = SafeAbsolutePath(a)
        }).ToList();

        // 点某张缩略图时定位到那一张；从标题按钮进入则从第一张开始。
        int startIndex = 0;
        if (parameter is string attachmentId)
        {
            int found = ordered.FindIndex(a => a.Id == attachmentId);
            if (found >= 0) startIndex = found;
        }

        _app.Dialogs.ViewPhotos(new PhotoViewerRequest
        {
            IssueCode = IssueCodeDisplay,
            IssueTitle = _issue.Title,
            MachineCode = _issue.MachineCode,
            SubprojectCode = _issue.SubprojectCode ?? "",
            Photos = photos,
            StartIndex = startIndex
        });
    }

    /// <summary>附件绝对路径；演示附件或文件缺失时返回空串（弹窗据此显示占位说明）。</summary>
    private string SafeAbsolutePath(Attachment attachment)
    {
        try
        {
            return _app.Attachments.FileExists(attachment)
                ? _app.Attachments.AbsolutePath(attachment.StoredPath)
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ShortBatch(string? batchId) =>
        string.IsNullOrWhiteSpace(batchId) ? "—" :
            batchId.Length <= 10 ? batchId : batchId[..10];

    private static string AppTimeDisplay(string? iso, string format = "yyyy-MM-dd HH:mm")
    {
        if (string.IsNullOrWhiteSpace(iso)) return "—";
        return DateTime.TryParse(iso, out var dt) ? dt.ToLocalTime().ToString(format) : iso;
    }

    /// <summary>阶段计划短日期：优先语义日期（MM-dd），其次原文（"已下图"保持原样）。</summary>
    private static string ShortDue(IssueStage? stage)
    {
        if (stage == null) return "—";
        if (stage.DueDate.HasValue) return stage.DueDate.Value.ToString("MM-dd");
        return string.IsNullOrWhiteSpace(stage.DueRaw) ? "—" : stage.DueRaw;
    }
}

/// <summary>
/// 单个阶段的可编辑行。责任人与计划都保留"原文"口径（due_raw），
/// 日期解析交给仓储；这里绝不把"11.5"格式化成年份，否则导出无法还原。
/// </summary>
public sealed class StageEditor : ObservableObject
{
    private readonly App _app;
    private readonly string _issueId;
    private readonly IssueDetailViewModel _parent;
    private string _ownerText;
    private string _dueRaw;
    private string _remark;
    private StageState _state;
    private string _actualDateRaw;
    private readonly DateOnly? _dueDate;

    public StageEditor(App app, string issueId, IssueStage stage, IssueDetailViewModel parent)
    {
        _app = app;
        _issueId = issueId;
        _parent = parent;
        Code = stage.Code;
        Label = stage.Label;
        Version = stage.Version;
        UpdatedAt = stage.UpdatedAt;
        _dueDate = stage.DueDate;
        StateOptions = new[]
        {
            new StageStateOption(StageState.NotStarted),
            new StageStateOption(StageState.InProgress),
            new StageStateOption(StageState.Done),
            new StageStateOption(StageState.NotApplicable)
        };
        _ownerText = stage.OwnerText;
        _dueRaw = stage.DueRaw;
        _remark = stage.Remark;
        _state = stage.State;
        _actualDateRaw = stage.ActualDate?.ToString("yyyy-MM-dd") ?? "";
        SaveCommand = new AsyncRelayCommand(SaveAsync);
    }

    public string Code { get; }
    public string Label { get; }
    public int Version { get; }
    public string UpdatedAt { get; }
    public IReadOnlyList<StageStateOption> StateOptions { get; }

    /// <summary>时间线左侧标记：完成 ✓ / 进行中 ● / 空白无标记。</summary>
    public string Icon => State switch
    {
        StageState.Done => "✓",
        StageState.InProgress => "●",
        StageState.NotApplicable => "/",
        _ => ""
    };

    public string IconTone => State switch
    {
        StageState.Done => "ok",
        StageState.InProgress => "on",
        StageState.NotApplicable => "plain",
        _ => "plain"
    };

    public bool IsDone => State == StageState.Done;
    public bool IsCurrent => State == StageState.InProgress;

    /// <summary>责任人是否已填：设计稿未填时显示"—（未填责任人）"，括号里是弱化文字。</summary>
    public bool HasOwner => !string.IsNullOrWhiteSpace(OwnerText);
    public string OwnerDisplay => string.IsNullOrWhiteSpace(OwnerText) ? "—" : OwnerText;

    /// <summary>计划原文（"11.5" / "已下图" / "/" 原样保留）。</summary>
    public string DueDisplay => string.IsNullOrWhiteSpace(DueRaw) ? "—" : DueRaw;

    /// <summary>行内计划列：有语义日期用完整日期（设计稿 2026-11-05），否则用原文。</summary>
    public string DueFullDisplay => _dueDate.HasValue
        ? _dueDate.Value.ToString("yyyy-MM-dd")
        : DueDisplay;

    /// <summary>该阶段是否已逾期（未完成/非不适用，且计划日早于今天）。</summary>
    public bool IsOverdue => State is StageState.NotStarted or StageState.InProgress
                             && _dueDate.HasValue && _dueDate.Value < AppTime.Today;

    public string StateWord => State switch
    {
        StageState.Done => "已完成",
        StageState.InProgress => "进行中",
        StageState.NotApplicable => "不适用",
        _ => "未填"
    };

    public string ChipText => State switch
    {
        StageState.Done => "完成",
        StageState.InProgress => "进行中",
        StageState.NotApplicable => "/",
        _ => "空白"
    };

    public string ChipTone => State switch
    {
        StageState.Done => "ok",
        StageState.InProgress => "on",
        _ => "plain"
    };

    /// <summary>行尾标签（设计稿 .tag）：逾期优先于阶段状态，绿=完成、琥珀=空白。</summary>
    public string TagText => IsOverdue
        ? "逾期"
        : State switch
        {
            StageState.Done => "完成",
            StageState.InProgress => "进行中",
            StageState.NotApplicable => "/",
            _ => "空白"
        };

    public string TagTone => IsOverdue
        ? "ng"
        : State switch
        {
            StageState.Done => "ok",
            StageState.InProgress => "on",
            _ => "plain"
        };

    public string OwnerText
    {
        get => _ownerText;
        set => SetProperty(ref _ownerText, value);
    }

    /// <summary>计划原文：允许 11.5、已下图、/ 等原表写法。</summary>
    public string DueRaw
    {
        get => _dueRaw;
        set => SetProperty(ref _dueRaw, value);
    }

    public string Remark
    {
        get => _remark;
        set => SetProperty(ref _remark, value);
    }

    public StageState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value)) OnPropertyChanged(nameof(StateDisplay));
        }
    }

    public string StateDisplay => StageStates.Display(State);

    public string ActualDateRaw
    {
        get => _actualDateRaw;
        set => SetProperty(ref _actualDateRaw, value);
    }

    public AsyncRelayCommand SaveCommand { get; }

    private async Task SaveAsync()
    {
        var draft = new StageDraft
        {
            Code = Code,
            OwnerText = OwnerText?.Trim() ?? "",
            DueRaw = DueRaw?.Trim() ?? "",
            State = State,
            ActualDateRaw = ActualDateRaw?.Trim() ?? "",
            Remark = Remark?.Trim() ?? "",
            Version = Version
        };

        var user = _app.Session.User;
        if (user == null)
        {
            _parent.OnFailed("登录已失效，请重新登录。");
            return;
        }

        try
        {
            await Task.Run(() => _app.Issues.SaveStage(_issueId, draft, user));
            _parent.OnSaved($"{Label}已保存。");
        }
        catch (Exception ex)
        {
            AppLog.Error($"{Label}保存失败", ex);
            _parent.OnFailed($"保存{Label}失败：{ex.Message}");
        }
    }
}

public sealed class StageStateOption
{
    public StageStateOption(StageState state) => State = state;

    public StageState State { get; }
    public string Label => StageStates.Display(State);
}
