import 'package:sqflite/sqflite.dart';

import '../models/project.dart';
import 'local_db.dart';

/// 项目仓储：总项目列表、切换用的实时规模、同步下行的项目落库。
///
/// 与 `IssueRepository` 分开是为了不把"问题查询"和"项目字典"混在一个类里；
/// 两者共用同一个 LocalDb，不另开数据库。
class ProjectRepository {
  ProjectRepository(this._db);

  final LocalDb _db;

  /// 全部总项目，按项目号排序（P7391 在 P7392 前）。
  Future<List<Project>> all() async {
    final db = await _db.database;
    final rows = await db.query('projects', orderBy: 'code ASC');
    return rows.map(Project.fromRow).toList();
  }

  Future<Project?> byId(String id) async {
    if (id.trim().isEmpty) return null;
    final db = await _db.database;
    final rows = await db
        .query('projects', where: 'id = ?', whereArgs: [id], limit: 1);
    return rows.isEmpty ? null : Project.fromRow(rows.first);
  }

  Future<List<Subproject>> subprojects(String projectId) async {
    final db = await _db.database;
    final rows = await db.query('subprojects',
        where: 'project_id = ?', whereArgs: [projectId], orderBy: 'sort_no, code');
    return rows.map(Subproject.fromRow).toList();
  }

  /// 切换弹层的数据源：一条查询拿到每个项目的分项目/设备/问题数，
  /// 数字实时算，和总览页口径一致（设备按项目内去重机台号计）。
  Future<List<ProjectSummary>> summaries({String? currentId}) async {
    final db = await _db.database;
    final rows = await db.rawQuery('''
      SELECT p.id, p.code, p.name, p.customer, p.status,
        (SELECT COUNT(*) FROM subprojects s WHERE s.project_id = p.id) AS sub_count,
        (SELECT COUNT(DISTINCT i.machine_code) FROM issues i
           WHERE i.project_id = p.id) AS machine_count,
        (SELECT COUNT(*) FROM issues i WHERE i.project_id = p.id) AS issue_count
      FROM projects p
      ORDER BY p.code
    ''');
    return rows
        .map((row) => ProjectSummary(
              project: Project.fromRow(row),
              subprojectCount: (row['sub_count'] as int?) ?? 0,
              machineCount: (row['machine_count'] as int?) ?? 0,
              issueCount: (row['issue_count'] as int?) ?? 0,
              isCurrent: row['id'] == currentId,
            ))
        .toList();
  }

  /// 下发项目：桌面端新建后写 change_log，手机 pull 到这里落库。
  /// 用 replace 保证重复同步幂等；项目表没有外键，替换不会连带删除问题。
  Future<void> applyServerProject(Map<String, dynamic> json) async {
    final id = (json['id'] as String? ?? '').trim();
    final code = (json['code'] as String? ?? '').trim().toUpperCase();
    if (id.isEmpty || code.isEmpty) return;
    final db = await _db.database;
    await db.insert(
      'projects',
      {
        'id': id,
        'code': code,
        'name': json['name'] as String? ?? code,
        'customer': json['customer'] as String? ?? '',
        'status': json['status'] as String? ?? 'active',
        'updated_at':
            json['updatedAt'] as String? ?? DateTime.now().toIso8601String(),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  /// 下发分项目：与项目同批到达，手机项目页暂不直接展示，
  /// 但保留下来供后续"分项目名称/机台段"展示，避免再次改表。
  Future<void> applyServerSubproject(Map<String, dynamic> json) async {
    final id = (json['id'] as String? ?? '').trim();
    final projectId = (json['projectId'] as String? ?? '').trim();
    final code = (json['code'] as String? ?? '').trim().toUpperCase();
    if (id.isEmpty || projectId.isEmpty || code.isEmpty) return;
    final db = await _db.database;
    await db.insert(
      'subprojects',
      {
        'id': id,
        'project_id': projectId,
        'code': code,
        'name': json['name'] as String? ?? '',
        'machine_range': json['machineRange'] as String? ?? '',
        'sort_no': (json['sortNo'] as num?)?.toInt() ?? 0,
        'updated_at':
            json['updatedAt'] as String? ?? DateTime.now().toIso8601String(),
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }
}
