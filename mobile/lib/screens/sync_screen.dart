import 'package:material_ui/material_ui.dart';

import '../data/issue_repository.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';

/// 同步中心（设计稿 m-sync）。
///
/// 当前版本没有配对服务，**不伪造"已连局域网 / 正在上传 1/3"**：
/// 上行队列列出真实 outbox 记录，按钮点了如实提示"服务未启用"。
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

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.state,
      builder: (context, _) {
        final p = context.palette;
        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 2, 16, 96),
          children: [
            GlassCard(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(Icons.sync_outlined, size: 18, color: p.accent),
                      const SizedBox(width: 8),
                      Text('本机待传',
                          style: TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.w700,
                              color: p.fg)),
                      const Spacer(),
                      SinanChip(
                        text: '${widget.state.pendingOutbox} 条',
                        tone: widget.state.pendingOutbox > 0 ? 'warn' : 'ok',
                        dense: true,
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Text(
                    '每条阶段/状态/评论/照片都会进入 outbox。'
                    '当前同步服务尚未接入，队列只在本机累积，不会静默丢弃。',
                    style:
                        TextStyle(fontSize: 10.5, height: 1.5, color: p.muted),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 10),
            if (_loading)
              const Padding(
                padding: EdgeInsets.all(24),
                child: Center(child: CircularProgressIndicator()),
              )
            else if (_pending.isEmpty)
              const EmptyState(
                title: '队列为空',
                description: '所有本机操作都已结算。',
                icon: Icons.check_circle_outline,
              )
            else ...[
              SectionTitle(
                title: '上行队列',
                subtitle: '按时间倒序',
                trailing: Text('${_pending.length} 条',
                    style: TextStyle(fontSize: 11, color: p.faint)),
              ),
              const SizedBox(height: 8),
              for (final op in _pending) _OperationTile(op: op, palette: p),
            ],
            const SizedBox(height: 16),
            SectionTitle(title: '移动端配对', subtitle: '桌面端生成 6 位码'),
            const SizedBox(height: 8),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              radius: 16,
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
                        onPressed: () {
                          ScaffoldMessenger.of(context).showSnackBar(
                            const SnackBar(
                              content: Text('配对服务尚未启用；当前版本不伪造连接状态。'),
                            ),
                          );
                        },
                        style: FilledButton.styleFrom(
                            minimumSize: const Size(76, 44)),
                        child: const Text('配对'),
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Text('桌面端默认端口 5180，仅监听局域网。',
                      style: TextStyle(fontSize: 10, color: p.faint)),
                ],
              ),
            ),
            const SizedBox(height: 12),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              radius: 16,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('同步规则',
                      style: TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                          color: p.fg)),
                  const SizedBox(height: 6),
                  Text(
                    '· 同一问题两端都改 → 标记冲突，必须人工选一边；\n'
                    '· 只有一端修改 → 自动合并，不打扰；\n'
                    '· 照片先传缩略图，原图在 Wi-Fi 下补传；\n'
                    '· 桌面端是唯一主库，手机端不会静默覆盖。',
                    style:
                        TextStyle(fontSize: 10.5, height: 1.75, color: p.muted),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              radius: 16,
              child: Row(
                children: [
                  Icon(Icons.cloud_off_outlined, size: 17, color: p.faint),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      '本机缓存 ${widget.state.issues.length} 条问题 · '
                      '上次完整同步：无记录',
                      style: TextStyle(fontSize: 10.5, color: p.faint),
                    ),
                  ),
                ],
              ),
            ),
          ],
        );
      },
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
    return GlassCard(
      margin: const EdgeInsets.only(bottom: 6),
      padding: const EdgeInsets.fromLTRB(11, 9, 11, 9),
      radius: 12,
      inset: true,
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
                Text(op.actionLabel,
                    style: TextStyle(
                        fontSize: 11.5,
                        fontWeight: FontWeight.w600,
                        color: p.fg)),
                const SizedBox(height: 1),
                Text(
                  '${op.entityId} · $timeText',
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
          const SinanChip(text: '等待', tone: 'warn', dense: true),
        ],
      ),
    );
  }
}
