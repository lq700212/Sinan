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

  Database? _db;

  Future<Database> get database async {
    final existing = _db;
    if (existing != null) return existing;
    final dir = await getApplicationDocumentsDirectory();
    final path = p.join(dir.path, 'sinan_mobile.db');
    final db = await openDatabase(
      path,
      version: 3,
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
    await _createAttachments(db);
    await _createSyncTables(db);
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
    final batch = db.batch();

    for (final entry in list.asMap().entries) {
      final item = entry.value;
      final machine = (item['machine'] as String? ?? '').trim();
      final id = item['id'] as String;
      batch.insert('issues', {
        'id': id,
        'seq_no': item['seq']?.toString() ?? '',
        'machine_code': machine,
        'subproject_code': Rules.resolveSubproject(machine),
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
