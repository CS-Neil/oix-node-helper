import 'package:animations/animations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../pages/about_page.dart';
import '../pages/dashboard_page.dart';
import '../pages/logs_page.dart';
import '../pages/nodes_page.dart';
import '../pages/settings_page.dart';
import '../providers/app_providers.dart';
import '../theme/app_theme.dart';

class AdaptiveShell extends ConsumerWidget {
  const AdaptiveShell({super.key});

  static const _destinations = <_DestinationData>[
    _DestinationData(
      AppDestination.dashboard,
      '概览',
      Icons.dashboard_outlined,
      Icons.dashboard_rounded,
    ),
    _DestinationData(
      AppDestination.nodes,
      '节点',
      Icons.hub_outlined,
      Icons.hub_rounded,
    ),
    _DestinationData(
      AppDestination.settings,
      '设置',
      Icons.tune_outlined,
      Icons.tune_rounded,
    ),
    _DestinationData(
      AppDestination.logs,
      '日志',
      Icons.subject_outlined,
      Icons.subject_rounded,
    ),
    _DestinationData(
      AppDestination.about,
      '关于',
      Icons.info_outline_rounded,
      Icons.info_rounded,
    ),
  ];

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final current = ref.watch(navigationProvider);
    final selectedIndex = _destinations.indexWhere((e) => e.value == current);
    final reduceMotion = MediaQuery.disableAnimationsOf(context);
    final body = PageTransitionSwitcher(
      duration: reduceMotion ? Duration.zero : AppMotion.emphasized,
      reverse: false,
      transitionBuilder: (child, primary, secondary) => SharedAxisTransition(
        animation: primary,
        secondaryAnimation: secondary,
        transitionType: SharedAxisTransitionType.horizontal,
        fillColor: Colors.transparent,
        child: child,
      ),
      child: KeyedSubtree(key: ValueKey(current), child: _pageFor(current)),
    );

    return LayoutBuilder(
      builder: (context, constraints) {
        final compact = constraints.maxWidth < 600;
        final expanded = constraints.maxWidth >= 1024;
        if (compact) {
          return Scaffold(
            body: body,
            bottomNavigationBar: NavigationBar(
              selectedIndex: selectedIndex,
              destinations: _destinations
                  .map((item) => NavigationDestination(
                        icon: Icon(item.icon),
                        selectedIcon: Icon(item.selectedIcon),
                        label: item.label,
                      ))
                  .toList(growable: false),
              onDestinationSelected: (index) => ref
                  .read(navigationProvider.notifier)
                  .select(_destinations[index].value),
            ),
          );
        }

        return Scaffold(
          body: Row(
            children: [
              NavigationRail(
                extended: expanded,
                minExtendedWidth: 216,
                selectedIndex: selectedIndex,
                groupAlignment: -0.82,
                leading: Padding(
                  padding: const EdgeInsets.only(top: 14, bottom: 24),
                  child: expanded
                      ? Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Icon(
                              Icons.cloud_circle_rounded,
                              color: Theme.of(context).colorScheme.primary,
                              size: 30,
                            ),
                            const SizedBox(width: 10),
                            Text(
                              'Oix Helper',
                              style: Theme.of(context).textTheme.titleMedium,
                            ),
                          ],
                        )
                      : Icon(
                          Icons.cloud_circle_rounded,
                          color: Theme.of(context).colorScheme.primary,
                          size: 30,
                        ),
                ),
                destinations: _destinations
                    .map((item) => NavigationRailDestination(
                          icon: Icon(item.icon),
                          selectedIcon: Icon(item.selectedIcon),
                          label: Text(item.label),
                        ))
                    .toList(growable: false),
                onDestinationSelected: (index) => ref
                    .read(navigationProvider.notifier)
                    .select(_destinations[index].value),
              ),
              VerticalDivider(
                width: 1,
                thickness: 1,
                color: Theme.of(context).colorScheme.outlineVariant,
              ),
              Expanded(child: body),
            ],
          ),
        );
      },
    );
  }

  Widget _pageFor(AppDestination destination) => switch (destination) {
        AppDestination.dashboard => const DashboardPage(),
        AppDestination.nodes => const NodesPage(),
        AppDestination.settings => const SettingsPage(),
        AppDestination.logs => const LogsPage(),
        AppDestination.about => const AboutPage(),
      };
}

class _DestinationData {
  const _DestinationData(
    this.value,
    this.label,
    this.icon,
    this.selectedIcon,
  );

  final AppDestination value;
  final String label;
  final IconData icon;
  final IconData selectedIcon;
}
