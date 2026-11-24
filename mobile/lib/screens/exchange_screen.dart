import 'package:material_ui/material_ui.dart';

import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';

class ExchangeScreen extends StatefulWidget {
  const ExchangeScreen({super.key, required this.state});

  final AppState state;

  @override
  State<ExchangeScreen> createState() => _ExchangeScreenState();
}

class _ExchangeScreenState extends State<ExchangeScreen> {
  final _pairCode = TextEditingController();
  int? _pending;

  @override
  void initState() {
    super.initState();
    _loadPending();
  }

  @override
  void dispose() {
    _pairCode.dispose();
    super.dispose();
  }

  Future<void> _loadPending() async {
    final pending = await widget.state.repository.pendingOutbox();
    if (!mounted) return;
    setState(() => _pending = pending);
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: widget.state,
      builder: (context, _) {
        final p = context.palette;
        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 96),
          children: [
            GlassCard(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(Icons.cloud_off_outlined, size: 20, color: p.accent),
                      const SizedBox(width: 8),
                      Text('离线优先',
                          style: TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                              color: p.fg)),
                      const Spacer(),
                      const SinanChip(text: '本机为副本', tone: 'info', dense: true),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Text(
                    '现场记录先写入本机 SQLite，不依赖网络。每次保存阶段/状态/评论都会进入待同步队列，'
                    '联网后按顺序上传；桌面端是唯一主库。',
                    style: TextStyle(fontSize: 12, height: 1.55, color: p.muted),
                  ),
                  const SizedBox(height: 10),
                  Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: p.well,
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Row(
                      children: [
                        Icon(Icons.outbox_outlined, size: 16, color: p.primary),
                        const SizedBox(width: 6),
                        Text('待同步',
                            style: TextStyle(fontSize: 11.5, color: p.muted)),
                        const SizedBox(width: 6),
                        Text('${_pending ?? 0}',
                            style: TextStyle(
                                fontSize: 17,
                                fontWeight: FontWeight.w700,
                                color: p.primary,
                                fontFeatures: const [
                                  FontFeature.tabularFigures()
                                ])),
                        Text(' 条操作',
                            style: TextStyle(fontSize: 11, color: p.faint)),
                        const Spacer(),
                        TextButton(
                          onPressed: _loadPending,
                          child: const Text('刷新'),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(Icons.qr_code_2, size: 20, color: p.primary),
                      const SizedBox(width: 8),
                      Text('移动端配对',
                          style: TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                              color: p.fg)),
                      const Spacer(),
                      const SinanChip(text: '尚未启用', tone: 'warn', dense: true),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Text(
                    '桌面端「移动端配对」生成 6 位码或二维码后，在下方输入即可建立信任。'
                    '当前版本为离线副本，配对服务随后接入，不显示假配对结果。',
                    style: TextStyle(fontSize: 12, height: 1.55, color: p.muted),
                  ),
                  const SizedBox(height: 10),
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
                          minimumSize: const Size(80, 44),
                          padding: const EdgeInsets.symmetric(horizontal: 16),
                        ),
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
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(Icons.table_chart_outlined,
                          size: 20, color: p.accent),
                      const SizedBox(width: 8),
                      Text('Excel 原表',
                          style: TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w700,
                              color: p.fg)),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Text(
                    '导入/导出由桌面端负责：原表版式（三行表头、四阶段分组）、'
                    'manifest.csv 与附件 ZIP 都在办公室完成。移动端只同步问题与照片，'
                    '不直接解析 .xls/.xlsx，避免手机上出现半套表头。',
                    style: TextStyle(fontSize: 12, height: 1.55, color: p.muted),
                  ),
                  const SizedBox(height: 10),
                  Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: p.well,
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(color: p.cardBorder),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('冲突处理规则',
                            style: TextStyle(
                                fontSize: 11,
                                fontWeight: FontWeight.w600,
                                color: p.fg)),
                        const SizedBox(height: 4),
                        Text(
                          '同一问题两端都改时，按 version 判定；桌面端不会被手机静默覆盖，'
                          '必须由人工选择「保留手机 / 保留桌面」。',
                          style: TextStyle(
                              fontSize: 10.5, height: 1.5, color: p.faint),
                        ),
                      ],
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
