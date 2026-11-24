import 'package:material_ui/material_ui.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../data/issue_repository.dart';
import '../models/issue.dart';

/// 应用级状态：会话、主题、问题列表与统计。
/// 只做编排，业务规则在 `models/rules.dart`，数据访问在 `IssueRepository`。
class AppState extends ChangeNotifier {
  AppState(this.repository);

  final IssueRepository repository;

  ThemeMode _themeMode = ThemeMode.light;
  bool _loading = true;
  String? _loadError;
  bool _loggedIn = false;
  String _userName = '';
  String _userRole = '';
  String _userAccount = '';

  List<Issue> _issues = const [];
  IssueStats? _stats;
  int _pendingOutbox = 0;

  ThemeMode get themeMode => _themeMode;
  bool get loading => _loading;
  String? get loadError => _loadError;
  bool get loggedIn => _loggedIn;
  String get userName => _userName;
  String get userRole => _userRole;
  String get userAccount => _userAccount;
  List<Issue> get issues => _issues;
  IssueStats? get stats => _stats;
  int get pendingOutbox => _pendingOutbox;

  /// 首次启动与“重试”共用：读主题、建库、灌种子、拉统计。
  /// 任何一步失败都不能让界面停在启动页，错误交给 `loadError` 由 UI 展示。
  Future<void> init() async {
    _loading = true;
    _loadError = null;
    notifyListeners();
    try {
      final prefs = await SharedPreferences.getInstance();
      _themeMode =
          prefs.getString('theme') == 'dark' ? ThemeMode.dark : ThemeMode.light;
      await refresh();
    } catch (e) {
      _loadError = e.toString();
    }
    _loading = false;
    notifyListeners();
  }

  Future<void> refresh() async {
    _issues = await repository.all();
    _stats = await repository.stats();
    _pendingOutbox = await repository.pendingOutbox();
    notifyListeners();
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
}
