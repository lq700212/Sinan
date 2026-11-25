import 'package:material_ui/material_ui.dart';

import '../data/issue_repository.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';

/// 同步中心（设计稿 m-sync）。
///
/// 当前版本没有配对服务，**不伪造"已连局域网 / 正在上传 1/3 / 上次同步正常"**：
/// - 上行队列逐条列出真实 outbox（机台 + 问题 + 操作 + 时间），队列不会静默丢；
/// - 下行只说明本机缓存条数，并明确"尚未与桌面端同步"；
/// - 「立即同步」与「配对」按钮如实提示服务未启用，不转假圈。
class SyncScreen extends StatefulWidget {
  const SyncScreen({super.key, required this.state});

  final AppState state;

  @override
  State<SyncScreen> createState() => _SyncScreenState();
}

class _SyncScreenState extends State<SyncScreen> {
  final _pairCode = TextEditingController();
  List<PendingOperation> _pending = const [];
  bool _loading = true;

  /// 上次渲染时的待传条数。本页被 IndexedStack 缓存不会重建，
  /// 只监听 state 变化重拉队列；否则会出现"顶部 1 条、列表说队列为空"。
  int _lastPending = 0;

  @override
  void initState() {
    super.initState();
    _lastPending = widget.state.pendingOutbox;
    widget.state.addListener(_onStateChanged);
    _load();
  }

  @override
  void dispose() {
    widget.state.removeListener(_onStateChanged);
    _pairCode.dispose();
    super.dispose();
  }

  void _onStateChanged() {
    if (!mounted) return;
    if (widget.state.pendingOutbox != _lastPending) {
      _lastPending = widget.state.pendingOutbox;
      _load();
    }
  }

  Future<void> _load() async {
    final list = await widget.state.repository.pendingOperations();
    if (!mounted) return;
    setState(() {
      _pending = list;
      _loading = false;
      _lastPending = widget.state.pendingOutbox;
    });
  }

  void _notEnabled() {
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('同步服务尚未启用；当前版本只在本机累积队列，不伪造连接状态。'),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.state,
      builder: (context, _) {
        final p = context.palette;
        final pending = widget.state.pendingOutbox;
        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 2, 16, 96),
          children: [
            _queueCard(p, pending),
            const SizedBox(height: 12),
            _downlinkCard(p),
            const SizedBox(height: 14),
            SectionTitle(title: '同步规则', subtitle: '与桌面端同一套判定'),
            const SizedBox(height: 7),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
              radius: 15,
              child: Text(
                '· 同一问题两端都改 → 标记冲突，必须人工选一边；\n'
                '· 只有一端修改 → 自动合并，不打扰；\n'
                '· 照片先传缩略图，原图在 Wi-Fi 下补传；\n'
                '· 桌面端是唯一主库，手机端不会静默覆盖。',
                style: TextStyle(fontSize: 10.5, height: 1.85, color: p.muted),
              ),
            ),
            const SizedBox(height: 10),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
              radius: 15,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      const SinanChip(text: '提醒', tone: 'ng', dense: true),
                      const SizedBox(width: 7),
                      Text('接入同步后，上传期间不要关闭 App',
                          style: TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.w700,
                              color: p.fg)),
                    ],
                  ),
                  const SizedBox(height: 4),
                  Text(
                    '队列会保留，但正在上传的照片可能产生半包，需要重新上传。',
                    style: TextStyle(fontSize: 10.5, height: 1.5, color: p.muted),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 10),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
              radius: 15,
              child: Row(
                children: [
                  Container(
                    width: 28,
                    height: 28,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: p.primaryDim,
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Icon(Icons.info_outline, size: 15, color: p.primary),
                  ),
                  const SizedBox(width: 9),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('上次完整同步',
                            style: TextStyle(
                                fontSize: 11,
                                fontWeight: FontWeight.w700,
                                color: p.fg)),
                        const SizedBox(height: 1),
                        Text('无记录 · 同步服务未启用',
                            style:
                                TextStyle(fontSize: 10, color: p.faint)),
                      ],
                    ),
                  ),
                  const SinanChip(text: '未启用', tone: 'muted', dense: true),
                ],
              ),
            ),
            const SizedBox(height: 14),
            SectionTitle(
              title: '移动端配对',
              subtitle: '桌面端生成 6 位码',
            ),
            const SizedBox(height: 7),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
              radius: 15,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: TextField(
                          controller: _pairCode,
                          keyboardType: TextInputType.number,
                          maxLength: 6,
                          style: const TextStyle(
                              fontSize: 16,
                              letterSpacing: 6,
                              fontWeight: FontWeight.w700),
                          decoration: const InputDecoration(
                            hintText: '6 位配对码',
                            counterText: '',
                            isDense: true,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      FilledButton(
                        onPressed: _notEnabled,
                        style: FilledButton.styleFrom(
                            minimumSize: const Size(76, 44)),
                        child: const Text('配对'),
                      ),
                    ],
                  ),
                  const SizedBox(height: 5),
                  Text('桌面端默认端口 5180，仅监听局域网。',
                      style: TextStyle(fontSize: 10, color: p.faint)),
                ],
              ),
            ),
          ],
        );
      },
    );
  }

  /// 上行队列：逐条列真实 outbox，不带假进度条、不带"上传中"。
  Widget _queueCard(SinanPalette p, int pending) {
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(12, 11, 12, 11),
      radius: 16,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.sync_outlined, size: 17, color: p.accent),
              const SizedBox(width: 7),
              Text('上行队列',
                  style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w700,
                      color: p.fg)),
              const Spacer(),
              SinanChip(
                text: '$pending 条',
                tone: pending > 0 ? 'warn' : 'ok',
                dense: true,
              ),
            ],
          ),
          const SizedBox(height: 5),
          Text(
            '同步服务尚未接入：队列只在本机累积，不会静默丢弃；'
            '接入后按时间顺序补传。',
            style: TextStyle(fontSize: 10.5, height: 1.5, color: p.muted),
          ),
          const SizedBox(height: 8),
          if (_loading)
            const Padding(
              padding: EdgeInsets.all(20),
              child: Center(child: CircularProgressIndicator()),
            )
          else if (_pending.isEmpty)
            const EmptyState(
              title: '队列为空',
              description: '本机的阶段、状态、评论与照片操作都已结算。',
              icon: Icons.check_circle_outline,
            )
          else ...[
            for (final op in _pending) _OperationTile(op: op, palette: p),
            if (pending > _pending.length)
              Padding(
                padding: const EdgeInsets.only(top: 2),
                child: Text('仅显示最近 ${_pending.length} 条，共 $pending 条待传。',
                    style: TextStyle(fontSize: 9.5, color: p.faint)),
              ),
          ],
          const SizedBox(height: 8),
          SizedBox(
            width: double.infinity,
            child: FilledButton.icon(
              onPressed: _notEnabled,
              icon: const Icon(Icons.sync, size: 17),
              label: Text(pending > 0 ? '立即同步 $pending 条' : '立即同步'),
              style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(44)),
            ),
          ),
        ],
      ),
    );
  }

  /// 下行：只说明本机缓存，不写"已拉取 / 已覆盖"，因为同步还没发生过。
  Widget _downlinkCard(SinanPalette p) {
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      radius: 15,
      inset: true,
      child: Row(
        children: [
          Container(
            width: 28,
            height: 28,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: p.well,
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: p.cardBorder),
            ),
            child: Icon(Icons.cloud_off_outlined, size: 15, color: p.faint),
          ),
          const SizedBox(width: 9),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('本机缓存 ${widget.state.issues.length} 条问题',
                    style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                        color: p.fg)),
                const SizedBox(height: 1),
                Text('种子导入 · 尚未与桌面端同步',
                    style: TextStyle(fontSize: 10, color: p.faint)),
              ],
            ),
          ),
          const SinanChip(text: '未同步', tone: 'muted', dense: true),
        ],
      ),
    );
  }
}

class _OperationTile extends StatelessWidget {
  const _OperationTile({required this.op, required this.palette});

  final PendingOperation op;
  final SinanPalette palette;

  @override
  Widget build(BuildContext context) {
    final p = palette;
    final time = DateTime.tryParse(op.createdAt);
    final timeText = time == null
        ? op.createdAt
        : '${time.month}-${time.day} '
            '${time.hour.toString().padLeft(2, '0')}:'
            '${time.minute.toString().padLeft(2, '0')}';
    final title = op.issueTitle.isEmpty
        ? op.entityId
        : '${op.machineCode} · ${op.issueTitle}';
    return Container(
      margin: const EdgeInsets.only(bottom: 6),
      padding: const EdgeInsets.fromLTRB(9, 8, 9, 8),
      decoration: BoxDecoration(
        color: p.well,
        borderRadius: BorderRadius.circular(11),
        border: Border.all(color: p.cardBorder),
      ),
      child: Row(
        children: [
          Icon(
            op.op == 'upload' ? Icons.photo_outlined : Icons.upload_outlined,
            size: 15,
            color: p.primary,
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w600,
                        color: p.fg,
                        fontFeatures: const [FontFeature.tabularFigures()])),
                const SizedBox(height: 1),
                Text(
                  '${op.actionLabel} · $timeText',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                      fontSize: 9.5,
                      color: p.faint,
                      fontFeatures: const [FontFeature.tabularFigures()]),
                ),
              ],
            ),
          ),
          const SizedBox(width: 6),
          const SinanChip(text: '等待', tone: 'warn', dense: true),
        ],
      ),
    );
  }
}
