import 'package:material_ui/material_ui.dart';

import '../data/issue_repository.dart';
import '../models/attachment.dart';
import '../models/issue.dart';
import '../models/rules.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import '../widgets/photo_tile.dart';
import 'photo_viewer_screen.dart';

class IssueDetailScreen extends StatefulWidget {
  const IssueDetailScreen({
    super.key,
    required this.state,
    required this.issueId,
  });

  final AppState state;
  final String issueId;

  @override
  State<IssueDetailScreen> createState() => _IssueDetailScreenState();
}

class _IssueDetailScreenState extends State<IssueDetailScreen> {
  Issue? _issue;
  List<IssueComment> _comments = const [];
  List<({IssueAttachment attachment, String? path})> _photos = const [];
  final _comment = TextEditingController();
  bool _sending = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _comment.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final issue = await widget.state.repository.byId(widget.issueId);
    final comments = await widget.state.repository.comments(widget.issueId);
    final attachments =
        await widget.state.repository.attachments(widget.issueId);
    final photos = <({IssueAttachment attachment, String? path})>[];
    for (final a in attachments) {
      photos.add((
        attachment: a,
        path: await widget.state.attachments.absolutePath(a.storedPath),
      ));
    }
    if (!mounted) return;
    setState(() {
      _issue = issue;
      _comments = comments;
      _photos = photos;
    });
  }

  /// 拍照 / 相册选图后立即挂到当前问题，并刷新总数与网格。
  Future<void> _addPhoto({required bool fromCamera}) async {
    final saved =
        await widget.state.addPhoto(widget.issueId, fromCamera: fromCamera);
    if (saved == null || !mounted) return;
    await _load();
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('照片已存本机，联网后补传。')),
    );
  }

  Future<void> _showPhotoSheet() async {
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (sheetContext) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.photo_camera_outlined),
              title: const Text('拍照'),
              subtitle: const Text('压缩到长边 1600px'),
              onTap: () {
                Navigator.of(sheetContext).pop();
                _addPhoto(fromCamera: true);
              },
            ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: const Text('从相册选择'),
              onTap: () {
                Navigator.of(sheetContext).pop();
                _addPhoto(fromCamera: false);
              },
            ),
            const SizedBox(height: 8),
          ],
        ),
      ),
    );
  }

  Future<void> _sendComment() async {
    final body = _comment.text.trim();
    if (body.isEmpty || _sending) return;
    setState(() => _sending = true);
    await widget.state.addComment(widget.issueId, body);
    _comment.clear();
    final comments = await widget.state.repository.comments(widget.issueId);
    if (!mounted) return;
    setState(() {
      _comments = comments;
      _sending = false;
    });
  }

  Future<void> _changeStatus(Issue issue, String status) async {
    final updated = await widget.state.updateStatus(issue, status);
    if (!mounted) return;
    setState(() => _issue = updated);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text('状态已更新为「$status」。')),
    );
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final issue = _issue;
    return Scaffold(
      body: AmbientBackground(
        child: SafeArea(
          child: Column(
            children: [
              _header(issue, p),
              Expanded(
                child: issue == null
                    ? const Center(child: CircularProgressIndicator())
                    : ListView(
                        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
                        children: [
                          _summaryCard(issue, p),
                          const SizedBox(height: 12),
                          SectionTitle(
                            title: '四阶段推进',
                            subtitle: '保存后自动重推问题状态',
                          ),
                          const SizedBox(height: 8),
                          for (final stage in issue.stages)
                            _StageCard(
                              key: ValueKey('${stage.code}-${stage.version}'),
                              state: widget.state,
                              issue: issue,
                              stage: stage,
                              onSaved: (updated) =>
                                  setState(() => _issue = updated),
                            ),
                          const SizedBox(height: 12),
                          _commentCard(p),
                          const SizedBox(height: 12),
                          _photoCard(issue, p),
                        ],
                      ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _header(Issue? issue, SinanPalette p) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(6, 8, 12, 6),
      child: Row(
        children: [
          IconButton(
            onPressed: () => Navigator.of(context).pop(),
            icon: Icon(Icons.arrow_back, size: 21, color: p.fg),
            tooltip: '返回',
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('问题详情',
                    style: TextStyle(
                        fontSize: 17, fontWeight: FontWeight.w700, color: p.fg)),
                Text('离线记录 · 保存后进入待同步队列',
                    style: TextStyle(fontSize: 10.5, color: p.faint)),
              ],
            ),
          ),
          if (issue != null)
            PopupMenuButton<String>(
              tooltip: '更新状态',
              onSelected: (status) => _changeStatus(issue, status),
              itemBuilder: (context) => [
                for (final status in IssueStatuses.all)
                  PopupMenuItem(
                    value: status,
                    child: Row(
                      children: [
                        Container(
                          width: 7,
                          height: 7,
                          decoration: BoxDecoration(
                            shape: BoxShape.circle,
                            color: status == issue.status ? p.primary : p.faint,
                          ),
                        ),
                        const SizedBox(width: 8),
                        Text(status),
                      ],
                    ),
                  ),
              ],
              child: SinanChip(
                text: issue.status,
                tone: statusTone(issue.status),
                icon: Icons.expand_more,
              ),
            ),
        ],
      ),
    );
  }

  Widget _summaryCard(Issue issue, SinanPalette p) {
    final overdue = Rules.overdueDays(issue);
    final inverted = Rules.isPlanInverted(issue);
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(issue.machineCode,
                  style: TextStyle(
                      fontSize: 11.5,
                      color: p.muted,
                      fontFeatures: const [FontFeature.tabularFigures()])),
              const SizedBox(width: 6),
              Text('· 序号 ${issue.seqNo}',
                  style: TextStyle(fontSize: 10.5, color: p.faint)),
              const SizedBox(width: 6),
              Expanded(
                child: Text(issue.subprojectCode ?? '待确认',
                    style: TextStyle(
                        fontSize: 10.5,
                        color: issue.subprojectCode == null ? p.warn : p.faint)),
              ),
              if (inverted) const SinanChip(text: '计划倒挂', tone: 'warn', dense: true),
              if (overdue > 0) ...[
                const SizedBox(width: 4),
                SinanChip(text: '逾期 $overdue 天', tone: 'ng', dense: true),
              ],
            ],
          ),
          const SizedBox(height: 8),
          Text(issue.title,
              style: TextStyle(
                  fontSize: 17,
                  height: 1.4,
                  fontWeight: FontWeight.w700,
                  color: p.fg)),
          if (issue.solution.trim().isNotEmpty) ...[
            const SizedBox(height: 8),
            Container(
              width: double.infinity,
              padding: const EdgeInsets.all(10),
              decoration: BoxDecoration(
                color: p.well,
                borderRadius: BorderRadius.circular(10),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('解决措施',
                      style: TextStyle(
                          fontSize: 10.5,
                          fontWeight: FontWeight.w600,
                          color: p.faint)),
                  const SizedBox(height: 3),
                  Text(issue.solution,
                      style: TextStyle(fontSize: 12.5, height: 1.5, color: p.fg)),
                ],
              ),
            ),
          ],
          const SizedBox(height: 10),
          Row(
            children: [
              _miniMetric('${issue.stages.where((s) => s.isDone).length}/4',
                  '阶段已完成', p.ok),
              _miniMetric('${issue.stages.where((s) => s.isNa).length}', '不适用', p.faint),
              // 真实附件优先：本机拍过的照片是硬数据；没有真实附件时才退回种子的
              // 演示张数（历史 Excel 无附件），并明确标注“演示”，不把占位值当真。
              _miniMetric(
                '${_photos.isEmpty ? issue.photoCount : _photos.length}',
                _photos.isEmpty && issue.photoCount > 0
                    ? '张照片（演示）'
                    : '张本机照片',
                p.accent,
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _miniMetric(String value, String label, Color color) {
    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(value,
              style: TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                  color: color,
                  fontFeatures: const [FontFeature.tabularFigures()])),
          Text(label, style: TextStyle(fontSize: 9.5, color: context.palette.faint)),
        ],
      ),
    );
  }

  Widget _commentCard(SinanPalette p) {
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionTitle(title: '现场评论', subtitle: '${_comments.length} 条'),
          const SizedBox(height: 8),
          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: TextField(
                  controller: _comment,
                  minLines: 1,
                  maxLines: 3,
                  style: const TextStyle(fontSize: 12.5),
                  decoration: const InputDecoration(
                    hintText: '记录现场口头结论、临时处理办法等',
                    isDense: true,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              FilledButton(
                onPressed: _sending ? null : _sendComment,
                style: FilledButton.styleFrom(
                  minimumSize: const Size(64, 42),
                  padding: const EdgeInsets.symmetric(horizontal: 14),
                ),
                child: _sending
                    ? const SizedBox(
                        width: 15,
                        height: 15,
                        child: CircularProgressIndicator(strokeWidth: 2))
                    : const Text('发送'),
              ),
            ],
          ),
          if (_comments.isEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 10),
              child: Text('还没有评论。',
                  style: TextStyle(fontSize: 11, color: p.faint)),
            )
          else
            for (final c in _comments) ...[
              const SizedBox(height: 10),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: p.well,
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Text(c.author,
                            style: TextStyle(
                                fontSize: 11.5,
                                fontWeight: FontWeight.w600,
                                color: p.fg)),
                        const Spacer(),
                        Text(_formatTime(c.createdAt),
                            style:
                                TextStyle(fontSize: 9.5, color: p.faint)),
                      ],
                    ),
                    const SizedBox(height: 3),
                    Text(c.body,
                        style: TextStyle(
                            fontSize: 12, height: 1.5, color: p.muted)),
                  ],
                ),
              ),
            ],
        ],
      ),
    );
  }

  Widget _photoCard(Issue issue, SinanPalette p) {
    final realCount = _photos.length;
    final placeholders = (issue.photoCount - realCount).clamp(0, 6);
    final tileWidth =
        (MediaQuery.sizeOf(context).width - 32 - 28 - 16) / 3;
    final paths = [
      for (final x in _photos)
        if (x.path != null) x.path!,
    ];

    return GlassCard(
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionTitle(
            title: '现场照片',
            subtitle: realCount == 0
                ? '本机暂无真实照片'
                : '$realCount 张 · 已存本机',
            trailing: placeholders > 0
                ? const DemoBadge(text: '含演示')
                : IconButton(
                    onPressed: _showPhotoSheet,
                    icon: Icon(Icons.add_a_photo_outlined,
                        size: 17, color: p.primary),
                    tooltip: '添加照片',
                  ),
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (var i = 0; i < _photos.length; i++)
                PhotoTile(
                  filePath: _photos[i].path,
                  width: tileWidth,
                  height: tileWidth * 0.82,
                  caption: _photos[i].attachment.timeLabel,
                  onTap: () {
                    final index = paths.indexOf(_photos[i].path ?? '');
                    Navigator.of(context).push(
                      MaterialPageRoute(
                        builder: (_) => PhotoViewerScreen(
                          paths: paths,
                          initialIndex: index < 0 ? 0 : index,
                          title: '现场照片',
                        ),
                      ),
                    );
                  },
                ),
              for (var i = 0; i < placeholders; i++)
                PhotoTile(
                  width: tileWidth,
                  height: tileWidth * 0.82,
                  demo: true,
                ),
              PhotoTile(
                add: true,
                width: tileWidth,
                height: tileWidth * 0.82,
                onTap: _showPhotoSheet,
              ),
            ],
          ),
          const SizedBox(height: 7),
          Text(
            realCount == 0
                ? '照片先存本机，联网后按队列补传；历史 Excel 无附件，统计中的张数为演示值。'
                : '照片已压缩到长边 1600px 并存在本机；原图随后台补传，导出 ZIP 由桌面端负责。',
            style: TextStyle(fontSize: 10, height: 1.45, color: p.faint),
          ),
        ],
      ),
    );
  }

  static String _formatTime(DateTime? time) {
    if (time == null) return '';
    final local = time.toLocal();
    return '${local.month}-${local.day} '
        '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
  }
}

class _StageCard extends StatefulWidget {
  const _StageCard({
    super.key,
    required this.state,
    required this.issue,
    required this.stage,
    required this.onSaved,
  });

  final AppState state;
  final Issue issue;
  final IssueStage stage;
  final ValueChanged<Issue> onSaved;

  @override
  State<_StageCard> createState() => _StageCardState();
}

class _StageCardState extends State<_StageCard> {
  late final TextEditingController _owner =
      TextEditingController(text: widget.stage.ownerText);
  late final TextEditingController _due =
      TextEditingController(text: widget.stage.dueRaw);
  late final TextEditingController _actual =
      TextEditingController(text: widget.stage.actualDate);
  late final TextEditingController _remark =
      TextEditingController(text: widget.stage.remark);
  late StageState _state = widget.stage.state;
  bool _editing = false;
  bool _saving = false;

  @override
  void dispose() {
    _owner.dispose();
    _due.dispose();
    _actual.dispose();
    _remark.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    setState(() => _saving = true);
    var actual = _actual.text.trim();
    if (actual.isEmpty && _state == StageState.done) {
      final now = DateTime.now();
      actual =
          '${now.year}-${now.month.toString().padLeft(2, '0')}-${now.day.toString().padLeft(2, '0')}';
    }
    final updated = await widget.state.saveStage(
      issue: widget.issue,
      code: widget.stage.code,
      ownerText: _owner.text,
      dueRaw: _due.text,
      state: _state,
      actualDate: actual,
      remark: _remark.text,
    );
    if (!mounted) return;
    setState(() {
      _saving = false;
      _editing = false;
      _actual.text = actual;
    });
    widget.onSaved(updated);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text('${widget.stage.label}已保存。')),
    );
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final stage = widget.stage;
    final parsed = Rules.parseRawDate(_due.text);
    return GlassCard(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.fromLTRB(14, 11, 14, 11),
      radius: 15,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                width: 22,
                height: 22,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: stage.isDone
                      ? p.okSoft
                      : stage.isNa
                          ? p.well
                          : p.primaryDim,
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  stage.isDone
                      ? Icons.check
                      : stage.isNa
                          ? Icons.remove
                          : Icons.circle_outlined,
                  size: 13,
                  color: stage.isDone
                      ? p.ok
                      : stage.isNa
                          ? p.faint
                          : p.primary,
                ),
              ),
              const SizedBox(width: 8),
              Text(stage.label,
                  style: TextStyle(
                      fontSize: 13.5,
                      fontWeight: FontWeight.w700,
                      color: p.fg)),
              const SizedBox(width: 6),
              SinanChip(
                text: stage.state.label,
                tone: stage.isDone
                    ? 'ok'
                    : stage.isNa
                        ? 'muted'
                        : stage.hasOwner || stage.hasPlan
                            ? 'primary'
                            : 'warn',
                dense: true,
              ),
              const Spacer(),
              TextButton(
                onPressed: () => setState(() => _editing = !_editing),
                child: Text(_editing ? '收起' : '编辑',
                    style: const TextStyle(fontSize: 11.5)),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Row(
            children: [
              Expanded(
                child: Text(
                  '责任人 ${stage.hasOwner ? stage.ownerText : '未安排'}',
                  style: TextStyle(fontSize: 11.5, color: p.muted),
                ),
              ),
              Text(
                '计划 ${stage.dueRaw.isEmpty ? '未排' : stage.dueRaw}',
                style: TextStyle(
                    fontSize: 11.5,
                    color: p.muted,
                    fontFeatures: const [FontFeature.tabularFigures()]),
              ),
            ],
          ),
          if (stage.actualDate.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 2),
              child: Text('实际完成 ${stage.actualDate}',
                  style: TextStyle(fontSize: 10.5, color: p.ok)),
            ),
          if (stage.remark.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 3),
              child: Text(stage.remark,
                  style: TextStyle(fontSize: 10.5, color: p.faint)),
            ),
          if (_editing) ...[
            const SizedBox(height: 10),
            const Divider(height: 1),
            const SizedBox(height: 10),
            TextField(
              controller: _owner,
              style: const TextStyle(fontSize: 12.5),
              decoration: const InputDecoration(
                labelText: '责任人（原文，多人用 / 分隔）',
                isDense: true,
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _due,
              style: const TextStyle(fontSize: 12.5),
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(
                labelText: '计划时间原文（如 11.25 / 已下图 / /）',
                isDense: true,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              _due.text.trim().isEmpty
                  ? '留空表示未排计划。'
                  : parsed != null
                      ? '解析为 ${parsed.year}-${parsed.month.toString().padLeft(2, '0')}-${parsed.day.toString().padLeft(2, '0')}（逾期按此日期算）'
                      : Rules.isNotApplicable(_due.text)
                          ? '“/”表示本阶段不适用，不参与逾期计算。'
                          : Rules.meansDrawingIssued(_due.text)
                              ? '“已下图”表示本阶段已完成，无具体日期。'
                              : '无法解析为日期，原文照存，不参与逾期计算。',
              style: TextStyle(fontSize: 10, color: p.faint),
            ),
            const SizedBox(height: 8),
            DropdownButtonFormField<StageState>(
              initialValue: _state,
              isDense: true,
              decoration: const InputDecoration(
                labelText: '阶段状态',
                isDense: true,
              ),
              items: [
                for (final s in StageState.values)
                  DropdownMenuItem(
                    value: s,
                    child: Text(s.label, style: const TextStyle(fontSize: 12.5)),
                  ),
              ],
              onChanged: (v) => setState(() => _state = v ?? _state),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _actual,
              style: const TextStyle(fontSize: 12.5),
              decoration: const InputDecoration(
                labelText: '实际完成日（YYYY-MM-DD，可空）',
                isDense: true,
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _remark,
              style: const TextStyle(fontSize: 12.5),
              maxLines: 2,
              decoration: const InputDecoration(
                labelText: '阶段备注',
                isDense: true,
              ),
            ),
            const SizedBox(height: 10),
            Row(
              children: [
                Expanded(
                  child: Text('保存只影响本阶段，问题状态由四阶段自动推导。',
                      style: TextStyle(fontSize: 9.5, color: p.faint)),
                ),
                const SizedBox(width: 8),
                FilledButton(
                  onPressed: _saving ? null : _save,
                  style: FilledButton.styleFrom(
                    minimumSize: const Size(92, 40),
                    padding: const EdgeInsets.symmetric(horizontal: 16),
                  ),
                  child: _saving
                      ? const SizedBox(
                          width: 15,
                          height: 15,
                          child: CircularProgressIndicator(strokeWidth: 2))
                      : const Text('保存本阶段'),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}
