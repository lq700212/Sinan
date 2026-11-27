import 'package:material_ui/material_ui.dart';

import '../app_info.dart';
import '../state/app_state.dart';
import '../theme/tokens.dart';
import '../widgets/common.dart';

class SettingsScreen extends StatelessWidget {
  const SettingsScreen({super.key, required this.state});

  final AppState state;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: state,
      builder: (context, _) {
        final p = context.palette;
        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 96),
          children: [
            SectionTitle(title: '外观', subtitle: '与桌面端同一套设计 Token'),
            const SizedBox(height: 8),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(Icons.palette_outlined, size: 18, color: p.primary),
                      const SizedBox(width: 8),
                      Text('主题',
                          style: TextStyle(
                              fontSize: 13.5,
                              fontWeight: FontWeight.w600,
                              color: p.fg)),
                      const Spacer(),
                      Text(
                        state.themeMode == ThemeMode.dark ? '暗色' : '浅色',
                        style: TextStyle(fontSize: 11.5, color: p.muted),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  Row(
                    children: [
                      Expanded(
                        child: _ThemeOption(
                          label: '晨光浅色',
                          icon: Icons.light_mode_outlined,
                          selected: state.themeMode == ThemeMode.light,
                          onTap: () => state.setThemeMode(ThemeMode.light),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: _ThemeOption(
                          label: '现场暗色',
                          icon: Icons.dark_mode_outlined,
                          selected: state.themeMode == ThemeMode.dark,
                          onTap: () => state.setThemeMode(ThemeMode.dark),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Text('主题写入本机，下次打开自动恢复；默认浅色。',
                      style: TextStyle(fontSize: 10.5, color: p.faint)),
                ],
              ),
            ),
            const SizedBox(height: 12),
            SectionTitle(title: '账号', subtitle: 'MVP 只做页面门禁'),
            const SizedBox(height: 8),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              child: Column(
                children: [
                  Row(
                    children: [
                      CircleAvatar(
                        radius: 18,
                        backgroundColor: p.primaryDim,
                        child: Text(
                          state.userName.isEmpty
                              ? '?'
                              : state.userName.substring(0, 1),
                          style: TextStyle(
                              fontSize: 14,
                              fontWeight: FontWeight.w700,
                              color: p.primary),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(state.userName,
                                style: TextStyle(
                                    fontSize: 13.5,
                                    fontWeight: FontWeight.w600,
                                    color: p.fg)),
                            Text('${state.userRole} · ${state.userAccount}',
                                style:
                                    TextStyle(fontSize: 10.5, color: p.faint)),
                          ],
                        ),
                      ),
                      OutlinedButton(
                        onPressed: state.logout,
                        style: OutlinedButton.styleFrom(
                          foregroundColor: p.ng,
                          side: BorderSide(color: p.ng.withValues(alpha: 0.4)),
                          minimumSize: const Size(0, 36),
                          padding: const EdgeInsets.symmetric(horizontal: 14),
                          shape: const StadiumBorder(),
                        ),
                        child: const Text('退出登录',
                            style: TextStyle(fontSize: 12)),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  Container(
                    width: double.infinity,
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: p.well,
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: Text(
                      '演示账号：engineer / 123456（工程师）、admin / 123456（管理员）。'
                      '首次登录后应改密；演示阶段允许直接进入。',
                      style: TextStyle(
                          fontSize: 10.5, height: 1.5, color: p.faint),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            SectionTitle(title: '数据', subtitle: '离线副本，桌面端为主库'),
            const SizedBox(height: 8),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              child: Column(
                children: [
                  _InfoRow(
                    icon: Icons.storage_outlined,
                    label: '本地数据库',
                    value: 'sinan_mobile.db',
                  ),
                  const SizedBox(height: 8),
                  _InfoRow(
                    icon: Icons.inventory_2_outlined,
                    label: '本机数据',
                    value:
                        '${state.issues.length} 条问题 / ${state.stats?.machines ?? 0} 台机台',
                  ),
                  const SizedBox(height: 8),
                  _InfoRow(
                    icon: Icons.sync_outlined,
                    label: '待同步',
                    value: '${state.pendingOutbox} 条操作',
                  ),
                  const SizedBox(height: 10),
                  Text(
                    '内置 36 条虚构种子（演示项目，照片数为演示占位值）；'
                    '本机新建的现场记录只存在这台手机，联网后按 outbox 队列补传。',
                    style:
                        TextStyle(fontSize: 10.5, height: 1.5, color: p.faint),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            SectionTitle(title: '关于', subtitle: '安装调试问题闭环'),
            const SizedBox(height: 8),
            GlassCard(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Container(
                        width: 34,
                        height: 34,
                        decoration: BoxDecoration(
                          gradient: p.primaryGradient,
                          borderRadius: BorderRadius.circular(10),
                        ),
                        alignment: Alignment.center,
                        child: const Text('司',
                            style: TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.w800,
                                color: Colors.white)),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(AppInfo.productLabel,
                                style: TextStyle(
                                    fontSize: 13.5,
                                    fontWeight: FontWeight.w700,
                                    color: p.fg)),
                            Text('安装调试问题闭环系统 · 现场端',
                                style: TextStyle(
                                    fontSize: 10.5, color: p.faint)),
                          ],
                        ),
                      ),
                      const SinanChip(
                          text: '离线可用', tone: 'ok', dense: true),
                    ],
                  ),
                  const SizedBox(height: 10),
                  Text(
                    '四阶段：方案确定 → 设计下图 → 物料准备 → 装配调试。'
                    '问题状态由阶段自动推导，也允许人工覆盖。任何覆盖都留痕。',
                    style:
                        TextStyle(fontSize: 10.5, height: 1.5, color: p.faint),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16),
            Center(
              child: Text('数据脱敏 · 双端一致 · 离线优先',
                  style: TextStyle(fontSize: 10, color: p.faint)),
            ),
          ],
        );
      },
    );
  }
}

class _ThemeOption extends StatelessWidget {
  const _ThemeOption({
    required this.label,
    required this.icon,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final IconData icon;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return InkWell(
      borderRadius: BorderRadius.circular(12),
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 10),
        decoration: BoxDecoration(
          color: selected ? p.primaryDim : p.well,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(
            color: selected ? p.primary : p.cardBorder,
            width: selected ? 1.3 : 1,
          ),
        ),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(icon, size: 17, color: selected ? p.primary : p.muted),
            const SizedBox(width: 6),
            Text(
              label,
              style: TextStyle(
                fontSize: 12,
                fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                color: selected ? p.primary : p.muted,
              ),
            ),
            if (selected) ...[
              const SizedBox(width: 4),
              Icon(Icons.check_circle, size: 13, color: p.primary),
            ],
          ],
        ),
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Row(
      children: [
        Icon(icon, size: 16, color: p.muted),
        const SizedBox(width: 8),
        Text(label, style: TextStyle(fontSize: 12, color: p.muted)),
        const Spacer(),
        Text(
          value,
          style: TextStyle(
            fontSize: 11.5,
            color: p.fg,
            fontFeatures: const [FontFeature.tabularFigures()],
          ),
        ),
      ],
    );
  }
}
