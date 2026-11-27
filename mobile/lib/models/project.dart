/// 总项目 / 分项目（手机端离线副本）。
///
/// 桌面端是主库：手机首启只有 P7391 演示项目，其余项目由同步下行
/// `project` / `subproject` 变更补全；手机端不负责新建总项目。
library;

/// 总项目。字段口径与桌面端 `Sinan.Core/Models/ProjectModels.cs` 对齐，
/// 手机端只保留现场展示与切换需要的列。
class Project {
  const Project({
    required this.id,
    required this.code,
    required this.name,
    this.customer = '',
    this.status = 'active',
  });

  final String id;
  final String code;
  final String name;
  final String customer;
  final String status;

  /// 项目名里若带项目号前缀（桌面种子是 "P7391 安装调试问题闭环"），
  /// 展示时去掉前缀，避免项目号 Chip 与标题重复一遍。
  String get shortName {
    final prefix = '$code ';
    final text = name.trim();
    if (text.isEmpty) return code;
    return text.startsWith(prefix) ? text.substring(prefix.length) : text;
  }

  String get statusLabel => switch (status) {
        'done' => '已完成',
        'paused' => '已暂停',
        _ => '进行中',
      };

  factory Project.fromRow(Map<String, Object?> row) => Project(
        id: row['id'] as String? ?? '',
        code: row['code'] as String? ?? '',
        name: row['name'] as String? ?? '',
        customer: row['customer'] as String? ?? '',
        status: row['status'] as String? ?? 'active',
      );
}

/// 分项目（P7391-01/02/03）。手机端只用于展示名称与机台段文案；
/// 问题归组仍以 `issues.subproject_code` 为准，避免两套归属。
class Subproject {
  const Subproject({
    required this.id,
    required this.projectId,
    required this.code,
    this.name = '',
    this.machineRange = '',
    this.sortNo = 0,
  });

  final String id;
  final String projectId;
  final String code;
  final String name;
  final String machineRange;
  final int sortNo;

  factory Subproject.fromRow(Map<String, Object?> row) => Subproject(
        id: row['id'] as String? ?? '',
        projectId: row['project_id'] as String? ?? '',
        code: row['code'] as String? ?? '',
        name: row['name'] as String? ?? '',
        machineRange: row['machine_range'] as String? ?? '',
        sortNo: (row['sort_no'] as int?) ?? 0,
      );
}

/// 切换总项目弹层的一行：项目本体 + 实时规模。
/// 数字全部由 ProjectRepository 查库填入，界面不写死演示值。
class ProjectSummary {
  const ProjectSummary({
    required this.project,
    required this.subprojectCount,
    required this.machineCount,
    required this.issueCount,
    this.isCurrent = false,
  });

  final Project project;
  final int subprojectCount;
  final int machineCount;
  final int issueCount;
  final bool isCurrent;

  String get metricText =>
      '$subprojectCount 个分项目 · $machineCount 台设备 · $issueCount 条问题';
}
