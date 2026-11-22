import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:soitho/app/app.dart';
import 'package:soitho/app/config.dart';
import 'package:soitho/app/providers.dart';
import 'package:soitho/auth/auth_controller.dart';
import 'package:soitho/auth/auth_interceptor.dart';
import 'package:soitho/auth/authorizer.dart';
import 'package:soitho/auth/token_manager.dart';
import 'package:soitho/auth/token_store.dart';
import 'package:soitho/data/finance_providers.dart';
import 'package:soitho/data/snapshot_cache.dart';
import 'package:soitho/lock/app_lock.dart';

import 'support/fake_server.dart';
import 'support/fixtures.dart';

class FakeAuthorizer implements Authorizer {
  AuthorizationGrant? grant = (code: 'code-1', codeVerifier: 'v' * 43);
  int calls = 0;
  @override
  Future<AuthorizationGrant?> authorize() async {
    calls++;
    return grant;
  }
}

class FakeUnlocker implements DeviceUnlocker {
  bool answer = true;
  int prompts = 0;
  @override
  Future<bool> isAvailable() async => true;
  @override
  Future<bool> unlock() async {
    prompts++;
    return answer;
  }
}

void main() {
  late FakeServer server;
  late MemoryTokenStore store;
  late FakeAuthorizer authorizer;
  late FakeUnlocker unlocker;
  late MemorySnapshotCache cache;

  setUp(() {
    store = MemoryTokenStore();
    authorizer = FakeAuthorizer();
    unlocker = FakeUnlocker();
    cache = MemorySnapshotCache();
    server = FakeServer((req, body) {
      switch (req.uri.path) {
        case '/oauth/token':
          return (
            status: 200,
            body: {
              'access_token': 'a',
              'expires_in': 900,
              'refresh_token': 'r',
            },
          );
        case '/oauth/revoke':
          return (status: 200, body: null);
        case '/api/v1/me':
          return (
            status: 200,
            body: {
              'id': 1,
              'userName': 'soi',
              'householdId': -1,
              'isAdmin': true,
            },
          );
        case '/api/v1/finance/dashboard':
          return (status: 200, body: dashboardJson);
        case '/api/v1/finance/budget':
          return (status: 200, body: budgetJson);
        case '/api/v1/finance/transactions':
          return (
            status: 200,
            body: {
              'items': [tx(3), tx(2)],
              'nextCursor': null,
            },
          );
      }
      return (status: 404, body: null);
    });
  });

  Widget app() {
    final config = AppConfig(
      server: Uri.parse('https://finance.test'),
      clientId: 'soitho-android',
      redirectUri: 'vn.digifusion.soitho:/oauth/callback',
    );
    return ProviderScope(
      overrides: [
        configProvider.overrideWithValue(config),
        tokenStoreProvider.overrideWithValue(store),
        tokenManagerProvider.overrideWith(
          (ref) => TokenManager(
            config: config,
            store: store,
            http: Dio()..httpClientAdapter = server,
          ),
        ),
        dioProvider.overrideWith((ref) {
          final dio = Dio(BaseOptions(baseUrl: 'https://finance.test'))
            ..httpClientAdapter = server;
          dio.interceptors.add(
            AuthInterceptor(ref.watch(tokenManagerProvider), dio),
          );
          return dio;
        }),
        authorizerProvider.overrideWithValue(authorizer),
        deviceUnlockerProvider.overrideWithValue(unlocker),
        deviceNamerProvider.overrideWithValue(() async => 'Test phone'),
        snapshotCacheProvider.overrideWithValue(cache),
        nowProvider.overrideWithValue(() => DateTime(2026, 11, 21, 14)),
      ],
      retry: (_, _) => null,
      child: const SoithoApp(),
    );
  }

  testWidgets('sign in, see the greeting, sign out', (tester) async {
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('login-button')), findsOneWidget);

    await tester.tap(find.byKey(const Key('login-button')));
    await tester.pumpAndSettle();
    expect(find.text('12.345.000 ₫'), findsOneWidget);
    expect(
      unlocker.prompts,
      0,
      reason: 'no Face ID right after typing the password',
    );
    expect(store.token, 'r');
    expect(form(server.requests.first.body)['device_name'], 'Test phone');

    await tester.tap(find.text('Thêm'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('sign-out')));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Đăng xuất'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('login-button')), findsOneWidget);
    expect(store.token, isNull);
    expect(server.paths.last, '/oauth/revoke');
    expect(
      cache.store,
      isEmpty,
      reason: 'no household data stays on the phone',
    );
  });

  testWidgets('closing the browser leaves the login screen as it was', (
    tester,
  ) async {
    authorizer.grant = null;
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('login-button')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('login-button')), findsOneWidget);
    expect(find.byKey(const Key('login-message')), findsNothing);
    expect(server.requests, isEmpty);
  });

  testWidgets('a saved sign-in opens locked and unlocks with the device', (
    tester,
  ) async {
    store.token = 'r0';
    unlocker.answer = false;
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    expect(find.text('Sói & Thỏ đang khóa'), findsOneWidget);
    expect(unlocker.prompts, 1);

    unlocker.answer = true;
    await tester.tap(find.text('Mở khóa'));
    await tester.pumpAndSettle();
    expect(find.text('Sói & Thỏ đang khóa'), findsNothing);
    expect(find.text('12.345.000 ₫'), findsOneWidget);
  });

  testWidgets('an ended sign-in goes back to login with a message', (
    tester,
  ) async {
    store.token = 'r0';
    server.handler = (req, body) =>
        (status: 400, body: {'error': 'invalid_grant'});
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('login-button')), findsOneWidget);
    expect(find.byKey(const Key('login-message')), findsOneWidget);
    expect(store.token, isNull);
  });
  Future<void> signedIn(WidgetTester tester) async {
    store.token = 'r0';
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    // The lock screen asks the (fake) fingerprint by itself and it says yes.
    expect(find.text('Sói & Thỏ đang khóa'), findsNothing);
  }

  testWidgets(
    'overview shows the cycle, upcoming items and recent transactions',
    (tester) async {
      await signedIn(tester);
      expect(find.text('Kỳ 5/11 – 4/12'), findsOneWidget);
      expect(find.text('Ngày 17/30'), findsOneWidget);
      expect(find.text('+25.000.000 ₫'), findsOneWidget);
      expect(find.text('−9.800.000 ₫'), findsOneWidget);
      expect(find.textContaining('300.000 ₫ suy ra từ số dư'), findsOneWidget);
      await tester.scrollUntilVisible(find.text('Giao dich 2'), 200);
      expect(find.text('Trả nợ'), findsOneWidget);
      expect(find.text('+16.000.000 ₫'), findsOneWidget);
    },
  );

  testWidgets('without a connection the last numbers stay, marked as old', (
    tester,
  ) async {
    await cache.write(
      'dashboard',
      dashboardJson,
      DateTime.utc(2026, 11, 21, 2, 30),
    );
    server.handler = (req, body) => req.uri.path == '/oauth/token'
        ? (
            status: 200,
            body: {
              'access_token': 'a',
              'expires_in': 900,
              'refresh_token': 'r',
            },
          )
        : throw DioException.connectionError(
            requestOptions: req,
            reason: 'offline',
          );
    await signedIn(tester);
    expect(find.text('12.345.000 ₫'), findsOneWidget);
    expect(find.byKey(const Key('offline-note')), findsOneWidget);
  });

  testWidgets('transactions are grouped by day and the budget plan is shown', (
    tester,
  ) async {
    await signedIn(tester);
    await tester.tap(find.text('Giao dịch').last);
    await tester.pumpAndSettle();
    expect(find.text('Hôm nay'), findsOneWidget);
    expect(find.text('Giao dich 3'), findsOneWidget);
    expect(find.text('Đã hết giao dịch'), findsOneWidget);

    await tester.tap(find.text('Ngân sách').last);
    await tester.pumpAndSettle();
    expect(find.text('9.000.000 ₫'), findsWidgets);
    expect(find.text('Quỹ dự phòng'), findsOneWidget);
    expect(find.textContaining('mục tiêu 25.000.000 ₫'), findsOneWidget);
  });

  testWidgets('a household without a budget gets a plain explanation', (
    tester,
  ) async {
    final base = server.handler;
    server.handler = (req, body) => req.uri.path == '/api/v1/finance/budget'
        ? (status: 404, body: {'title': 'No budget yet'})
        : base(req, body);
    await signedIn(tester);
    await tester.tap(find.text('Ngân sách').last);
    await tester.pumpAndSettle();
    expect(find.textContaining('Chưa thiết lập ngân sách'), findsOneWidget);
  });
}
