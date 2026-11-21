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
import 'package:soitho/lock/app_lock.dart';

import 'support/fake_server.dart';

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

  setUp(() {
    store = MemoryTokenStore();
    authorizer = FakeAuthorizer();
    unlocker = FakeUnlocker();
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
      ],
      child: const SoithoApp(),
    );
  }

  testWidgets('sign in, see the greeting, sign out', (tester) async {
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('login-button')), findsOneWidget);

    await tester.tap(find.byKey(const Key('login-button')));
    await tester.pumpAndSettle();
    expect(find.text('Xin chào soi'), findsOneWidget);
    expect(unlocker.prompts, 0, reason: 'no Face ID right after typing the password');
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
  });

  testWidgets('closing the browser leaves the login screen as it was', (tester) async {
    authorizer.grant = null;
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('login-button')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('login-button')), findsOneWidget);
    expect(find.byKey(const Key('login-message')), findsNothing);
    expect(server.requests, isEmpty);
  });

  testWidgets('a saved sign-in opens locked and unlocks with the device', (tester) async {
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
    expect(find.text('Xin chào soi'), findsOneWidget);
  });

  testWidgets('an ended sign-in goes back to login with a message', (tester) async {
    store.token = 'r0';
    server.handler = (req, body) => (status: 400, body: {'error': 'invalid_grant'});
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('login-button')), findsOneWidget);
    expect(find.byKey(const Key('login-message')), findsOneWidget);
    expect(store.token, isNull);
  });
}
