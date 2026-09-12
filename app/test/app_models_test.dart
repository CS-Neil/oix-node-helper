import 'package:flutter_test/flutter_test.dart';
import 'package:oix_node_helper/models/app_models.dart';

void main() {
  test('snapshot parses host protocol payload', () {
    final snapshot = AppSnapshot.fromJson({
      'protocolVersion': 1,
      'health': {
        'status': 'ok',
        'coreRunning': true,
        'nodeCount': 1,
        'lastRefreshUtc': '2026-09-06T00:00:00Z',
        'lastError': '',
        'version': '0.3.0',
        'stage': '运行正常',
        'consecutiveEmptyRefreshes': 0,
      },
      'nodes': [
        {
          'name': 'test-node',
          'type': 'ss',
          'port': 7200,
          'endpoint': '127.0.0.1:7200',
        }
      ],
      'providerUrl': 'http://127.0.0.1:6172/clash',
    });

    expect(snapshot.protocolVersion, 1);
    expect(snapshot.health.coreRunning, isTrue);
    expect(snapshot.nodes.single.port, 7200);
  });

  test('subscription parameters normalize to the core fragment form', () {
    expect(normalizeOixParams('&mode=premium&tfo=true'), '&mode=premium&tfo=true');
    expect(normalizeOixParams('mode=premium'), '&mode=premium');
    expect(normalizeOixParams('?mode=premium&love=1'), '&mode=premium&love=1');
    expect(normalizeOixParams('   '), '');
    expect(normalizeOixParams(null), '');
  });

  test('health reads the effective subscription parameters', () {
    final health = HealthState.fromJson(const {
      'oixParamsEffective': '&mode=premium&tfo=true',
      'oixParamsDefault': '&mode=premium',
      'oixParamsSource': 'file',
    });

    expect(health.oixParamsEffective, '&mode=premium&tfo=true');
    expect(health.oixParamsDefault, '&mode=premium');
    expect(health.oixParamsSource, 'file');
  });

  test('an older host without subscription parameters still parses', () {
    final health = HealthState.fromJson(const {'coreRunning': true});

    expect(health.oixParamsEffective, '');
    expect(health.oixParamsDefault, '');
  });

  test('settings never serializes an absent access token', () {
    const settings = HostSettings(
      corePath: 'mihomo-oix.exe',
      controllerUrl: 'http://127.0.0.1:6173',
      providerPort: 6172,
      baseNodePort: 7200,
      maxNodes: 100,
      pollSeconds: 60,
      includeRegex: '',
      excludeRegex: '',
      oixParams: '',
      portRetentionDays: 14,
      emptyRefreshThreshold: 3,
      startWithWindows: false,
      tokenConfigured: true,
    );

    expect(settings.toJson(), isNot(contains('accessToken')));
  });
}
