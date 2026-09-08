import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:path/path.dart' as path;

import '../models/app_models.dart';

class HostRuntime {
  const HostRuntime({
    required this.process,
    required this.port,
    required this.sessionKey,
  });

  final Process process;
  final int port;
  final String sessionKey;
}

class HostLauncher {
  static Future<HostRuntime> launch() async {
    final hostPath = _resolveHostPath();
    if (hostPath == null) {
      throw const HostException(
        '找不到 OixNodeHost.exe。请先运行 build.ps1 构建后台程序。',
        code: 'host_not_found',
      );
    }

    final process = await Process.start(
      hostPath,
      ['--parent-pid=$pid'],
      mode: ProcessStartMode.normal,
      workingDirectory: path.dirname(hostPath),
    );
    final sessionKey = _createSessionKey();
    process.stdin.writeln(sessionKey);
    await process.stdin.flush();

    final handshakeLine = await process.stdout
        .transform(utf8.decoder)
        .transform(const LineSplitter())
        .first
        .timeout(const Duration(seconds: 15));
    final handshake = decodeJsonObject(handshakeLine);
    if (handshake['ok'] != true) {
      process.kill();
      throw HostException(
        handshake['error'] as String? ?? '后台程序启动失败。',
        code: 'host_start_failed',
      );
    }
    final protocol = (handshake['protocolVersion'] as num?)?.toInt() ?? 0;
    if (protocol != 1) {
      process.kill();
      throw HostException(
        'GUI 与后台协议版本不兼容（Host: $protocol，GUI: 1）。',
        code: 'protocol_mismatch',
      );
    }
    return HostRuntime(
      process: process,
      port: (handshake['port'] as num).toInt(),
      sessionKey: sessionKey,
    );
  }

  static String? _resolveHostPath() {
    final executableDirectory = path.dirname(Platform.resolvedExecutable);
    final candidates = <String>[
      path.join(executableDirectory, 'OixNodeHost.exe'),
      path.join(Directory.current.path, 'OixNodeHost.exe'),
      path.join(Directory.current.path, 'build', 'OixNodeHost.exe'),
      path.join(Directory.current.parent.path, 'build', 'OixNodeHost.exe'),
    ];
    for (final candidate in candidates) {
      if (File(candidate).existsSync()) return path.normalize(candidate);
    }
    return null;
  }

  static String _createSessionKey() {
    final random = Random.secure();
    final bytes = List<int>.generate(32, (_) => random.nextInt(256));
    return base64UrlEncode(bytes).replaceAll('=', '');
  }
}

class HostClient {
  HostClient(this.runtime);

  final HostRuntime runtime;
  int _requestId = 0;
  bool _disposed = false;

  Future<Map<String, dynamic>> call(
    String method, [
    Map<String, dynamic>? parameters,
  ]) async {
    if (_disposed) {
      throw const HostException('后台连接已经关闭。', code: 'disposed');
    }
    final socket = await Socket.connect(
      InternetAddress.loopbackIPv4,
      runtime.port,
      timeout: const Duration(seconds: 5),
    );
    try {
      final id = (++_requestId).toString();
      socket.writeln(jsonEncode({
        'id': id,
        'auth': runtime.sessionKey,
        'method': method,
        'params': ?parameters,
      }));
      await socket.flush();
      final line = await socket
          .cast<List<int>>()
          .transform(utf8.decoder)
          .transform(const LineSplitter())
          .first
          .timeout(const Duration(seconds: 20));
      final response = decodeJsonObject(line);
      if (response['ok'] != true) {
        final error = Map<String, dynamic>.from(
          response['error'] as Map? ?? const {},
        );
        throw HostException(
          error['message'] as String? ?? '后台操作失败。',
          code: error['code'] as String? ?? 'host_error',
        );
      }
      return Map<String, dynamic>.from(
        response['result'] as Map? ?? const {},
      );
    } finally {
      await socket.close();
    }
  }

  Future<AppSnapshot> getSnapshot() async =>
      AppSnapshot.fromJson(await call('snapshot.get'));

  Future<HostSettings> getSettings() async =>
      HostSettings.fromJson(await call('settings.get'));

  Future<HostSettings> saveSettings(
    HostSettings settings, {
    String? accessToken,
  }) async {
    final values = settings.toJson(accessToken: accessToken);
    values['frontendPath'] = Platform.resolvedExecutable;
    return HostSettings.fromJson(await call('settings.save', values));
  }

  Future<void> refresh() async => call('actions.refresh');
  Future<void> restart() async => call('actions.restart');
  Future<void> openDataFolder() async => call('actions.openDataFolder');
  Future<void> openProvider() async => call('actions.openProvider');

  Future<List<String>> getLogs({int limit = 300}) async {
    final result = await call('logs.get', {'limit': limit});
    return (result['lines'] as List? ?? const []).cast<String>();
  }

  Stream<AppSnapshot> watchSnapshots() async* {
    var retry = 0;
    while (!_disposed) {
      Socket? socket;
      try {
        socket = await Socket.connect(
          InternetAddress.loopbackIPv4,
          runtime.port,
          timeout: const Duration(seconds: 5),
        );
        socket.writeln(jsonEncode({
          'id': 'events',
          'auth': runtime.sessionKey,
          'method': 'events.subscribe',
        }));
        await socket.flush();
        retry = 0;
        await for (final line in socket
            .cast<List<int>>()
            .transform(utf8.decoder)
            .transform(const LineSplitter())) {
          final message = decodeJsonObject(line);
          if (message['event'] == 'snapshot') {
            yield AppSnapshot.fromJson(
              Map<String, dynamic>.from(message['data'] as Map),
            );
          }
        }
      } catch (_) {
        if (_disposed) break;
        retry = min(retry + 1, 5);
        await Future<void>.delayed(Duration(milliseconds: 250 * (1 << retry)));
      } finally {
        socket?.destroy();
      }
    }
  }

  Future<void> dispose() async {
    if (_disposed) return;
    try {
      await call('host.shutdown').timeout(const Duration(seconds: 2));
    } catch (_) {
      runtime.process.kill();
    } finally {
      _disposed = true;
    }
  }
}
