import 'package:material_ui/material_ui.dart';

import '../theme/tokens.dart';

/// 阴影预设：所有圆角容器的投影只从这里取，禁止页面里手写 `BoxShadow`。
///
/// 取值口径（2026-11-27 实测）：
/// - 旧版 `GlassCard` 用 `blurRadius: 22 + offset: (0, 10)`，阴影在圆角处形成
///   一条紧贴边缘的深色弧带；亮色卡片与暗弧的高对比看起来像"凸起一个小包"。
/// - 现在统一改成"贴地短影"：`blurRadius` 不超过 16、`offset` 不超过 6，
///   阴影只在卡片下缘 2~10px 内可见，圆角处不再鼓包。
enum SinanShadow {
  /// 不投影：内嵌卡、Chip、进度条、纯色块。
  none,

  /// 常规卡片：极淡贴地投影，只用于 `GlassCard` 这类大块内容。
  card,

  /// 浮起元素：弹层、悬浮按钮，比 [card] 稍大但仍受控。
  floating,

  /// 主色发光：只用于小面积图形（品牌印章、+ 按钮），不用于文字卡片。
  glow,
}

/// 统一圆角容器：背景 / 渐变 / 边框 / 阴影 / 子元素裁剪一次画对。
///
/// 为什么要有这个基类：
/// 1. `Container.decoration` 同时画 `boxShadow` 与半透明渐变时，阴影会透过玻璃层，
///    在圆角处形成一圈紧贴边缘的深色弧带，视觉上像"凸起一个小包"；
/// 2. 旧实现外层 `borderRadius: radius`、内层 `ClipRRect(radius - 1)`，
///    两个圆角不同心，抗锯齿边缘会露白；
/// 3. 每个页面各写一套 `BoxDecoration`，修一处漏一处。
///
/// 落地口径：
/// - 子元素用 `Container.clipBehavior` 按同一个 [radius] 裁剪，不再单独 `ClipRRect`；
/// - 阴影只走 [SinanShadow] 预设，页面里不再出现 `BoxShadow`；
/// - 卡片背景保持近不透明，避免阴影透进内容区。
///
/// 用法：
/// ```dart
/// SinanSurface(
///   radius: 20,
///   gradient: p.cardGradient,
///   border: Border.all(color: p.cardBorder),
///   shadow: SinanShadow.card,
///   padding: const EdgeInsets.all(16),
///   child: ...,
/// )
/// ```
class SinanSurface extends StatelessWidget {
  const SinanSurface({
    super.key,
    required this.child,
    this.radius = 20,
    this.color,
    this.gradient,
    this.border,
    this.shadow = SinanShadow.card,
    this.padding = EdgeInsets.zero,
    this.margin = EdgeInsets.zero,
    this.topHighlight = false,
    this.onTap,
    this.clipBehavior = Clip.antiAlias,
  }) : assert(color != null || gradient != null,
            'SinanSurface 必须提供 color 或 gradient 之一');

  final Widget child;
  final double radius;
  final Color? color;
  final Gradient? gradient;
  final BoxBorder? border;
  final SinanShadow shadow;
  final EdgeInsets padding;
  final EdgeInsets margin;

  /// 顶缘玻璃反光。只在顶部直边段绘制（从 `radius` 到 `width - radius`），
  /// 不碰圆角，避免旧版高光线被裁剪后在圆角处留下月牙形亮斑。
  final bool topHighlight;

  final VoidCallback? onTap;
  final Clip clipBehavior;

  @override
  Widget build(BuildContext context) {
    final p = context.palette;
    final r = BorderRadius.circular(radius);

    // 内容层：Stack 让顶缘高光叠在内容之上，但仍被 Container 的圆角裁剪。
    // 高光从 x=radius 开始，正好避开圆角圆弧，不会出现"突然中断"的亮线。
    Widget body = Stack(
      children: [
        Padding(padding: padding, child: child),
        if (topHighlight)
          Positioned(
            left: radius,
            right: radius,
            top: 0,
            child: IgnorePointer(
              child: Container(
                height: 1,
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    colors: [
                      Colors.transparent,
                      p.glassHi.withValues(alpha: p.isDark ? 0.18 : 0.65),
                      Colors.transparent,
                    ],
                  ),
                ),
              ),
            ),
          ),
      ],
    );

    // 关键：背景 / 边框 / 阴影由同一个 decoration 画出，child 用同一 radius 裁剪。
    // 不再出现外层 20、内层 19 的同心误差。
    Widget card = Container(
      clipBehavior: clipBehavior,
      decoration: BoxDecoration(
        color: gradient == null ? color : null,
        gradient: gradient,
        borderRadius: r,
        border: border,
        boxShadow: _shadows(p),
      ),
      child: body,
    );

    if (onTap != null) {
      card = Material(
        color: Colors.transparent,
        child: InkWell(borderRadius: r, onTap: onTap, child: card),
      );
    }

    // margin 放在最外层：水波纹与阴影都只覆盖卡片本身，不跟着 margin 外扩。
    if (margin != EdgeInsets.zero) {
      card = Padding(padding: margin, child: card);
    }
    return card;
  }

  List<BoxShadow>? _shadows(SinanPalette p) {
    return switch (shadow) {
      SinanShadow.none => null,
      SinanShadow.card => [
          BoxShadow(
            color: p.isDark
                ? Colors.black.withValues(alpha: 0.18)
                : const Color(0xFF8A5A2B).withValues(alpha: 0.05),
            blurRadius: p.isDark ? 10 : 8,
            offset: const Offset(0, 2),
          ),
        ],
      SinanShadow.floating => [
          BoxShadow(
            color: p.isDark
                ? Colors.black.withValues(alpha: 0.26)
                : const Color(0xFF8A5A2B).withValues(alpha: 0.08),
            blurRadius: 16,
            offset: const Offset(0, 6),
          ),
        ],
      SinanShadow.glow => [
          BoxShadow(
            color: p.primary.withValues(alpha: p.isDark ? 0.24 : 0.22),
            blurRadius: 12,
            offset: const Offset(0, 4),
          ),
        ],
    };
  }
}
