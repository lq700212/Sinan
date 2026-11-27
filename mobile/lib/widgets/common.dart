import 'package:material_ui/material_ui.dart';

import '../models/issue.dart';
import '../theme/tokens.dart';
import 'surface.dart';

/// 页面底层柔光斑：琥珀 / 深青 / 暖橙三团，替代桌面端 XAML 的 RadialGradientBrush。
/// 玻璃态只作用于内容卡片，侧栏/底栏保持不透明，避免整屏糊成一片。
class AmbientBackground extends StatelessWidget {
  const AmbientBackground({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Stack(
      children: [
        Positioned.fill(child: ColoredBox(color: p.bg)),
        Positioned(
          top: -120,
          right: -80,
          child: _Glow(color: p.primary.withValues(alpha: p.isDark ? 0.16 : 0.14), size: 340),
        ),
        Positioned(
          top: 260,
          left: -140,
          child: _Glow(color: p.accent.withValues(alpha: p.isDark ? 0.12 : 0.10), size: 320),
        ),
        Positioned(
          bottom: -160,
          right: 40,
          child: _Glow(color: p.primary2.withValues(alpha: p.isDark ? 0.10 : 0.12), size: 360),
        ),
        Positioned.fill(child: child),
      ],
    );
  }
}

class _Glow extends StatelessWidget {
  const _Glow({required this.color, required this.size});

  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(
            colors: [color, color.withValues(alpha: 0)],
            stops: const [0, 1],
          ),
        ),
      ),
    );
  }
}

/// 玻璃卡片：半透明渐变底 + 1px 亮边 + 顶缘高光 + 贴地柔影。
///
/// 绘制细节全部下沉到 [SinanSurface]：阴影只走预设、子元素按同一半径裁剪。
/// 页面只认这个组件，不再自己写 `BoxDecoration + boxShadow`。
class GlassCard extends StatelessWidget {
  const GlassCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.margin = EdgeInsets.zero,
    this.radius = 20,
    this.inset = false,
    this.onTap,
  });

  final Widget child;
  final EdgeInsets padding;
  final EdgeInsets margin;
  final double radius;
  final bool inset;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return SinanSurface(
      margin: margin,
      radius: radius,
      color: inset ? p.well : null,
      gradient: inset ? null : p.cardGradient,
      border: Border.all(color: p.cardBorder),
      shadow: inset ? SinanShadow.none : SinanShadow.card,
      padding: padding,
      topHighlight: !inset,
      onTap: onTap,
      child: child,
    );
  }
}

/// 状态/优先级小标签。color 语义：ok 绿、warn 黄、ng 红、info 青、primary 琥珀。
class SinanChip extends StatelessWidget {
  const SinanChip({
    super.key,
    required this.text,
    this.tone = 'info',
    this.icon,
    this.dense = false,
  });

  final String text;
  final String tone;
  final IconData? icon;
  final bool dense;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final (fg, bg) = switch (tone) {
      'ok' => (p.ok, p.okSoft),
      'warn' => (p.warn, p.warnSoft),
      'ng' => (p.ng, p.ngSoft),
      'muted' => (p.faint, p.well),
      'primary' => (p.primary, p.primaryDim),
      _ => (p.info, p.infoSoft),
    };
    return SinanSurface(
      radius: 99,
      color: bg,
      border: Border.all(color: fg.withValues(alpha: 0.35)),
      shadow: SinanShadow.none,
      padding: EdgeInsets.symmetric(
        horizontal: dense ? 7 : 9,
        vertical: dense ? 2.5 : 4,
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: dense ? 11 : 13, color: fg),
            const SizedBox(width: 3),
          ],
          Text(
            text,
            style: TextStyle(
              fontSize: dense ? 10.5 : 11.5,
              fontWeight: FontWeight.w600,
              color: fg,
            ),
          ),
        ],
      ),
    );
  }
}

/// 问题状态 → Chip 语义色。
String statusTone(String status) => switch (status) {
      IssueStatuses.closed => 'ok',
      IssueStatuses.cancelled => 'muted',
      IssueStatuses.verifying => 'info',
      IssueStatuses.unassigned => 'warn',
      _ => 'primary',
    };

/// 四阶段进度点：已完成实心、进行中半环、不适用空心。
class StageDots extends StatelessWidget {
  const StageDots({super.key, required this.issue});

  final Issue issue;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Row(
      children: [
        for (final s in issue.stages) ...[
          Container(
            width: 8,
            height: 8,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: s.isDone
                  ? p.ok
                  : s.state == StageState.inProgress
                      ? p.primary
                      : Colors.transparent,
              border: Border.all(
                color: s.isNa
                    ? p.faint.withValues(alpha: 0.5)
                    : s.isDone
                        ? p.ok
                        : s.state == StageState.inProgress
                            ? p.primary
                            : p.faint,
              ),
            ),
          ),
          if (s != issue.stages.last) const SizedBox(width: 4),
        ],
      ],
    );
  }
}

/// KPI 小卡：一个数字 + 说明 + 语义色边。
class KpiTile extends StatelessWidget {
  const KpiTile({
    super.key,
    required this.label,
    required this.value,
    this.unit,
    this.sub,
    this.tone = 'primary',
    this.onTap,
  });

  final String label;
  final String value;
  final String? unit;
  final String? sub;
  final String tone;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final color = switch (tone) {
      'ok' => p.ok,
      'warn' => p.warn,
      'ng' => p.ng,
      'info' => p.info,
      _ => p.primary,
    };
    return GlassCard(
      padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
      margin: const EdgeInsets.only(right: 10),
      radius: 16,
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            children: [
              Container(
                width: 6,
                height: 6,
                decoration: BoxDecoration(color: color, shape: BoxShape.circle),
              ),
              const SizedBox(width: 6),
              Flexible(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 11.5, color: p.muted),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Row(
            crossAxisAlignment: CrossAxisAlignment.baseline,
            textBaseline: TextBaseline.alphabetic,
            children: [
              Text(
                value,
                style: TextStyle(
                  fontSize: 24,
                  height: 1.1,
                  fontWeight: FontWeight.w700,
                  color: color,
                  fontFeatures: const [FontFeature.tabularFigures()],
                ),
              ),
              if (unit != null) ...[
                const SizedBox(width: 2),
                Text(unit!,
                    style: TextStyle(fontSize: 11, color: p.muted)),
              ],
            ],
          ),
          if (sub != null) ...[
            const SizedBox(height: 3),
            Text(
              sub!,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 10.5, color: p.faint),
            ),
          ],
        ],
      ),
    );
  }
}

/// 细进度条（桌面端 Bar.Thin 的 Flutter 版）：0-1 的比例。
class ThinBar extends StatelessWidget {
  const ThinBar({
    super.key,
    required this.fraction,
    this.color,
    this.height = 6,
  });

  final double fraction;
  final Color? color;
  final double height;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final value = fraction.isNaN ? 0.0 : fraction.clamp(0.0, 1.0);
    return SizedBox(
      height: height,
      child: SinanSurface(
        radius: 99,
        color: p.well,
        shadow: SinanShadow.none,
        child: FractionallySizedBox(
          alignment: Alignment.centerLeft,
          widthFactor: value,
          child: Container(
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [color ?? p.primary, color ?? p.primary2],
              ),
              borderRadius: BorderRadius.circular(99),
            ),
          ),
        ),
      ),
    );
  }
}

class SectionTitle extends StatelessWidget {
  const SectionTitle({
    super.key,
    required this.title,
    this.subtitle,
    this.trailing,
  });

  final String title;
  final String? subtitle;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        SizedBox(
          width: 4,
          height: 16,
          child: SinanSurface(
            radius: 2,
            gradient: p.primaryGradient,
            shadow: SinanShadow.none,
            child: const SizedBox.shrink(),
          ),
        ),
        const SizedBox(width: 8),
        Text(
          title,
          style: TextStyle(
            fontSize: 15,
            fontWeight: FontWeight.w700,
            color: p.fg,
          ),
        ),
        if (subtitle != null) ...[
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              subtitle!,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 11, color: p.faint),
            ),
          ),
        ] else
          const Spacer(),
        if (trailing != null) ?trailing,
      ],
    );
  }
}

class EmptyState extends StatelessWidget {
  const EmptyState({
    super.key,
    required this.title,
    this.description,
    this.icon = Icons.inbox_outlined,
  });

  final String title;
  final String? description;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 36, horizontal: 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 40, color: p.faint.withValues(alpha: 0.7)),
            const SizedBox(height: 10),
            Text(title,
                style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: p.muted)),
            if (description != null) ...[
              const SizedBox(height: 4),
              Text(
                description!,
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 12, color: p.faint),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// 演示数据角标："演示值"三个字，不允许把虚构数字伪装成真实统计。
class DemoBadge extends StatelessWidget {
  const DemoBadge({super.key, this.text = '演示值'});

  final String text;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    return SinanSurface(
      radius: 4,
      color: p.warnSoft,
      border: Border.all(color: p.warn.withValues(alpha: 0.3)),
      shadow: SinanShadow.none,
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1.5),
      child: Text(
        text,
        style: TextStyle(fontSize: 9.5, color: p.warn, fontWeight: FontWeight.w600),
      ),
    );
  }
}
