import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

class PageHeader extends StatelessWidget {
  const PageHeader({
    super.key,
    required this.title,
    this.subtitle,
    this.actions = const [],
  });

  final String title;
  final String? subtitle;
  final List<Widget> actions;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(24, 20, 16, 12),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: Theme.of(context).textTheme.headlineMedium),
                  if (subtitle != null) ...[
                    const SizedBox(height: 4),
                    Text(
                      subtitle!,
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                            color: Theme.of(context)
                                .colorScheme
                                .onSurfaceVariant,
                          ),
                    ),
                  ],
                ],
              ),
            ),
            ...actions,
          ],
        ),
      );
}

class ContentCard extends StatelessWidget {
  const ContentCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(20),
    this.color,
    this.onTap,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final Color? color;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Card(
        color: color,
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(24),
          child: Padding(padding: padding, child: child),
        ),
      );
}

class StatusPill extends StatelessWidget {
  const StatusPill({
    super.key,
    required this.label,
    required this.kind,
  });

  final String label;
  final StatusKind kind;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final semantics = Theme.of(context).extension<AppSemanticColors>()!;
    final (background, foreground, icon) = switch (kind) {
      StatusKind.success =>
        (semantics.success, semantics.onSuccess, Icons.check_circle_rounded),
      StatusKind.warning =>
        (semantics.warning, semantics.onWarning, Icons.warning_rounded),
      StatusKind.error =>
        (scheme.error, scheme.onError, Icons.error_rounded),
      StatusKind.neutral => (
          scheme.surfaceContainerHighest,
          scheme.onSurfaceVariant,
          Icons.info_rounded,
        ),
    };
    return AnimatedContainer(
      duration: AppMotion.standard,
      curve: AppMotion.curve,
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
      decoration: ShapeDecoration(
        color: background,
        shape: const StadiumBorder(),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 16, color: foreground),
          const SizedBox(width: 6),
          Text(
            label,
            style: Theme.of(context)
                .textTheme
                .labelLarge
                ?.copyWith(color: foreground),
          ),
        ],
      ),
    );
  }
}

enum StatusKind { success, warning, error, neutral }

class EmptyState extends StatelessWidget {
  const EmptyState({
    super.key,
    required this.icon,
    required this.title,
    required this.message,
    this.action,
  });

  final IconData icon;
  final String title;
  final String message;
  final Widget? action;

  @override
  Widget build(BuildContext context) => Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: Padding(
            padding: const EdgeInsets.all(32),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  icon,
                  size: 64,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(height: 20),
                Text(title, style: Theme.of(context).textTheme.headlineSmall),
                const SizedBox(height: 8),
                Text(
                  message,
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.bodyLarge?.copyWith(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                ),
                if (action != null) ...[
                  const SizedBox(height: 24),
                  action!,
                ],
              ],
            ),
          ),
        ),
      );
}

class LoadingPane extends StatelessWidget {
  const LoadingPane({super.key, this.label = '正在连接后台…'});
  final String label;

  @override
  Widget build(BuildContext context) => Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const CircularProgressIndicator(),
            const SizedBox(height: 18),
            Text(label),
          ],
        ),
      );
}
