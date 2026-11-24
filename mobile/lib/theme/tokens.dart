import 'package:material_ui/material_ui.dart';

/// 司南设计 Token，与桌面端 `Themes/Sinan.Light.xaml` / `Sinan.Dark.xaml` 同源。
///
/// 只允许在这里定义色值；页面与组件一律通过 `context.palette` 取值，
/// 禁止出现 `Color(0xFF...)` 之类的硬编码，否则双端换肤必然分叉。
@immutable
class SinanPalette extends ThemeExtension<SinanPalette> {
  const SinanPalette({
    required this.bg,
    required this.panel,
    required this.cardTop,
    required this.cardBottom,
    required this.cardBorder,
    required this.cardHi,
    required this.fg,
    required this.muted,
    required this.faint,
    required this.primary,
    required this.primary2,
    required this.primaryDim,
    required this.onPrimary,
    required this.accent,
    required this.accentSoft,
    required this.ok,
    required this.okSoft,
    required this.warn,
    required this.warnSoft,
    required this.ng,
    required this.ngSoft,
    required this.info,
    required this.infoSoft,
    required this.well,
    required this.sepLine,
    required this.inputBg,
    required this.glassLine,
    required this.glassHi,
    required this.isDark,
  });

  final Color bg;
  final Color panel;
  final Color cardTop;
  final Color cardBottom;
  final Color cardBorder;
  final Color cardHi;
  final Color fg;
  final Color muted;
  final Color faint;
  final Color primary;
  final Color primary2;
  final Color primaryDim;
  final Color onPrimary;
  final Color accent;
  final Color accentSoft;
  final Color ok;
  final Color okSoft;
  final Color warn;
  final Color warnSoft;
  final Color ng;
  final Color ngSoft;
  final Color info;
  final Color infoSoft;
  final Color well;
  final Color sepLine;
  final Color inputBg;
  final Color glassLine;
  final Color glassHi;
  final bool isDark;

  /// 品牌渐变：按钮、主进度条、选中态。
  LinearGradient get primaryGradient => LinearGradient(
        begin: Alignment.centerLeft,
        end: Alignment.centerRight,
        colors: [primary, primary2],
      );

  /// 卡片玻璃底：上亮下暗，模拟桌面端 `Brush.GlassBg`。
  LinearGradient get cardGradient => LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [cardTop, cardBottom],
      );

  static const light = SinanPalette(
    bg: Color(0xFFFBF6EE),
    panel: Color(0xFFFFFFFF),
    cardTop: Color(0xF2FFFFFF),
    cardBottom: Color(0xE6FFFDF8),
    cardBorder: Color(0xFFEDDFCC),
    cardHi: Color(0xFAFFFFFF),
    fg: Color(0xFF2A2118),
    muted: Color(0xFF7D6E5E),
    faint: Color(0xFFA89A88),
    primary: Color(0xFFD97706),
    primary2: Color(0xFFF59E0B),
    primaryDim: Color(0x1FD97706),
    onPrimary: Color(0xFFFFFFFF),
    accent: Color(0xFF0F766E),
    accentSoft: Color(0x1F0F766E),
    ok: Color(0xFF15803D),
    okSoft: Color(0x1F15803D),
    warn: Color(0xFFB45309),
    warnSoft: Color(0x1FB45309),
    ng: Color(0xFFB91C1C),
    ngSoft: Color(0x1FB91C1C),
    info: Color(0xFF0F766E),
    infoSoft: Color(0x1F0F766E),
    well: Color(0x80FCF7EF),
    sepLine: Color(0xB3EDDFCC),
    inputBg: Color(0xFFFFFFFF),
    glassLine: Color(0xEBFFFFFF),
    glassHi: Color(0xFAFFFFFF),
    isDark: false,
  );

  static const dark = SinanPalette(
    bg: Color(0xFF1C140D),
    panel: Color(0xFF261B12),
    cardTop: Color(0xE62E2116),
    cardBottom: Color(0xD924170E),
    cardBorder: Color(0xFF463322),
    cardHi: Color(0x2EFFFFFF),
    fg: Color(0xFFF6EBDD),
    muted: Color(0xFFB7A18C),
    faint: Color(0xFF8A7561),
    primary: Color(0xFFF59E0B),
    primary2: Color(0xFFFBBF24),
    primaryDim: Color(0x33F59E0B),
    onPrimary: Color(0xFF1C140D),
    accent: Color(0xFF2DD4BF),
    accentSoft: Color(0x262DD4BF),
    ok: Color(0xFF4ADE80),
    okSoft: Color(0x264ADE80),
    warn: Color(0xFFFBBF24),
    warnSoft: Color(0x26FBBF24),
    ng: Color(0xFFF87171),
    ngSoft: Color(0x26F87171),
    info: Color(0xFF2DD4BF),
    infoSoft: Color(0x262DD4BF),
    well: Color(0x0AFFFFFF),
    sepLine: Color(0xB3463322),
    inputBg: Color(0xFF1F160E),
    glassLine: Color(0x2EFFFFFF),
    glassHi: Color(0x2EFFFFFF),
    isDark: true,
  );

  @override
  SinanPalette copyWith({
    Color? bg,
    Color? panel,
    Color? cardTop,
    Color? cardBottom,
    Color? cardBorder,
    Color? cardHi,
    Color? fg,
    Color? muted,
    Color? faint,
    Color? primary,
    Color? primary2,
    Color? primaryDim,
    Color? onPrimary,
    Color? accent,
    Color? accentSoft,
    Color? ok,
    Color? okSoft,
    Color? warn,
    Color? warnSoft,
    Color? ng,
    Color? ngSoft,
    Color? info,
    Color? infoSoft,
    Color? well,
    Color? sepLine,
    Color? inputBg,
    Color? glassLine,
    Color? glassHi,
    bool? isDark,
  }) {
    return SinanPalette(
      bg: bg ?? this.bg,
      panel: panel ?? this.panel,
      cardTop: cardTop ?? this.cardTop,
      cardBottom: cardBottom ?? this.cardBottom,
      cardBorder: cardBorder ?? this.cardBorder,
      cardHi: cardHi ?? this.cardHi,
      fg: fg ?? this.fg,
      muted: muted ?? this.muted,
      faint: faint ?? this.faint,
      primary: primary ?? this.primary,
      primary2: primary2 ?? this.primary2,
      primaryDim: primaryDim ?? this.primaryDim,
      onPrimary: onPrimary ?? this.onPrimary,
      accent: accent ?? this.accent,
      accentSoft: accentSoft ?? this.accentSoft,
      ok: ok ?? this.ok,
      okSoft: okSoft ?? this.okSoft,
      warn: warn ?? this.warn,
      warnSoft: warnSoft ?? this.warnSoft,
      ng: ng ?? this.ng,
      ngSoft: ngSoft ?? this.ngSoft,
      info: info ?? this.info,
      infoSoft: infoSoft ?? this.infoSoft,
      well: well ?? this.well,
      sepLine: sepLine ?? this.sepLine,
      inputBg: inputBg ?? this.inputBg,
      glassLine: glassLine ?? this.glassLine,
      glassHi: glassHi ?? this.glassHi,
      isDark: isDark ?? this.isDark,
    );
  }

  @override
  SinanPalette lerp(ThemeExtension<SinanPalette>? other, double t) {
    if (other is! SinanPalette) return this;
    return t < 0.5 ? this : other;
  }

  static SinanPalette of(BuildContext context) =>
      Theme.of(context).extension<SinanPalette>() ??
      (Theme.of(context).brightness == Brightness.dark ? dark : light);
}

extension SinanPaletteAccess on BuildContext {
  SinanPalette get palette => SinanPalette.of(this);
}
