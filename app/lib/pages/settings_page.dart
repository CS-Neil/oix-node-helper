import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/app_models.dart';
import '../providers/app_providers.dart';
import '../widgets/common_widgets.dart';

class SettingsPage extends ConsumerStatefulWidget {
  const SettingsPage({super.key});

  @override
  ConsumerState<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends ConsumerState<SettingsPage> {
  final _formKey = GlobalKey<FormState>();
  final _corePath = TextEditingController();
  final _token = TextEditingController();
  final _controllerUrl = TextEditingController();
  final _providerPort = TextEditingController();
  final _baseNodePort = TextEditingController();
  final _maxNodes = TextEditingController();
  final _pollSeconds = TextEditingController();
  final _retentionDays = TextEditingController();
  final _emptyThreshold = TextEditingController();
  final _includeRegex = TextEditingController();
  final _excludeRegex = TextEditingController();
  final _oixParams = TextEditingController();
  var _startWithWindows = false;
  var _tokenConfigured = false;
  var _loaded = false;
  var _showToken = false;

  @override
  void dispose() {
    for (final controller in [
      _corePath,
      _token,
      _controllerUrl,
      _providerPort,
      _baseNodePort,
      _maxNodes,
      _pollSeconds,
      _retentionDays,
      _emptyThreshold,
      _includeRegex,
      _excludeRegex,
      _oixParams,
    ]) {
      controller.dispose();
    }
    super.dispose();
  }

  void _load(HostSettings settings) {
    if (_loaded) return;
    _loaded = true;
    _corePath.text = settings.corePath;
    _controllerUrl.text = settings.controllerUrl;
    _providerPort.text = settings.providerPort.toString();
    _baseNodePort.text = settings.baseNodePort.toString();
    _maxNodes.text = settings.maxNodes.toString();
    _pollSeconds.text = settings.pollSeconds.toString();
    _retentionDays.text = settings.portRetentionDays.toString();
    _emptyThreshold.text = settings.emptyRefreshThreshold.toString();
    _includeRegex.text = settings.includeRegex;
    _excludeRegex.text = settings.excludeRegex;
    _oixParams.text = settings.oixParams;
    _startWithWindows = settings.startWithWindows;
    _tokenConfigured = settings.tokenConfigured;
  }

  @override
  Widget build(BuildContext context) {
    final settings = ref.watch(settingsProvider);
    final preferences = ref.watch(uiPreferencesProvider);
    return Scaffold(
      body: settings.when(
        loading: () => const LoadingPane(label: '正在读取设置…'),
        error: (error, _) => EmptyState(
          icon: Icons.settings_suggest_outlined,
          title: '设置读取失败',
          message: error.toString(),
          action: FilledButton.icon(
            onPressed: () => ref.read(settingsProvider.notifier).reload(),
            icon: const Icon(Icons.refresh_rounded),
            label: const Text('重试'),
          ),
        ),
        data: (value) {
          _load(value);
          return Form(
            key: _formKey,
            child: CustomScrollView(
              slivers: [
                SliverToBoxAdapter(
                  child: PageHeader(
                    title: '设置',
                    subtitle: _tokenConfigured
                        ? 'Access Token 已安全保存在当前 Windows 用户下'
                        : '请配置 Access Token 后开始刷新节点',
                  ),
                ),
                SliverPadding(
                  padding: const EdgeInsets.fromLTRB(20, 4, 20, 28),
                  sliver: SliverToBoxAdapter(
                    child: Align(
                      alignment: Alignment.topCenter,
                      child: ConstrainedBox(
                        constraints: const BoxConstraints(maxWidth: 980),
                        child: Column(
                          children: [
                            _Section(
                              icon: Icons.cloud_rounded,
                              title: 'OixCloud',
                              description: 'Token 仅发送给本机 Host，并由 DPAPI 加密保存。',
                              children: [
                                TextFormField(
                                  controller: _token,
                                  obscureText: !_showToken,
                                  decoration: InputDecoration(
                                    labelText: 'Access Token',
                                    hintText: _tokenConfigured
                                        ? '留空表示保留当前 Token'
                                        : '输入 Access Token',
                                    prefixIcon: const Icon(Icons.key_rounded),
                                    suffixIcon: IconButton(
                                      tooltip: _showToken ? '隐藏' : '显示',
                                      onPressed: () => setState(
                                        () => _showToken = !_showToken,
                                      ),
                                      icon: Icon(_showToken
                                          ? Icons.visibility_off_rounded
                                          : Icons.visibility_rounded),
                                    ),
                                  ),
                                  validator: (text) {
                                    if (!_tokenConfigured &&
                                        (text == null || text.trim().isEmpty)) {
                                      return '首次使用必须填写 Access Token';
                                    }
                                    return null;
                                  },
                                ),
                              ],
                            ),
                            const SizedBox(height: 14),
                            _Section(
                              icon: Icons.memory_rounded,
                              title: 'Core',
                              description: '官方 mihomo-oix 程序和本地 Controller。',
                              children: [
                                TextFormField(
                                  controller: _corePath,
                                  decoration: InputDecoration(
                                    labelText: 'mihomo-oix.exe 路径',
                                    prefixIcon:
                                        const Icon(Icons.terminal_rounded),
                                    suffixIcon: IconButton(
                                      tooltip: '选择文件',
                                      onPressed: _pickCore,
                                      icon: const Icon(Icons.folder_open_rounded),
                                    ),
                                  ),
                                  validator: _required,
                                ),
                                const SizedBox(height: 12),
                                TextFormField(
                                  controller: _controllerUrl,
                                  decoration: const InputDecoration(
                                    labelText: 'Controller URL',
                                    prefixIcon: Icon(Icons.lan_rounded),
                                  ),
                                  validator: (text) {
                                    final uri = Uri.tryParse(text ?? '');
                                    if (uri == null ||
                                        uri.scheme != 'http' ||
                                        !{'127.0.0.1', 'localhost'}
                                            .contains(uri.host)) {
                                      return '必须是 localhost 的 http:// 地址';
                                    }
                                    return null;
                                  },
                                ),
                              ],
                            ),
                            const SizedBox(height: 14),
                            _Section(
                              icon: Icons.settings_ethernet_rounded,
                              title: '端口与刷新',
                              description: '更改端口后 Host 会自动重启相关服务。',
                              children: [
                                _ResponsiveFields(children: [
                                  _NumberField(
                                    controller: _providerPort,
                                    label: 'Provider 端口',
                                    min: 1024,
                                    max: 65535,
                                  ),
                                  _NumberField(
                                    controller: _baseNodePort,
                                    label: '节点起始端口',
                                    min: 1024,
                                    max: 65000,
                                  ),
                                  _NumberField(
                                    controller: _maxNodes,
                                    label: '最大节点数',
                                    min: 1,
                                    max: 500,
                                  ),
                                  _NumberField(
                                    controller: _pollSeconds,
                                    label: '刷新间隔（秒）',
                                    min: 120,
                                    max: 86400,
                                  ),
                                  _NumberField(
                                    controller: _retentionDays,
                                    label: '端口保留天数',
                                    min: 1,
                                    max: 365,
                                  ),
                                  _NumberField(
                                    controller: _emptyThreshold,
                                    label: '空节点确认次数',
                                    min: 1,
                                    max: 10,
                                  ),
                                ]),
                              ],
                            ),
                            const SizedBox(height: 14),
                            _Section(
                              icon: Icons.filter_alt_rounded,
                              title: '筛选与高级选项',
                              description:
                                  '订阅参数决定官方返回哪些节点，正则表达式再在本地筛一遍。两者留空都表示不过滤。',
                              children: [
                                TextFormField(
                                  controller: _includeRegex,
                                  decoration: const InputDecoration(
                                    labelText: '包含正则',
                                    prefixIcon: Icon(Icons.add_circle_outline),
                                  ),
                                ),
                                const SizedBox(height: 12),
                                TextFormField(
                                  controller: _excludeRegex,
                                  decoration: const InputDecoration(
                                    labelText: '排除正则',
                                    prefixIcon:
                                        Icon(Icons.remove_circle_outline),
                                  ),
                                ),
                                const SizedBox(height: 12),
                                TextFormField(
                                  controller: _oixParams,
                                  validator: _validateOixParams,
                                  autovalidateMode:
                                      AutovalidateMode.onUserInteraction,
                                  decoration: const InputDecoration(
                                    labelText: '订阅过滤参数',
                                    hintText: '&mode=premium&love=1',
                                    helperText:
                                        '拼接到 OixCloud 订阅地址后面，决定官方返回哪些节点。留空表示使用套餐默认值。',
                                    helperMaxLines: 3,
                                    prefixIcon: Icon(Icons.tune_rounded),
                                  ),
                                ),
                                _OixParamsReadout(
                                  health: ref
                                      .watch(snapshotProvider)
                                      .value
                                      ?.health,
                                  onUseDefault: (value) => setState(
                                    () => _oixParams.text = value,
                                  ),
                                ),
                                const SizedBox(height: 8),
                                SwitchListTile(
                                  contentPadding: EdgeInsets.zero,
                                  title: const Text('登录 Windows 后自动启动'),
                                  subtitle: const Text('使用当前用户的启动项，不需要管理员权限'),
                                  value: _startWithWindows,
                                  onChanged: (value) => setState(
                                    () => _startWithWindows = value,
                                  ),
                                ),
                              ],
                            ),
                            const SizedBox(height: 14),
                            _ThemeSection(preferences: preferences.value),
                            const SizedBox(height: 22),
                            Align(
                              alignment: Alignment.centerRight,
                              child: FilledButton.icon(
                                onPressed: settings.isLoading ? null : _save,
                                icon: const Icon(Icons.save_rounded),
                                label: const Padding(
                                  padding: EdgeInsets.symmetric(vertical: 12),
                                  child: Text('保存并重启'),
                                ),
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
        },
      ),
    );
  }

  Future<void> _pickCore() async {
    final result = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: const ['exe'],
      dialogTitle: '选择官方 mihomo-oix.exe',
    );
    final selected = result?.files.single.path;
    if (selected != null) _corePath.text = selected;
  }

  String? _required(String? value) =>
      value == null || value.trim().isEmpty ? '此项不能为空' : null;

  // Mirrors AppController.ValidateOixParams so a malformed fragment is caught
  // before it reaches the Host, where it would otherwise silently change which
  // nodes the subscription returns.
  String? _validateOixParams(String? value) {
    final normalized = normalizeOixParams(value);
    if (normalized.isEmpty) return null;
    if (normalized.length > 512) return '订阅参数过长，请控制在 512 个字符以内';
    for (final pair in normalized.substring(1).split('&')) {
      final separator = pair.indexOf('=');
      if (separator <= 0 || separator == pair.length - 1) {
        return '必须是 key=value 形式，例如 &mode=premium（出错：$pair）';
      }
      if (pair.contains('#') || pair.contains(RegExp(r'\s'))) {
        return '不能包含空格或 #（出错：$pair）';
      }
    }
    return null;
  }

  int _number(TextEditingController controller) =>
      int.parse(controller.text.trim());

  Future<void> _save() async {
    if (!_formKey.currentState!.validate()) return;
    final settings = HostSettings(
      corePath: _corePath.text.trim(),
      controllerUrl: _controllerUrl.text.trim(),
      providerPort: _number(_providerPort),
      baseNodePort: _number(_baseNodePort),
      maxNodes: _number(_maxNodes),
      pollSeconds: _number(_pollSeconds),
      includeRegex: _includeRegex.text.trim(),
      excludeRegex: _excludeRegex.text.trim(),
      oixParams: normalizeOixParams(_oixParams.text),
      portRetentionDays: _number(_retentionDays),
      emptyRefreshThreshold: _number(_emptyThreshold),
      startWithWindows: _startWithWindows,
      tokenConfigured: _tokenConfigured || _token.text.trim().isNotEmpty,
    );
    final saved = await ref.read(settingsProvider.notifier).save(
          settings,
          accessToken: _token.text.trim().isEmpty ? null : _token.text.trim(),
        );
    if (!mounted) return;
    if (saved) {
      setState(() {
        _token.clear();
        _tokenConfigured = true;
        _loaded = false;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('设置已保存，后台正在重新加载')), 
      );
    } else {
      final error = ref.read(settingsProvider).error;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(error?.toString() ?? '保存失败')),
      );
    }
  }
}

/// Shows what the core is actually sending upstream. Without it the field can
/// read as empty while the account plan is quietly contributing parameters of
/// its own, which is exactly how a smaller-than-expected node count hides.
class _OixParamsReadout extends StatelessWidget {
  const _OixParamsReadout({required this.health, required this.onUseDefault});

  final HealthState? health;
  final ValueChanged<String> onUseDefault;

  @override
  Widget build(BuildContext context) {
    final state = health;
    if (state == null || state.oixParamsEffective.isEmpty) {
      return const SizedBox.shrink();
    }
    final theme = Theme.of(context);
    final muted = theme.textTheme.bodySmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );
    final planDefault = state.oixParamsDefault;
    return Padding(
      padding: const EdgeInsets.only(top: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 8,
            runSpacing: 4,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              Text('核心当前生效', style: muted),
              SelectableText(
                state.oixParamsEffective,
                style: theme.textTheme.bodySmall?.copyWith(
                  fontFamily: 'Consolas',
                  color: theme.colorScheme.primary,
                ),
              ),
            ],
          ),
          const SizedBox(height: 2),
          Text(
            '核心会自动补回它保留的键（例如 tfo），所以生效值通常比上面填的多。',
            style: muted,
          ),
          if (planDefault.isNotEmpty) ...[
            const SizedBox(height: 4),
            Row(
              children: [
                Expanded(
                  child: Text('套餐默认 $planDefault', style: muted),
                ),
                TextButton(
                  onPressed: () => onUseDefault(planDefault),
                  child: const Text('填入默认值'),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

class _Section extends StatelessWidget {
  const _Section({
    required this.icon,
    required this.title,
    required this.description,
    required this.children,
  });
  final IconData icon;
  final String title;
  final String description;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => ContentCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(icon, color: Theme.of(context).colorScheme.primary),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(title, style: Theme.of(context).textTheme.titleLarge),
                      const SizedBox(height: 2),
                      Text(
                        description,
                        style: Theme.of(context).textTheme.bodySmall?.copyWith(
                              color: Theme.of(context)
                                  .colorScheme
                                  .onSurfaceVariant,
                            ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 20),
            ...children,
          ],
        ),
      );
}

class _ResponsiveFields extends StatelessWidget {
  const _ResponsiveFields({required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
        builder: (context, constraints) {
          final columns = constraints.maxWidth >= 720 ? 3 : constraints.maxWidth >= 440 ? 2 : 1;
          final width = (constraints.maxWidth - (columns - 1) * 12) / columns;
          return Wrap(
            spacing: 12,
            runSpacing: 12,
            children: children
                .map((child) => SizedBox(width: width, child: child))
                .toList(growable: false),
          );
        },
      );
}

class _NumberField extends StatelessWidget {
  const _NumberField({
    required this.controller,
    required this.label,
    required this.min,
    required this.max,
  });
  final TextEditingController controller;
  final String label;
  final int min;
  final int max;

  @override
  Widget build(BuildContext context) => TextFormField(
        controller: controller,
        keyboardType: TextInputType.number,
        decoration: InputDecoration(labelText: label),
        validator: (text) {
          final value = int.tryParse(text ?? '');
          if (value == null || value < min || value > max) {
            return '请输入 $min–$max';
          }
          return null;
        },
      );
}

class _ThemeSection extends ConsumerWidget {
  const _ThemeSection({required this.preferences});
  final UiPreferences? preferences;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = preferences ??
        const UiPreferences(
          themeMode: ThemeMode.system,
          pureBlack: false,
          seedColor: 0xFF6750A4,
        );
    return _Section(
      icon: Icons.palette_rounded,
      title: '外观',
      description: 'Material 3 配色、深色模式与动态过渡。',
      children: [
        SegmentedButton<ThemeMode>(
          segments: const [
            ButtonSegment(
              value: ThemeMode.system,
              icon: Icon(Icons.brightness_auto_rounded),
              label: Text('跟随系统'),
            ),
            ButtonSegment(
              value: ThemeMode.light,
              icon: Icon(Icons.light_mode_rounded),
              label: Text('浅色'),
            ),
            ButtonSegment(
              value: ThemeMode.dark,
              icon: Icon(Icons.dark_mode_rounded),
              label: Text('深色'),
            ),
          ],
          selected: {value.themeMode},
          onSelectionChanged: (selection) => ref
              .read(uiPreferencesProvider.notifier)
              .setPreferences(themeMode: selection.first),
        ),
        const SizedBox(height: 10),
        SwitchListTile(
          contentPadding: EdgeInsets.zero,
          title: const Text('纯黑深色主题'),
          subtitle: const Text('在深色模式下使用纯黑背景'),
          value: value.pureBlack,
          onChanged: (enabled) => ref
              .read(uiPreferencesProvider.notifier)
              .setPreferences(pureBlack: enabled),
        ),
      ],
    );
  }
}
