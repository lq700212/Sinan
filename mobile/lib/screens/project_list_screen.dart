import 'package:material_ui/material_ui.dart';

import '../data/issue_repository.dart';
import '../models/attachment.dart';
import '../models/issue.dart';
import '../models/project.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import '../widgets/photo_tile.dart';
import 'photo_viewer_screen.dart';
import 'subproject_screen.dart';

/// 项目总览（设计稿 m-projects）：先在手机上看总项目 P7391，再点分项目看设备。
class ProjectListScreen extends StatefulWidget {
  const ProjectListScreen({super.key, required this.state, this.onOpenLedger});

  final AppState state;
  final VoidCallback? onOpenLedger;

  @override
  State<ProjectListScreen> createState() => _ProjectListScreenState();
}

class _ProjectListScreenState extends State<ProjectListScreen> {
  List<({IssueAttachment attachment, String? path})> _photos = const [];

  @override
  void initState() {
    super.initState();
    _loadPhotos();
  }

  Future<void> _loadPhotos() async {
    final list = await widget.state.repository.recentAttachments(
        limit: 6, projectId: widget.state.currentProjectId);
    final resolved = <({IssueAttachment attachment, String? path})>[];
    for (final a in list) {
      resolved.add((attachment: a, path: await widget.state.attachments.absolutePath(a.storedPath)));
    }
    if (!mounted) return;
    setState(() => _photos = resolved);
  }

  /// 切换总项目：点一行即切，切完重载本项目的现场照片。
  /// 新项目由桌面端创建并同步下发，手机端不在这里造项目。
  Future<void> _openProjectSwitch() async {
    final selected = await showModalBottomSheet<Project>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => _ProjectSwitchSheet(state: widget.state),
    );
    if (selected == null) return;
    await widget.state.switchProject(selected);
    if (!mounted) return;
    await _loadPhotos();
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.state,
      builder: (context, _) {
        final p = context.palette;
        final stats = widget.state.stats;
        if (stats == null) {
          return const Center(child: CircularProgressIndicator());
        }
        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 2, 16, 96),
          children: [
            _hero(p, stats),
            const SizedBox(height: 16),
            SectionTitle(
              title: '分项目',
              subtitle: '点卡片看设备与问题',
              trailing: Text('${stats.subprojects.length} 个分组',
                  style: TextStyle(fontSize: 11, color: p.faint)),
            ),
            const SizedBox(height: 8),
            _subprojectGrid(p, stats),
            const SizedBox(height: 6),
            if (stats.photoCount > 0)
              Text(
                '照片 ${stats.photoCount} 张为演示值，历史 Excel 无附件；'
                '真实照片以本机拍摄的为准。',
                style: TextStyle(fontSize: 9.5, height: 1.4, color: p.faint),
              ),
            const SizedBox(height: 16),
            SectionTitle(
              title: '阶段填写率',
              subtitle: '按责任人/计划/状态是否已填',
              trailing: Text('n = ${stats.total}',
                  style: TextStyle(fontSize: 11, color: p.faint)),
            ),
            const SizedBox(height: 8),
            _stageFillCard(p),
            const SizedBox(height: 16),
            SectionTitle(
              title: '最近现场照片',
              subtitle: _photos.isEmpty ? '尚未拍摄真实照片' : '${_photos.length} 张 · 本机原图',
              trailing: _photos.isEmpty && stats.photoCount > 0
                  ? const DemoBadge(text: '演示统计')
                  : null,
            ),
            const SizedBox(height: 8),
            _photoRow(p),
            const SizedBox(height: 10),
            _syncStrip(p),
          ],
        );
      },
    );
  }

  Widget _hero(SinanPalette p, IssueStats stats) {
    final project = widget.state.currentProject;
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  gradient: p.primaryGradient,
                  borderRadius: BorderRadius.circular(7),
                ),
                child: Text(widget.state.projectCode,
                    style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w800,
                        color: p.onPrimary,
                        fontFeatures: const [FontFeature.tabularFigures()])),
              ),
              const Spacer(),
              // 项目切换入口：与桌面端同一位置口径，放在状态 Chip 左侧。
              TextButton.icon(
                onPressed: _openProjectSwitch,
                icon: Icon(Icons.swap_horiz, size: 14, color: p.primary),
                label: Text('切换',
                    style: TextStyle(
                        fontSize: 10.5,
                        fontWeight: FontWeight.w600,
                        color: p.primary)),
                style: TextButton.styleFrom(
                  minimumSize: const Size(0, 26),
                  padding: const EdgeInsets.symmetric(horizontal: 8),
                  tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                ),
              ),
              const SizedBox(width: 4),
              SinanChip(
                  text: project?.statusLabel ?? '进行中',
                  tone: 'ok',
                  dense: true),
            ],
          ),
          const SizedBox(height: 8),
          Text(project?.shortName ?? '安装调试问题闭环',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                  fontSize: 15, fontWeight: FontWeight.w700, color: p.fg)),
          const SizedBox(height: 2),
          Text('现场记录 · 离线优先 · 桌面端为主库',
              style: TextStyle(fontSize: 10.5, color: p.faint)),
          const SizedBox(height: 12),
          Row(
            children: [
              _HeroMetric(
                  value: '${_formalProjectCount(stats)}', label: '分项目'),
              _HeroMetric(value: '${stats.machines}', label: '设备', unit: '台'),
              _HeroMetric(
                  value: '${stats.total}', label: '问题', unit: '条'),
              _HeroMetric(
                  value: '${stats.inverted}',
                  label: '倒挂',
                  unit: '条',
                  color: stats.inverted > 0 ? p.warn : null),
            ],
          ),
        ],
      ),
    );
  }

  /// 正式分项目数：`待确认` 是未归组桶，不是分项目；它单独以虚线卡片展示。
  int _formalProjectCount(IssueStats stats) =>
      stats.subprojects.where((s) => s.code != '待确认').length;

  Widget _subprojectGrid(SinanPalette p, IssueStats stats) {
    return GridView.count(
      crossAxisCount: 2,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      mainAxisSpacing: 8,
      crossAxisSpacing: 8,
      childAspectRatio: 2.12,
      children: [
        for (final s in stats.subprojects)
          _SubprojectTile(
            summary: s,
            projectCode: widget.state.projectCode,
            onTap: () => Navigator.of(context).push(
              MaterialPageRoute(
                builder: (_) => SubprojectScreen(
                  state: widget.state,
                  code: s.code,
                ),
              ),
            ),
          ),
      ],
    );
  }

  Widget _stageFillCard(SinanPalette p) {
    final issues = widget.state.issues;
    final total = issues.length;
    final counts = <String, int>{for (final c in StageCodes.all) c: 0};
    for (final issue in issues) {
      for (final stage in issue.stages) {
        final filled = stage.hasOwner ||
            stage.hasPlan ||
            stage.state != StageState.notStarted;
        if (filled) counts[stage.code] = (counts[stage.code] ?? 0) + 1;
      }
    }
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 12),
      radius: 16,
      child: Column(
        children: [
          for (final code in StageCodes.all) ...[
            Row(
              children: [
                SizedBox(
                  width: 56,
                  child: Text(StageCodes.label(code),
                      style: TextStyle(fontSize: 10, color: p.muted)),
                ),
                Expanded(
                  child: ThinBar(
                    fraction: total == 0 ? 0 : (counts[code] ?? 0) / total,
                    height: 5,
                    color: code == StageCodes.assembly ? p.ok : p.primary,
                  ),
                ),
                SizedBox(
                  width: 46,
                  child: Text('${counts[code] ?? 0} / $total',
                      textAlign: TextAlign.right,
                      style: TextStyle(
                          fontSize: 9.5,
                          color: p.faint,
                          fontFeatures: const [FontFeature.tabularFigures()])),
                ),
              ],
            ),
            if (code != StageCodes.all.last) const SizedBox(height: 8),
          ],
        ],
      ),
    );
  }

  Widget _photoRow(SinanPalette p) {
    if (_photos.isEmpty) {
      return GlassCard(
        padding: const EdgeInsets.fromLTRB(12, 14, 12, 14),
        radius: 14,
        child: Row(
          children: [
            Icon(Icons.photo_outlined, size: 18, color: p.faint),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                '本机还没有真实照片。历史 Excel 无附件，'
                '统计里的 ${widget.state.stats?.photoCount ?? 0} 张为演示值。',
                style: TextStyle(fontSize: 10.5, height: 1.5, color: p.muted),
              ),
            ),
          ],
        ),
      );
    }
    return SizedBox(
      height: 92,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: _photos.length,
        separatorBuilder: (_, _) => const SizedBox(width: 8),
        itemBuilder: (context, i) {
          final item = _photos[i];
          return PhotoTile(
            filePath: item.path,
            width: 122,
            height: 92,
            caption: item.attachment.timeLabel,
            onTap: () {
              final paths = [
                for (final x in _photos)
                  if (x.path != null) x.path!,
              ];
              final index = paths.indexOf(item.path ?? '');
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
          );
        },
      ),
    );
  }

  Widget _syncStrip(SinanPalette p) {
    final paired = widget.state.syncConfigured;
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      radius: 14,
      child: Row(
        children: [
          Icon(paired ? Icons.cloud_done_outlined : Icons.cloud_off_outlined,
              size: 16, color: p.accent),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              // 同步服务已随桌面端启动：这里只说本机缓存/待传与配对状态，
              // 不再写"同步服务未启用"（未配对时给出去同步中心填写地址的指引）。
              '本机缓存 ${widget.state.issues.length} 条 · '
              '${widget.state.pendingOutbox} 条待传 · '
              '${paired ? '已配对 ${widget.state.syncServerUrl}' : '未配对，到同步中心填写桌面端地址'}',
              style: TextStyle(fontSize: 10.5, color: p.muted),
            ),
          ),
        ],
      ),
    );
  }
}

class _HeroMetric extends StatelessWidget {
  const _HeroMetric({
    required this.value,
    required this.label,
    this.unit,
    this.color,
  });

  final String value;
  final String label;
  final String? unit;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.baseline,
            textBaseline: TextBaseline.alphabetic,
            children: [
              Text(value,
                  style: TextStyle(
                      fontSize: 19,
                      fontWeight: FontWeight.w700,
                      color: color ?? p.fg,
                      fontFeatures: const [FontFeature.tabularFigures()])),
              if (unit != null)
                Text(unit!,
                    style: TextStyle(fontSize: 9.5, color: p.faint)),
            ],
          ),
          Text(label, style: TextStyle(fontSize: 9.5, color: p.faint)),
        ],
      ),
    );
  }
}

class _SubprojectTile extends StatelessWidget {
  const _SubprojectTile({
    required this.summary,
    required this.projectCode,
    required this.onTap,
  });

  final SubprojectSummary summary;
  final String projectCode;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final pending = summary.code == '待确认';
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      radius: 16,
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(pending ? '$projectCode-??' : summary.code,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                        fontSize: 12.5,
                        fontWeight: FontWeight.w700,
                        color: pending ? p.warn : p.fg,
                        fontFeatures: const [FontFeature.tabularFigures()])),
              ),
              if (pending)
                const SinanChip(text: '待确认', tone: 'warn', dense: true)
              else if (summary.inverted > 0)
                SinanChip(
                    text: '倒挂 ${summary.inverted}',
                    tone: 'ng',
                    dense: true),
            ],
          ),
          const SizedBox(height: 4),
          Text(
              summary.photoCount > 0
                  ? '${summary.machineCodes.length} 台 · ${summary.issueCount} 条 · 照片 ${summary.photoCount}'
                  : '${summary.machineCodes.length} 台 · ${summary.issueCount} 条',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 10, color: p.muted)),
        ],
      ),
    );
  }
}

/// 总项目切换弹层：点一行即切换，规模数字来自 AppState 的实时缓存。
/// 手机端不在这里新建项目——桌面端是主库，新建后靠同步下发。
class _ProjectSwitchSheet extends StatelessWidget {
  const _ProjectSwitchSheet({required this.state});

  final AppState state;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final summaries = state.projectSummaries;
    return SafeArea(
      top: false,
      child: Container(
        margin: const EdgeInsets.fromLTRB(10, 0, 10, 10),
        decoration: BoxDecoration(
          color: p.panel,
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: p.cardBorder),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 14, 8, 0),
              child: Row(
                children: [
                  Icon(Icons.swap_horiz, size: 18, color: p.primary),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text('切换总项目',
                        style: TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w700,
                            color: p.fg)),
                  ),
                  IconButton(
                    onPressed: () => Navigator.of(context).pop(),
                    tooltip: '关闭',
                    icon: Icon(Icons.close, size: 18, color: p.muted),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 10),
              child: Align(
                alignment: Alignment.centerLeft,
                child: Text('点一行即切换；待办、台账、统计全部跟随新项目。',
                    style: TextStyle(fontSize: 10.5, color: p.faint)),
              ),
            ),
            if (summaries.isEmpty)
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 4, 16, 16),
                child: Row(
                  children: [
                    Icon(Icons.info_outline, size: 16, color: p.faint),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text('本机还没有项目，先与桌面端同步一次。',
                          style: TextStyle(fontSize: 10.5, color: p.muted)),
                    ),
                  ],
                ),
              )
            else
              ConstrainedBox(
                constraints: BoxConstraints(
                    maxHeight: MediaQuery.of(context).size.height * 0.5),
                child: ListView.separated(
                  shrinkWrap: true,
                  padding: const EdgeInsets.fromLTRB(10, 0, 10, 6),
                  itemCount: summaries.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 6),
                  itemBuilder: (context, i) => _ProjectSwitchRow(
                    summary: summaries[i],
                    onTap: () =>
                        Navigator.of(context).pop(summaries[i].project),
                  ),
                ),
              ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 12),
              child: Row(
                children: [
                  Icon(Icons.desktop_windows_outlined,
                      size: 14, color: p.faint),
                  const SizedBox(width: 6),
                  Expanded(
                    child: Text('新项目在桌面端创建，同步后出现在这里。',
                        style: TextStyle(fontSize: 10, color: p.faint)),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ProjectSwitchRow extends StatelessWidget {
  const _ProjectSwitchRow({required this.summary, required this.onTap});

  final ProjectSummary summary;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final project = summary.project;
    return Material(
      color: summary.isCurrent ? p.primaryDim : p.well,
      borderRadius: BorderRadius.circular(14),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(14),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(10, 10, 10, 10),
          child: Row(
            children: [
              Container(
                width: 44,
                height: 44,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  gradient: p.primaryGradient,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Text(project.code,
                    style: TextStyle(
                        fontSize: 10,
                        fontWeight: FontWeight.w800,
                        color: p.onPrimary,
                        fontFeatures: const [FontFeature.tabularFigures()])),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(project.shortName,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                            fontSize: 13.5,
                            fontWeight: FontWeight.w700,
                            color: p.fg)),
                    const SizedBox(height: 3),
                    Text(summary.metricText,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 10, color: p.muted)),
                  ],
                ),
              ),
              const SizedBox(width: 6),
              if (summary.isCurrent)
                const SinanChip(text: '当前', tone: 'ok', dense: true)
              else
                Icon(Icons.chevron_right, size: 18, color: p.faint),
            ],
          ),
        ),
      ),
    );
  }
}
