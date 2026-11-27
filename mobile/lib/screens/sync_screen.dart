import 'package:material_ui/material_ui.dart';

import '../data/issue_repository.dart';
import '../models/pair_payload.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';
import '../widgets/surface.dart';
import 'pair_scan_screen.dart';

/// 同步中心（设计稿 m-sync）。
///
/// 真实能力（不是占位）：
/// - 配对：填桌面端地址 + 桌面端生成的 6 位一次性码，换设备令牌；
/// - 立即同步：按 outbox 顺序上行（业务操作 → 照片），再按游标下行问题快照；
/// - 冲突：桌面端也改过同一实体时**不静默覆盖**，逐条让用户选"保留手机 / 保留桌面"；
/// - 队列与上次同步时间全部来自本机数据库，不写演示值。
class SyncScreen extends StatefulWidget {
  const SyncScreen({super.key, required this.state});

  final AppState state;

  @override
  State<SyncScreen> createState() => _SyncScreenState();
}

class _SyncScreenState extends State<SyncScreen> {
  final _serverUrl = TextEditingController();
  final _pairCode = TextEditingController();
  List<PendingOperation> _pending = const [];
  bool _loading = true;

  /// 上次渲染时的待传条数。本页被 IndexedStack 缓存不会重建，
  /// 只监听 state 变化重拉队列；否则会出现"顶部 1 条、列表说队列为空"。
  int _lastPending = 0;
  int _lastConflicts = 0;

  @override
  void initState() {
    super.initState();
    _lastPending = widget.state.pendingOutbox;
    _lastConflicts = widget.state.syncConflictCount;
    _serverUrl.text = widget.state.syncServerUrl;
    widget.state.addListener(_onStateChanged);
    _load();
  }

  @override
  void dispose() {
    widget.state.removeListener(_onStateChanged);
    _serverUrl.dispose();
    _pairCode.dispose();
    super.dispose();
  }

  void _onStateChanged() {
    if (!mounted) return;
    if (widget.state.pendingOutbox != _lastPending ||
        widget.state.syncConflictCount != _lastConflicts) {
      _lastPending = widget.state.pendingOutbox;
      _lastConflicts = widget.state.syncConflictCount;
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

  Future<void> _pair() async {
    final ok = await widget.state.pair(
      serverUrl: _serverUrl.text,
      code: _pairCode.text,
    );
    if (!mounted) return;
    if (ok) {
      _pairCode.clear();
      _serverUrl.text = widget.state.syncServerUrl;
    }
    _showStatus();
  }

  /// 扫码配对：二维码里已带桌面端地址与 6 位码，扫到后直接走同一条
  /// [AppState.pair] 链路（ping 校验 → /api/pair 换 token → 立即同步），
  /// 不另写一套配对逻辑；扫码只是把"手输两个字段"换成"扫一次"。
  Future<void> _scanPair() async {
    final payload = await Navigator.of(context).push<PairQrPayload>(
      MaterialPageRoute(builder: (_) => const PairScanScreen()),
    );
    if (payload == null || !mounted) return;

    _serverUrl.text = payload.serverUrl;
    _pairCode.text = payload.code;
    await _pair();
  }

  Future<void> _syncNow() async {
    await widget.state.syncNow();
    if (!mounted) return;
    _showStatus();
    await _load();
  }

  Future<void> _unpair() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('解除配对'),
        content: const Text('解除后需要重新配对才能同步；本机待传队列会保留。'),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('取消')),
          FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('解除')),
        ],
      ),
    );
    if (confirmed != true) return;
    await widget.state.resetPairing();
    if (!mounted) return;
    _serverUrl.text = '';
    _showStatus();
  }

  Future<void> _resolve(SyncConflict conflict, {required bool keepMobile}) async {
    await widget.state
        .resolveConflict(conflict, keepMobile: keepMobile);
    if (!mounted) return;
    _showStatus();
    await _load();
  }

  void _showStatus() {
    final text = widget.state.syncStatus;
    if (text.isEmpty) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(text)));
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.state,
      builder: (context, _) {
        final p = context.palette;
        final state = widget.state;
        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 2, 16, 96),
          children: [
            _connectionCard(p, state),
            if (state.syncConflicts.isNotEmpty) ...[
              const SizedBox(height: 12),
              _conflictCard(p, state),
            ],
            const SizedBox(height: 12),
            _queueCard(p, state),
            const SizedBox(height: 12),
            _downlinkCard(p, state),
            const SizedBox(height: 14),
            SectionTitle(title: '同步规则', subtitle: '与桌面端同一套判定'),
            const SizedBox(height: 7),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
              radius: 15,
              child: Text(
                '· 同一问题两端都改 → 标记冲突，必须人工选一边；\n'
                '· 只有一端修改 → 自动合并，不打扰；\n'
                '· 照片按队列逐张上传，失败留在队列下次重试；\n'
                '· 桌面端是唯一主库，手机端不会静默覆盖。',
                style: TextStyle(fontSize: 10.5, height: 1.85, color: p.muted),
              ),
            ),
          ],
        );
      },
    );
  }

  /// 连接与配对：已配对时只留"立即同步 / 解除配对"，不再让用户误以为要重填。
  Widget _connectionCard(SinanPalette p, AppState state) {
    final paired = state.syncConfigured;
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(12, 11, 12, 11),
      radius: 16,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(paired ? Icons.lan_outlined : Icons.link_off,
                  size: 17, color: paired ? p.ok : p.faint),
              const SizedBox(width: 7),
              Text('局域网连接',
                  style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w700,
                      color: p.fg)),
              const Spacer(),
              SinanChip(
                text: paired ? '已配对' : '未配对',
                tone: paired ? 'ok' : 'muted',
                dense: true,
              ),
            ],
          ),
          const SizedBox(height: 5),
          Text(
            paired
                ? '${state.syncDeviceName} · ${state.syncServerUrl}'
                : '点桌面端顶栏的手机图标（或「系统设置 → 移动端配对」）弹出二维码，'
                    '手机与桌面端连同一局域网后扫码即可。',
            style: TextStyle(fontSize: 10.5, height: 1.5, color: p.muted),
          ),
          const SizedBox(height: 9),
          if (!paired) ...[
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: state.syncBusy ? null : _scanPair,
                icon: const Icon(Icons.qr_code_scanner, size: 18),
                label: const Text('扫描二维码配对'),
                style: FilledButton.styleFrom(
                    minimumSize: const Size.fromHeight(44)),
              ),
            ),
            const SizedBox(height: 10),
            Row(
              children: [
                Expanded(child: Divider(color: p.cardBorder)),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 8),
                  child: Text('或手动填写',
                      style: TextStyle(fontSize: 10, color: p.faint)),
                ),
                Expanded(child: Divider(color: p.cardBorder)),
              ],
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _serverUrl,
              keyboardType: TextInputType.url,
              style: const TextStyle(fontSize: 13),
              decoration: const InputDecoration(
                labelText: '桌面端地址',
                hintText: '例如 192.168.1.26:5180',
                isDense: true,
                prefixIcon: Icon(Icons.dns_outlined, size: 17),
              ),
            ),
            const SizedBox(height: 8),
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
                      labelText: '配对码',
                      hintText: '6 位数字',
                      counterText: '',
                      isDense: true,
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                FilledButton(
                  onPressed: state.syncBusy ? null : _pair,
                  style: FilledButton.styleFrom(
                      minimumSize: const Size(82, 44)),
                  child: state.syncBusy
                      ? const SizedBox(
                          width: 16,
                          height: 16,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text('配对'),
                ),
              ],
            ),
            const SizedBox(height: 2),
            Text('桌面端默认端口 5180，仅监听局域网。',
                style: TextStyle(fontSize: 10, color: p.faint)),
          ] else ...[
            Row(
              children: [
                Expanded(
                  child: FilledButton.icon(
                    onPressed: state.syncBusy ? null : _syncNow,
                    icon: state.syncBusy
                        ? const SizedBox(
                            width: 15,
                            height: 15,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.sync, size: 17),
                    label: Text(state.syncBusy ? '正在同步…' : '立即同步'),
                    style: FilledButton.styleFrom(
                        minimumSize: const Size.fromHeight(44)),
                  ),
                ),
                const SizedBox(width: 8),
                OutlinedButton(
                  onPressed: state.syncBusy ? null : _unpair,
                  style: OutlinedButton.styleFrom(
                      minimumSize: const Size(88, 44)),
                  child: const Text('解除配对'),
                ),
              ],
            ),
            const SizedBox(height: 7),
            Row(
              children: [
                Icon(
                    state.syncStatus.isEmpty
                        ? Icons.info_outline
                        : Icons.check_circle_outline,
                    size: 13,
                    color: state.syncStatus.isEmpty ? p.faint : p.accent),
                const SizedBox(width: 5),
                Expanded(
                  child: Text(
                    state.syncStatus.isEmpty
                        ? '尚未同步 · 上次同步 ${_timeText(state.syncLastAt)}'
                        : state.syncStatus,
                    style: TextStyle(fontSize: 10, color: p.muted),
                  ),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }

  /// 冲突二选一：每条都来自服务端 conflict 结果或 pull 撞上未结算队列。
  Widget _conflictCard(SinanPalette p, AppState state) {
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(12, 11, 12, 11),
      radius: 16,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.warning_amber_rounded, size: 17, color: p.warn),
              const SizedBox(width: 7),
              Text('待处理冲突',
                  style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w700,
                      color: p.fg)),
              const Spacer(),
              SinanChip(
                text: '${state.syncConflictCount} 条',
                tone: 'warn',
                dense: true,
              ),
            ],
          ),
          const SizedBox(height: 6),
          for (final conflict in state.syncConflicts) ...[
            SinanSurface(
              margin: const EdgeInsets.only(bottom: 7),
              radius: 11,
              color: p.warnSoft,
              border: Border.all(color: p.cardBorder),
              shadow: SinanShadow.none,
              padding: const EdgeInsets.fromLTRB(9, 8, 9, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${conflict.label} · ${conflict.entityId}',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                        color: p.fg),
                  ),
                  const SizedBox(height: 2),
                  Text(conflict.message,
                      style: TextStyle(
                          fontSize: 10, height: 1.45, color: p.muted)),
                  const SizedBox(height: 7),
                  Row(
                    children: [
                      Expanded(
                        child: OutlinedButton(
                          onPressed: state.syncBusy
                              ? null
                              : () =>
                                  _resolve(conflict, keepMobile: true),
                          style: OutlinedButton.styleFrom(
                              minimumSize: const Size.fromHeight(34),
                              padding: const EdgeInsets.symmetric(horizontal: 6)),
                          child: const Text('保留手机',
                              style: TextStyle(fontSize: 11)),
                        ),
                      ),
                      const SizedBox(width: 7),
                      Expanded(
                        child: FilledButton(
                          onPressed: state.syncBusy
                              ? null
                              : () =>
                                  _resolve(conflict, keepMobile: false),
                          style: FilledButton.styleFrom(
                              minimumSize: const Size.fromHeight(34),
                              padding: const EdgeInsets.symmetric(horizontal: 6)),
                          child: const Text('保留桌面',
                              style: TextStyle(fontSize: 11)),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }

  /// 上行队列：逐条列真实 outbox，不带假进度条、不带"上传中"。
  Widget _queueCard(SinanPalette p, AppState state) {
    final pending = state.pendingOutbox;
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
            pending == 0
                ? '本机的阶段、状态、评论与照片操作都已结算。'
                : '按记录顺序补传；失败或冲突会留在队列/冲突区，不会静默丢弃。',
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
          if (pending > 0) ...[
            const SizedBox(height: 8),
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: state.syncBusy || !state.syncConfigured
                    ? null
                    : _syncNow,
                icon: const Icon(Icons.sync, size: 17),
                label: Text(state.syncConfigured
                    ? '立即同步 $pending 条'
                    : '先完成配对再同步'),
                style: FilledButton.styleFrom(
                    minimumSize: const Size.fromHeight(44)),
              ),
            ),
          ],
        ],
      ),
    );
  }

  /// 下行：本机缓存条数与上次同步时间，全部取真实状态。
  Widget _downlinkCard(SinanPalette p, AppState state) {
    final paired = state.syncConfigured;
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
            child: Icon(
                paired ? Icons.cloud_done_outlined : Icons.cloud_off_outlined,
                size: 15,
                color: paired ? p.accent : p.faint),
          ),
          const SizedBox(width: 9),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('本机缓存 ${state.issues.length} 条问题',
                    style: TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                        color: p.fg)),
                const SizedBox(height: 1),
                Text(
                    paired
                        ? '上次同步 ${_timeText(state.syncLastAt)}'
                        : '尚未与桌面端同步',
                    style: TextStyle(fontSize: 10, color: p.faint)),
              ],
            ),
          ),
          SinanChip(
            text: paired ? '已配对' : '未同步',
            tone: paired ? 'ok' : 'muted',
            dense: true,
          ),
        ],
      ),
    );
  }

  static String _timeText(String iso) {
    if (iso.isEmpty) return '无记录';
    final time = DateTime.tryParse(iso);
    if (time == null) return iso;
    final local = time.toLocal();
    return '${local.month}-${local.day} '
        '${local.hour.toString().padLeft(2, '0')}:'
        '${local.minute.toString().padLeft(2, '0')}';
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
    return SinanSurface(
      margin: const EdgeInsets.only(bottom: 6),
      radius: 11,
      color: p.well,
      border: Border.all(color: p.cardBorder),
      shadow: SinanShadow.none,
      padding: const EdgeInsets.fromLTRB(9, 8, 9, 8),
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
