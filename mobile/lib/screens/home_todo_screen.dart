import 'package:material_ui/material_ui.dart';

import '../data/issue_repository.dart';
import '../models/issue.dart';
import '../models/rules.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import 'issue_detail_screen.dart';
import 'machine_select_screen.dart';
import 'new_issue_screen.dart';
import 'subproject_screen.dart';

/// 手机端首屏「我的待办」。
///
/// 设计口径来自 `docs/ui_design_mockups.html` 的 m-home：
/// - 打开手机先看"今天该我做什么"，不先看 36 条全量台账；
/// - 扫码 / 拍照是最高频动作，做成并排主按钮；
/// - 顶部不伪造"已联网"，没有同步服务就如实标"本机缓存"。
class HomeTodoScreen extends StatelessWidget {
  const HomeTodoScreen({
    super.key,
    required this.state,
    this.onOpenProjects,
    this.onOpenSync,
  });

  final AppState state;

  /// 点"P7391 总览"切到项目 tab；由 Shell 提供，页面不自己切 tab。
  final VoidCallback? onOpenProjects;

  /// 点"查看"切到同步 tab；同样由 Shell 提供。
  final VoidCallback? onOpenSync;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) {
        final p = context.palette;
        final open = state.issues.where((i) => !i.isClosed).toList();
        final mine = Rules.myStageTasks(state.issues, state.userName);
        final risky = Rules.attentionOrder(state.issues);
        final inverted = state.stats?.inverted ?? 0;

        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 2, 16, 96),
          children: [
            _greeting(context, p, open.length),
            const SizedBox(height: 8),
            _projectChips(context, p),
            const SizedBox(height: 10),
            Row(
              children: [
                Expanded(
                  child: KpiTile(
                    label: '我的阶段任务',
                    value: '${mine.length}',
                    unit: '项',
                    sub: mine.isEmpty ? '无未完成阶段' : '${state.userName} 负责',
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: KpiTile(
                    label: '计划倒挂',
                    value: '$inverted',
                    unit: '条',
                    sub: '设计晚于装配',
                    tone: 'warn',
                  ),
                ),
              ],
            ),
            const SizedBox(height: 10),
            Row(
              children: [
                Expanded(
                  child: KpiTile(
                    label: '本机待传',
                    value: '${state.pendingOutbox}',
                    unit: '条',
                    sub: '联网后自动补传',
                    tone: 'info',
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: KpiTile(
                    label: '今日已记录',
                    value: '${state.todayRecorded}',
                    unit: '条',
                    sub: '本机操作 · 待同步',
                    tone: state.todayRecorded > 0 ? 'ok' : 'primary',
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: FilledButton.icon(
                    onPressed: () => _openScan(context),
                    icon: const Icon(Icons.qr_code_scanner, size: 18),
                    label: const Text('扫码选机台'),
                    style: FilledButton.styleFrom(
                        minimumSize: const Size.fromHeight(44)),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: OutlinedButton.icon(
                    onPressed: () => _openNew(context),
                    icon: const Icon(Icons.photo_camera_outlined, size: 18),
                    label: const Text('拍照新建'),
                    style: OutlinedButton.styleFrom(
                        minimumSize: const Size.fromHeight(44)),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 18),
            SectionTitle(
              title: '今天先处理',
              subtitle: '倒挂优先 → 逾期最久',
              trailing: Text('${open.length} 条未闭环',
                  style: TextStyle(fontSize: 11, color: p.faint)),
            ),
            const SizedBox(height: 8),
            if (open.isEmpty)
              const EmptyState(
                title: '暂无待处理问题',
                description: '所有问题都已闭环。',
                icon: Icons.check_circle_outline,
              )
            else
              for (final issue in risky.take(3))
                _TodoTile(issue: issue, state: state),
            const SizedBox(height: 10),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
              radius: 14,
              onTap: onOpenSync,
              child: Row(
                children: [
                  Icon(
                      state.syncConfigured
                          ? Icons.cloud_done_outlined
                          : Icons.cloud_off_outlined,
                      size: 17,
                      color: p.accent),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('最近同步',
                            style: TextStyle(
                                fontSize: 12,
                                fontWeight: FontWeight.w600,
                                color: p.fg)),
                        const SizedBox(height: 2),
                        Text(
                          // 同步能力已实现：按真实配对与上次同步时间显示，
                          // 不再写死"桌面端服务未启用"（那会让用户以为功能没做）。
                          '${state.syncConfigured ? (state.syncLastAt.isEmpty ? '已配对 · 尚未同步' : '已配对 · 上次同步 ${_syncTimeText(state.syncLastAt)}') : '未配对 · 到同步中心填写桌面端地址'}；'
                          '本机缓存 ${state.issues.length} 条 · ${state.pendingOutbox} 条待传',
                          style: TextStyle(fontSize: 10, height: 1.45, color: p.faint),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 6),
                  Text('查看 ›',
                      style: TextStyle(
                          fontSize: 10.5,
                          fontWeight: FontWeight.w700,
                          color: p.primary)),
                ],
              ),
            ),
          ],
        );
      },
    );
  }

  /// 上次同步时间只显示"月-日 时:分"；落库是 UTC ISO，展示转本地。
  static String _syncTimeText(String iso) {
    final time = DateTime.tryParse(iso);
    if (time == null) return iso;
    final local = time.toLocal();
    return '${local.month}-${local.day} '
        '${local.hour.toString().padLeft(2, '0')}:'
        '${local.minute.toString().padLeft(2, '0')}';
  }

  Widget _greeting(BuildContext context, SinanPalette p, int openCount) {    final now = DateTime.now();
    final greeting = now.hour < 12
        ? '早上好'
        : now.hour < 18
            ? '下午好'
            : '晚上好';
    return Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('$greeting，${state.userName}',
                  style: TextStyle(
                      fontSize: 19,
                      fontWeight: FontWeight.w700,
                      color: p.fg)),
              const SizedBox(height: 2),
              Text(
                '${state.projectCode} · ${_projectCount(state.stats)} 个分项目'
                '${_hasPendingGroup(state.stats) ? ' + 1 待确认' : ''} · '
                '${now.month} 月 ${now.day} 日 · $openCount 条未闭环',
                style: TextStyle(fontSize: 10.5, color: p.faint),
              ),
            ],
          ),
        ),
        const SinanChip(text: '离线缓存', tone: 'warn', dense: true),
      ],
    );
  }

  /// 分项目快捷条（设计稿 m-home 的 ph-projbar）：
  /// 现场班组长从待办一屏直达某个分项目，不必先切项目 tab 再点卡片。
  Widget _projectChips(BuildContext context, SinanPalette p) {
    final stats = state.stats;
    final subprojects = stats?.subprojects ?? const <SubprojectSummary>[];
    return SizedBox(
      height: 30,
      child: ListView(
        scrollDirection: Axis.horizontal,
        children: [
          _chip(
            p,
            '${state.projectCode} 总览',
            active: true,
            onTap: onOpenProjects,
          ),
          for (final s in subprojects) ...[
            const SizedBox(width: 6),
            _chip(
              p,
              s.code,
              active: false,
              onTap: () => Navigator.of(context).push(
                MaterialPageRoute(
                  builder: (_) => SubprojectScreen(state: state, code: s.code),
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _chip(SinanPalette p, String text,
      {required bool active, VoidCallback? onTap}) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 5),
        decoration: BoxDecoration(
          color: active ? p.primaryDim : p.well,
          borderRadius: BorderRadius.circular(99),
          border: Border.all(
            color: active ? p.primary.withValues(alpha: 0.5) : p.cardBorder,
          ),
        ),
        child: Text(
          text,
          style: TextStyle(
            fontSize: 11,
            fontWeight: active ? FontWeight.w700 : FontWeight.w500,
            color: active ? p.primary : p.muted,
            fontFeatures: const [FontFeature.tabularFigures()],
          ),
        ),
      ),
    );
  }

  /// 正式分项目数：`待确认` 是未归组桶，不是分项目，不能算进"3 个分项目"。
  int _projectCount(IssueStats? stats) =>
      stats?.subprojects.where((s) => s.code != '待确认').length ?? 0;

  bool _hasPendingGroup(IssueStats? stats) =>
      stats?.subprojects.any((s) => s.code == '待确认') ?? false;

  Future<void> _openScan(BuildContext context) async {
    final machine = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => MachineSelectScreen(state: state)),
    );
    if (machine == null || !context.mounted) return;
    await _pushNew(context, machine: machine);
  }

  Future<void> _openNew(BuildContext context) async {
    await _pushNew(context);
  }

  Future<void> _pushNew(BuildContext context, {String? machine}) async {
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => NewIssueScreen(state: state, initialMachine: machine),
      ),
    );
  }
}

class _TodoTile extends StatelessWidget {
  const _TodoTile({required this.issue, required this.state});

  final Issue issue;
  final AppState state;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final overdue = Rules.overdueDays(issue);
    final inverted = Rules.isPlanInverted(issue);
    IssueStage? current;
    for (final s in issue.stages) {
      if (s.state == StageState.inProgress) {
        current = s;
        break;
      }
    }
    final stageLabel = current?.label ?? '待推进';
    final owner = current == null || current.ownerText.isEmpty
        ? '未分派'
        : current.ownerText;
    final due = current?.dueRaw.trim() ?? '';

    return GlassCard(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      radius: 14,
      onTap: () => Navigator.of(context).push(
        MaterialPageRoute(
          builder: (_) => IssueDetailScreen(state: state, issueId: issue.id),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              if (inverted)
                const SinanChip(text: '倒挂', tone: 'warn', dense: true)
              else if (overdue > 0)
                SinanChip(text: '逾期 $overdue 天', tone: 'ng', dense: true)
              else
                SinanChip(text: '待推进', tone: 'info', dense: true),
              const SizedBox(width: 6),
              Text(issue.machineCode,
                  style: TextStyle(
                      fontSize: 10.5,
                      color: p.muted,
                      fontFeatures: const [FontFeature.tabularFigures()])),
              const Spacer(),
              Text(
                due.isEmpty ? '$stageLabel · $owner' : '$stageLabel $due',
                style: TextStyle(
                    fontSize: 10,
                    color: p.faint,
                    fontFeatures: const [FontFeature.tabularFigures()]),
              ),
            ],
          ),
          const SizedBox(height: 5),
          Text(issue.title,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                  fontSize: 13.5,
                  fontWeight: FontWeight.w700,
                  color: p.fg,
                  height: 1.35)),
          const SizedBox(height: 4),
          Text(
            issue.solution.isEmpty ? '尚未填写解决措施' : issue.solution,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(fontSize: 10.5, color: p.muted),
          ),
          const SizedBox(height: 5),
          Row(
            children: [
              Text(
                overdue > 0 ? '距计划日已过 $overdue 天' : '责任人 $owner',
                style: TextStyle(
                    fontSize: 9.5,
                    color: overdue > 0 ? p.ng : p.faint,
                    fontFeatures: const [FontFeature.tabularFigures()]),
              ),
              const Spacer(),
              Text('去处理 ›',
                  style: TextStyle(
                      fontSize: 10.5,
                      fontWeight: FontWeight.w700,
                      color: p.primary)),
            ],
          ),
        ],
      ),
    );
  }
}
