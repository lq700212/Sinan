import 'package:material_ui/material_ui.dart';

import 'tokens.dart';

/// 应用主题：只产出 ThemeData，不在这里写页面级样式。
/// 明/暗两套都从 [SinanPalette] 派生，保证与桌面端同色。
ThemeData buildSinanTheme(Brightness brightness) {
  final p = brightness == Brightness.light ? SinanPalette.light : SinanPalette.dark;
  final base = ThemeData(useMaterial3: true, brightness: brightness);
  final scheme = ColorScheme.fromSeed(
    seedColor: p.primary,
    brightness: brightness,
  ).copyWith(
    primary: p.primary,
    onPrimary: p.onPrimary,
    secondary: p.accent,
    onSecondary: Colors.white,
    surface: p.panel,
    onSurface: p.fg,
    error: p.ng,
    onError: Colors.white,
    outline: p.cardBorder,
    outlineVariant: p.sepLine,
  );

  final text = base.textTheme.apply(bodyColor: p.fg, displayColor: p.fg).copyWith(
        titleLarge: TextStyle(
          fontSize: 22,
          fontWeight: FontWeight.w700,
          color: p.fg,
          letterSpacing: 0.2,
        ),
        titleMedium: TextStyle(
          fontSize: 16,
          fontWeight: FontWeight.w600,
          color: p.fg,
        ),
        bodyMedium: TextStyle(fontSize: 14, height: 1.5, color: p.fg),
        bodySmall: TextStyle(fontSize: 12, height: 1.45, color: p.muted),
        labelSmall: TextStyle(fontSize: 11, color: p.faint),
      );

  return base.copyWith(
    extensions: [p],
    scaffoldBackgroundColor: p.bg,
    canvasColor: p.bg,
    colorScheme: scheme,
    textTheme: text,
    dividerColor: p.sepLine,
    appBarTheme: AppBarTheme(
      backgroundColor: Colors.transparent,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      foregroundColor: p.fg,
      centerTitle: false,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: p.inputBg,
      isDense: true,
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
      hintStyle: TextStyle(fontSize: 13, color: p.faint),
      labelStyle: TextStyle(fontSize: 13, color: p.muted),
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(12),
        borderSide: BorderSide(color: p.cardBorder),
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(12),
        borderSide: BorderSide(color: p.cardBorder),
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(12),
        borderSide: BorderSide(color: p.primary, width: 1.4),
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: p.primary,
        foregroundColor: p.onPrimary,
        minimumSize: const Size(0, 46),
        shape: const StadiumBorder(),
        textStyle: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        foregroundColor: p.primary,
        textStyle: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600),
      ),
    ),
    chipTheme: ChipThemeData(
      backgroundColor: p.well,
      side: BorderSide(color: p.cardBorder),
      labelStyle: TextStyle(fontSize: 12, color: p.muted),
      shape: const StadiumBorder(),
    ),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: p.panel.withValues(alpha: 0.92),
      surfaceTintColor: Colors.transparent,
      indicatorColor: p.primaryDim,
      elevation: 0,
      labelTextStyle: WidgetStateProperty.resolveWith((states) {
        final selected = states.contains(WidgetState.selected);
        return TextStyle(
          fontSize: 11.5,
          fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
          color: selected ? p.primary : p.muted,
        );
      }),
      iconTheme: WidgetStateProperty.resolveWith((states) {
        final selected = states.contains(WidgetState.selected);
        return IconThemeData(
          size: 22,
          color: selected ? p.primary : p.muted,
        );
      }),
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      backgroundColor: p.isDark ? const Color(0xFF3A2A1B) : const Color(0xFF3B2F22),
      contentTextStyle: const TextStyle(fontSize: 13, color: Color(0xFFFFF7EA)),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
    ),
  );
}
