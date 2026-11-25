import 'package:material_ui/material_ui.dart';

import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import 'home_todo_screen.dart';
import 'issue_list_screen.dart';
import 'new_issue_screen.dart';
import 'project_list_screen.dart';
import 'settings_screen.dart';
import 'sync_screen.dart';

/// 现场端主壳：设计稿 m-home/m-projects/m-list/m-sync 共用的五槽底栏。
///
/// 顺序固定为 待办 · 项目 · ＋ · 台账 · 同步；设置放进右上角账号菜单，
/// 不再占一个底部 tab（现场人员一天用不到一次）。
class ShellScreen extends StatefulWidget {
  const ShellScreen({super.key, required this.state});

  final AppState state;

  @override
  State<ShellScreen> createState() => _ShellScreenState();
}

class _ShellScreenState extends State<ShellScreen> {
  int _index = 0;
  bool _refreshing = false;
  int _syncEpoch = 0;

  static const _tabs = [
    (Icons.dashboard_outlined, Icons.dashboard, '待办'),
    (Icons.grid_view_outlined, Icons.grid_view, '项目'),
    (Icons.list_alt_outlined, Icons.list_alt, '台账'),
    (Icons.sync_outlined, Icons.sync, '同步'),
  ];

  static const _subtitles = [
    '我的阶段任务 · 今天先处理',
    '分项目 · 机台 · 风险清单',
    '搜索 · 筛选 · 推进阶段',
    '本机待传 · 配对 · 同步规则',
  ];

  Future<void> _refresh() async {
    setState(() => _refreshing = true);
    await widget.state.refresh();
    if (mounted) {
      setState(() {
        _refreshing = false;
        _syncEpoch++;
      });
    }
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('本地数据已刷新。')),
    );
  }

  void _select(int index) {
    setState(() {
      _index = index;
      if (index == 3) _syncEpoch++;
    });
  }

  Future<void> _openNew() async {
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => NewIssueScreen(state: widget.state),
      ),
    );
  }

  void _openSettings() {
    Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => Scaffold(
          body: AmbientBackground(
            child: SafeArea(
              child: Column(
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(6, 6, 16, 4),
                    child: Row(
                      children: [
                        IconButton(
                          onPressed: () => Navigator.of(context).pop(),
                          icon: Icon(Icons.arrow_back_ios_new,
                              size: 18, color: context.palette.muted),
                        ),
                        Text('系统设置',
                            style: TextStyle(
                                fontSize: 17,
                                fontWeight: FontWeight.w700,
                                color: context.palette.fg)),
                      ],
                    ),
                  ),
                  Expanded(child: SettingsScreen(state: widget.state)),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Scaffold(
      extendBody: true,
      body: AmbientBackground(
        child: SafeArea(
          bottom: false,
          child: Column(
            children: [
              _topBar(p),
              Expanded(
                child: IndexedStack(
                  index: _index,
                  children: [
                    HomeTodoScreen(
                      state: widget.state,
                      onOpenProjects: () => _select(1),
                      onOpenSync: () => _select(3),
                    ),
                    ProjectListScreen(
                      state: widget.state,
                      onOpenLedger: () => _select(2),
                    ),
                    IssueListScreen(state: widget.state),
                    SyncScreen(
                      key: ValueKey('sync-$_syncEpoch'),
                      state: widget.state,
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
      bottomNavigationBar: _bottomBar(p),
    );
  }

  Widget _topBar(SinanPalette p) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 10, 6),
      child: Row(
        children: [
          Container(
            width: 36,
            height: 36,
            decoration: BoxDecoration(
              gradient: p.primaryGradient,
              borderRadius: BorderRadius.circular(12),
              boxShadow: [
                BoxShadow(
                  color: p.primary.withValues(alpha: 0.28),
                  blurRadius: 12,
                  offset: const Offset(0, 4),
                ),
              ],
            ),
            alignment: Alignment.center,
            child: Text('司',
                style: TextStyle(
                    fontSize: 17,
                    fontWeight: FontWeight.w800,
                    color: p.onPrimary)),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  _tabs[_index].$3,
                  style: TextStyle(
                      fontSize: 18, fontWeight: FontWeight.w700, color: p.fg),
                ),
                Text(
                  _subtitles[_index],
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 10.5, color: p.faint),
                ),
              ],
            ),
          ),
          if (_refreshing)
            const Padding(
              padding: EdgeInsets.symmetric(horizontal: 10),
              child: SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2)),
            )
          else
            IconButton(
              onPressed: _refresh,
              icon: Icon(Icons.refresh, size: 20, color: p.muted),
              tooltip: '刷新本地数据',
            ),
          PopupMenuButton<String>(
            tooltip: '账号',
            onSelected: (value) {
              if (value == 'logout') widget.state.logout();
              if (value == 'settings') _openSettings();
            },
            itemBuilder: (context) => [
              PopupMenuItem(
                enabled: false,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(widget.state.userName,
                        style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w600,
                            color: p.fg)),
                    Text(widget.state.userRole,
                        style: TextStyle(fontSize: 11, color: p.faint)),
                  ],
                ),
              ),
              const PopupMenuDivider(),
              const PopupMenuItem(value: 'settings', child: Text('系统设置')),
              const PopupMenuItem(value: 'logout', child: Text('退出登录')),
            ],
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 5),
              decoration: BoxDecoration(
                color: p.well,
                borderRadius: BorderRadius.circular(99),
                border: Border.all(color: p.cardBorder),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  CircleAvatar(
                    radius: 10,
                    backgroundColor: p.primaryDim,
                    child: Text(
                      widget.state.userName.isEmpty
                          ? '?'
                          : widget.state.userName.substring(0, 1),
                      style: TextStyle(
                          fontSize: 9.5,
                          fontWeight: FontWeight.w700,
                          color: p.primary),
                    ),
                  ),
                  const SizedBox(width: 5),
                  Text(widget.state.userName,
                      style: TextStyle(fontSize: 11, color: p.fg)),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _bottomBar(SinanPalette p) {
    return Container(
      decoration: BoxDecoration(
        color: p.panel,
        border: Border(top: BorderSide(color: p.sepLine)),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: p.isDark ? 0.28 : 0.05),
            blurRadius: 16,
            offset: const Offset(0, -4),
          ),
        ],
      ),
      child: SafeArea(
        top: false,
        child: SizedBox(
          height: 62,
          child: Row(
            children: [
              _tabItem(p, 0, Icons.dashboard_outlined, Icons.dashboard, '待办'),
              _tabItem(p, 1, Icons.grid_view_outlined, Icons.grid_view, '项目'),
              _fab(p),
              _tabItem(p, 2, Icons.list_alt_outlined, Icons.list_alt, '台账'),
              _tabItem(p, 3, Icons.sync_outlined, Icons.sync, '同步'),
            ],
          ),
        ),
      ),
    );
  }

  Widget _tabItem(SinanPalette p, int index, IconData icon, IconData active,
      String label) {
    final selected = _index == index;
    final color = selected ? p.primary : p.faint;
    return Expanded(
      child: InkWell(
        onTap: () => _select(index),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(selected ? active : icon, size: 21, color: color),
            const SizedBox(height: 2),
            Text(label,
                style: TextStyle(
                    fontSize: 10,
                    fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                    color: color)),
          ],
        ),
      ),
    );
  }

  Widget _fab(SinanPalette p) {
    return Expanded(
      child: Center(
        child: GestureDetector(
          onTap: _openNew,
          child: Container(
            width: 48,
            height: 48,
            decoration: BoxDecoration(
              gradient: p.primaryGradient,
              shape: BoxShape.circle,
              boxShadow: [
                BoxShadow(
                  color: p.primary.withValues(alpha: 0.4),
                  blurRadius: 14,
                  offset: const Offset(0, 5),
                ),
              ],
            ),
            child: Icon(Icons.add, size: 26, color: p.onPrimary),
          ),
        ),
      ),
    );
  }
}
