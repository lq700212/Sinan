import 'package:material_ui/material_ui.dart';

import '../data/issue_repository.dart';
import '../models/issue.dart';
import '../models/rules.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import 'issue_detail_screen.dart';

class OverviewScreen extends StatelessWidget {
  const OverviewScreen({
    super.key,
    required this.state,
    required this.onOpenTab,
  });

  final AppState state;
  final ValueChanged<int> onOpenTab;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) {
        final stats = state.stats;
        if (stats == null) {
          return const Center(child: CircularProgressIndicator());
        }
        final risky = state.issues.where((i) => !i.isClosed).toList()
          ..sort((a, b) {
            final ai = Rules.isPlanInverted(a) ? 1 : 0;
            final bi = Rules.isPlanInverted(b) ? 1 : 0;
            if (ai != bi) return bi.compareTo(ai);
            final ao = Rules.overdueDays(a);
            final bo = Rules.overdueDays(b);
            if (ao != bo) return bo.compareTo(ao);
            return (a.sourceRow ?? 9999).compareTo(b.sourceRow ?? 9999);
          });

        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 96),
          children: [
            _kpiGrid(context, stats),
            const SizedBox(height: 16),
            SectionTitle(
              title: '分项目',
              subtitle: '点卡片进入问题台账',
              trailing: Text('${stats.subprojects.length} 个分组',
                  style: TextStyle(fontSize: 11, color: context.palette.faint)),
            ),
            const SizedBox(height: 8),
            for (final s in stats.subprojects)
              _SubprojectCard(
                summary: s,
                total: stats.total,
                onTap: () => onOpenTab(1),
              ),
            const SizedBox(height: 16),
            SectionTitle(
              title: '风险清单',
              subtitle: '倒挂优先 → 逾期最久',
              trailing: Text('${risky.length} 条未闭环',
                  style: TextStyle(fontSize: 11, color: context.palette.faint)),
            ),
            const SizedBox(height: 8),
            if (risky.isEmpty)
              const EmptyState(
                title: '暂无风险项',
                description: '所有问题都已闭环，或还没有数据。',
                icon: Icons.check_circle_outline,
              )
            else
              for (final issue in risky.take(5)) _RiskTile(issue: issue, state: state),
          ],
        );
      },
    );
  }

  Widget _kpiGrid(BuildContext context, IssueStats stats) {
    final p = context.palette;
    final closedRate =
        stats.total == 0 ? 0.0 : stats.closed * 100 / stats.total;
    final invertedRate =
        stats.total == 0 ? 0.0 : stats.inverted * 100 / stats.total;
    return Column(
      children: [
        Row(
          children: [
            Expanded(
              child: KpiTile(
                label: '在册问题',
                value: '${stats.total}',
                unit: '条',
                sub: '${stats.machines} 台机台',
                onTap: () => onOpenTab(1),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: KpiTile(
                label: '已闭环',
                value: '${stats.closed}',
                unit: '条',
                sub: '${closedRate.toStringAsFixed(1)}%',
                tone: 'ok',
                onTap: () => onOpenTab(1),
              ),
            ),
          ],
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            Expanded(
              child: KpiTile(
                label: '计划倒挂',
                value: '${stats.inverted}',
                unit: '条',
                sub: '占 ${invertedRate.toStringAsFixed(1)}%',
                tone: 'warn',
                onTap: () => onOpenTab(1),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: KpiTile(
                label: '逾期未闭环',
                value: '${stats.overdue}',
                unit: '条',
                sub: '只读语义日期',
                tone: 'ng',
                onTap: () => onOpenTab(1),
              ),
            ),
          ],
        ),
        const SizedBox(height: 10),
        GlassCard(
          padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
          radius: 16,
          child: Row(
            children: [
              Icon(Icons.photo_camera_outlined, size: 18, color: p.accent),
              const SizedBox(width: 8),
              Text('现场照片', style: TextStyle(fontSize: 12, color: p.muted)),
              const SizedBox(width: 8),
              Text('${stats.photoCount}',
                  style: TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.w700,
                      color: p.accent,
                      fontFeatures: const [FontFeature.tabularFigures()])),
              Text(' 张 / ${stats.photoIssues} 条问题',
                  style: TextStyle(fontSize: 11, color: p.muted)),
              const Spacer(),
              const DemoBadge(),
            ],
          ),
        ),
      ],
    );
  }
}

class _SubprojectCard extends StatelessWidget {
  const _SubprojectCard({
    required this.summary,
    required this.total,
    required this.onTap,
  });

  final SubprojectSummary summary;
  final int total;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final isPending = summary.code == '待确认';
    final fraction = total == 0 ? 0.0 : summary.issueCount / total;
    return GlassCard(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
      radius: 16,
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(summary.code,
                  style: TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w700,
                      color: isPending ? p.warn : p.fg,
                      fontFeatures: const [FontFeature.tabularFigures()])),
              const SizedBox(width: 8),
              SinanChip(
                text: isPending ? '待归组' : '进行中',
                tone: isPending ? 'warn' : 'ok',
                dense: true,
              ),
              const Spacer(),
              Icon(Icons.chevron_right, size: 18, color: p.faint),
            ],
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              _Metric(value: '${summary.machineCodes.length}', label: '台机台'),
              _Metric(value: '${summary.issueCount}', label: '条问题'),
              _Metric(value: '${summary.owners.length}', label: '位责任人'),
              _Metric(value: '${summary.photoCount}', label: '张照片', demo: true),
            ],
          ),
          const SizedBox(height: 9),
          ThinBar(fraction: fraction, height: 4),
          const SizedBox(height: 4),
          Text(
            '占全部问题 ${(fraction * 100).toStringAsFixed(1)}%',
            style: TextStyle(fontSize: 10, color: p.faint),
          ),
        ],
      ),
    );
  }
}

class _Metric extends StatelessWidget {
  const _Metric({required this.value, required this.label, this.demo = false});

  final String value;
  final String label;
  final bool demo;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Expanded(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.baseline,
        textBaseline: TextBaseline.alphabetic,
        children: [
          Text(value,
              style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w700,
                  color: p.fg,
                  fontFeatures: const [FontFeature.tabularFigures()])),
          const SizedBox(width: 3),
          Text(label, style: TextStyle(fontSize: 10, color: p.faint)),
          if (demo) const DemoBadge(text: '演示'),
        ],
      ),
    );
  }
}

class _RiskTile extends StatelessWidget {
  const _RiskTile({required this.issue, required this.state});

  final Issue issue;
  final AppState state;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final overdue = Rules.overdueDays(issue);
    final inverted = Rules.isPlanInverted(issue);
    return GlassCard(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      radius: 14,
      onTap: () => Navigator.of(context).push(
        MaterialPageRoute(
          builder: (_) => IssueDetailScreen(state: state, issueId: issue.id),
        ),
      ),
      child: Row(
        children: [
          Container(
            width: 34,
            height: 34,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: p.well,
              borderRadius: BorderRadius.circular(10),
              border: Border.all(color: p.cardBorder),
            ),
            child: Text(
              '#${issue.seqNo}',
              style: TextStyle(
                  fontSize: 10.5,
                  fontWeight: FontWeight.w700,
                  color: p.muted,
                  fontFeatures: const [FontFeature.tabularFigures()]),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(issue.title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                        color: p.fg)),
                const SizedBox(height: 3),
                Row(
                  children: [
                    Text(issue.machineCode,
                        style: TextStyle(
                            fontSize: 10.5,
                            color: p.muted,
                            fontFeatures: const [FontFeature.tabularFigures()])),
                    const SizedBox(width: 6),
                    StageDots(issue: issue),
                    const Spacer(),
                    if (inverted)
                      const SinanChip(text: '倒挂', tone: 'warn', dense: true),
                    if (overdue > 0) ...[
                      const SizedBox(width: 4),
                      SinanChip(
                          text: '逾期 $overdue 天', tone: 'ng', dense: true),
                    ],
                  ],
                ),
              ],
            ),
          ),
          const SizedBox(width: 4),
          Icon(Icons.chevron_right, size: 17, color: p.faint),
        ],
      ),
    );
  }
}
