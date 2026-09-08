import 'dart:io';

import 'package:dynamic_color/dynamic_color.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:path/path.dart' as path;
import 'package:tray_manager/tray_manager.dart';
import 'package:window_manager/window_manager.dart';

import 'providers/app_providers.dart';
import 'theme/app_theme.dart';
import 'widgets/adaptive_shell.dart';
import 'widgets/common_widgets.dart';

class OixApplication extends ConsumerWidget {
  const OixApplication({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final preferences = ref.watch(uiPreferencesProvider).value ??
        const UiPreferences(
          themeMode: ThemeMode.system,
          pureBlack: false,
          seedColor: 0xFF6750A4,
        );
    return DynamicColorBuilder(
      builder: (lightDynamic, darkDynamic) => MaterialApp(
        title: 'Oix Node Helper',
        debugShowCheckedModeBanner: false,
        themeMode: preferences.themeMode,
        themeAnimationDuration: AppMotion.emphasized,
        themeAnimationCurve: AppMotion.curve,
        theme: buildAppTheme(
          brightness: Brightness.light,
          seedColor: Color(preferences.seedColor),
          dynamicColorScheme: lightDynamic,
        ),
        darkTheme: buildAppTheme(
          brightness: Brightness.dark,
          seedColor: Color(preferences.seedColor),
          dynamicColorScheme: darkDynamic,
          pureBlack: preferences.pureBlack,
        ),
        home: const _DesktopLifecycle(child: _ApplicationFrame()),
      ),
    );
  }
}

class _ApplicationFrame extends ConsumerWidget {
  const _ApplicationFrame();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final backend = ref.watch(backendClientProvider);
    return Material(
      color: Theme.of(context).colorScheme.surface,
      child: Column(
        children: [
          if (Platform.isWindows) const _DesktopTitleBar(),
          Expanded(
            child: backend.when(
              loading: () => const LoadingPane(),
              error: (error, _) => EmptyState(
                icon: Icons.dns_outlined,
                title: '后台启动失败',
                message: error.toString(),
                action: FilledButton.icon(
                  onPressed: () => ref.invalidate(backendClientProvider),
                  icon: const Icon(Icons.refresh_rounded),
                  label: const Text('重试'),
                ),
              ),
              data: (_) => const AdaptiveShell(),
            ),
          ),
        ],
      ),
    );
  }
}

class _DesktopLifecycle extends ConsumerStatefulWidget {
  const _DesktopLifecycle({required this.child});
  final Widget child;

  @override
  ConsumerState<_DesktopLifecycle> createState() =>
      _DesktopLifecycleState();
}

class _DesktopLifecycleState extends ConsumerState<_DesktopLifecycle>
    with TrayListener, WindowListener {
  var _quitting = false;

  @override
  void initState() {
    super.initState();
    if (Platform.isWindows) {
      windowManager.addListener(this);
      trayManager.addListener(this);
      _configureTray();
    }
  }

  Future<void> _configureTray() async {
    final icon = path.join(
      path.dirname(Platform.resolvedExecutable),
      'app_icon.ico',
    );
    if (File(icon).existsSync()) await trayManager.setIcon(icon);
    await trayManager.setToolTip('Oix Node Helper');
    await trayManager.setContextMenu(Menu(items: [
      MenuItem(key: 'show', label: '显示 Oix Node Helper'),
      MenuItem.separator(),
      MenuItem(key: 'refresh', label: '刷新节点'),
      MenuItem(key: 'restart', label: '重启 Core'),
      MenuItem.separator(),
      MenuItem(key: 'quit', label: '退出'),
    ]));
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(snapshotProvider, (previous, next) {
      final health = next.value?.health;
      if (health != null && Platform.isWindows) {
        final text = health.lastError.isNotEmpty
            ? 'Oix Node Helper · 更新失败'
            : 'Oix Node Helper · ${health.stage}';
        trayManager.setToolTip(
          text.length > 120 ? text.substring(0, 120) : text,
        );
      }
    });
    return widget.child;
  }

  @override
  void onWindowClose() {
    if (_quitting) return;
    windowManager.hide();
  }

  @override
  void onTrayIconMouseDown() => _showWindow();

  @override
  void onTrayMenuItemClick(MenuItem menuItem) {
    switch (menuItem.key) {
      case 'show':
        _showWindow();
        return;
      case 'refresh':
        ref.read(hostActionsProvider.notifier).refresh();
        return;
      case 'restart':
        ref.read(hostActionsProvider.notifier).restart();
        return;
      case 'quit':
        _quit();
        return;
    }
  }

  Future<void> _showWindow() async {
    await windowManager.show();
    await windowManager.focus();
  }

  Future<void> _quit() async {
    if (_quitting) return;
    _quitting = true;
    try {
      final client = await ref.read(backendClientProvider.future);
      await client.dispose();
    } catch (_) {}
    await trayManager.destroy();
    await windowManager.setPreventClose(false);
    await windowManager.destroy();
  }

  @override
  void dispose() {
    if (Platform.isWindows) {
      trayManager.removeListener(this);
      windowManager.removeListener(this);
    }
    super.dispose();
  }
}

class _DesktopTitleBar extends StatefulWidget {
  const _DesktopTitleBar();

  @override
  State<_DesktopTitleBar> createState() => _DesktopTitleBarState();
}

class _DesktopTitleBarState extends State<_DesktopTitleBar>
    with WindowListener {
  var _maximized = false;

  @override
  void initState() {
    super.initState();
    windowManager.addListener(this);
    windowManager.isMaximized().then((value) {
      if (mounted) setState(() => _maximized = value);
    });
  }

  @override
  void onWindowMaximize() => setState(() => _maximized = true);

  @override
  void onWindowUnmaximize() => setState(() => _maximized = false);

  @override
  void dispose() {
    windowManager.removeListener(this);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return SizedBox(
      height: 46,
      child: Row(
        children: [
          Expanded(
            child: DragToMoveArea(
              child: Padding(
                padding: const EdgeInsets.only(left: 16),
                child: Row(
                  children: [
                    Icon(Icons.cloud_circle_rounded,
                        size: 20, color: scheme.primary),
                    const SizedBox(width: 8),
                    Text(
                      'Oix Node Helper',
                      style: Theme.of(context).textTheme.labelLarge,
                    ),
                  ],
                ),
              ),
            ),
          ),
          _CaptionButton(
            tooltip: '最小化',
            icon: Icons.remove_rounded,
            onPressed: windowManager.minimize,
          ),
          _CaptionButton(
            tooltip: _maximized ? '还原' : '最大化',
            icon: _maximized
                ? Icons.filter_none_rounded
                : Icons.crop_square_rounded,
            onPressed: () async {
              if (await windowManager.isMaximized()) {
                await windowManager.unmaximize();
              } else {
                await windowManager.maximize();
              }
            },
          ),
          _CaptionButton(
            tooltip: '关闭到托盘',
            icon: Icons.close_rounded,
            danger: true,
            onPressed: windowManager.close,
          ),
        ],
      ),
    );
  }
}

class _CaptionButton extends StatelessWidget {
  const _CaptionButton({
    required this.tooltip,
    required this.icon,
    required this.onPressed,
    this.danger = false,
  });
  final String tooltip;
  final IconData icon;
  final Future<void> Function() onPressed;
  final bool danger;

  @override
  Widget build(BuildContext context) => SizedBox(
        width: 48,
        height: 46,
        child: IconButton(
          tooltip: tooltip,
          style: danger
              ? IconButton.styleFrom(
                  hoverColor: Theme.of(context).colorScheme.error,
                )
              : null,
          onPressed: onPressed,
          icon: Icon(icon, size: 18),
        ),
      );
}
