import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../providers/app_providers.dart';
import '../widgets/common_widgets.dart';

class AboutPage extends ConsumerWidget {
  const AboutPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final version = ref.watch(
      snapshotProvider.select((state) => state.value?.health.version ?? '—'),
    );
    return Scaffold(
      body: CustomScrollView(
        slivers: [
          const SliverToBoxAdapter(
            child: PageHeader(
              title: '关于',
              subtitle: 'OixNodeHelper 的版本与项目边界',
            ),
          ),
          SliverPadding(
            padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
            sliver: SliverToBoxAdapter(
              child: Align(
                alignment: Alignment.topCenter,
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 720),
                  child: Column(
                    children: [
                      ContentCard(
                        child: Column(
                          children: [
                            Icon(
                              Icons.cloud_circle_rounded,
                              size: 82,
                              color: Theme.of(context).colorScheme.primary,
                            ),
                            const SizedBox(height: 14),
                            Text(
                              'Oix Node Helper',
                              style: Theme.of(context).textTheme.headlineSmall,
                            ),
                            const SizedBox(height: 6),
                            Text('Host 版本 $version'),
                            const SizedBox(height: 18),
                            Text(
                              'Flutter + Material 3 前端，配合独立的 C# 后台管理官方 mihomo-oix Core。',
                              textAlign: TextAlign.center,
                              style: TextStyle(
                                color: Theme.of(context)
                                    .colorScheme
                                    .onSurfaceVariant,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 14),
                      ContentCard(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text('项目边界',
                                style: Theme.of(context).textTheme.titleLarge),
                            const SizedBox(height: 12),
                            const _BoundaryItem('只通过官方 Core 的公开参数和 Controller API 工作'),
                            const _BoundaryItem('不读取或修改 FlClash 的数据库与配置'),
                            const _BoundaryItem('不实现或逆向 OixCloud 私有协议'),
                            const _BoundaryItem('所有本地服务仅监听回环地址'),
                          ],
                        ),
                      ),
                      const SizedBox(height: 14),
                      ContentCard(
                        onTap: () => launchUrl(
                          Uri.parse('https://oixcloud.com/user/token'),
                        ),
                        child: const ListTile(
                          contentPadding: EdgeInsets.zero,
                          leading: Icon(Icons.open_in_browser_rounded),
                          title: Text('打开 OixCloud Token 页面'),
                          trailing: Icon(Icons.chevron_right_rounded),
                        ),
                      ),
                    ],
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

class _BoundaryItem extends StatelessWidget {
  const _BoundaryItem(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 7),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(
              Icons.check_circle_outline_rounded,
              size: 20,
              color: Theme.of(context).colorScheme.primary,
            ),
            const SizedBox(width: 10),
            Expanded(child: Text(text)),
          ],
        ),
      );
}
