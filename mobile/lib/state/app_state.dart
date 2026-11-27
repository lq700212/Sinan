import 'dart:convert';

import 'package:material_ui/material_ui.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../data/attachment_store.dart';
import '../data/issue_repository.dart';
import '../data/local_db.dart';
import '../data/project_repository.dart';
import '../data/sync_client.dart';
import '../models/attachment.dart';
import '../models/issue.dart';
import '../models/project.dart';

/// 应用级状态：会话、主题、当前总项目、问题列表与统计。
/// 只做编排，业务规则在 `models/rules.dart`，数据访问在 Repository。
class AppState extends ChangeNotifier {
  AppState(this.repository,
      {ProjectRepository? projects,
      AttachmentStore? attachments,
      SyncClient? syncClient})
      : projects = projects ?? ProjectRepository(LocalDb.instance),
        attachments = attachments ?? AttachmentStore(),
        syncClient = syncClient ?? SyncClient();

  final IssueRepository repository;
  final ProjectRepository projects;
  final AttachmentStore attachments;
  final SyncClient syncClient;

  ThemeMode _themeMode = ThemeMode.light;
  String? _loadError;
  bool _loggedIn = false;
  String _userName = '';
  String _userRole = '';
  String _userAccount = '';

  List<Issue> _issues = const [];
  IssueStats? _stats;
  int _pendingOutbox = 0;
  int _todayRecorded = 0;

  List<Project> _projectList = const [];
  List<ProjectSummary> _projectSummaries = const [];
  Project? _currentProject;

  bool _syncBusy = false;
  String _syncStatus = '';
  String _syncServerUrl = '';
  String _syncDeviceId = '';
  String _syncDeviceName = '';
  String _syncToken = '';
  int _syncCursor = 0;
  String _syncLastAt = '';
  List<SyncConflict> _syncConflicts = const [];

  ThemeMode get themeMode => _themeMode;
  String? get loadError => _loadError;
  bool get loggedIn => _loggedIn;
  String get userName => _userName;
  String get userRole => _userRole;
  String get userAccount => _userAccount;
  List<Issue> get issues => _issues;
  IssueStats? get stats => _stats;
  int get pendingOutbox => _pendingOutbox;
  int get todayRecorded => _todayRecorded;

  /// 全部总项目（离线副本）；切换弹层与设置页共用。
  List<Project> get projectList => _projectList;

  /// 切换弹层的实时规模行；`isCurrent` 由当前选择标记。
  List<ProjectSummary> get projectSummaries => _projectSummaries;

  /// 当前总项目：所有列表/统计/新建问题都跟随它。
  Project? get currentProject => _currentProject;
  String? get currentProjectId => _currentProject?.id;

  /// 当前项目号；无项目时退回演示项目号，只用于文案兜底，不用于写库。
  String get projectCode => _currentProject?.code ?? LocalDb.demoProjectCode;

  bool get syncBusy => _syncBusy;
  String get syncStatus => _syncStatus;
  String get syncServerUrl => _syncServerUrl;
  String get syncDeviceId => _syncDeviceId;
  String get syncDeviceName => _syncDeviceName;
  String get syncLastAt => _syncLastAt;
  List<SyncConflict> get syncConflicts => _syncConflicts;
  int get syncConflictCount => _syncConflicts.length;
  bool get syncConfigured =>
      _syncToken.isNotEmpty && _syncServerUrl.isNotEmpty;

  /// 首次启动与“重试”共用：读主题、建库、灌种子、拉统计。
  /// 不挡首屏——调用方已先渲染登录页，这里读完只负责 notifyListeners；
  /// 任何一步失败都不留空白，错误交给 `loadError` 由 UI 展示。
  Future<void> init() async {
    _loadError = null;
    notifyListeners();
    try {
      final prefs = await SharedPreferences.getInstance();
      _themeMode =
          prefs.getString('theme') == 'dark' ? ThemeMode.dark : ThemeMode.light;
      await _loadSyncConfig();
      await _loadProjects();
      await refresh();
      await _refreshConflicts();
    } catch (e) {
      _loadError = e.toString();
    }
    notifyListeners();
  }

  Future<void> refresh() async {
    final projectId = _currentProject?.id;
    // 没有项目就不展示全库：多项目后"全库"不等于任何人的当前工作。
    _issues = projectId == null
        ? const []
        : await repository.all(projectId: projectId);
    _stats =
        projectId == null ? null : await repository.stats(projectId: projectId);
    _pendingOutbox = await repository.pendingOutbox();
    _todayRecorded = await repository.todayOutboxCount();
    _projectSummaries =
        await projects.summaries(currentId: _currentProject?.id);
    notifyListeners();
  }

  /// 加载项目字典并恢复上次选择。
  /// 选择持久化在 `sync_meta.current_project_id`，重启回到上次项目；
  /// 若记录的项目已不存在（例如桌面端归档），退回项目号最小的一个。
  Future<void> _loadProjects() async {
    _projectList = await projects.all();
    final saved = await repository.readMeta('current_project_id') ?? '';
    Project? selected;
    for (final project in _projectList) {
      if (project.id == saved) {
        selected = project;
        break;
      }
    }
    _currentProject =
        selected ?? (_projectList.isEmpty ? null : _projectList.first);
    if (_currentProject != null) {
      await repository.writeMeta('current_project_id', _currentProject!.id);
    }
    _projectSummaries =
        await projects.summaries(currentId: _currentProject?.id);
  }

  /// 切换总项目：写持久化选择 → 重查列表与统计 → 通知所有页面重建。
  /// 切换后各页面只保留当前项目数据，避免 P7392 页面还挂着 P7391 的缓存。
  Future<void> switchProject(Project project) async {
    if (_currentProject?.id == project.id) return;
    _currentProject = project;
    await repository.writeMeta('current_project_id', project.id);
    await refresh();
  }

  /// 演示账号：engineer / 123456（张工，工程师）、admin / 123456（系统管理员）。
  /// MVP 只在页面层做门禁，不做真实鉴权。
  bool login(String account, String password) {
    final name = account.trim();
    if (password != '123456') return false;
    if (name == 'engineer') {
      _userAccount = 'engineer';
      _userName = '张工';
      _userRole = '工程师';
    } else if (name == 'admin') {
      _userAccount = 'admin';
      _userName = '系统管理员';
      _userRole = '管理员';
    } else {
      return false;
    }
    _loggedIn = true;
    notifyListeners();
    return true;
  }

  void logout() {
    _loggedIn = false;
    _userAccount = '';
    _userName = '';
    _userRole = '';
    notifyListeners();
  }

  Future<void> setThemeMode(ThemeMode mode) async {
    if (_themeMode == mode) return;
    _themeMode = mode;
    notifyListeners();
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString('theme', mode == ThemeMode.dark ? 'dark' : 'light');
  }

  Future<Issue> saveStage({
    required Issue issue,
    required String code,
    required String ownerText,
    required String dueRaw,
    required StageState state,
    required String actualDate,
    required String remark,
  }) async {
    final updated = await repository.saveStage(
      issue: issue,
      code: code,
      ownerText: ownerText,
      dueRaw: dueRaw,
      state: state,
      actualDate: actualDate,
      remark: remark,
    );
    await refresh();
    return updated;
  }

  Future<Issue> updateStatus(Issue issue, String status) async {
    final updated = await repository.updateStatus(issue, status);
    await refresh();
    return updated;
  }

  Future<void> addComment(String issueId, String body) async {
    await repository.addComment(issueId, _userName, body);
    notifyListeners();
  }

  /// 现场新建问题（不拍照也可先存），返回带四个阶段的新问题。
  Future<Issue> createIssue({
    required String machineCode,
    required String title,
    String solution = '',
    String stageCode = StageCodes.assembly,
    String ownerText = '',
    String dueRaw = '',
    String? status,
  }) async {
    final project = _currentProject;
    if (project == null) {
      throw StateError('当前没有总项目，请先在桌面端新建并同步到手机。');
    }
    final issue = await repository.createIssue(
      projectId: project.id,
      projectCode: project.code,
      machineCode: machineCode,
      title: title,
      solution: solution,
      stageCode: stageCode,
      ownerText: ownerText,
      dueRaw: dueRaw,
      status: status,
    );
    await refresh();
    return issue;
  }

  /// 拍照 / 从相册选一张并挂到问题；用户取消返回 null。
  /// 先落文件再入库：入库失败时文件成为孤儿，但绝不出现"库里有一条指向不存在文件"的记录。
  Future<IssueAttachment?> addPhoto(String issueId,
      {bool fromCamera = true}) async {
    final picked =
        await attachments.pick(issueId: issueId, fromCamera: fromCamera);
    if (picked == null) return null;
    final saved = await repository.addAttachment(issueId: issueId, picked: picked);
    await refresh();
    return saved;
  }

  /// 新建页先拍照、后保存：保存问题时才把系统缓存图复制进应用目录。
  Future<IssueAttachment> addPhotoFromPath(
      String issueId, String sourcePath) async {
    final picked =
        await attachments.persist(sourcePath, issueId: issueId);
    final saved = await repository.addAttachment(issueId: issueId, picked: picked);
    await refresh();
    return saved;
  }

  // ---------------------------------------------------------------------
  // 局域网同步
  // ---------------------------------------------------------------------

  Future<void> _loadSyncConfig() async {
    _syncServerUrl = await repository.readMeta('server_url') ?? '';
    _syncDeviceId = await repository.readMeta('device_id') ?? '';
    _syncDeviceName = await repository.readMeta('device_name') ?? '';
    _syncToken = await repository.readMeta('token') ?? '';
    _syncCursor = int.tryParse(await repository.readMeta('cursor') ?? '') ?? 0;
    _syncLastAt = await repository.readMeta('last_sync_at') ?? '';
  }

  Future<void> _refreshConflicts() async {
    _syncConflicts = await repository.listConflicts();
  }

  /// 配对：先 ping 确认是司南服务，再用一次性 6 位码换 token。
  /// 成功即保存地址/令牌并立刻跑一次同步；失败把可读原因写进 syncStatus。
  Future<bool> pair({required String serverUrl, required String code}) async {
    if (_syncBusy) return false;
    final baseUrl = SyncClient.normalizeBaseUrl(serverUrl);
    if (baseUrl.isEmpty) {
      _syncStatus = '请填写桌面端地址，例如 192.168.1.26:5180';
      notifyListeners();
      return false;
    }
    if (code.trim().length != 6) {
      _syncStatus = '配对码是桌面端生成的 6 位数字。';
      notifyListeners();
      return false;
    }

    _syncBusy = true;
    _syncStatus = '正在连接 $baseUrl …';
    notifyListeners();
    try {
      final ping = await syncClient.ping(baseUrl);
      final deviceName = _syncDeviceName.isEmpty
          ? '${_userName.isEmpty ? '现场' : _userName}的 Android'
          : _syncDeviceName;
      final result = await syncClient.pair(
        baseUrl: baseUrl,
        code: code,
        deviceName: deviceName,
        model: '',
        employeeNo: _userAccount,
      );

      await repository.writeMeta('server_url', baseUrl);
      await repository.writeMeta('device_id', result.deviceId);
      await repository.writeMeta('device_name', deviceName);
      await repository.writeMeta('token', result.token);
      await repository.writeMeta('cursor', '0');
      _syncServerUrl = baseUrl;
      _syncDeviceId = result.deviceId;
      _syncDeviceName = deviceName;
      _syncToken = result.token;
      _syncCursor = 0;
      _syncStatus = '已配对 · ${ping.serverName}';
      notifyListeners();

      _syncBusy = false;
      await syncNow();
      return true;
    } on SyncException catch (e) {
      _syncStatus = '配对失败：${e.message}';
      return false;
    } catch (e) {
      _syncStatus = '配对失败：$e';
      return false;
    } finally {
      _syncBusy = false;
      notifyListeners();
    }
  }

  /// 全量同步：先上行（业务操作 → 照片），再按游标下行。
  /// 冲突不静默覆盖：写 conflicts 表，等用户在同步页选"保留手机 / 保留桌面"。
  Future<bool> syncNow() async {
    if (!syncConfigured) {
      _syncStatus = '尚未配对：请先在桌面端生成配对码。';
      notifyListeners();
      return false;
    }
    if (_syncBusy) return false;

    _syncBusy = true;
    _syncStatus = '正在同步…';
    notifyListeners();

    var uploaded = 0;
    var downloaded = 0;
    var failed = 0;
    var conflicts = 0;
    final errors = <String>[];

    try {
      final entries = await repository.pendingOutboxEntries();
      final business =
          entries.where((e) => e.entity != 'attachment').toList();
      final photos = entries.where((e) => e.entity == 'attachment').toList();

      if (business.isNotEmpty) {
        final results = await syncClient.push(
          baseUrl: _syncServerUrl,
          token: _syncToken,
          entries: business,
        );
        final settled = <int>[];
        for (final result in results) {
          OutboxEntry? entry;
          for (final item in business) {
            if (item.id == result.clientOpId) {
              entry = item;
              break;
            }
          }
          if (entry == null) continue;

          switch (result.status) {
            case 'applied':
            case 'duplicate':
              settled.add(entry.id);
              uploaded++;
              break;
            case 'conflict':
              await repository.addConflict(
                entity: entry.entity,
                entityId: entry.entityId,
                op: entry.op,
                payload: entry.payload,
                serverVersion: result.serverVersion,
                serverPayload: jsonEncode(result.serverPayload ?? const {}),
                message: result.message.isEmpty
                    ? '桌面端也修改了这条记录，请选择保留哪一边。'
                    : result.message,
              );
              settled.add(entry.id);
              conflicts++;
              break;
            default:
              failed++;
              if (result.message.isNotEmpty) errors.add(result.message);
              break;
          }
        }
        await repository.markOutboxSynced(settled);
      }

      for (final entry in photos) {
        final payload = _decodeObject(entry.payload);
        final issueId = payload['issue_id'] as String? ?? '';
        final storedPath = payload['stored_path'] as String? ?? '';
        final fileName = payload['file_name'] as String? ?? 'IMG.jpg';
        if (issueId.isEmpty || storedPath.isEmpty) {
          failed++;
          continue;
        }
        final path = await attachments.absolutePath(storedPath);
        if (path == null) {
          failed++;
          errors.add('照片文件已丢失：$storedPath');
          continue;
        }
        final result = await syncClient.uploadAttachment(
          baseUrl: _syncServerUrl,
          token: _syncToken,
          issueId: issueId,
          attachmentId: entry.entityId,
          fileName: fileName,
          filePath: path,
        );
        if (result.ok) {
          await repository.markOutboxSynced([entry.id]);
          await repository.markAttachmentSynced(entry.entityId);
          uploaded++;
        } else {
          failed++;
          if (result.message.isNotEmpty) errors.add(result.message);
        }
      }

      var cursor = _syncCursor;
      var hasMore = true;
      while (hasMore) {
        final pull = await syncClient.pull(
          baseUrl: _syncServerUrl,
          token: _syncToken,
          cursor: cursor,
        );
        for (final change in pull.changes) {
          final map = change.payload is Map
              ? (change.payload as Map).cast<String, dynamic>()
              : const <String, dynamic>{};
          if (map.isEmpty) continue;

          // 项目字典先落库：桌面端新建总项目会发 project + subproject 两类变更，
          // 手机端拿到后即可在项目页切换，不需要重新配对或全量重灌。
          if (change.entity == 'project') {
            await projects.applyServerProject(map);
            downloaded++;
            continue;
          }
          if (change.entity == 'subproject') {
            await projects.applyServerSubproject(map);
            downloaded++;
            continue;
          }
          if (change.entity != 'issue') continue;

          if (await repository.hasPendingFor(change.entityId)) {
            // 本机还有未结算操作：不覆盖，交人工选择。
            await repository.addConflict(
              entity: 'issue',
              entityId: change.entityId,
              op: 'pull',
              payload: '{}',
              serverVersion: (map['version'] as num?)?.toInt() ?? 0,
              serverPayload: jsonEncode(map),
              message: '桌面端更新了这条问题，本机仍有待传操作。',
            );
            conflicts++;
          } else {
            await repository.applyServerIssue(map);
            downloaded++;
          }
        }
        cursor = pull.cursor;
        hasMore = pull.hasMore;
        if (pull.changes.isEmpty) break;
      }

      await repository.writeMeta('cursor', cursor.toString());
      _syncCursor = cursor;
      final now = DateTime.now().toIso8601String();
      await repository.writeMeta('last_sync_at', now);
      _syncLastAt = now;

      if (failed == 0) {
        _syncStatus = '同步完成：上行 $uploaded 条 · 下行 $downloaded 条'
            '${conflicts > 0 ? ' · 冲突 $conflicts 条' : ''}';
      } else {
        final reason = errors.isEmpty ? '' : '：${errors.first}';
        _syncStatus = '部分完成：上行 $uploaded 条 · 失败 $failed 条'
            '${conflicts > 0 ? ' · 冲突 $conflicts 条' : ''}$reason';
      }
      // 新项目/新问题都可能在下行里出现：先重载项目字典再刷新台账，
      // 否则项目页看不到刚同步下来的 P7392。
      await _loadProjects();
      await refresh();
      await _refreshConflicts();
      return failed == 0;
    } on SyncException catch (e) {
      _syncStatus = '同步失败：${e.message}';
      return false;
    } catch (e) {
      _syncStatus = '同步失败：$e';
      return false;
    } finally {
      _syncBusy = false;
      notifyListeners();
    }
  }

  /// 冲突处置：保留手机 = 用桌面最新版本号重发本机操作；
  /// 保留桌面 = 用 serverPayload 覆盖本机对应字段。
  Future<void> resolveConflict(SyncConflict conflict,
      {required bool keepMobile}) async {
    try {
      if (keepMobile) {
        await repository.requeueOperation(
          entity: conflict.entity,
          entityId: conflict.entityId,
          op: conflict.op == 'pull' ? 'upsert' : conflict.op,
          payload: conflict.payload,
          baseVersion: conflict.serverVersion,
        );
      } else {
        final map = _decodeObject(conflict.serverPayload);
        if (conflict.entity == 'issue_stage') {
          final parts = conflict.entityId.split(':');
          if (parts.length == 2) {
            await repository.applyServerStage(parts[0], parts[1], map);
          }
        } else if (conflict.entity == 'issue') {
          await repository.applyServerIssue(map);
        }
      }
      await repository.deleteConflict(conflict.id);
      await _refreshConflicts();
      notifyListeners();

      if (keepMobile) {
        await syncNow();
      } else {
        await refresh();
      }
    } catch (e) {
      _syncStatus = '处理冲突失败：$e';
      notifyListeners();
    }
  }

  /// 解除配对：清掉令牌与游标，本机队列保留，重新配对后可继续补传。
  Future<void> resetPairing() async {
    await repository.clearSyncMeta();
    _syncServerUrl = '';
    _syncDeviceId = '';
    _syncDeviceName = '';
    _syncToken = '';
    _syncCursor = 0;
    _syncLastAt = '';
    _syncStatus = '已解除配对。本机待传队列仍保留。';
    notifyListeners();
  }

  static Map<String, dynamic> _decodeObject(String text) {
    try {
      final decoded = jsonDecode(text);
      return decoded is Map ? decoded.cast<String, dynamic>() : const {};
    } catch (_) {
      return const {};
    }
  }
}
