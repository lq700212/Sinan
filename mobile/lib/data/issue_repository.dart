import 'dart:convert';

import 'package:sqflite/sqflite.dart';

import '../models/attachment.dart';
import '../models/issue.dart';
import '../models/rules.dart';
import 'attachment_store.dart';
import 'local_db.dart';

class IssueRepository {
  IssueRepository(this._db);

  final LocalDb _db;

  Future<List<Issue>> all() async {
    final db = await _db.database;
    final issueRows = await db.query('issues', orderBy: 'source_row ASC, seq_no ASC');
    if (issueRows.isEmpty) return const [];

    final stageRows = await db.query('issue_stages');
    final byIssue = <String, List<IssueStage>>{};
    for (final row in stageRows) {
      final stage = _toStage(row);
      byIssue.putIfAbsent(row['issue_id'] as String, () => []).add(stage);
    }

    final result = <Issue>[];
    for (final row in issueRows) {
      final id = row['id'] as String;
      final stages = byIssue[id] ?? const <IssueStage>[];
      stages.sort(
          (a, b) => StageCodes.index(a.code).compareTo(StageCodes.index(b.code)));
      result.add(_toIssue(row, stages));
    }
    return result;
  }

  Future<Issue?> byId(String id) async {
    final db = await _db.database;
    final rows = await db.query('issues', where: 'id = ?', whereArgs: [id], limit: 1);
    if (rows.isEmpty) return null;
    final stageRows = await db.query('issue_stages',
        where: 'issue_id = ?', whereArgs: [id]);
    final stages = stageRows.map(_toStage).toList()
      ..sort((a, b) =>
          StageCodes.index(a.code).compareTo(StageCodes.index(b.code)));
    return _toIssue(rows.first, stages);
  }

  /// 阶段保存：只改本阶段 + 按规则重推问题状态，并写入 outbox 待同步。
  Future<Issue> saveStage({
    required Issue issue,
    required String code,
    required String ownerText,
    required String dueRaw,
    required StageState state,
    required String actualDate,
    required String remark,
  }) async {
    final db = await _db.database;
    final now = DateTime.now().toIso8601String();
    final dueDate = Rules.parseRawDate(dueRaw);

    final stages = issue.stages
        .map((s) => s.code == code
            ? s.copyWith(
                ownerText: ownerText.trim(),
                dueRaw: dueRaw.trim(),
                dueDate: dueDate,
                clearDueDate: dueDate == null,
                state: state,
                actualDate: actualDate.trim(),
                remark: remark.trim(),
              )
            : s)
        .toList();

    final derived = Rules.deriveStatus(stages,
        completionText: issue.completionText);

    await db.transaction((txn) async {
      await txn.update(
        'issue_stages',
        {
          'owner_text': ownerText.trim(),
          'due_raw': dueRaw.trim(),
          'due_date': dueDate?.toIso8601String().substring(0, 10),
          'state': state.code,
          'actual_date': actualDate.trim(),
          'remark': remark.trim(),
        },
        where: 'issue_id = ? AND stage_code = ?',
        whereArgs: [issue.id, code],
      );
      // version 不在这里递增：它是"桌面主库的版本号"，本地写只改内容，
      // 递增留给主库；否则下次上行 baseVersion 会比桌面实际版本大，误报冲突。
      await txn.update(
        'issues',
        {
          'status': derived,
          'updated_at': now,
        },
        where: 'id = ?',
        whereArgs: [issue.id],
      );
      await _enqueue(txn, 'issue_stage', '${issue.id}:$code', 'upsert', {
        'owner_text': ownerText.trim(),
        'due_raw': dueRaw.trim(),
        'due_date': dueDate?.toIso8601String().substring(0, 10),
        'state': state.code,
        'actual_date': actualDate.trim(),
        'remark': remark.trim(),
      }, now, baseVersion: issue.stage(code)?.version ?? 1);
      // 不再顺带入队 issue/status：问题状态由主库按同一套 StageRules 从四个阶段推导，
      // 手机只上行阶段本身。否则两端推导时序不同会把状态来回覆盖，还会误报冲突。
    });

    return (await byId(issue.id))!;
  }

  Future<Issue> updateStatus(Issue issue, String status) async {
    final db = await _db.database;
    final now = DateTime.now().toIso8601String();
    await db.transaction((txn) async {
      await txn.update(
        'issues',
        {
          'status': status,
          'updated_at': now,
        },
        where: 'id = ?',
        whereArgs: [issue.id],
      );
      await _enqueue(txn, 'issue', issue.id, 'status', {'status': status}, now,
          baseVersion: issue.version);
    });
    return (await byId(issue.id))!;
  }

  Future<void> addComment(String issueId, String author, String body) async {
    final db = await _db.database;
    final now = DateTime.now().toIso8601String();
    await db.transaction((txn) async {
      await txn.insert('comments', {
        'issue_id': issueId,
        'author': author,
        'body': body,
        'created_at': now,
      });
      await _enqueue(txn, 'issue', issueId, 'comment', {
        'author': author,
        'body': body,
      }, now);
    });
  }

  Future<List<IssueComment>> comments(String issueId) async {
    final db = await _db.database;
    final rows = await db.query('comments',
        where: 'issue_id = ?', whereArgs: [issueId], orderBy: 'id DESC');
    return rows
        .map((r) => IssueComment(
              author: r['author'] as String? ?? '',
              body: r['body'] as String? ?? '',
              createdAt: DateTime.tryParse(r['created_at'] as String? ?? ''),
            ))
        .toList();
  }

  /// 现场新建问题：本机立即落库并记 outbox，不等网络。
  ///
  /// 序号用 `L001` 占位（local），同步到桌面端后由主库重新编号；
  /// 这样现场一眼能看出"这条是新记的"，也不会和原表 1~36 的序号抢号。
  Future<Issue> createIssue({
    required String machineCode,
    required String title,
    String solution = '',
    String stageCode = StageCodes.assembly,
    String ownerText = '',
    String dueRaw = '',
    String? status,
  }) async {
    final machine = machineCode.trim().toUpperCase();
    final headline = title.trim();
    if (machine.isEmpty) throw ArgumentError('机台号不能为空');
    if (headline.isEmpty) throw ArgumentError('问题描述不能为空');

    final db = await _db.database;
    final now = DateTime.now().toIso8601String();
    final id = 'local-${DateTime.now().microsecondsSinceEpoch}';
    final localCount = Sqflite.firstIntValue(await db.rawQuery(
          "SELECT COUNT(*) FROM issues WHERE id LIKE 'local-%'",
        )) ??
        0;
    final seqNo = 'L${(localCount + 1).toString().padLeft(3, '0')}';

    final stages = [
      for (final code in StageCodes.all)
        IssueStage(
          code: code,
          ownerText: code == stageCode ? ownerText.trim() : '',
          dueRaw: code == stageCode ? dueRaw.trim() : '',
          dueDate: code == stageCode ? Rules.parseRawDate(dueRaw) : null,
          state: code == stageCode
              ? Rules.initialStageState(ownerText, dueRaw)
              : StageState.notStarted,
        ),
    ];
    final finalStatus = status ?? IssueStatuses.forStage(stageCode);

    await db.transaction((txn) async {
      await txn.insert('issues', {
        'id': id,
        'seq_no': seqNo,
        'machine_code': machine,
        'subproject_code': Rules.resolveSubproject(machine),
        'title': headline,
        'solution': solution.trim(),
        'status': finalStatus,
        'completion_text': '',
        'source_row': null,
        'photo_count': 0,
        'version': 1,
        'updated_at': now,
      });
      for (final stage in stages) {
        await txn.insert('issue_stages', {
          'issue_id': id,
          'stage_code': stage.code,
          'owner_text': stage.ownerText,
          'due_raw': stage.dueRaw,
          'due_date': stage.dueDate?.toIso8601String().substring(0, 10),
          'state': stage.state.code,
          'actual_date': '',
          'remark': '',
          'version': 1,
        });
      }
      await _enqueue(txn, 'issue', id, 'create', {
        'seq_no': seqNo,
        'machine_code': machine,
        'title': headline,
        'solution': solution.trim(),
        'status': finalStatus,
        'stage_code': stageCode,
        'owner_text': ownerText.trim(),
        'due_raw': dueRaw.trim(),
        'state': stages
            .firstWhere((s) => s.code == stageCode,
                orElse: () => stages.last)
            .state
            .code,
      }, now);
    });

    return (await byId(id))!;
  }

  /// 附件落库：`photo_count` 用 SQL 自增而不是先读后写，避免连拍时互相覆盖。
  Future<IssueAttachment> addAttachment({
    required String issueId,
    required PickedAttachment picked,
  }) async {
    final db = await _db.database;
    final now = DateTime.now().toIso8601String();
    final id = 'att-${DateTime.now().microsecondsSinceEpoch}';
    final attachment = IssueAttachment(
      id: id,
      issueId: issueId,
      fileName: picked.fileName,
      storedName: picked.storedName,
      storedPath: picked.storedPath,
      size: picked.size,
      createdAt: now,
      syncState: 'local',
    );

    await db.transaction((txn) async {
      await txn.insert('attachments', {
        'id': attachment.id,
        'issue_id': issueId,
        'file_name': attachment.fileName,
        'stored_name': attachment.storedName,
        'stored_path': attachment.storedPath,
        'size': attachment.size,
        'mime': attachment.mime,
        'created_at': attachment.createdAt,
        'is_demo': 0,
        'sync_state': attachment.syncState,
      });
      await txn.rawUpdate(
        'UPDATE issues SET photo_count = photo_count + 1, updated_at = ? '
        'WHERE id = ?',
        [now, issueId],
      );
      await _enqueue(txn, 'attachment', attachment.id, 'upload', {
        'issue_id': issueId,
        'stored_path': attachment.storedPath,
        'file_name': attachment.fileName,
        'size': attachment.size,
      }, now);
    });
    return attachment;
  }

  Future<List<IssueAttachment>> attachments(String issueId) async {
    final db = await _db.database;
    final rows = await db.query('attachments',
        where: 'issue_id = ?', whereArgs: [issueId], orderBy: 'created_at ASC');
    return rows.map(_toAttachment).toList();
  }

  /// 项目总览的"最近现场照片"横条：跨问题取最新几张。
  Future<List<IssueAttachment>> recentAttachments({int limit = 6}) async {
    final db = await _db.database;
    final rows = await db.query('attachments',
        orderBy: 'created_at DESC', limit: limit);
    return rows.map(_toAttachment).toList();
  }

  /// 机台聚合：扫码页"最近机台"与总览共用，避免各页面各查一遍。
  Future<List<MachineSummary>> machines() async {
    final issues = await all();
    final db = await _db.database;
    // 最近更新日取 issues.updated_at 的最大值；Issue 模型不带该字段，
    // 单独查一次比给整条记录扩列更省，机台页只需要一个「最近 MM-DD」。
    final lastRows = await db.rawQuery(
      'SELECT machine_code, MAX(updated_at) AS last FROM issues '
      'GROUP BY machine_code',
    );
    final lastMap = <String, String>{
      for (final r in lastRows)
        (r['machine_code'] as String? ?? ''): (r['last'] as String? ?? ''),
    };
    final map = <String, MachineSummary>{};
    for (final issue in issues) {
      final item = map.putIfAbsent(
          issue.machineCode, () => MachineSummary(machineCode: issue.machineCode));
      item.issueCount++;
      if (Rules.isPlanInverted(issue)) item.inverted++;
      if (Rules.overdueDays(issue) > 0) item.overdue++;
      item.photoCount += issue.photoCount;
      final last = lastMap[issue.machineCode] ?? '';
      if (last.compareTo(item.lastUpdated) > 0) item.lastUpdated = last;
    }
    final list = map.values.toList()
      ..sort((a, b) => a.machineCode.compareTo(b.machineCode));
    return list;
  }

  Future<int> pendingOutbox() async {
    final db = await _db.database;
    return Sqflite.firstIntValue(await db.rawQuery(
          'SELECT COUNT(*) FROM outbox WHERE synced_at IS NULL',
        )) ??
        0;
  }

  /// 今日记录数：今天写入 outbox 的操作条数（新建/阶段/状态/评论/照片）。
  /// 种子导入不写 outbox，所以首启为 0，不会把 36 条演示数据算成"今天记的"。
  Future<int> todayOutboxCount() async {
    final db = await _db.database;
    final now = DateTime.now();
    final start = DateTime(now.year, now.month, now.day).toIso8601String();
    return Sqflite.firstIntValue(await db.rawQuery(
          'SELECT COUNT(*) FROM outbox WHERE created_at >= ?',
          [start],
        )) ??
        0;
  }

  /// 同步中心要"逐条讲清楚哪条没传"，不能只给一个总数。
  /// 队列里显示机台号与问题标题，现场才知道"这条到底是哪台设备的哪件事"。
  /// 阶段操作的 entity_id 是 `issueId:stageCode`，照片操作的要读 payload 里的 issue_id。
  Future<List<PendingOperation>> pendingOperations({int limit = 20}) async {
    final db = await _db.database;
    final rows = await db.query('outbox',
        where: 'synced_at IS NULL',
        orderBy: 'id DESC',
        limit: limit);
    String issueIdOf(Map<String, Object?> row) {
      final entity = row['entity'] as String? ?? '';
      final entityId = row['entity_id'] as String? ?? '';
      if (entity == 'issue_stage') return entityId.split(':').first;
      if (entity == 'attachment') {
        try {
          final payload = jsonDecode(row['payload'] as String? ?? '{}');
          if (payload is Map && payload['issue_id'] is String) {
            return payload['issue_id'] as String;
          }
        } catch (_) {
          // payload 损坏时退回实体 id，至少不丢这一行。
        }
      }
      return entityId;
    }

    final issueIds = rows.map(issueIdOf).toSet().toList();
    final issues = <String, Map<String, Object?>>{};
    if (issueIds.isNotEmpty) {
      final marks = List.filled(issueIds.length, '?').join(',');
      final issueRows = await db.query('issues',
          columns: ['id', 'machine_code', 'title'],
          where: 'id IN ($marks)',
          whereArgs: issueIds);
      for (final r in issueRows) {
        issues[r['id'] as String? ?? ''] = r;
      }
    }

    return rows.map((r) {
      final issue = issues[issueIdOf(r)];
      return PendingOperation(
        id: (r['id'] as int?) ?? 0,
        entity: r['entity'] as String? ?? '',
        entityId: r['entity_id'] as String? ?? '',
        op: r['op'] as String? ?? '',
        payload: r['payload'] as String? ?? '{}',
        baseVersion: (r['base_version'] as int?) ?? 0,
        createdAt: r['created_at'] as String? ?? '',
        machineCode: issue?['machine_code'] as String? ?? '',
        issueTitle: issue?['title'] as String? ?? '',
      );
    }).toList();
  }

  // ---------------------------------------------------------------------
  // 局域网同步：队列结算 / 元数据 / 下行应用 / 冲突
  // ---------------------------------------------------------------------

  /// 同步客户端一次取一批待传操作（含 baseVersion，桌面端据此判乐观锁）。
  Future<List<OutboxEntry>> pendingOutboxEntries({int limit = 200}) async {
    final db = await _db.database;
    final rows = await db.query('outbox',
        where: 'synced_at IS NULL', orderBy: 'id ASC', limit: limit);
    return rows
        .map((r) => OutboxEntry(
              id: (r['id'] as int?) ?? 0,
              entity: r['entity'] as String? ?? '',
              entityId: r['entity_id'] as String? ?? '',
              op: r['op'] as String? ?? '',
              payload: r['payload'] as String? ?? '{}',
              baseVersion: (r['base_version'] as int?) ?? 0,
              createdAt: r['created_at'] as String? ?? '',
            ))
        .toList();
  }

  Future<void> markOutboxSynced(List<int> ids) async {
    if (ids.isEmpty) return;
    final db = await _db.database;
    final marks = List.filled(ids.length, '?').join(',');
    await db.rawUpdate(
        'UPDATE outbox SET synced_at = ? WHERE id IN ($marks)',
        [DateTime.now().toIso8601String(), ...ids]);
  }

  Future<void> deleteOutboxEntry(int id) async {
    final db = await _db.database;
    await db.delete('outbox', where: 'id = ?', whereArgs: [id]);
  }

  /// 冲突里"保留手机"：用桌面端当前版本号重发本机操作。
  /// 不直接复用旧 outbox 行，避免旧 baseVersion 再次撞冲突。
  Future<void> requeueOperation({
    required String entity,
    required String entityId,
    required String op,
    required String payload,
    required int baseVersion,
  }) async {
    final db = await _db.database;
    await db.insert('outbox', {
      'entity': entity,
      'entity_id': entityId,
      'op': op,
      'payload': payload,
      'base_version': baseVersion,
      'created_at': DateTime.now().toIso8601String(),
    });
  }

  Future<String?> readMeta(String key) async {
    final db = await _db.database;
    final rows =
        await db.query('sync_meta', where: 'key = ?', whereArgs: [key], limit: 1);
    return rows.isEmpty ? null : rows.first['value'] as String?;
  }

  Future<void> writeMeta(String key, String value) async {
    final db = await _db.database;
    await db.insert('sync_meta', {'key': key, 'value': value},
        conflictAlgorithm: ConflictAlgorithm.replace);
  }

  Future<void> clearSyncMeta() async {
    final db = await _db.database;
    await db.delete('sync_meta');
  }

  /// 下行覆盖前检查：本机是否还有该问题未结算的操作（含阶段与照片）。
  Future<bool> hasPendingFor(String issueId) async {
    final db = await _db.database;
    final count = Sqflite.firstIntValue(await db.rawQuery(
          "SELECT COUNT(*) FROM outbox WHERE synced_at IS NULL "
          "AND (entity_id = ? OR entity_id LIKE ? OR payload LIKE ?)",
          [issueId, '$issueId:%', '%"issue_id":"$issueId"%'],
        )) ??
        0;
    return count > 0;
  }

  /// 应用桌面端下行的问题快照（camelCase Issue JSON）。
  /// 只在"本机没有该问题待传操作"时调用；有冲突时走 conflicts 表等人工选择。
  Future<void> applyServerIssue(Map<String, dynamic> json) async {
    final id = json['id'] as String? ?? '';
    if (id.isEmpty) return;

    final deletedAt = json['deletedAt'] as String?;
    if (deletedAt != null && deletedAt.isNotEmpty) {
      await applyServerDelete(id);
      return;
    }

    final db = await _db.database;
    final attachments = json['attachments'] as List?;
    final photoCount =
        (json['attachmentCount'] as int?) ?? (attachments?.length ?? 0);
    final stages = (json['stages'] as List?) ?? const [];

    await db.transaction((txn) async {
      await txn.insert(
        'issues',
        {
          'id': id,
          'seq_no': json['seqNo'] as String? ?? '',
          'machine_code': json['machineCode'] as String? ?? '',
          'subproject_code': json['subprojectCode'] as String?,
          'title': json['title'] as String? ?? '',
          'solution': json['solution'] as String? ?? '',
          'status': json['status'] as String? ?? IssueStatuses.unassigned,
          'completion_text': json['completionText'] as String? ?? '',
          'source_row': json['sourceRow'] as int?,
          'photo_count': photoCount,
          'version': json['version'] as int? ?? 1,
          'updated_at':
              json['updatedAt'] as String? ?? DateTime.now().toIso8601String(),
        },
        conflictAlgorithm: ConflictAlgorithm.replace,
      );

      for (final raw in stages) {
        if (raw is! Map) continue;
        final stage = raw.cast<String, dynamic>();
        final code = stage['code'] as String? ?? '';
        if (code.isEmpty) continue;
        await txn.insert(
          'issue_stages',
          {
            'issue_id': id,
            'stage_code': code,
            'owner_text': stage['ownerText'] as String? ?? '',
            'due_raw': stage['dueRaw'] as String? ?? '',
            'due_date': stage['dueDate'] as String?,
            'state': _stateFromJson(stage['state']),
            'actual_date': stage['actualDate'] as String? ?? '',
            'remark': stage['remark'] as String? ?? '',
            'version': stage['version'] as int? ?? 1,
          },
          conflictAlgorithm: ConflictAlgorithm.replace,
        );
      }
    });
  }

  /// "保留桌面"时只覆盖单个阶段，不动本机其它字段。
  Future<void> applyServerStage(
      String issueId, String stageCode, Map<String, dynamic> payload) async {
    final db = await _db.database;
    await db.update(
      'issue_stages',
      {
        'owner_text': payload['ownerText'] as String? ?? '',
        'due_raw': payload['dueRaw'] as String? ?? '',
        'due_date': payload['dueDate'] as String?,
        'state': _stateFromJson(payload['state']),
        'actual_date': payload['actualDate'] as String? ?? '',
        'remark': payload['remark'] as String? ?? '',
        'version': payload['version'] as int? ?? 1,
      },
      where: 'issue_id = ? AND stage_code = ?',
      whereArgs: [issueId, stageCode],
    );
  }

  Future<void> applyServerDelete(String issueId) async {
    final db = await _db.database;
    await db.delete('issues', where: 'id = ?', whereArgs: [issueId]);
  }

  Future<List<SyncConflict>> listConflicts() async {
    final db = await _db.database;
    final rows = await db.query('conflicts', orderBy: 'id DESC');
    return rows
        .map((r) => SyncConflict(
              id: (r['id'] as int?) ?? 0,
              entity: r['entity'] as String? ?? '',
              entityId: r['entity_id'] as String? ?? '',
              op: r['op'] as String? ?? '',
              payload: r['payload'] as String? ?? '{}',
              serverVersion: (r['server_version'] as int?) ?? 0,
              serverPayload: r['server_payload'] as String? ?? '{}',
              message: r['message'] as String? ?? '',
              createdAt: r['created_at'] as String? ?? '',
            ))
        .toList();
  }

  Future<int> pendingConflictCount() async {
    final db = await _db.database;
    return Sqflite.firstIntValue(
          await db.rawQuery('SELECT COUNT(*) FROM conflicts'),
        ) ??
        0;
  }

  Future<void> addConflict({
    required String entity,
    required String entityId,
    required String op,
    required String payload,
    required int serverVersion,
    required String serverPayload,
    required String message,
  }) async {
    final db = await _db.database;
    await db.delete('conflicts',
        where: 'entity = ? AND entity_id = ?', whereArgs: [entity, entityId]);
    await db.insert('conflicts', {
      'entity': entity,
      'entity_id': entityId,
      'op': op,
      'payload': payload,
      'server_version': serverVersion,
      'server_payload': serverPayload,
      'message': message,
      'created_at': DateTime.now().toIso8601String(),
    });
  }

  Future<void> deleteConflict(int id) async {
    final db = await _db.database;
    await db.delete('conflicts', where: 'id = ?', whereArgs: [id]);
  }

  Future<void> markAttachmentSynced(String attachmentId) async {
    final db = await _db.database;
    await db.update('attachments', {'sync_state': 'synced'},
        where: 'id = ?', whereArgs: [attachmentId]);
  }

  /// 桌面端 `StageState` 枚举序列化为数字（0=na / 1=not_started / 2=in_progress / 3=done），
  /// 手机端库内统一存字符串；两种形态都要能读，避免协议演进时把状态读成"未开始"。
  static String _stateFromJson(dynamic value) {
    if (value is int) {
      return switch (value) {
        0 => StageState.na.code,
        2 => StageState.inProgress.code,
        3 => StageState.done.code,
        _ => StageState.notStarted.code,
      };
    }
    final text = value?.toString() ?? '';
    return text.isEmpty ? StageState.notStarted.code : text;
  }

  Future<IssueStats> stats() async {
    final issues = await all();
    var closed = 0;
    var inverted = 0;
    var overdue = 0;
    var photoCount = 0;
    var photoIssues = 0;

    for (final issue in issues) {
      // 与桌面端一致：已关闭 + 已取消都算闭环终态。
      if (issue.isClosed) closed++;
      if (Rules.isPlanInverted(issue)) inverted++;
      if (Rules.overdueDays(issue) > 0) overdue++;
      photoCount += issue.photoCount;
      if (issue.photoCount > 0) photoIssues++;
    }

    final machines = issues.map((i) => i.machineCode).toSet().length;
    final subprojects = <String, SubprojectSummary>{};
    for (final issue in issues) {
      final code = issue.subprojectCode ?? '待确认';
      final item = subprojects.putIfAbsent(
          code, () => SubprojectSummary(code: code));
      item.issueCount++;
      item.machineCodes.add(issue.machineCode);
      item.photoCount += issue.photoCount;
      if (Rules.isPlanInverted(issue)) item.inverted++;
      for (final stage in issue.stages) {
        for (final name in _splitOwners(stage.ownerText)) {
          item.owners.add(name);
        }
      }
    }

    final ordered = subprojects.values.toList()
      ..sort((a, b) => a.code == '待确认'
          ? 1
          : b.code == '待确认'
              ? -1
              : a.code.compareTo(b.code));

    return IssueStats(
      total: issues.length,
      machines: machines,
      closed: closed,
      inverted: inverted,
      overdue: overdue,
      photoCount: photoCount,
      photoIssues: photoIssues,
      subprojects: ordered,
    );
  }

  Future<void> _enqueue(DatabaseExecutor txn, String entity, String entityId,
      String op, Map<String, dynamic> payload, String now,
      {int baseVersion = 0}) async {
    await txn.insert('outbox', {
      'entity': entity,
      'entity_id': entityId,
      'op': op,
      'payload': jsonEncode(payload),
      'base_version': baseVersion,
      'created_at': now,
    });
  }

  static Iterable<String> _splitOwners(String text) sync* {
    for (final part in text.split(RegExp(r'[/、,，]'))) {
      final name = part.trim();
      if (name.isNotEmpty) yield name;
    }
  }

  static IssueAttachment _toAttachment(Map<String, Object?> row) {
    return IssueAttachment(
      id: row['id'] as String,
      issueId: row['issue_id'] as String,
      fileName: row['file_name'] as String? ?? '',
      storedName: row['stored_name'] as String? ?? '',
      storedPath: row['stored_path'] as String? ?? '',
      size: (row['size'] as int?) ?? 0,
      mime: row['mime'] as String? ?? 'image/jpeg',
      createdAt: row['created_at'] as String? ?? '',
      isDemo: ((row['is_demo'] as int?) ?? 0) == 1,
      syncState: row['sync_state'] as String? ?? 'local',
    );
  }

  static IssueStage _toStage(Map<String, Object?> row) {
    final dueText = row['due_date'] as String?;
    return IssueStage(
      code: row['stage_code'] as String,
      ownerText: row['owner_text'] as String? ?? '',
      dueRaw: row['due_raw'] as String? ?? '',
      dueDate: dueText == null || dueText.isEmpty ? null : DateTime.tryParse(dueText),
      state: StageState.parse(row['state'] as String?),
      actualDate: row['actual_date'] as String? ?? '',
      remark: row['remark'] as String? ?? '',
      version: (row['version'] as int?) ?? 1,
    );
  }

  static Issue _toIssue(Map<String, Object?> row, List<IssueStage> stages) {
    return Issue(
      id: row['id'] as String,
      seqNo: row['seq_no'] as String? ?? '',
      machineCode: row['machine_code'] as String? ?? '',
      subprojectCode: row['subproject_code'] as String?,
      title: row['title'] as String? ?? '',
      solution: row['solution'] as String? ?? '',
      status: row['status'] as String? ?? IssueStatuses.unassigned,
      completionText: row['completion_text'] as String? ?? '',
      stages: stages,
      sourceRow: row['source_row'] as int?,
      photoCount: (row['photo_count'] as int?) ?? 0,
      version: (row['version'] as int?) ?? 1,
    );
  }
}

class IssueComment {
  const IssueComment({required this.author, required this.body, this.createdAt});

  final String author;
  final String body;
  final DateTime? createdAt;
}

/// 机台维度的现场概览：问题数 / 倒挂 / 逾期 / 照片。
class MachineSummary {
  MachineSummary({required this.machineCode});

  final String machineCode;
  int issueCount = 0;
  int inverted = 0;
  int overdue = 0;
  int photoCount = 0;

  /// 该机台最后一条问题的更新时间（原文 `YYYY-MM-DD...`），用于「最近 11-22」。
  /// 取不到时为空，界面省略，不编日期。
  String lastUpdated = '';
}

/// 待传队列中的一条操作（同步中心逐条展示）。
class PendingOperation {
  const PendingOperation({
    required this.id,
    required this.entity,
    required this.entityId,
    required this.op,
    required this.payload,
    required this.createdAt,
    this.baseVersion = 0,
    this.machineCode = '',
    this.issueTitle = '',
  });

  final int id;
  final String entity;
  final String entityId;
  final String op;
  final String payload;
  final int baseVersion;
  final String createdAt;

  /// 由 outbox 关联 issues 表补出的展示字段；查不到时为空，界面退回实体 id。
  final String machineCode;
  final String issueTitle;

  String get actionLabel => switch (op) {
        'create' => '新建问题',
        'upload' => '上传照片',
        'status' => '状态变更',
        'comment' => '现场评论',
        'upsert' => '阶段推进',
        _ => op,
      };
}

/// 待同步队列的一条原始记录（同步客户端按 id 升序推送）。
class OutboxEntry {
  const OutboxEntry({
    required this.id,
    required this.entity,
    required this.entityId,
    required this.op,
    required this.payload,
    required this.baseVersion,
    required this.createdAt,
  });

  final int id;
  final String entity;
  final String entityId;
  final String op;
  final String payload;
  final int baseVersion;
  final String createdAt;
}

/// 冲突：手机与桌面都改了同一实体，等用户选"保留手机 / 保留桌面"。
class SyncConflict {
  const SyncConflict({
    required this.id,
    required this.entity,
    required this.entityId,
    required this.op,
    required this.payload,
    required this.serverVersion,
    required this.serverPayload,
    required this.message,
    required this.createdAt,
  });

  final int id;
  final String entity;
  final String entityId;
  final String op;

  /// 手机端原始操作 payload（保留手机时按 serverVersion 重发）。
  final String payload;

  final int serverVersion;

  /// 桌面端当前值（保留桌面时覆盖本机）。
  final String serverPayload;

  final String message;
  final String createdAt;

  String get label => switch (entity) {
        'issue_stage' => '阶段',
        'issue' => '问题',
        'attachment' => '照片',
        _ => entity,
      };
}

class SubprojectSummary {
  SubprojectSummary({required this.code});

  final String code;
  int issueCount = 0;
  int photoCount = 0;

  /// 分项目内计划倒挂条数，用于项目页卡片右上角的红色徽标。
  int inverted = 0;
  final Set<String> machineCodes = {};
  final Set<String> owners = {};
}

class IssueStats {
  const IssueStats({
    required this.total,
    required this.machines,
    required this.closed,
    required this.inverted,
    required this.overdue,
    required this.photoCount,
    required this.photoIssues,
    required this.subprojects,
  });

  final int total;
  final int machines;
  final int closed;
  final int inverted;
  final int overdue;
  final int photoCount;
  final int photoIssues;
  final List<SubprojectSummary> subprojects;
}
