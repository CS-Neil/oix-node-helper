import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/app_models.dart';
import '../providers/app_providers.dart';
import '../widgets/common_widgets.dart';

class DashboardPage extends ConsumerWidget {
  const DashboardPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final snapshot = ref.watch(snapshotProvider);
    final action = ref.watch(hostActionsProvider);
    ref.listen(hostActionsProvider, (previous, next) {
      if (next.hasError && previous?.error != next.error) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(next.error.toString())),
        );
      }
    });

    return Scaffold(
      floatingActionButton: FloatingActionButton.extended(
        onPressed: action.isLoading
            ? null
            : () => ref.read(hostActionsProvider.notifier).refresh(),
        icon: action.isLoading
            ? const SizedBox.square(
                dimension: 20,
                child: CircularProgressIndicator(strokeWidth: 2),
              )
            : const Icon(Icons.refresh_rounded),
        label: const Text('立即刷新'),
      ),
      body: snapshot.when(
        loading: () => const LoadingPane(),
        error: (error, _) => EmptyState(
          icon: Icons.cloud_off_rounded,
          title: '无法连接后台',
          message: error.toString(),
          action: FilledButton.icon(
            onPressed: () => ref.invalidate(backendClientProvider),
            icon: const Icon(Icons.replay_rounded),
            label: const Text('重新连接'),
          ),
        ),
        data: (value) => _Dashboard(snapshot: value),
      ),
    );
  }
}

class _Dashboard extends ConsumerWidget {
  const _Dashboard({required this.snapshot});
  final AppSnapshot snapshot;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final health = snapshot.health;
    return CustomScrollView(
      slivers: [
        const SliverToBoxAdapter(
          child: PageHeader(
            title: '概览',
            subtitle: 'OixCloud 节点与本地 Provider 的运行状态',
          ),
        ),
        SliverPadding(
          padding: const EdgeInsets.fromLTRB(20, 4, 20, 100),
          sliver: SliverGrid(
            gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
              maxCrossAxisExtent: 360,
              mainAxisExtent: 184,
              crossAxisSpacing: 14,
              mainAxisSpacing: 14,
            ),
            delegate: SliverChildListDelegate([
              _CoreStatusCard(health: health),
              _MetricCard(
                icon: Icons.hub_rounded,
                label: '当前节点',
                value: health.nodeCount.toString(),
                supporting: health.nodeCount == 0 ? '等待节点刷新' : '本地端口映射已建立',
                onTap: () => ref
                    .read(navigationProvider.notifier)
                    .select(AppDestination.nodes),
              ),
              _MetricCard(
                icon: Icons.schedule_rounded,
                label: '上次更新',
                value: _formatTime(health.lastRefresh),
                supporting: health.lastRefresh == null
                    ? '尚未成功刷新'
                    : _formatDate(health.lastRefresh!),
              ),
              _ProviderCard(url: snapshot.providerUrl),
              _ActionCard(
                icon: Icons.restart_alt_rounded,
                title: '重启 Core',
                description: '重新启动 mihomo-oix 并加载最后可用配置',
                onTap: () => ref.read(hostActionsProvider.notifier).restart(),
              ),
              _ActionCard(
                icon: Icons.folder_open_rounded,
                title: '数据目录',
                description: '打开配置、缓存和脱敏日志所在目录',
                onTap: () =>
                    ref.read(hostActionsProvider.notifier).openDataFolder(),
              ),
              if (health.lastError.isNotEmpty)
                _ErrorCard(message: health.lastError),
            ]),
          ),
        ),
      ],
    );
  }

  static String _formatTime(DateTime? value) {
    if (value == null) return '—';
    return '${value.hour.toString().padLeft(2, '0')}:${value.minute.toString().padLeft(2, '0')}';
  }

  static String _formatDate(DateTime value) =>
      '${value.year}-${value.month.toString().padLeft(2, '0')}-${value.day.toString().padLeft(2, '0')}';
}

class _CoreStatusCard extends StatelessWidget {
  const _CoreStatusCard({required this.health});
  final HealthState health;

  @override
  Widget build(BuildContext context) {
    final hasError = health.lastError.isNotEmpty;
    final kind = hasError
        ? StatusKind.error
        : health.coreRunning
            ? StatusKind.success
            : StatusKind.warning;
    final label = hasError
        ? '运行异常'
        : health.coreRunning
            ? '运行正常'
            : '等待启动';
    return ContentCard(
      color: Theme.of(context).colorScheme.primaryContainer,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Icon(
                Icons.power_settings_new_rounded,
                color: Theme.of(context).colorScheme.onPrimaryContainer,
              ),
              StatusPill(label: label, kind: kind),
            ],
          ),
          const Spacer(),
          Text('Core 状态', style: Theme.of(context).textTheme.labelLarge),
          const SizedBox(height: 6),
          AnimatedSwitcher(
            duration: const Duration(milliseconds: 240),
            child: Text(
              health.stage.isEmpty ? label : health.stage,
              key: ValueKey(health.stage),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.titleLarge,
            ),
          ),
        ],
      ),
    );
  }
}

class _MetricCard extends StatelessWidget {
  const _MetricCard({
    required this.icon,
    required this.label,
    required this.value,
    required this.supporting,
    this.onTap,
  });
  final IconData icon;
  final String label;
  final String value;
  final String supporting;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => ContentCard(
        onTap: onTap,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: Theme.of(context).colorScheme.primary),
            const Spacer(),
            Text(label, style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 4),
            AnimatedSwitcher(
              duration: const Duration(milliseconds: 240),
              child: Text(
                value,
                key: ValueKey(value),
                style: Theme.of(context).textTheme.headlineMedium,
              ),
            ),
            const SizedBox(height: 3),
            Text(
              supporting,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
            ),
          ],
        ),
      );
}

class _ProviderCard extends StatelessWidget {
  const _ProviderCard({required this.url});
  final String url;

  @override
  Widget build(BuildContext context) => ContentCard(
        onTap: () async {
          await Clipboard.setData(ClipboardData(text: url));
          if (context.mounted) {
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(content: Text('Provider 地址已复制')),
            );
          }
        },
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(Icons.link_rounded,
                    color: Theme.of(context).colorScheme.primary),
                const Spacer(),
                const Icon(Icons.content_copy_rounded, size: 20),
              ],
            ),
            const Spacer(),
            Text('Provider 地址', style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 8),
            Text(
              url,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodyLarge,
            ),
          ],
        ),
      );
}

class _ActionCard extends StatelessWidget {
  const _ActionCard({
    required this.icon,
    required this.title,
    required this.description,
    required this.onTap,
  });
  final IconData icon;
  final String title;
  final String description;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => ContentCard(
        onTap: onTap,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: Theme.of(context).colorScheme.primary),
            const Spacer(),
            Text(title, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 6),
            Text(
              description,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
            ),
          ],
        ),
      );
}

class _ErrorCard extends StatelessWidget {
  const _ErrorCard({required this.message});
  final String message;

  @override
  Widget build(BuildContext context) => ContentCard(
        color: Theme.of(context).colorScheme.errorContainer,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(Icons.error_rounded,
                color: Theme.of(context).colorScheme.onErrorContainer),
            const Spacer(),
            Text('最近错误', style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 6),
            Text(
              message,
              maxLines: 3,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                color: Theme.of(context).colorScheme.onErrorContainer,
              ),
            ),
          ],
        ),
      );
}
