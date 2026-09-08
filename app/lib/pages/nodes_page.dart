import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/app_models.dart';
import '../providers/app_providers.dart';
import '../widgets/common_widgets.dart';

class NodesPage extends ConsumerWidget {
  const NodesPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final snapshot = ref.watch(snapshotProvider);
    final nodes = ref.watch(filteredNodesProvider);
    return Scaffold(
      body: Column(
        children: [
          PageHeader(
            title: '节点',
            subtitle: snapshot.value == null
                ? '正在读取节点…'
                : '共 ${snapshot.value!.nodes.length} 个本地端口映射',
            actions: [
              IconButton.filledTonal(
                tooltip: '刷新节点',
                onPressed: () =>
                    ref.read(hostActionsProvider.notifier).refresh(),
                icon: const Icon(Icons.refresh_rounded),
              ),
            ],
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 4, 20, 14),
            child: TextField(
              onChanged: ref.read(nodeFilterProvider.notifier).update,
              decoration: const InputDecoration(
                prefixIcon: Icon(Icons.search_rounded),
                hintText: '搜索名称、协议或端口',
              ),
            ),
          ),
          Expanded(
            child: AnimatedSwitcher(
              duration: const Duration(milliseconds: 260),
              child: snapshot.when(
                loading: () => const LoadingPane(label: '正在读取节点…'),
                error: (error, _) => EmptyState(
                  icon: Icons.cloud_off_rounded,
                  title: '节点读取失败',
                  message: error.toString(),
                ),
                data: (_) => nodes.isEmpty
                    ? const EmptyState(
                        icon: Icons.hub_outlined,
                        title: '没有匹配的节点',
                        message: '刷新节点，或者调整上方的搜索条件。',
                      )
                    : _NodeGrid(nodes: nodes),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _NodeGrid extends StatelessWidget {
  const _NodeGrid({required this.nodes});
  final List<NodeInfo> nodes;

  @override
  Widget build(BuildContext context) => GridView.builder(
        key: ValueKey(nodes.map((node) => node.name).join('|')),
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
        gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
          maxCrossAxisExtent: 340,
          mainAxisExtent: 132,
          crossAxisSpacing: 12,
          mainAxisSpacing: 12,
        ),
        itemCount: nodes.length,
        itemBuilder: (context, index) => _NodeCard(node: nodes[index]),
      );
}

class _NodeCard extends StatelessWidget {
  const _NodeCard({required this.node});
  final NodeInfo node;

  @override
  Widget build(BuildContext context) => ContentCard(
        padding: const EdgeInsets.all(16),
        onTap: () async {
          await Clipboard.setData(ClipboardData(text: node.endpoint));
          if (context.mounted) {
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(content: Text('${node.name} 的地址已复制')),
            );
          }
        },
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                CircleAvatar(
                  radius: 18,
                  child: Text(
                    node.type.isEmpty ? '?' : node.type[0].toUpperCase(),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    node.name,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                const Icon(Icons.content_copy_rounded, size: 18),
              ],
            ),
            const Spacer(),
            Row(
              children: [
                Chip(
                  visualDensity: VisualDensity.compact,
                  label: Text(node.type.isEmpty ? '未知协议' : node.type),
                ),
                const Spacer(),
                Text(
                  node.endpoint,
                  style: Theme.of(context).textTheme.labelLarge,
                ),
              ],
            ),
          ],
        ),
      );
}
