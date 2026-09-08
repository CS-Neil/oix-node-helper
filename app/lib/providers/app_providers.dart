import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../backend/host_client.dart';
import '../models/app_models.dart';

enum AppDestination { dashboard, nodes, settings, logs, about }

final backendClientProvider = FutureProvider<HostClient>((ref) async {
  final runtime = await HostLauncher.launch();
  final client = HostClient(runtime);
  ref.onDispose(client.dispose);
  return client;
});

final snapshotProvider = StreamProvider<AppSnapshot>((ref) async* {
  final client = await ref.watch(backendClientProvider.future);
  yield await client.getSnapshot();
  yield* client.watchSnapshots();
});

final settingsProvider =
    AsyncNotifierProvider<SettingsController, HostSettings>(
  SettingsController.new,
);

class SettingsController extends AsyncNotifier<HostSettings> {
  @override
  Future<HostSettings> build() async {
    final client = await ref.watch(backendClientProvider.future);
    return client.getSettings();
  }

  Future<void> reload() async {
    state = const AsyncLoading();
    state = await AsyncValue.guard(() async {
      final client = await ref.read(backendClientProvider.future);
      return client.getSettings();
    });
  }

  Future<bool> save(HostSettings settings, {String? accessToken}) async {
    final previous = state.value;
    state = const AsyncLoading();
    final result = await AsyncValue.guard(() async {
      final client = await ref.read(backendClientProvider.future);
      return client.saveSettings(settings, accessToken: accessToken);
    });
    state = result;
    if (result.hasError && previous != null) {
      state = AsyncError(result.error!, result.stackTrace!);
    }
    return !result.hasError;
  }
}

final hostActionsProvider =
    AsyncNotifierProvider<HostActionsController, void>(
  HostActionsController.new,
);

class HostActionsController extends AsyncNotifier<void> {
  @override
  FutureOr<void> build() {}

  Future<void> refresh() => _run((client) => client.refresh());
  Future<void> restart() => _run((client) => client.restart());
  Future<void> openDataFolder() =>
      _run((client) => client.openDataFolder());
  Future<void> openProvider() => _run((client) => client.openProvider());

  Future<void> _run(Future<void> Function(HostClient client) action) async {
    if (state.isLoading) return;
    state = const AsyncLoading();
    state = await AsyncValue.guard(() async {
      final client = await ref.read(backendClientProvider.future);
      await action(client);
    });
  }
}

final logsProvider = AsyncNotifierProvider<LogsController, List<String>>(
  LogsController.new,
);

class LogsController extends AsyncNotifier<List<String>> {
  @override
  Future<List<String>> build() async {
    final client = await ref.watch(backendClientProvider.future);
    return client.getLogs();
  }

  Future<void> reload() async {
    state = const AsyncLoading();
    state = await AsyncValue.guard(() async {
      final client = await ref.read(backendClientProvider.future);
      return client.getLogs();
    });
  }
}

final navigationProvider =
    NotifierProvider<NavigationController, AppDestination>(
  NavigationController.new,
);

class NavigationController extends Notifier<AppDestination> {
  @override
  AppDestination build() => AppDestination.dashboard;

  void select(AppDestination destination) => state = destination;
}

final nodeFilterProvider = NotifierProvider<NodeFilterController, String>(
  NodeFilterController.new,
);

class NodeFilterController extends Notifier<String> {
  @override
  String build() => '';

  void update(String value) => state = value.trim().toLowerCase();
}

final filteredNodesProvider = Provider<List<NodeInfo>>((ref) {
  final query = ref.watch(nodeFilterProvider);
  final snapshot = ref.watch(snapshotProvider).value;
  final nodes = snapshot?.nodes ?? const <NodeInfo>[];
  if (query.isEmpty) return nodes;
  return nodes
      .where((node) =>
          node.name.toLowerCase().contains(query) ||
          node.type.toLowerCase().contains(query) ||
          node.endpoint.contains(query))
      .toList(growable: false);
});

class UiPreferences {
  const UiPreferences({
    required this.themeMode,
    required this.pureBlack,
    required this.seedColor,
  });

  final ThemeMode themeMode;
  final bool pureBlack;
  final int seedColor;

  UiPreferences copyWith({
    ThemeMode? themeMode,
    bool? pureBlack,
    int? seedColor,
  }) =>
      UiPreferences(
        themeMode: themeMode ?? this.themeMode,
        pureBlack: pureBlack ?? this.pureBlack,
        seedColor: seedColor ?? this.seedColor,
      );
}

final uiPreferencesProvider =
    AsyncNotifierProvider<UiPreferencesController, UiPreferences>(
  UiPreferencesController.new,
);

class UiPreferencesController extends AsyncNotifier<UiPreferences> {
  static const _themeKey = 'themeMode';
  static const _pureBlackKey = 'pureBlack';
  static const _seedColorKey = 'seedColor';

  @override
  Future<UiPreferences> build() async {
    final preferences = await SharedPreferences.getInstance();
    final themeName = preferences.getString(_themeKey) ?? 'system';
    return UiPreferences(
      themeMode: ThemeMode.values.firstWhere(
        (mode) => mode.name == themeName,
        orElse: () => ThemeMode.system,
      ),
      pureBlack: preferences.getBool(_pureBlackKey) ?? false,
      seedColor: preferences.getInt(_seedColorKey) ?? 0xFF6750A4,
    );
  }

  Future<void> setPreferences({
    ThemeMode? themeMode,
    bool? pureBlack,
    int? seedColor,
  }) async {
    final value = (state.value ??
            const UiPreferences(
              themeMode: ThemeMode.system,
              pureBlack: false,
              seedColor: 0xFF6750A4,
            ))
        .copyWith(
      themeMode: themeMode,
      pureBlack: pureBlack,
      seedColor: seedColor,
    );
    state = AsyncData(value);
    final preferences = await SharedPreferences.getInstance();
    await preferences.setString(_themeKey, value.themeMode.name);
    await preferences.setBool(_pureBlackKey, value.pureBlack);
    await preferences.setInt(_seedColorKey, value.seedColor);
  }
}
