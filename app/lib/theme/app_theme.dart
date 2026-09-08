import 'package:flutter/material.dart';

@immutable
class AppSemanticColors extends ThemeExtension<AppSemanticColors> {
  const AppSemanticColors({
    required this.success,
    required this.onSuccess,
    required this.warning,
    required this.onWarning,
  });

  final Color success;
  final Color onSuccess;
  final Color warning;
  final Color onWarning;

  @override
  AppSemanticColors copyWith({
    Color? success,
    Color? onSuccess,
    Color? warning,
    Color? onWarning,
  }) =>
      AppSemanticColors(
        success: success ?? this.success,
        onSuccess: onSuccess ?? this.onSuccess,
        warning: warning ?? this.warning,
        onWarning: onWarning ?? this.onWarning,
      );

  @override
  AppSemanticColors lerp(AppSemanticColors? other, double t) {
    if (other == null) return this;
    return AppSemanticColors(
      success: Color.lerp(success, other.success, t)!,
      onSuccess: Color.lerp(onSuccess, other.onSuccess, t)!,
      warning: Color.lerp(warning, other.warning, t)!,
      onWarning: Color.lerp(onWarning, other.onWarning, t)!,
    );
  }
}

abstract final class AppMotion {
  static const fast = Duration(milliseconds: 120);
  static const standard = Duration(milliseconds: 240);
  static const emphasized = Duration(milliseconds: 360);
  static const curve = Curves.easeOutCubic;
}

ThemeData buildAppTheme({
  required Brightness brightness,
  required Color seedColor,
  ColorScheme? dynamicColorScheme,
  bool pureBlack = false,
}) {
  var scheme = dynamicColorScheme ??
      ColorScheme.fromSeed(seedColor: seedColor, brightness: brightness);
  if (pureBlack && brightness == Brightness.dark) {
    scheme = scheme.copyWith(
      surface: Colors.black,
      surfaceContainerLowest: Colors.black,
      surfaceContainerLow: const Color(0xFF080808),
      surfaceContainer: const Color(0xFF101010),
      surfaceContainerHigh: const Color(0xFF181818),
    );
  }

  final isDark = brightness == Brightness.dark;
  return ThemeData(
    useMaterial3: true,
    brightness: brightness,
    colorScheme: scheme,
    scaffoldBackgroundColor: scheme.surface,
    visualDensity: VisualDensity.standard,
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: scheme.surfaceContainerHighest.withValues(alpha: 0.46),
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide.none,
      ),
      enabledBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide.none,
      ),
      focusedBorder: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide(color: scheme.primary, width: 2),
      ),
    ),
    cardTheme: CardThemeData(
      elevation: 0,
      color: scheme.surfaceContainerLow,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(24)),
    ),
    dialogTheme: DialogThemeData(
      backgroundColor: scheme.surfaceContainerHigh,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(28)),
    ),
    snackBarTheme: SnackBarThemeData(
      behavior: SnackBarBehavior.floating,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
    ),
    navigationBarTheme: NavigationBarThemeData(
      height: 76,
      elevation: 0,
      backgroundColor: scheme.surfaceContainer,
      indicatorColor: scheme.secondaryContainer,
      indicatorShape: const StadiumBorder(),
    ),
    navigationRailTheme: NavigationRailThemeData(
      backgroundColor: scheme.surfaceContainer,
      indicatorColor: scheme.secondaryContainer,
      indicatorShape: const StadiumBorder(),
      selectedIconTheme: IconThemeData(color: scheme.onSecondaryContainer),
    ),
    extensions: [
      AppSemanticColors(
        success: isDark ? const Color(0xFF67DB8B) : const Color(0xFF146C36),
        onSuccess: isDark ? const Color(0xFF003918) : Colors.white,
        warning: isDark ? const Color(0xFFFFB95C) : const Color(0xFF8A4F00),
        onWarning: isDark ? const Color(0xFF4A2800) : Colors.white,
      ),
    ],
  );
}
