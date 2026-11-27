import 'package:material_ui/material_ui.dart';

import '../data/attachment_store.dart';
import '../data/issue_repository.dart';
import '../models/attachment.dart';
import '../models/issue.dart';
import '../models/rules.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import '../widgets/photo_tile.dart';
import 'photo_viewer_screen.dart';

/// 问题详情（设计稿 m-detail）：站在机台边单手看。
///
/// - 四阶段压成四行，不画大时间轴，当前卡在哪一行一眼可见；
/// - 底部固定「加照片 / 推进阶段」，滚动时永远够得着；
/// - 倒挂只用一句话说清（设计晚于装配 N 天），不展开长文；
/// - 现场记录与留痕都先落本机，联网后由 outbox 补传，界面不伪造"已同步"。
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
  /// 失败必须给 SnackBar：不接异常时现场只看到"点了没反应"。
  Future<void> _addPhoto({required bool fromCamera}) async {
    try {
      final saved =
          await widget.state.addPhoto(widget.issueId, fromCamera: fromCamera);
      if (saved == null || !mounted) return;
      await _load();
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('照片已存本机，联网后补传。')),
      );
    } on AttachmentException catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(e.message)));
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text('拍照失败：$e')));
    }
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

  /// 打开阶段编辑弹窗（设计稿 dlg-m-note「推进阶段」）。
  /// 传入 code 决定先落在哪个阶段；弹窗内仍可切到其他阶段，现场不用退出来回点。
  Future<void> _openStageEditor(String code) async {
    final issue = _issue;
    if (issue == null) return;
    final updated = await showModalBottomSheet<Issue>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      backgroundColor: context.palette.panel,
      builder: (_) => _StageEditorSheet(
        state: widget.state,
        issue: issue,
        initialCode: code,
      ),
    );
    if (updated == null || !mounted) return;
    setState(() => _issue = updated);
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('阶段已保存到本机；问题状态按四阶段自动推导。')),
    );
  }

  /// 当前阶段：第一个进行中的阶段；没有则取第一个未完成/非不适用的阶段。
  IssueStage? _currentStage(Issue issue) {
    for (final s in issue.stages) {
      if (s.state == StageState.inProgress) return s;
    }
    for (final s in issue.stages) {
      if (!s.isDone && !s.isNa) return s;
    }
    return issue.stages.isEmpty ? null : issue.stages.last;
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
                        padding: const EdgeInsets.fromLTRB(16, 4, 16, 18),
                        children: [
                          _summaryCard(issue, p),
                          const SizedBox(height: 12),
                          SectionTitle(
                            title: '四阶段',
                            subtitle: '点某一行直接填写该阶段',
                          ),
                          const SizedBox(height: 6),
                          _stageList(issue, p),
                          if (Rules.isPlanInverted(issue) ||
                              Rules.overdueDays(issue) > 0) ...[
                            const SizedBox(height: 10),
                            _riskCard(issue, p),
                          ],
                          const SizedBox(height: 14),
                          _photoCard(issue, p),
                          const SizedBox(height: 14),
                          _commentCard(p),
                        ],
                      ),
              ),
              if (issue != null) _actionBar(issue, p),
            ],
          ),
        ),
      ),
    );
  }

  Widget _header(Issue? issue, SinanPalette p) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(6, 6, 12, 4),
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
                Text(issue?.id ?? '问题详情',
                    style: TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w700,
                        color: p.fg,
                        fontFeatures: const [FontFeature.tabularFigures()])),
                Text(
                  issue == null
                      ? '本机缓存'
                      : '${issue.subprojectCode ?? '待确认'} · '
                          '${issue.machineCode} · ${issue.status}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 10.5, color: p.faint),
                ),
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
                            color:
                                status == issue.status ? p.primary : p.faint,
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

  /// 摘要卡：标题 + 解决措施 + 溯源标签。
  /// 序号/源行保留原表信息，现场对 Excel 时有据可查；本机新建的标"本机新建"。
  Widget _summaryCard(Issue issue, SinanPalette p) {
    final local = issue.id.startsWith('local-');
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(14, 13, 14, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(issue.title,
              style: TextStyle(
                  fontSize: 17,
                  height: 1.4,
                  fontWeight: FontWeight.w700,
                  color: p.fg)),
          if (issue.solution.trim().isNotEmpty) ...[
            const SizedBox(height: 5),
            Text('解决措施：${issue.solution}',
                style: TextStyle(fontSize: 11.5, height: 1.55, color: p.muted)),
          ],
          const SizedBox(height: 9),
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              _tag(p, '序号 ${issue.seqNo}'),
              _tag(p, issue.sourceRow == null ? '源行 未记录' : '源行 ${issue.sourceRow}'),
              _tag(p, local ? '本机新建 · 待同步' : '本机缓存',
                  color: local ? p.warn : p.info),
            ],
          ),
        ],
      ),
    );
  }

  Widget _tag(SinanPalette p, String text, {Color? color}) {
    final c = color ?? p.faint;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2.5),
      decoration: BoxDecoration(
        color: c.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(6),
        border: Border.all(color: c.withValues(alpha: 0.28)),
      ),
      child: Text(text,
          style: TextStyle(
              fontSize: 9.5,
              color: c,
              fontFeatures: const [FontFeature.tabularFigures()])),
    );
  }

  /// 四阶段列表：设计稿 `.ph-stage` 四行，一行一个阶段。
  Widget _stageList(Issue issue, SinanPalette p) {
    return GlassCard(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 2),
      child: Column(
        children: [
          for (var i = 0; i < issue.stages.length; i++) ...[
            _stageRow(issue, issue.stages[i], p),
            if (i != issue.stages.length - 1)
              Divider(height: 1, color: p.sepLine.withValues(alpha: 0.6)),
          ],
        ],
      ),
    );
  }

  Widget _stageRow(Issue issue, IssueStage stage, SinanPalette p) {
    final current = _currentStage(issue);
    final isCurrent = current?.code == stage.code;
    final owner = stage.hasOwner ? stage.ownerText : '未填责任人';
    final due = stage.dueRaw.trim().isEmpty ? '—' : stage.dueRaw.trim();
    return InkWell(
      borderRadius: BorderRadius.circular(12),
      onTap: () => _openStageEditor(stage.code),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 9),
        child: Row(
          children: [
            _stageDot(stage, isCurrent, p),
            const SizedBox(width: 10),
            SizedBox(
              width: 62,
              child: Text(stage.label,
                  style: TextStyle(
                      fontSize: 12.5,
                      fontWeight:
                          isCurrent ? FontWeight.w700 : FontWeight.w600,
                      color: stage.isNa ? p.faint : p.fg)),
            ),
            Expanded(
              child: Text(
                owner,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                    fontSize: 11,
                    color: stage.hasOwner ? p.muted : p.faint),
              ),
            ),
            Text(due,
                style: TextStyle(
                    fontSize: 11,
                    color: stage.isDone ? p.ok : p.muted,
                    fontFeatures: const [FontFeature.tabularFigures()])),
            const SizedBox(width: 2),
            Icon(Icons.chevron_right, size: 15, color: p.faint),
          ],
        ),
      ),
    );
  }

  /// 阶段圆点：已完成 ✓、不适用 /、进行中实心、未开始空心。
  Widget _stageDot(IssueStage stage, bool current, SinanPalette p) {
    if (stage.isDone) {
      return Container(
        width: 20,
        height: 20,
        alignment: Alignment.center,
        decoration: BoxDecoration(color: p.okSoft, shape: BoxShape.circle),
        child: Icon(Icons.check, size: 12, color: p.ok),
      );
    }
    if (stage.isNa) {
      return Container(
        width: 20,
        height: 20,
        alignment: Alignment.center,
        decoration: BoxDecoration(color: p.well, shape: BoxShape.circle),
        child: Text('/', style: TextStyle(fontSize: 11, color: p.faint)),
      );
    }
    return Container(
      width: 20,
      height: 20,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: current ? p.primaryDim : Colors.transparent,
        shape: BoxShape.circle,
        border: Border.all(
            color: current ? p.primary : p.faint.withValues(alpha: 0.6),
            width: current ? 1.6 : 1.2),
      ),
      child: current
          ? Container(
              width: 7,
              height: 7,
              decoration:
                  BoxDecoration(color: p.primary, shape: BoxShape.circle),
            )
          : null,
    );
  }

  /// 风险卡：倒挂/逾期各一句话，设计稿明确"不展开长文"。
  Widget _riskCard(Issue issue, SinanPalette p) {
    final inverted = Rules.isPlanInverted(issue);
    final days = Rules.invertedDays(issue);
    final overdue = Rules.overdueDays(issue);
    final design = issue.designStage;
    final assembly = issue.assemblyStage;
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      radius: 15,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (inverted) ...[
            Row(
              children: [
                const SinanChip(text: '倒挂', tone: 'ng', dense: true),
                const SizedBox(width: 7),
                Text('设计晚于装配 $days 天',
                    style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w700,
                        color: p.fg)),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              '设计计划 ${design?.dueRaw.trim() ?? '—'}，'
              '装配计划 ${assembly?.dueRaw.trim() ?? '—'}。'
              '按现计划，装配开始时图纸还没下发。',
              style: TextStyle(fontSize: 10.5, height: 1.55, color: p.muted),
            ),
          ],
          if (inverted && overdue > 0) const SizedBox(height: 8),
          if (overdue > 0)
            Row(
              children: [
                const SinanChip(text: '逾期', tone: 'ng', dense: true),
                const SizedBox(width: 7),
                Expanded(
                  child: Text(
                    '最早计划日已过 $overdue 天；逾期只按可解析的计划日期计算。',
                    style: TextStyle(fontSize: 10.5, color: p.muted),
                  ),
                ),
              ],
            ),
        ],
      ),
    );
  }

  Widget _photoCard(Issue issue, SinanPalette p) {
    final realCount = _photos.length;
    final placeholders = (issue.photoCount - realCount).clamp(0, 6);
    final tileWidth =
        (MediaQuery.sizeOf(context).width - 32 - 24 - 16) / 3;
    final paths = [
      for (final x in _photos)
        if (x.path != null) x.path!,
    ];
    // 设计稿标题右侧是「2 / 9 张」：分子是本机真实照片，分母是问题记录的张数。
    // 分母里有演示占位，所以只要含占位就一直挂「含演示」角标，不把演示值说成实拍。
    final countText = issue.photoCount == 0
        ? '本机暂无照片'
        : '$realCount / ${issue.photoCount} 张';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SectionTitle(
          title: '现场记录',
          subtitle: countText,
          trailing: placeholders > 0 ? const DemoBadge(text: '含演示') : null,
        ),
        const SizedBox(height: 7),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (var i = 0; i < _photos.length; i++)
              PhotoTile(
                filePath: _photos[i].path,
                width: tileWidth,
                height: tileWidth * 0.86,
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
                height: tileWidth * 0.86,
                demo: true,
              ),
            PhotoTile(
              add: true,
              addLabel: '拍照',
              width: tileWidth,
              height: tileWidth * 0.86,
              onTap: _showPhotoSheet,
            ),
          ],
        ),
        const SizedBox(height: 6),
        Text(
          _photoInfoLine(realCount),
          style: TextStyle(fontSize: 9.5, height: 1.45, color: p.faint),
        ),
      ],
    );
  }

  /// 照片卡底部信息行：设计稿是「时间 · 尺寸 · 原图留本机」。
  /// 宽高没有入库，不编尺寸；用真实的最新拍摄时间与文件总大小。
  String _photoInfoLine(int realCount) {
    if (realCount == 0) {
      return '照片先存本机，联网后按队列补传；历史 Excel 无附件，统计中的张数为演示值。';
    }
    final latest = _photos
        .map((x) => x.attachment.createdAt)
        .where((s) => s.isNotEmpty)
        .toList()
      ..sort();
    final totalBytes =
        _photos.fold<int>(0, (sum, x) => sum + x.attachment.size);
    final sizeText = totalBytes >= 1024 * 1024
        ? '${(totalBytes / 1024 / 1024).toStringAsFixed(1)} MB'
        : '${(totalBytes / 1024).round()} KB';
    final time = latest.isEmpty ? '' : _formatTime(DateTime.tryParse(latest.last));
    final head = [time, sizeText].where((s) => s.isNotEmpty).join(' · ');
    return '$head · 原图留本机，导出由桌面端负责。';
  }

  /// 最近留痕：现场评论即时间线。没有 activity_log 表，不伪造系统事件。
  Widget _commentCard(SinanPalette p) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SectionTitle(
          title: '最近留痕',
          subtitle: '${_comments.length} 条 · 本机待同步',
        ),
        const SizedBox(height: 7),
        GlassCard(
          padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
          radius: 16,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (_comments.isEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 6),
                  child: Text('还没有留痕。现场口头结论、临时处理办法可以先记在这里。',
                      style: TextStyle(
                          fontSize: 10.5, height: 1.5, color: p.faint)),
                )
              else
                for (var i = 0; i < _comments.length; i++)
                  Padding(
                    padding: EdgeInsets.only(
                        bottom: i == _comments.length - 1 ? 4 : 10),
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Padding(
                          padding: const EdgeInsets.only(top: 5),
                          child: Container(
                            width: 6,
                            height: 6,
                            decoration: BoxDecoration(
                              color: i == 0 ? p.primary : p.faint,
                              shape: BoxShape.circle,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Row(
                                children: [
                                  Text(_comments[i].author,
                                      style: TextStyle(
                                          fontSize: 11,
                                          fontWeight: FontWeight.w600,
                                          color: p.fg)),
                                  const Spacer(),
                                  Text(_formatTime(_comments[i].createdAt),
                                      style: TextStyle(
                                          fontSize: 9.5,
                                          color: p.faint,
                                          fontFeatures: const [
                                            FontFeature.tabularFigures()
                                          ])),
                                ],
                              ),
                              const SizedBox(height: 2),
                              Text(_comments[i].body,
                                  style: TextStyle(
                                      fontSize: 11.5,
                                      height: 1.5,
                                      color: p.muted)),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
              const SizedBox(height: 6),
              Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Expanded(
                    child: TextField(
                      controller: _comment,
                      minLines: 1,
                      maxLines: 3,
                      style: const TextStyle(fontSize: 12),
                      decoration: const InputDecoration(
                        hintText: '记一条现场留痕',
                        isDense: true,
                      ),
                    ),
                  ),
                  const SizedBox(width: 8),
                  FilledButton(
                    onPressed: _sending ? null : _sendComment,
                    style: FilledButton.styleFrom(
                      minimumSize: const Size(62, 40),
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
            ],
          ),
        ),
      ],
    );
  }

  /// 底部固定操作条（设计稿 m-detail）：左侧扫码、右侧推进阶段。
  /// 扫码能力尚未接入，点击如实说明，不放一个假取景框。
  Widget _actionBar(Issue issue, SinanPalette p) {
    final current = _currentStage(issue);
    return Container(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 10),
      decoration: BoxDecoration(
        color: p.panel.withValues(alpha: 0.96),
        border: Border(top: BorderSide(color: p.sepLine)),
      ),
      child: Row(
        children: [
          SizedBox(
            width: 104,
            child: OutlinedButton.icon(
              onPressed: _scanNotEnabled,
              icon: const Icon(Icons.qr_code_scanner, size: 16),
              label: const Text('扫码'),
              style: OutlinedButton.styleFrom(
                  minimumSize: const Size.fromHeight(46)),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: FilledButton.icon(
              onPressed:
                  current == null ? null : () => _openStageEditor(current.code),
              icon: const Icon(Icons.check_circle_outline, size: 17),
              label: Text(
                  current == null ? '推进阶段' : '推进阶段 · ${current.label}'),
              style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(46)),
            ),
          ),
        ],
      ),
    );
  }

  void _scanNotEnabled() {
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('扫码定位问题待接入；当前可从台账搜索或机台列表进入，拍照入口在现场记录卡。'),
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

/// 推进阶段弹窗（设计稿 dlg-m-note）：
/// 顶部四阶段分段可切换，阶段结果、责任人、计划/实际日期、备注一次填完。
/// 保存只写本阶段，问题状态由 `Rules` 自动推导；先落本机再进 outbox。
class _StageEditorSheet extends StatefulWidget {
  const _StageEditorSheet({
    required this.state,
    required this.issue,
    required this.initialCode,
  });

  final AppState state;
  final Issue issue;
  final String initialCode;

  @override
  State<_StageEditorSheet> createState() => _StageEditorSheetState();
}

class _StageEditorSheetState extends State<_StageEditorSheet> {
  late String _code = widget.initialCode;
  late final TextEditingController _owner;
  late final TextEditingController _due;
  late final TextEditingController _actual;
  late final TextEditingController _remark;
  late StageState _state;
  bool _saving = false;

  @override
  void initState() {
    super.initState();
    final stage = widget.issue.stage(_code) ?? widget.issue.stages.first;
    _owner = TextEditingController(text: stage.ownerText);
    _due = TextEditingController(text: stage.dueRaw);
    _actual = TextEditingController(text: stage.actualDate);
    _remark = TextEditingController(text: stage.remark);
    _state = stage.state;
  }

  @override
  void dispose() {
    _owner.dispose();
    _due.dispose();
    _actual.dispose();
    _remark.dispose();
    super.dispose();
  }

  /// 切换阶段：把该阶段原文带回输入框，避免把 A 阶段的内容写到 B 阶段。
  void _switchTo(String code) {
    final stage = widget.issue.stage(code);
    if (stage == null) return;
    setState(() {
      _code = code;
      _owner.text = stage.ownerText;
      _due.text = stage.dueRaw;
      _actual.text = stage.actualDate;
      _remark.text = stage.remark;
      _state = stage.state;
    });
  }

  Future<void> _save() async {
    if (_saving) return;
    setState(() => _saving = true);
    var actual = _actual.text.trim();
    if (actual.isEmpty && _state == StageState.done) {
      final now = DateTime.now();
      actual = '${now.year}-'
          '${now.month.toString().padLeft(2, '0')}-'
          '${now.day.toString().padLeft(2, '0')}';
    }
    final updated = await widget.state.saveStage(
      issue: widget.issue,
      code: _code,
      ownerText: _owner.text,
      dueRaw: _due.text,
      state: _state,
      actualDate: actual,
      remark: _remark.text,
    );
    if (!mounted) return;
    Navigator.of(context).pop(updated);
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final stage = widget.issue.stage(_code);
    final parsed = Rules.parseRawDate(_due.text);
    return Padding(
      padding:
          EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                Text('推进阶段',
                    style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                        color: p.fg)),
                const SizedBox(width: 8),
                Text(widget.issue.id,
                    style: TextStyle(
                        fontSize: 11,
                        color: p.faint,
                        fontFeatures: const [FontFeature.tabularFigures()])),
                const Spacer(),
                const SinanChip(text: '离线', tone: 'warn', dense: true),
              ],
            ),
            const SizedBox(height: 12),
            // 四阶段分段（设计稿 .mseg）
            Row(
              children: [
                for (final code in StageCodes.all) ...[
                  Expanded(
                    child: GestureDetector(
                      onTap: () => _switchTo(code),
                      child: Container(
                        margin: const EdgeInsets.only(right: 6),
                        padding: const EdgeInsets.symmetric(vertical: 8),
                        alignment: Alignment.center,
                        decoration: BoxDecoration(
                          color: _code == code ? p.primaryDim : p.well,
                          borderRadius: BorderRadius.circular(10),
                          border: Border.all(
                            color: _code == code
                                ? p.primary.withValues(alpha: 0.45)
                                : p.cardBorder,
                          ),
                        ),
                        child: Text(
                          StageCodes.shortLabel(code),
                          style: TextStyle(
                            fontSize: 11.5,
                            fontWeight: _code == code
                                ? FontWeight.w700
                                : FontWeight.w500,
                            color: _code == code ? p.primary : p.muted,
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ],
            ),
            const SizedBox(height: 14),
            _label(p, '阶段结果'),
            const SizedBox(height: 6),
            Wrap(
              spacing: 6,
              children: [
                for (final s in [
                  StageState.notStarted,
                  StageState.inProgress,
                  StageState.done,
                  StageState.na,
                ])
                  ChoiceChip(
                    label: Text(s.label,
                        style: TextStyle(
                            fontSize: 11.5,
                            fontWeight: _state == s
                                ? FontWeight.w700
                                : FontWeight.w500,
                            color: _state == s ? p.primary : p.muted)),
                    selected: _state == s,
                    showCheckmark: false,
                    side: BorderSide(
                        color: _state == s
                            ? p.primary.withValues(alpha: 0.4)
                            : p.cardBorder),
                    backgroundColor: p.well,
                    selectedColor: p.primaryDim,
                    onSelected: (_) => setState(() => _state = s),
                  ),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      _label(p, '责任人'),
                      const SizedBox(height: 5),
                      TextField(
                        controller: _owner,
                        style: const TextStyle(fontSize: 12.5),
                        decoration: const InputDecoration(
                          hintText: '如 王工/冯工',
                          isDense: true,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      _label(p, '计划时间原文'),
                      const SizedBox(height: 5),
                      TextField(
                        controller: _due,
                        style: const TextStyle(fontSize: 12.5),
                        onChanged: (_) => setState(() {}),
                        decoration: const InputDecoration(
                          hintText: '11.25 / 已下图 / /',
                          isDense: true,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              _due.text.trim().isEmpty
                  ? '留空表示未排计划；"/"表示本阶段不适用，不参与逾期。'
                  : parsed != null
                      ? '解析为 ${parsed.year}-${parsed.month.toString().padLeft(2, '0')}-${parsed.day.toString().padLeft(2, '0')}，逾期按此日期算。'
                      : Rules.isNotApplicable(_due.text)
                          ? '“/”表示本阶段不适用，不参与逾期计算。'
                          : Rules.meansDrawingIssued(_due.text)
                              ? '“已下图”表示本阶段已完成，无具体日期。'
                              : '无法解析为日期，原文照存，不参与逾期计算。',
              style: TextStyle(fontSize: 9.5, height: 1.4, color: p.faint),
            ),
            const SizedBox(height: 12),
            _label(p, '实际完成'),
            const SizedBox(height: 5),
            TextField(
              controller: _actual,
              style: const TextStyle(fontSize: 12.5),
              decoration: const InputDecoration(
                hintText: 'YYYY-MM-DD，留空自动填今天（已完成时）',
                isDense: true,
              ),
            ),
            const SizedBox(height: 12),
            _label(p, '备注'),
            const SizedBox(height: 5),
            TextField(
              controller: _remark,
              style: const TextStyle(fontSize: 12.5),
              minLines: 2,
              maxLines: 3,
              decoration: const InputDecoration(
                hintText: '现场处理结果、复测数据等',
                isDense: true,
              ),
            ),
            const SizedBox(height: 10),
            Text(
              '先存本机，联网后补传；保存只影响「${stage?.label ?? _code}」，'
              '问题状态由四阶段自动推导。',
              style: TextStyle(fontSize: 9.5, height: 1.45, color: p.faint),
            ),
            const SizedBox(height: 14),
            Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    onPressed: _saving
                        ? null
                        : () => Navigator.of(context).pop(),
                    style: OutlinedButton.styleFrom(
                        minimumSize: const Size.fromHeight(46)),
                    child: const Text('取消'),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  flex: 2,
                  child: FilledButton(
                    onPressed: _saving ? null : _save,
                    style: FilledButton.styleFrom(
                        minimumSize: const Size.fromHeight(46)),
                    child: _saving
                        ? const SizedBox(
                            width: 17,
                            height: 17,
                            child: CircularProgressIndicator(
                                strokeWidth: 2, color: Colors.white))
                        : const Text('保存本阶段'),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _label(SinanPalette p, String text) {
    return Text(text,
        style: TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w600,
            color: p.muted));
  }
}
