import 'issue.dart';

/// 纯规则：日期原文解析、阶段状态推导、逾期/倒挂。
/// 与桌面端 `Sinan.Core/Utils/StageRules.cs`、`RawDateParser.cs` 同口径，
/// 双端统计数字必须能对得上，禁止在页面里另写一套判断。
class Rules {
  static final _datePattern =
      RegExp(r'^(\d{1,4})[.\-/年](\d{1,2})(?:[.\-/月](\d{1,2}))?\s*日?$');

  static String normalize(String? text) => (text ?? '').trim();

  static bool isNotApplicable(String? raw) {
    final t = normalize(raw);
    return t == '/' || t == '／' || t == '-' || t == '不适用' || t.toUpperCase() == 'N/A' || t.toUpperCase() == 'NA';
  }

  static bool meansDrawingIssued(String? raw) {
    final t = normalize(raw);
    return t.contains('已下图') || t.contains('下发了');
  }

  /// 解析原表日期文本；`11.5` / `11-5` / `11/5` / `2026.11.5` 都支持。
  /// `/`、`已下图`、空白返回 null（语义上"无计划日期"，不是错误）。
  static DateTime? parseRawDate(String? raw, {int? baseYear}) {
    final text = normalize(raw);
    if (text.isEmpty || isNotApplicable(text) || meansDrawingIssued(text)) {
      return null;
    }
    final m = _datePattern.firstMatch(text);
    if (m == null) return null;

    int year;
    int month;
    int day;
    if (m.group(3) != null) {
      year = int.parse(m.group(1)!);
      month = int.parse(m.group(2)!);
      day = int.parse(m.group(3)!);
      if (year < 100) year += 2000;
    } else {
      year = baseYear ?? DateTime.now().year;
      month = int.parse(m.group(1)!);
      day = int.parse(m.group(2)!);
    }
    if (year < 2000 || year > 2100) return null;
    if (month < 1 || month > 12) return null;
    if (day < 1 || day > _daysInMonth(year, month)) return null;
    return DateTime(year, month, day);
  }

  static int _daysInMonth(int year, int month) =>
      DateTime(year, month + 1, 0).day;

  static String formatRawDate(DateTime? date) =>
      date == null ? '' : '${date.month}.${date.day}';

  /// `/` → 不适用；`已下图`/实际完成 → 已完成；责任人与计划都空 → 未开始；否则进行中。
  static StageState inferState(String ownerText, String dueRaw,
      {bool actualDone = false}) {
    if (isNotApplicable(dueRaw)) return StageState.na;
    if (actualDone || meansDrawingIssued(dueRaw)) return StageState.done;
    if (ownerText.trim().isEmpty && dueRaw.trim().isEmpty) {
      return StageState.notStarted;
    }
    return StageState.inProgress;
  }

  /// 新建问题时所选"责任阶段"的初始状态。
  ///
  /// 与 [inferState] 的区别：这里用户已经明确"这条问题现在归这个阶段"，
  /// 即使责任人/日期没填，也必须是"进行中"——否则会出现
  /// "状态=装配中、四个阶段却全是未开始"的自相矛盾。
  /// 只有 `/`（不适用）与 `已下图`（已完成）保留原语义。
  static StageState initialStageState(String ownerText, String dueRaw) {
    final due = normalize(dueRaw);
    if (isNotApplicable(due)) return StageState.na;
    if (meansDrawingIssued(due)) return StageState.done;
    return StageState.inProgress;
  }

  /// 由四阶段推导状态：完成情况有文本或全部适用阶段完成 → 待验证；
  /// 否则取第一个未完成阶段映射状态；第一道工序完全没安排才算待分派。
  static String deriveStatus(List<IssueStage> stages,
      {String? completionText}) {
    if (stages.isEmpty) return IssueStatuses.unassigned;
    if ((completionText ?? '').trim().isNotEmpty) {
      return IssueStatuses.verifying;
    }

    final applicable =
        stages.where((s) => s.state != StageState.na).toList();
    if (applicable.isNotEmpty &&
        applicable.every((s) => s.state == StageState.done)) {
      return IssueStatuses.verifying;
    }

    final ordered = [...stages]..sort(
        (a, b) => StageCodes.index(a.code).compareTo(StageCodes.index(b.code)));
    IssueStage? current;
    for (final s in ordered) {
      if (s.state == StageState.notStarted ||
          s.state == StageState.inProgress) {
        current = s;
        break;
      }
    }
    if (current == null) return IssueStatuses.verifying;

    final earlierHandled = stages.any((s) =>
        StageCodes.index(s.code) < StageCodes.index(current!.code) &&
        (s.state == StageState.done || s.state == StageState.na));
    if (!earlierHandled &&
        !current.hasOwner &&
        !current.hasPlan &&
        current.state == StageState.notStarted) {
      return IssueStatuses.unassigned;
    }
    return IssueStatuses.forStage(current.code);
  }

  /// 计划倒挂：设计下图计划日晚于装配调试计划日。
  static bool isPlanInverted(Issue issue) {
    final design = issue.designStage?.dueDate;
    final assembly = issue.assemblyStage?.dueDate;
    if (design == null || assembly == null) return false;
    return design.isAfter(assembly);
  }

  /// 逾期天数：未完成/非不适用阶段的计划日早于今天的最长天数。
  /// 已关闭/已取消不算逾期。
  static int overdueDays(Issue issue, {DateTime? today}) {
    if (issue.isClosed) return 0;
    final now = today ?? DateTime.now();
    final base = DateTime(now.year, now.month, now.day);
    var worst = 0;
    for (final s in issue.stages) {
      if (s.state == StageState.done || s.state == StageState.na) continue;
      final due = s.dueDate;
      if (due == null) continue;
      final diff = base.difference(due).inDays;
      if (diff > worst) worst = diff;
    }
    return worst;
  }

  /// 机台号 → 分项目：1xx→01、2xx→02、3xx→03；位数异常返回 null（待确认）。
  static String? resolveSubproject(String machineCode) {
    final m = RegExp(r'^P7391(\d{3})(-\d)?$').firstMatch(machineCode.trim());
    if (m == null) return null;
    final seq = int.parse(m.group(1)!);
    if (seq >= 100 && seq <= 199) return 'P7391-01';
    if (seq >= 200 && seq <= 299) return 'P7391-02';
    if (seq >= 300 && seq <= 399) return 'P7391-03';
    return null;
  }

  /// 机台号格式：P7391 + 3 位序号，允许 `-1` 后缀。
  /// `P73913118` 这类笔误不匹配格式，但历史数据仍按原文保留（不静默改正）。
  static bool isMachineCode(String machineCode) =>
      RegExp(r'^P7391\d{3}(-\d)?$').hasMatch(machineCode.trim().toUpperCase());

  /// 附件相对路径：`attachments/{issueId}/{storedName}`。
  /// 数据库只存这个相对路径，换机/迁移目录时不失效；安卓与桌面端统一用 `/`。
  static String attachmentRelativePath(String issueId, String storedName) =>
      'attachments/$issueId/$storedName';

  /// 我的阶段任务：当前账号是责任人、阶段未完成/非不适用、问题未闭环。
  /// 现场首屏只显示这些，避免把 36 条全量台账一股脑推给用户。
  static List<({Issue issue, IssueStage stage})> myStageTasks(
      Iterable<Issue> issues, String userName) {
    final name = userName.trim();
    if (name.isEmpty) return const [];
    return [
      for (final issue in issues)
        if (!issue.isClosed)
          for (final stage in issue.stages)
            if (stage.ownerText.contains(name) &&
                stage.state != StageState.done &&
                stage.state != StageState.na)
              (issue: issue, stage: stage),
    ];
  }

  /// 风险排序：倒挂优先 → 逾期最久 → 原表行序。
  /// 台账、待办、总览共用同一套顺序，禁止各页面自己写 comparator。
  static List<Issue> attentionOrder(Iterable<Issue> issues) {
    final list = issues.where((i) => !i.isClosed).toList();
    list.sort((a, b) {
      final ai = isPlanInverted(a) ? 1 : 0;
      final bi = isPlanInverted(b) ? 1 : 0;
      if (ai != bi) return bi.compareTo(ai);
      final ao = overdueDays(a);
      final bo = overdueDays(b);
      if (ao != bo) return bo.compareTo(ao);
      return (a.sourceRow ?? 9999).compareTo(b.sourceRow ?? 9999);
    });
    return list;
  }
}
