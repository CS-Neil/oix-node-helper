import 'dart:convert';

class HealthState {
  const HealthState({
    required this.status,
    required this.coreRunning,
    required this.nodeCount,
    required this.lastRefreshUtc,
    required this.lastError,
    required this.version,
    required this.stage,
    required this.consecutiveEmptyRefreshes,
    required this.coreHealth,
    required this.coreConnections,
    required this.coreHandles,
    required this.coreMemoryMb,
    required this.ephemeralPortsInUse,
    required this.oixParamsEffective,
    required this.oixParamsDefault,
    required this.oixParamsSource,
  });

  factory HealthState.fromJson(Map<String, dynamic> json) => HealthState(
        status: json['status'] as String? ?? 'degraded',
        coreRunning: json['coreRunning'] as bool? ?? false,
        nodeCount: (json['nodeCount'] as num?)?.toInt() ?? 0,
        lastRefreshUtc: json['lastRefreshUtc'] as String? ?? '',
        lastError: json['lastError'] as String? ?? '',
        version: json['version'] as String? ?? '',
        stage: json['stage'] as String? ?? '',
        consecutiveEmptyRefreshes:
            (json['consecutiveEmptyRefreshes'] as num?)?.toInt() ?? 0,
        // Absent on hosts older than the core watchdog; treat as healthy so an
        // older Host keeps working with a newer GUI.
        coreHealth: json['coreHealth'] as String? ?? 'Healthy',
        coreConnections: (json['coreConnections'] as num?)?.toInt() ?? 0,
        coreHandles: (json['coreHandles'] as num?)?.toInt() ?? 0,
        coreMemoryMb: (json['coreMemoryMb'] as num?)?.toInt() ?? 0,
        ephemeralPortsInUse:
            (json['ephemeralPortsInUse'] as num?)?.toInt() ?? 0,
        // Absent on hosts older than the subscription-parameter feature; an empty
        // string simply hides the effective-parameter readout.
        oixParamsEffective: json['oixParamsEffective'] as String? ?? '',
        oixParamsDefault: json['oixParamsDefault'] as String? ?? '',
        oixParamsSource: json['oixParamsSource'] as String? ?? '',
      );

  final String status;
  final bool coreRunning;
  final int nodeCount;
  final String lastRefreshUtc;
  final String lastError;
  final String version;
  final String stage;
  final int consecutiveEmptyRefreshes;
  final String coreHealth;
  final int coreConnections;
  final int coreHandles;
  final int coreMemoryMb;
  final int ephemeralPortsInUse;
  final String oixParamsEffective;
  final String oixParamsDefault;
  final String oixParamsSource;

  bool get coreDegraded => coreHealth == 'Degraded' || coreHealth == 'Critical';

  DateTime? get lastRefresh {
    if (lastRefreshUtc.isEmpty) return null;
    return DateTime.tryParse(lastRefreshUtc)?.toLocal();
  }
}

class NodeInfo {
  const NodeInfo({
    required this.name,
    required this.type,
    required this.port,
    required this.endpoint,
  });

  factory NodeInfo.fromJson(Map<String, dynamic> json) => NodeInfo(
        name: json['name'] as String? ?? '',
        type: json['type'] as String? ?? '',
        port: (json['port'] as num?)?.toInt() ?? 0,
        endpoint: json['endpoint'] as String? ?? '',
      );

  final String name;
  final String type;
  final int port;
  final String endpoint;
}

class AppSnapshot {
  const AppSnapshot({
    required this.protocolVersion,
    required this.health,
    required this.nodes,
    required this.providerUrl,
  });

  factory AppSnapshot.fromJson(Map<String, dynamic> json) => AppSnapshot(
        protocolVersion: (json['protocolVersion'] as num?)?.toInt() ?? 0,
        health: HealthState.fromJson(
          Map<String, dynamic>.from(json['health'] as Map? ?? const {}),
        ),
        nodes: (json['nodes'] as List? ?? const [])
            .map((item) => NodeInfo.fromJson(
                  Map<String, dynamic>.from(item as Map),
                ))
            .toList(growable: false),
        providerUrl: json['providerUrl'] as String? ?? '',
      );

  final int protocolVersion;
  final HealthState health;
  final List<NodeInfo> nodes;
  final String providerUrl;
}

class HostSettings {
  const HostSettings({
    required this.corePath,
    required this.controllerUrl,
    required this.providerPort,
    required this.baseNodePort,
    required this.maxNodes,
    required this.pollSeconds,
    required this.includeRegex,
    required this.excludeRegex,
    required this.oixParams,
    required this.portRetentionDays,
    required this.emptyRefreshThreshold,
    required this.startWithWindows,
    required this.tokenConfigured,
  });

  factory HostSettings.fromJson(Map<String, dynamic> json) => HostSettings(
        corePath: json['corePath'] as String? ?? '',
        controllerUrl:
            json['controllerUrl'] as String? ?? 'http://127.0.0.1:6173',
        providerPort: (json['providerPort'] as num?)?.toInt() ?? 6172,
        baseNodePort: (json['baseNodePort'] as num?)?.toInt() ?? 7200,
        maxNodes: (json['maxNodes'] as num?)?.toInt() ?? 100,
        pollSeconds: (json['pollSeconds'] as num?)?.toInt() ?? 300,
        includeRegex: json['includeRegex'] as String? ?? '',
        excludeRegex: json['excludeRegex'] as String? ?? '',
        oixParams: json['oixParams'] as String? ?? '',
        portRetentionDays:
            (json['portRetentionDays'] as num?)?.toInt() ?? 14,
        emptyRefreshThreshold:
            (json['emptyRefreshThreshold'] as num?)?.toInt() ?? 3,
        startWithWindows: json['startWithWindows'] as bool? ?? false,
        tokenConfigured: json['tokenConfigured'] as bool? ?? false,
      );

  final String corePath;
  final String controllerUrl;
  final int providerPort;
  final int baseNodePort;
  final int maxNodes;
  final int pollSeconds;
  final String includeRegex;
  final String excludeRegex;
  final String oixParams;
  final int portRetentionDays;
  final int emptyRefreshThreshold;
  final bool startWithWindows;
  final bool tokenConfigured;

  Map<String, dynamic> toJson({String? accessToken}) => {
        'corePath': corePath,
        'controllerUrl': controllerUrl,
        'providerPort': providerPort,
        'baseNodePort': baseNodePort,
        'maxNodes': maxNodes,
        'pollSeconds': pollSeconds,
        'includeRegex': includeRegex,
        'excludeRegex': excludeRegex,
        'oixParams': oixParams,
        'portRetentionDays': portRetentionDays,
        'emptyRefreshThreshold': emptyRefreshThreshold,
        'startWithWindows': startWithWindows,
        if (accessToken != null && accessToken.isNotEmpty)
          'accessToken': accessToken,
      };
}

class HostException implements Exception {
  const HostException(this.message, {this.code = 'host_error'});

  final String code;
  final String message;

  @override
  String toString() => message;
}

/// Accepts what a user is likely to paste out of a subscription URL — a bare
/// `love=1`, a `?love=1` query string, or a full `&a=1&b=2` fragment — and
/// returns the `&a=1&b=2` form the core expects. Mirrors
/// `CoreClient.NormalizeOixParams` on the Host side.
String normalizeOixParams(String? value) {
  if (value == null) return '';
  final pairs = value
      .trim()
      .split(RegExp(r'[&?]'))
      .map((pair) => pair.trim())
      .where((pair) => pair.isNotEmpty)
      .toList(growable: false);
  if (pairs.isEmpty) return '';
  return '&${pairs.join('&')}';
}

Map<String, dynamic> decodeJsonObject(String line) {
  final value = jsonDecode(line);
  if (value is! Map) {
    throw const HostException('后台返回了无效的数据。');
  }
  return Map<String, dynamic>.from(value);
}
