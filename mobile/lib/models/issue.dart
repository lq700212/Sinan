/// 问题、阶段与状态模型。字段口径与桌面端 `Sinan.Core/Models` 对齐，
/// 但移动端只保留离线记录必需的子集（同步字段在 outbox 里另存）。
library;

/// 阶段状态：与 schema 的 `not_started|in_progress|done|na` 一一对应。
enum StageState {
  notStarted('not_started', '未开始'),
  inProgress('in_progress', '进行中'),
  done('done', '已完成'),
  na('na', '不适用');

  const StageState(this.code, this.label);

  final String code;
  final String label;

  static StageState parse(String? code) => switch (code) {
        'in_progress' => StageState.inProgress,
        'done' => StageState.done,
        'na' => StageState.na,
        _ => StageState.notStarted,
      };
}

/// 四个阶段的编码顺序：方案确定 → 设计下图 → 物料准备 → 装配调试。
class StageCodes {
  static const plan = 'plan';
  static const design = 'design';
  static const material = 'material';
  static const assembly = 'assembly';

  static const all = [plan, design, material, assembly];

  static int index(String code) {
    final i = all.indexOf(code);
    return i < 0 ? 99 : i;
  }

  static String label(String code) => switch (code) {
        plan => '方案确定',
        design => '设计下图',
        material => '物料准备',
        assembly => '装配调试',
        _ => code,
      };
}

class IssueStage {
  const IssueStage({
    required this.code,
    required this.ownerText,
    required this.dueRaw,
    required this.state,
    this.dueDate,
    this.actualDate = '',
    this.remark = '',
    this.version = 1,
  });

  final String code;
  final String ownerText;
  final String dueRaw;
  final DateTime? dueDate;
  final StageState state;
  final String actualDate;
  final String remark;
  final int version;

  String get label => StageCodes.label(code);
  bool get hasOwner => ownerText.trim().isNotEmpty;
  bool get hasPlan => dueRaw.trim().isNotEmpty || dueDate != null;
  bool get isDone => state == StageState.done;
  bool get isNa => state == StageState.na;

  IssueStage copyWith({
    String? ownerText,
    String? dueRaw,
    DateTime? dueDate,
    bool clearDueDate = false,
    StageState? state,
    String? actualDate,
    String? remark,
    int? version,
  }) {
    return IssueStage(
      code: code,
      ownerText: ownerText ?? this.ownerText,
      dueRaw: dueRaw ?? this.dueRaw,
      dueDate: clearDueDate ? null : (dueDate ?? this.dueDate),
      state: state ?? this.state,
      actualDate: actualDate ?? this.actualDate,
      remark: remark ?? this.remark,
      version: version ?? this.version,
    );
  }
}

class Issue {
  const Issue({
    required this.id,
    required this.seqNo,
    required this.machineCode,
    required this.subprojectCode,
    required this.title,
    required this.solution,
    required this.status,
    required this.stages,
    this.completionText = '',
    this.sourceRow,
    this.photoCount = 0,
    this.version = 1,
  });

  final String id;
  final String seqNo;
  final String machineCode;
  final String? subprojectCode;
  final String title;
  final String solution;
  final String status;
  final String completionText;
  final List<IssueStage> stages;
  final int? sourceRow;
  final int photoCount;
  final int version;

  IssueStage? stage(String code) {
    for (final s in stages) {
      if (s.code == code) return s;
    }
    return null;
  }

  IssueStage? get planStage => stage(StageCodes.plan);
  IssueStage? get designStage => stage(StageCodes.design);
  IssueStage? get materialStage => stage(StageCodes.material);
  IssueStage? get assemblyStage => stage(StageCodes.assembly);

  bool get isClosed => IssueStatuses.isTerminal(status);

  Issue copyWith({
    String? status,
    String? solution,
    String? completionText,
    List<IssueStage>? stages,
    int? version,
    int? photoCount,
  }) {
    return Issue(
      id: id,
      seqNo: seqNo,
      machineCode: machineCode,
      subprojectCode: subprojectCode,
      title: title,
      solution: solution ?? this.solution,
      status: status ?? this.status,
      completionText: completionText ?? this.completionText,
      stages: stages ?? this.stages,
      sourceRow: sourceRow,
      photoCount: photoCount ?? this.photoCount,
      version: version ?? this.version,
    );
  }
}

/// 问题状态（中文入库，与桌面端 `IssueStatuses` 完全一致）。
class IssueStatuses {
  static const unassigned = '待分派';
  static const planning = '方案中';
  static const designing = '设计中';
  static const material = '备料中';
  static const assembling = '装配中';
  static const verifying = '待验证';
  static const closed = '已关闭';
  static const cancelled = '已取消';

  static const all = [
    unassigned,
    planning,
    designing,
    material,
    assembling,
    verifying,
  ];

  static bool isTerminal(String status) =>
      status == closed || status == cancelled;

  static String forStage(String code) => switch (code) {
        StageCodes.plan => planning,
        StageCodes.design => designing,
        StageCodes.material => material,
        StageCodes.assembly => assembling,
        _ => unassigned,
      };
}
