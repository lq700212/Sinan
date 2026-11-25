import 'package:material_ui/material_ui.dart';

import '../data/issue_repository.dart';
import '../models/rules.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';

/// 选机台（设计稿 m-scan）。
///
/// 本机没有接入相机扫码组件，**不画一个假取景框冒充能扫**：
/// 手输 + 最近机台是当前真实可用的路径，扫码位明确标注"待接入"。
/// 返回选中的机台号，由调用方决定是新建还是筛选台账。
class MachineSelectScreen extends StatefulWidget {
  const MachineSelectScreen({super.key, required this.state});

  final AppState state;

  @override
  State<MachineSelectScreen> createState() => _MachineSelectScreenState();
}

class _MachineSelectScreenState extends State<MachineSelectScreen> {
  final _machine = TextEditingController();
  List<MachineSummary> _machines = const [];
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _machine.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final list = await widget.state.repository.machines();
    if (!mounted) return;
    setState(() => _machines = list);
  }

  void _confirm(String value) {
    final machine = value.trim().toUpperCase();
    if (!Rules.isMachineCode(machine)) {
      setState(() => _error = '机台号格式应为 P7391 + 3 位序号，例如 P7391106。');
      return;
    }
    Navigator.of(context).pop(machine);
  }

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Scaffold(
      body: AmbientBackground(
        child: SafeArea(
          child: Column(
            children: [
              _header(context, p),
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
                  children: [
                    _scanHint(p),
                    const SizedBox(height: 12),
                    Row(
                      children: [
                        Expanded(
                          child: TextField(
                            controller: _machine,
                            textCapitalization: TextCapitalization.characters,
                            onSubmitted: _confirm,
                            decoration: const InputDecoration(
                              labelText: '手动输入机台号',
                              hintText: 'P7391106',
                              prefixIcon: Icon(Icons.keyboard_alt_outlined, size: 18),
                              isDense: true,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        FilledButton(
                          onPressed: () => _confirm(_machine.text),
                          style: FilledButton.styleFrom(
                              minimumSize: const Size(72, 48)),
                          child: const Text('确定'),
                        ),
                      ],
                    ),
                    if (_error != null) ...[
                      const SizedBox(height: 6),
                      Text(_error!, style: TextStyle(fontSize: 11, color: p.ng)),
                    ],
                    const SizedBox(height: 16),
                    SectionTitle(
                      title: '最近机台',
                      subtitle: '按机台号排序',
                      trailing: Text('${_machines.length} 台',
                          style: TextStyle(fontSize: 11, color: p.faint)),
                    ),
                    const SizedBox(height: 8),
                    if (_machines.isEmpty)
                      const EmptyState(
                        title: '还没有机台数据',
                        description: '先在台账里确认问题已缓存到本机。',
                      )
                    else
                      for (final m in _machines.take(12))
                        _MachineTile(
                          machine: m,
                          onTap: () => _confirm(m.machineCode),
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

  Widget _header(BuildContext context, SinanPalette p) {
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
                Text('选择机台',
                    style: TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w700,
                        color: p.fg)),
                Text('手输或从最近机台选择',
                    style: TextStyle(fontSize: 10.5, color: p.faint)),
              ],
            ),
          ),
          const SinanChip(text: '离线可用', tone: 'info', dense: true),
        ],
      ),
    );
  }

  Widget _scanHint(SinanPalette p) {
    return Container(
      height: 132,
      decoration: BoxDecoration(
        color: p.isDark ? const Color(0xFF0B1218) : const Color(0xFF121A21),
        borderRadius: BorderRadius.circular(18),
        border: Border.all(color: p.cardBorder),
      ),
      child: Stack(
        alignment: Alignment.center,
        children: [
          // 取景框四角只做"这里将来是扫码位"的视觉占位，中心明确写未接入。
          for (final align in [
            Alignment.topLeft,
            Alignment.topRight,
            Alignment.bottomLeft,
            Alignment.bottomRight,
          ])
            Align(
              alignment: align,
              child: Container(
                width: 22,
                height: 22,
                margin: const EdgeInsets.all(18),
                decoration: BoxDecoration(
                  border: Border(
                    top: align.y < 0
                        ? const BorderSide(color: Colors.white70, width: 2)
                        : BorderSide.none,
                    bottom: align.y > 0
                        ? const BorderSide(color: Colors.white70, width: 2)
                        : BorderSide.none,
                    left: align.x < 0
                        ? const BorderSide(color: Colors.white70, width: 2)
                        : BorderSide.none,
                    right: align.x > 0
                        ? const BorderSide(color: Colors.white70, width: 2)
                        : BorderSide.none,
                  ),
                ),
              ),
            ),
          Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.qr_code_scanner, size: 28, color: Colors.white70),
              const SizedBox(height: 8),
              const Text('相机扫码待真机接入',
                  style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w600,
                      color: Colors.white)),
              const SizedBox(height: 3),
              Text('当前请手输机台号，或点下方最近机台',
                  style: TextStyle(
                      fontSize: 9.5,
                      color: Colors.white.withValues(alpha: 0.66))),
            ],
          ),
        ],
      ),
    );
  }
}

class _MachineTile extends StatelessWidget {
  const _MachineTile({required this.machine, required this.onTap});

  final MachineSummary machine;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return GlassCard(
      margin: const EdgeInsets.only(bottom: 7),
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      radius: 14,
      onTap: onTap,
      child: Row(
        children: [
          Container(
            width: 34,
            height: 34,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: p.primaryDim,
              borderRadius: BorderRadius.circular(10),
            ),
            child: Icon(Icons.precision_manufacturing_outlined,
                size: 17, color: p.primary),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(machine.machineCode,
                    style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w700,
                        color: p.fg,
                        fontFeatures: const [FontFeature.tabularFigures()])),
                const SizedBox(height: 2),
                Text(
                  '${machine.issueCount} 条问题'
                  '${machine.inverted > 0 ? ' · 倒挂 ${machine.inverted}' : ''}'
                  '${machine.overdue > 0 ? ' · 逾期 ${machine.overdue}' : ''}',
                  style: TextStyle(fontSize: 10, color: p.faint),
                ),
              ],
            ),
          ),
          if (machine.inverted > 0)
            SinanChip(text: '倒挂 ${machine.inverted}', tone: 'warn', dense: true)
          else if (machine.overdue > 0)
            SinanChip(text: '逾期 ${machine.overdue}', tone: 'ng', dense: true)
          else
            const SinanChip(text: '正常', tone: 'ok', dense: true),
          const SizedBox(width: 4),
          Icon(Icons.chevron_right, size: 16, color: p.faint),
        ],
      ),
    );
  }
}
