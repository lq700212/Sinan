import 'package:material_ui/material_ui.dart';

import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import 'exchange_screen.dart';
import 'issue_list_screen.dart';
import 'overview_screen.dart';
import 'settings_screen.dart';

class ShellScreen extends StatefulWidget {
  const ShellScreen({super.key, required this.state});

  final AppState state;

  @override
  State<ShellScreen> createState() => _ShellScreenState();
}

class _ShellScreenState extends State<ShellScreen> {
  int _index = 0;
  bool _refreshing = false;

  static const _tabs = [
    (Icons.dashboard_outlined, Icons.dashboard, '项目总览'),
    (Icons.list_alt_outlined, Icons.list_alt, '问题台账'),
    (Icons.swap_horiz_outlined, Icons.swap_horiz, '数据交换'),
    (Icons.settings_outlined, Icons.settings, '系统设置'),
  ];

  static const _subtitles = [
    '分项目 · 机台 · 风险清单',
    '搜索 · 筛选 · 推进阶段',
    '离线记录 · 配对 · 同步',
    '主题 · 数据 · 账号',
  ];

  Future<void> _refresh() async {
    setState(() => _refreshing = true);
    await widget.state.refresh();
    if (!mounted) return;
    setState(() => _refreshing = false);
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('本地数据已刷新。')),
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
                    OverviewScreen(state: widget.state, onOpenTab: (i) => setState(() => _index = i)),
                    IssueListScreen(state: widget.state),
                    ExchangeScreen(state: widget.state),
                    SettingsScreen(state: widget.state),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        height: 64,
        onDestinationSelected: (i) => setState(() => _index = i),
        destinations: [
          for (final tab in _tabs)
            NavigationDestination(
              icon: Icon(tab.$1),
              selectedIcon: Icon(tab.$2),
              label: tab.$3,
            ),
        ],
      ),
    );
  }

  Widget _topBar(SinanPalette p) {
    final now = DateTime.now();
    final hhmm =
        '${now.hour.toString().padLeft(2, '0')}:${now.minute.toString().padLeft(2, '0')}';
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 10, 12, 6),
      child: Row(
        children: [
          Container(
            width: 38,
            height: 38,
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
            child: const Text('司',
                style: TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w800,
                    color: Colors.white)),
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
                  '${_subtitles[_index]} · 数据截至 $hhmm',
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
          const SizedBox(width: 2),
          PopupMenuButton<String>(
            tooltip: '账号',
            onSelected: (value) {
              if (value == 'logout') widget.state.logout();
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
                    radius: 11,
                    backgroundColor: p.primaryDim,
                    child: Text(
                      widget.state.userName.isEmpty
                          ? '?'
                          : widget.state.userName.substring(0, 1),
                      style: TextStyle(
                          fontSize: 10,
                          fontWeight: FontWeight.w700,
                          color: p.primary),
                    ),
                  ),
                  const SizedBox(width: 5),
                  Text(widget.state.userName,
                      style: TextStyle(fontSize: 11.5, color: p.fg)),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
