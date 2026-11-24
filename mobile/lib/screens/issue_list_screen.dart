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
  bool _includeClosed = true;

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  List<Issue> _filtered(List<Issue> source) {
    final keyword = _search.text.trim().toLowerCase();
    var list = source.where((issue) {
      if (!_includeClosed && issue.isClosed) return false;
      if (_filter == 'inverted' && !Rules.isPlanInverted(issue)) return false;
      if (_filter == 'overdue' && Rules.overdueDays(issue) <= 0) return false;
      if (_filter == 'photo' && issue.photoCount <= 0) return false;
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
                  _filterChip('逾期', 'overdue'),
                  _filterChip('有照片', 'photo'),
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
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
              child: Row(
                children: [
                  Text('共 ${list.length} 条',
                      style: TextStyle(fontSize: 11, color: p.faint)),
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
    return Padding(
      padding: const EdgeInsets.only(right: 6),
      child: ChoiceChip(
        label: Text(label,
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
    final current = _currentStageText(issue);
    return GlassCard(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.fromLTRB(12, 11, 12, 11),
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
              Container(
                padding:
                    const EdgeInsets.symmetric(horizontal: 6, vertical: 2.5),
                decoration: BoxDecoration(
                  color: p.well,
                  borderRadius: BorderRadius.circular(6),
                  border: Border.all(color: p.cardBorder),
                ),
                child: Text(
                  '#${issue.seqNo}',
                  style: TextStyle(
                      fontSize: 10,
                      fontWeight: FontWeight.w700,
                      color: p.muted,
                      fontFeatures: const [FontFeature.tabularFigures()]),
                ),
              ),
              const SizedBox(width: 6),
              Text(issue.machineCode,
                  style: TextStyle(
                      fontSize: 11,
                      color: p.muted,
                      fontFeatures: const [FontFeature.tabularFigures()])),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  issue.subprojectCode ?? '待确认',
                  style: TextStyle(
                      fontSize: 10,
                      color: issue.subprojectCode == null ? p.warn : p.faint),
                ),
              ),
              SinanChip(
                  text: issue.status,
                  tone: statusTone(issue.status),
                  dense: true),
            ],
          ),
          const SizedBox(height: 7),
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
          const SizedBox(height: 7),
          Row(
            children: [
              StageDots(issue: issue),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  current,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 10.5, color: p.muted),
                ),
              ),
              if (inverted)
                const SinanChip(text: '倒挂', tone: 'warn', dense: true),
              if (overdue > 0) ...[
                const SizedBox(width: 4),
                SinanChip(text: '逾期 $overdue 天', tone: 'ng', dense: true),
              ],
            ],
          ),
        ],
      ),
    );
  }

  static String _currentStageText(Issue issue) {
    for (final s in issue.stages) {
      if (s.state == StageState.inProgress) {
        final who = s.hasOwner ? s.ownerText : '未定责任人';
        final due = s.dueRaw.isNotEmpty ? '计划 ${s.dueRaw}' : '未排计划';
        return '${s.label} · $who · $due';
      }
    }
    for (final s in issue.stages) {
      if (s.state == StageState.notStarted) {
        return '${s.label} · 未开始';
      }
    }
    return issue.status;
  }
}
