import 'package:material_ui/material_ui.dart';

import '../models/issue.dart';
import '../models/rules.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import 'issue_detail_screen.dart';

class IssueListScreen extends StatefulWidget {
  const IssueListScreen({super.key, required this.state});

  final AppState state;

  @override
  State<IssueListScreen> createState() => _IssueListScreenState();
}

class _IssueListScreenState extends State<IssueListScreen> {
  final _search = TextEditingController();
  String _filter = 'all';
  String _projectFilter = 'all';
  bool _includeClosed = true;

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  /// 单个筛选条件是否命中（不带搜索词）。筛选 chip 的计数也走这里，
  /// 保证"倒挂 13"点进去就是 13 条，不会出现 chip 数字与列表长度对不上。
  bool _passesChip(Issue issue, String value) {
    switch (value) {
      case 'inverted':
        return Rules.isPlanInverted(issue);
      case 'text':
        return Rules.hasTextPlan(issue);
      case 'photo':
        return issue.photoCount > 0;
      case 'overdue':
        return Rules.overdueDays(issue) > 0;
      default:
        return true;
    }
  }

  List<Issue> _filtered(List<Issue> source) {
    final keyword = _search.text.trim().toLowerCase();
    var list = source.where((issue) {
      if (!_includeClosed && issue.isClosed) return false;
      if (_projectFilter != 'all' &&
          (issue.subprojectCode ?? '待确认') != _projectFilter) {
        return false;
      }
      if (!_passesChip(issue, _filter)) return false;
      if (keyword.isEmpty) return true;
      final owners = issue.stages.map((s) => s.ownerText).join(' ');
      final haystack =
          '${issue.title} ${issue.machineCode} ${issue.seqNo} ${issue.status} ${issue.solution} $owners'
              .toLowerCase();
      return haystack.contains(keyword);
    }).toList();

    list.sort((a, b) {
      final ai = Rules.isPlanInverted(a) ? 1 : 0;
      final bi = Rules.isPlanInverted(b) ? 1 : 0;
      if (ai != bi) return bi.compareTo(ai);
      final ao = Rules.overdueDays(a);
      final bo = Rules.overdueDays(b);
      if (ao != bo) return bo.compareTo(ao);
      return (a.sourceRow ?? 9999).compareTo(b.sourceRow ?? 9999);
    });
    return list;
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return ListenableBuilder(
      listenable: widget.state,
      builder: (context, _) {
        final list = _filtered(widget.state.issues);
        return Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 0),
              child: TextField(
                controller: _search,
                onChanged: (_) => setState(() {}),
                decoration: InputDecoration(
                  hintText: '搜索问题 / 机台号 / 序号 / 责任人',
                  prefixIcon: const Icon(Icons.search, size: 19),
                  suffixIcon: _search.text.isEmpty
                      ? null
                      : IconButton(
                          icon: const Icon(Icons.close, size: 18),
                          onPressed: () {
                            _search.clear();
                            setState(() {});
                          },
                        ),
                ),
              ),
            ),
            const SizedBox(height: 8),
            SizedBox(
              height: 34,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 16),
                children: [
                  _filterChip('全部', 'all'),
                  _filterChip('倒挂', 'inverted'),
                  _filterChip('文本', 'text'),
                  _filterChip('有照片', 'photo'),
                  _filterChip('逾期', 'overdue'),
                  const SizedBox(width: 4),
                  FilterChip(
                    label: Text('含已关闭', style: TextStyle(fontSize: 11.5, color: p.muted)),
                    selected: _includeClosed,
                    showCheckmark: false,
                    side: BorderSide(color: p.cardBorder),
                    backgroundColor: p.well,
                    selectedColor: p.okSoft,
                    onSelected: (v) =>
                        setState(() => _includeClosed = v),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 6),
            SizedBox(
              height: 32,
              child: ListView(
                scrollDirection: Axis.horizontal,
                padding: const EdgeInsets.symmetric(horizontal: 16),
                children: [
                  _projectChip('全部', 'all'),
                  for (final s in widget.state.stats?.subprojects ?? const [])
                    _projectChip(
                      s.code == '待确认' ? '待确认' : s.code,
                      s.code,
                      count: s.issueCount,
                    ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
              child: Row(
                children: [
                  Text('筛出 ${list.length} 条',
                      style: TextStyle(fontSize: 11, color: p.faint)),
                  Text(' / 共 ${widget.state.issues.length} 条',
                      style: TextStyle(fontSize: 10.5, color: p.faint)),
                  const Spacer(),
                  Text('倒挂优先 → 逾期最久',
                      style: TextStyle(fontSize: 10.5, color: p.faint)),
                ],
              ),
            ),
            Expanded(
              child: list.isEmpty
                  ? const EmptyState(
                      title: '没有匹配的问题',
                      description: '换个关键字，或把筛选切回“全部”。',
                      icon: Icons.search_off,
                    )
                  : ListView.builder(
                      padding: const EdgeInsets.fromLTRB(16, 2, 16, 96),
                      itemCount: list.length,
                      itemBuilder: (context, index) =>
                          _IssueRow(issue: list[index], state: widget.state),
                    ),
            ),
          ],
        );
      },
    );
  }

  Widget _filterChip(String label, String value) {
    final p = context.palette;
    final selected = _filter == value;
    final count = value == 'all'
        ? widget.state.issues.length
        : widget.state.issues.where((i) => _passesChip(i, value)).length;
    return Padding(
      padding: const EdgeInsets.only(right: 6),
      child: ChoiceChip(
        label: Text('$label $count',
            style: TextStyle(
                fontSize: 11.5,
                fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                color: selected ? p.primary : p.muted)),
        selected: selected,
        showCheckmark: false,
        side: BorderSide(
            color: selected
                ? p.primary.withValues(alpha: 0.35)
                : p.cardBorder),
        backgroundColor: p.well,
        selectedColor: p.primaryDim,
        onSelected: (_) => setState(() => _filter = value),
      ),
    );
  }

  /// 分项目筛选（设计稿 m-list 第二行）：现场按分项目看进度，不必回总览再点。
  Widget _projectChip(String label, String value, {int? count}) {
    final p = context.palette;
    final selected = _projectFilter == value;
    return Padding(
      padding: const EdgeInsets.only(right: 6),
      child: GestureDetector(
        onTap: () => setState(() => _projectFilter = value),
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
          decoration: BoxDecoration(
            color: selected ? p.primaryDim : p.well,
            borderRadius: BorderRadius.circular(99),
            border: Border.all(
              color: selected ? p.primary.withValues(alpha: 0.45) : p.cardBorder,
            ),
          ),
          child: Text(
            count == null ? label : '$label · $count',
            style: TextStyle(
              fontSize: 10.5,
              fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
              color: selected ? p.primary : p.muted,
              fontFeatures: const [FontFeature.tabularFigures()],
            ),
          ),
        ),
      ),
    );
  }
}

class _IssueRow extends StatelessWidget {
  const _IssueRow({required this.issue, required this.state});

  final Issue issue;
  final AppState state;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final overdue = Rules.overdueDays(issue);
    final inverted = Rules.isPlanInverted(issue);
    final textPlan = Rules.hasTextPlan(issue);
    return GlassCard(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      radius: 15,
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
              else if (textPlan)
                const SinanChip(text: '文本', tone: 'info', dense: true)
              else
                Container(
                  width: 14,
                  alignment: Alignment.center,
                  child: Text('—',
                      style: TextStyle(fontSize: 10, color: p.faint)),
                ),
              const SizedBox(width: 6),
              Text(issue.machineCode,
                  style: TextStyle(
                      fontSize: 10.5,
                      color: p.muted,
                      fontFeatures: const [FontFeature.tabularFigures()])),
              if (overdue > 0) ...[
                const SizedBox(width: 5),
                Text('逾期 $overdue 天',
                    style: TextStyle(fontSize: 9.5, color: p.ng)),
              ],
              const Spacer(),
              SinanChip(
                  text: issue.status,
                  tone: statusTone(issue.status),
                  dense: true),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            issue.title,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
                fontSize: 13.5,
                height: 1.35,
                fontWeight: FontWeight.w600,
                color: p.fg),
          ),
          const SizedBox(height: 4),
          Text(
            issue.solution.isEmpty ? '尚未填写解决措施' : issue.solution,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(fontSize: 10, color: p.faint),
          ),
          const SizedBox(height: 7),
          Row(
            children: [
              Expanded(
                  child: _StageInfo(label: '设计', stage: issue.designStage)),
              const SizedBox(width: 8),
              Expanded(
                  child: _StageInfo(label: '装配', stage: issue.assemblyStage)),
            ],
          ),
        ],
      ),
    );
  }
}

/// 台账行底部的一阶段摘要：设计 何工 11-05 / 装配 王工/冯工 11-01。
/// 原文照抄（含 `/` 多人），不把"已下图"改写成日期。
class _StageInfo extends StatelessWidget {
  const _StageInfo({required this.label, required this.stage});

  final String label;
  final IssueStage? stage;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final s = stage;
    final owner = s == null || s.ownerText.trim().isEmpty
        ? '未分派'
        : s.ownerText.trim();
    final due = s == null || s.dueRaw.trim().isEmpty ? '未排' : s.dueRaw.trim();
    return Row(
      children: [
        Text('$label ',
            style: TextStyle(fontSize: 9.5, color: p.faint)),
        Flexible(
          child: Text(
            owner,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(fontSize: 10, color: p.muted),
          ),
        ),
        const SizedBox(width: 4),
        Text(due,
            style: TextStyle(
                fontSize: 9.5,
                color: p.faint,
                fontFeatures: const [FontFeature.tabularFigures()])),
      ],
    );
  }
}
