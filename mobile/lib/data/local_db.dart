import 'dart:convert';

import 'package:flutter/services.dart' show rootBundle;
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import 'package:sqflite/sqflite.dart';

import '../models/issue.dart';
import '../models/rules.dart';

/// 移动端本地 SQLite（离线副本）。
///
/// 口径与桌面端 `shared/db/schema.sql` 对齐，但只保留现场记录需要的字段；
/// 写操作同时落 `outbox`，联网后按队列补传（MVP 先记账，同步服务随后接）。
class LocalDb {
  LocalDb._();

  static final LocalDb instance = LocalDb._();

  /// 首启演示总项目：与桌面端 SeedBootstrapper 的 id/code/名称保持一致，
  /// 这样手机端先离线产生的问题，同步到桌面后仍落在同一个总项目里。
  static const demoProjectId = 'PRJ-P7391';
  static const demoProjectCode = 'P7391';

  Database? _db;

  Future<Database> get database async {
    final existing = _db;
    if (existing != null) return existing;
    final dir = await getApplicationDocumentsDirectory();
    final path = p.join(dir.path, 'sinan_mobile.db');
    final db = await openDatabase(
      path,
      version: 4,
      onConfigure: (d) => d.execute('PRAGMA foreign_keys = ON'),
      onCreate: _create,
      onUpgrade: _upgrade,
    );
    await _seedIfEmpty(db);
    _db = db;
    return db;
  }

  Future<String> databasePath() async {
    final dir = await getApplicationDocumentsDirectory();
    return p.join(dir.path, 'sinan_mobile.db');
  }

  Future<void> _create(Database db, int version) async {
    await db.execute('''
      CREATE TABLE issues(
        id TEXT PRIMARY KEY,
        seq_no TEXT NOT NULL,
        machine_code TEXT NOT NULL,
        subproject_code TEXT,
        project_id TEXT NOT NULL DEFAULT '',
        title TEXT NOT NULL,
        solution TEXT NOT NULL DEFAULT '',
        status TEXT NOT NULL,
        completion_text TEXT NOT NULL DEFAULT '',
        source_row INTEGER,
        photo_count INTEGER NOT NULL DEFAULT 0,
        version INTEGER NOT NULL DEFAULT 1,
        updated_at TEXT NOT NULL
      )
    ''');
    await db.execute('''
      CREATE TABLE issue_stages(
        issue_id TEXT NOT NULL,
        stage_code TEXT NOT NULL,
        owner_text TEXT NOT NULL DEFAULT '',
        due_raw TEXT NOT NULL DEFAULT '',
        due_date TEXT,
        state TEXT NOT NULL DEFAULT 'not_started',
        actual_date TEXT NOT NULL DEFAULT '',
        remark TEXT NOT NULL DEFAULT '',
        version INTEGER NOT NULL DEFAULT 1,
        PRIMARY KEY(issue_id, stage_code),
        FOREIGN KEY(issue_id) REFERENCES issues(id) ON DELETE CASCADE
      )
    ''');
    await db.execute('''
      CREATE TABLE comments(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        issue_id TEXT NOT NULL,
        author TEXT NOT NULL,
        body TEXT NOT NULL,
        created_at TEXT NOT NULL
      )
    ''');
    await db.execute('''
      CREATE TABLE outbox(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        entity TEXT NOT NULL,
        entity_id TEXT NOT NULL,
        op TEXT NOT NULL,
        payload TEXT NOT NULL,
        base_version INTEGER NOT NULL DEFAULT 0,
        created_at TEXT NOT NULL,
        synced_at TEXT
      )
    ''');
    await db.execute('CREATE INDEX idx_issues_machine ON issues(machine_code)');
    await db.execute('CREATE INDEX idx_outbox_pending ON outbox(synced_at)');
    await _createProjects(db);
    await db.execute(
        'CREATE INDEX IF NOT EXISTS idx_issues_project ON issues(project_id)');
    await _createAttachments(db);
    await _createSyncTables(db);
  }

  /// v4 新增：总项目 / 分项目字典。手机端只读（桌面端主库下发），
  /// 但必须本地落表才能在离线时展示项目列表与切换。
  Future<void> _createProjects(Database db) async {
    await db.execute('''
      CREATE TABLE IF NOT EXISTS projects(
        id TEXT PRIMARY KEY,
        code TEXT NOT NULL UNIQUE,
        name TEXT NOT NULL DEFAULT '',
        customer TEXT NOT NULL DEFAULT '',
        status TEXT NOT NULL DEFAULT 'active',
        updated_at TEXT NOT NULL
      )
    ''');
    await db.execute('''
      CREATE TABLE IF NOT EXISTS subprojects(
        id TEXT PRIMARY KEY,
        project_id TEXT NOT NULL,
        code TEXT NOT NULL,
        name TEXT NOT NULL DEFAULT '',
        machine_range TEXT NOT NULL DEFAULT '',
        sort_no INTEGER NOT NULL DEFAULT 0,
        updated_at TEXT NOT NULL
      )
    ''');
    await db.execute(
        'CREATE INDEX IF NOT EXISTS idx_subprojects_project ON subprojects(project_id)');
  }

  /// v2 新增：现场照片附件表（字段对齐共享 schema，去掉桌面端同步专用列）。
  /// 已有安装升级时不能重建库，否则现场记录会丢，只能补表。
  Future<void> _upgrade(Database db, int oldVersion, int newVersion) async {
    if (oldVersion < 2) {
      await _createAttachments(db);
    }
    if (oldVersion < 3) {
      // v3：上行操作补 base_version（桌面端据此判乐观锁），并新增同步元数据与冲突表。
      await db.execute(
          'ALTER TABLE outbox ADD COLUMN base_version INTEGER NOT NULL DEFAULT 0');
      await _createSyncTables(db);
    }
    if (oldVersion < 4) {
      // v4：多总项目。补项目字典表 + 给问题加 project_id。
      // 升级前的数据只可能属于 P7391（多项目是 v4 才引入的），统一归演示项目。
      await _createProjects(db);
      await db.execute(
          "ALTER TABLE issues ADD COLUMN project_id TEXT NOT NULL DEFAULT ''");
      final now = DateTime.now().toIso8601String();
      await _seedDemoProject(db, now);
      await db.rawUpdate(
          'UPDATE issues SET project_id = ? WHERE project_id = ?',
          [demoProjectId, '']);
      await db.execute(
          'CREATE INDEX IF NOT EXISTS idx_issues_project ON issues(project_id)');
    }
  }

  /// 首启/升级时补演示总项目与三个分项目。
  /// id / code / 名称 / 机台范围全部与桌面端 SeedBootstrapper 对齐，避免双端各一套。
  Future<void> _seedDemoProject(DatabaseExecutor db, String now) async {
    await db.insert(
      'projects',
      {
        'id': demoProjectId,
        'code': demoProjectCode,
        'name': '$demoProjectCode 安装调试问题闭环',
        'customer': '',
        'status': 'active',
        'updated_at': now,
      },
      conflictAlgorithm: ConflictAlgorithm.ignore,
    );

    const subs = [
      (
        id: 'SUB-P7391-01',
        code: 'P7391-01',
        name: '机台 1xx 段',
        machineRange: 'P7391101 – P7391110',
        sort: 1
      ),
      (
        id: 'SUB-P7391-02',
        code: 'P7391-02',
        name: '机台 2xx 段',
        machineRange: 'P7391201 – P7391208',
        sort: 2
      ),
      (
        id: 'SUB-P7391-03',
        code: 'P7391-03',
        name: '机台 3xx 段',
        machineRange: 'P7391301 – P7391312',
        sort: 3
      ),
    ];
    for (final sub in subs) {
      await db.insert(
        'subprojects',
        {
          'id': sub.id,
          'project_id': demoProjectId,
          'code': sub.code,
          'name': sub.name,
          'machine_range': sub.machineRange,
          'sort_no': sub.sort,
          'updated_at': now,
        },
        conflictAlgorithm: ConflictAlgorithm.ignore,
      );
    }
  }

  /// 同步运行态：服务器地址 / token / 游标存 sync_meta（键值对），
  /// 冲突单独成表，保留桌面当前值供用户"保留桌面"时落地。
  Future<void> _createSyncTables(Database db) async {
    await db.execute('''
      CREATE TABLE IF NOT EXISTS sync_meta(
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
      )
    ''');
    await db.execute('''
      CREATE TABLE IF NOT EXISTS conflicts(
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        entity TEXT NOT NULL,
        entity_id TEXT NOT NULL,
        op TEXT NOT NULL,
        payload TEXT NOT NULL,
        server_version INTEGER NOT NULL,
        server_payload TEXT NOT NULL,
        message TEXT NOT NULL DEFAULT '',
        created_at TEXT NOT NULL
      )
    ''');
  }

  Future<void> _createAttachments(Database db) async {
    await db.execute('''
      CREATE TABLE IF NOT EXISTS attachments(
        id TEXT PRIMARY KEY,
        issue_id TEXT NOT NULL,
        file_name TEXT NOT NULL,
        stored_name TEXT NOT NULL,
        stored_path TEXT NOT NULL,
        size INTEGER NOT NULL DEFAULT 0,
        mime TEXT NOT NULL DEFAULT 'image/jpeg',
        created_at TEXT NOT NULL,
        is_demo INTEGER NOT NULL DEFAULT 0,
        sync_state TEXT NOT NULL DEFAULT 'local'
      )
    ''');
    await db.execute(
        'CREATE INDEX IF NOT EXISTS idx_attachments_issue ON attachments(issue_id)');
  }

  /// 首启种入 36 条虚构演示数据（与桌面端同一份 JSON）。
  /// 空库才种；已有数据绝不覆盖，避免现场记录被演示数据冲掉。
  Future<void> _seedIfEmpty(Database db) async {
    final count = Sqflite.firstIntValue(
          await db.rawQuery('SELECT COUNT(*) FROM issues'),
        ) ??
        0;
    if (count > 0) return;

    final raw = await rootBundle.loadString('assets/seed/issues.seed.json');
    final list = (jsonDecode(raw) as List).cast<Map<String, dynamic>>();
    final now = DateTime.now().toIso8601String();
    await _seedDemoProject(db, now);
    final batch = db.batch();

    for (final entry in list.asMap().entries) {
      final item = entry.value;
      final machine = (item['machine'] as String? ?? '').trim();
      final id = item['id'] as String;
      batch.insert('issues', {
        'id': id,
        'seq_no': item['seq']?.toString() ?? '',
        'machine_code': machine,
        'subproject_code': Rules.resolveSubproject(machine,
            projectCode: demoProjectCode),
        'project_id': demoProjectId,
        'title': item['title'] ?? '',
        'solution': item['solution'] ?? '',
        'status': item['status'] ?? IssueStatuses.unassigned,
        'completion_text': item['completion'] ?? '',
        'source_row': item['source_row'],
        // 与桌面端演示口径一致：12 张占位照片 / 6 条问题，界面标注"演示值"。
        'photo_count': entry.key < 6 ? 2 : 0,
        'version': 1,
        'updated_at': now,
      });

      final stages = (item['stages'] as Map).cast<String, dynamic>();
      for (final code in StageCodes.all) {
        final stage = (stages[code] as Map?)?.cast<String, dynamic>() ?? {};
        final dueRaw = stage['raw_due'] as String? ?? '';
        final dueText = stage['due'] as String?;
        batch.insert('issue_stages', {
          'issue_id': id,
          'stage_code': code,
          'owner_text': stage['owner'] ?? '',
          'due_raw': dueRaw,
          'due_date': dueText,
          'state': stage['state'] ?? 'not_started',
          'actual_date': '',
          'remark': stage['note'] ?? '',
          'version': 1,
        });
      }
    }

    await batch.commit(noResult: true);
  }
}
