import 'package:material_ui/material_ui.dart';

import '../data/issue_repository.dart';
import '../models/issue.dart';
import '../models/rules.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import 'issue_detail_screen.dart';

/// 分项目设备问题（设计稿 m-project）：
/// 按机台逐台展开问题点，点行直接进详情；顶部 chips 横向切分项目，不退回总览。
class SubprojectScreen extends StatefulWidget {
  const SubprojectScreen({super.key, required this.state, required this.code});

  final AppState state;
  final String code;

  @override
  State<SubprojectScreen> createState() => _SubprojectScreenState();
}

class _SubprojectScreenState extends State<SubprojectScreen> {
  late String _code = widget.code;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final all = widget.state.issues;
    final codes = <String>[
      for (final s in widget.state.stats?.subprojects ?? const <SubprojectSummary>[])
        s.code,
    ];
    final issues = all.where((i) => (i.subprojectCode ?? '待确认') == _code).toList()
      ..sort((a, b) => a.machineCode.compareTo(b.machineCode));
    final machines = <String, List<Issue>>{};
    for (final issue in issues) {
      machines.putIfAbsent(issue.machineCode, () => []).add(issue);
    }
    final inverted = issues.where(Rules.isPlanInverted).length;
    final overdue = issues.where((i) => Rules.overdueDays(i) > 0).length;

    return Scaffold(
      body: AmbientBackground(
        child: SafeArea(
          child: Column(
            children: [
              _header(context, p, machines.length, issues.length, inverted),
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
                  children: [
                    SizedBox(
                      height: 30,
                      child: ListView(
                        scrollDirection: Axis.horizontal,
                        children: [
                          for (final code in codes) ...[
                            GestureDetector(
                              onTap: () => setState(() => _code = code),
                              child: Container(
                                padding: const EdgeInsets.symmetric(
                                    horizontal: 11, vertical: 5),
                                decoration: BoxDecoration(
                                  color: code == _code ? p.primaryDim : p.well,
                                  borderRadius: BorderRadius.circular(99),
                                  border: Border.all(
                                    color: code == _code
                                        ? p.primary.withValues(alpha: 0.5)
                                        : p.cardBorder,
                                  ),
                                ),
                                child: Text(
                                  code,
                                  style: TextStyle(
                                    fontSize: 11,
                                    fontWeight: code == _code
                                        ? FontWeight.w700
                                        : FontWeight.w500,
                                    color: code == _code ? p.primary : p.muted,
                                  ),
                                ),
                              ),
                            ),
                            const SizedBox(width: 6),
                          ],
                        ],
                      ),
                    ),
                    const SizedBox(height: 10),
                    GlassCard(
                      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
                      radius: 14,
                      child: Row(
                        children: [
                          Text('${machines.length} 台设备',
                              style: TextStyle(fontSize: 11, color: p.muted)),
                          const SizedBox(width: 10),
                          Text('${issues.length} 条问题',
                              style: TextStyle(fontSize: 11, color: p.muted)),
                          const Spacer(),
                          Text(
                            '照片 ${issues.fold<int>(0, (sum, i) => sum + i.photoCount)}（演示）',
                            style: TextStyle(fontSize: 9.5, color: p.faint),
                          ),
                          if (inverted > 0) ...[
                            const SizedBox(width: 6),
                            SinanChip(
                                text: '倒挂 $inverted',
                                tone: 'warn',
                                dense: true),
                          ],
                          if (overdue > 0) ...[
                            const SizedBox(width: 4),
                            SinanChip(
                                text: '逾期 $overdue', tone: 'ng', dense: true),
                          ],
                        ],
                      ),
                    ),
                    const SizedBox(height: 12),
                    if (machines.isEmpty)
                      const EmptyState(
                        title: '该分项目暂无问题',
                        description: '可以点底部「＋」新建一条现场问题。',
                      )
                    else
                      for (final entry in machines.entries)
                        _MachineCard(
                          machineCode: entry.key,
                          issues: entry.value,
                          state: widget.state,
                        ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _header(BuildContext context, SinanPalette p, int machineCount,
      int issueCount, int inverted) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(6, 6, 16, 8),
      child: Row(
        children: [
          IconButton(
            onPressed: () => Navigator.of(context).pop(),
            icon: Icon(Icons.arrow_back_ios_new, size: 18, color: p.muted),
            tooltip: '返回',
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(_code,
                    style: TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w700,
                        color: p.fg,
                        fontFeatures: const [FontFeature.tabularFigures()])),
                Text('$machineCount 台设备 · $issueCount 条问题',
                    style: TextStyle(fontSize: 10.5, color: p.faint)),
              ],
            ),
          ),
          if (inverted > 0)
            SinanChip(text: '倒挂 $inverted', tone: 'ng', dense: true),
        ],
      ),
    );
  }
}

class _MachineCard extends StatelessWidget {
  const _MachineCard({
    required this.machineCode,
    required this.issues,
    required this.state,
  });

  final String machineCode;
  final List<Issue> issues;
  final AppState state;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final inverted = issues.where(Rules.isPlanInverted).length;
    final photoCount = issues.fold<int>(0, (sum, i) => sum + i.photoCount);
    return GlassCard(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 8),
      radius: 16,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.precision_manufacturing_outlined,
                  size: 15, color: p.primary),
              const SizedBox(width: 6),
              Text(machineCode,
                  style: TextStyle(
                      fontSize: 12.5,
                      fontWeight: FontWeight.w700,
                      color: p.fg,
                      fontFeatures: const [FontFeature.tabularFigures()])),
              const SizedBox(width: 6),
              if (inverted > 0)
                SinanChip(text: '倒挂 $inverted', tone: 'warn', dense: true),
              const Spacer(),
              const SizedBox(width: 4),
              if (photoCount > 0)
                SinanChip(
                    text: '演示照片 $photoCount', tone: 'info', dense: true),
              const SizedBox(width: 6),
              Text('${issues.length} 条',
                  style: TextStyle(fontSize: 9.5, color: p.faint)),
            ],
          ),
          const SizedBox(height: 4),
          for (final issue in issues)
            InkWell(
              borderRadius: BorderRadius.circular(8),
              onTap: () => Navigator.of(context).push(
                MaterialPageRoute(
                  builder: (_) =>
                      IssueDetailScreen(state: state, issueId: issue.id),
                ),
              ),
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 6),
                child: Row(
                  children: [
                    SizedBox(
                      width: 34,
                      child: Text(
                        issue.seqNo,
                        style: TextStyle(
                            fontSize: 9.5,
                            color: p.faint,
                            fontFeatures: const [FontFeature.tabularFigures()]),
                      ),
                    ),
                    Expanded(
                      child: Text(
                        issue.title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 12, color: p.fg),
                      ),
                    ),
                    const SizedBox(width: 6),
                    _OwnerText(issue: issue, palette: p),
                    Icon(Icons.chevron_right, size: 14, color: p.faint),
                  ],
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class _OwnerText extends StatelessWidget {
  const _OwnerText({required this.issue, required this.palette});

  final Issue issue;
  final SinanPalette palette;

  @override
  Widget build(BuildContext context) {
    IssueStage? stage;
    for (final s in issue.stages) {
      if (s.state == StageState.inProgress) {
        stage = s;
        break;
      }
    }
    final text = stage?.ownerText.trim() ?? '';
    return SizedBox(
      width: 66,
      child: Text(
        text.isEmpty ? '未分派' : text,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        textAlign: TextAlign.right,
        style: TextStyle(fontSize: 9.5, color: palette.muted),
      ),
    );
  }
}
