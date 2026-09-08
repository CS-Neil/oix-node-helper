import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../providers/app_providers.dart';
import '../widgets/common_widgets.dart';

class LogsPage extends ConsumerWidget {
  const LogsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final logs = ref.watch(logsProvider);
    return Scaffold(
      body: Column(
        children: [
          PageHeader(
            title: '日志',
            subtitle: '敏感信息已由后台脱敏',
            actions: [
              IconButton(
                tooltip: '复制日志',
                onPressed: logs.value == null
                    ? null
                    : () async {
                        await Clipboard.setData(
                          ClipboardData(text: logs.value!.join('\n')),
                        );
                        if (context.mounted) {
                          ScaffoldMessenger.of(context).showSnackBar(
                            const SnackBar(content: Text('日志已复制')),
                          );
                        }
                      },
                icon: const Icon(Icons.content_copy_rounded),
              ),
              IconButton.filledTonal(
                tooltip: '重新读取',
                onPressed: () => ref.read(logsProvider.notifier).reload(),
                icon: const Icon(Icons.refresh_rounded),
              ),
            ],
          ),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(20, 4, 20, 20),
              child: ContentCard(
                padding: EdgeInsets.zero,
                child: logs.when(
                  loading: () => const LoadingPane(label: '正在读取日志…'),
                  error: (error, _) => EmptyState(
                    icon: Icons.error_outline_rounded,
                    title: '日志读取失败',
                    message: error.toString(),
                  ),
                  data: (lines) => lines.isEmpty
                      ? const EmptyState(
                          icon: Icons.subject_rounded,
                          title: '暂无日志',
                          message: '后台产生运行记录后会显示在这里。',
                        )
                      : SelectionArea(
                          child: ListView.separated(
                            padding: const EdgeInsets.all(16),
                            itemCount: lines.length,
                            separatorBuilder: (_, _) => const SizedBox(height: 4),
                            itemBuilder: (context, index) => Text(
                              lines[index],
                              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                                    fontFamily: 'Consolas',
                                    height: 1.45,
                                  ),
                            ),
                          ),
                        ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
