import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:soitho/app/config.dart';
import 'package:soitho/auth/token_manager.dart';
import 'package:soitho/auth/token_store.dart';

import 'support/fake_server.dart';

final config = AppConfig(
  server: Uri.parse('https://finance.test'),
  clientId: 'soitho-android',
  redirectUri: 'vn.digifusion.soitho:/oauth/callback',
);

void main() {
  late FakeServer server;
  late MemoryTokenStore store;
  late DateTime now;
  late TokenManager tokens;
  var issued = 0;

  FakeResponse issue() {
    issued++;
    return (
      status: 200,
      body: {
        'access_token': 'access-$issued',
        'token_type': 'Bearer',
        'expires_in': 900,
        'refresh_token': 'refresh-$issued',
      },
    );
  }

  setUp(() {
    issued = 0;
    now = DateTime(2026, 11, 21, 9);
    store = MemoryTokenStore();
    server = FakeServer((req, body) => issue());
    tokens = TokenManager(
      config: config,
      store: store,
      http: Dio()..httpClientAdapter = server,
      clock: () => now,
    );
  });

  test('code exchange sends PKCE verifier, client and device name', () async {
    await tokens.exchangeCode(
      code: 'abc',
      codeVerifier: 'v' * 43,
      deviceName: 'Pixel 8',
    );
    final sent = form(server.requests.single.body);
    expect(server.paths.single, '/oauth/token');
    expect(sent['grant_type'], 'authorization_code');
    expect(sent['client_id'], 'soitho-android');
    expect(sent['redirect_uri'], 'vn.digifusion.soitho:/oauth/callback');
    expect(sent['code_verifier'], 'v' * 43);
    expect(sent['device_name'], 'Pixel 8');
    expect(store.token, 'refresh-1');
    expect(await tokens.validAccessToken(), 'access-1');
  });

  test('a refused code surfaces the OAuth error', () async {
    server.handler = (_, _) => (
      status: 400,
      body: {'error': 'invalid_grant', 'error_description': 'used'},
    );
    expect(
      () => tokens.exchangeCode(code: 'x', codeVerifier: 'v' * 43),
      throwsA(isA<OAuthException>().having((e) => e.error, 'error', 'invalid_grant')),
    );
  });

  test('access token is reused until 30 seconds before it runs out', () async {
    await tokens.exchangeCode(code: 'abc', codeVerifier: 'v' * 43);
    now = now.add(const Duration(minutes: 14));
    expect(await tokens.validAccessToken(), 'access-1');
    now = now.add(const Duration(seconds: 31));
    expect(await tokens.validAccessToken(), 'access-2');
    final sent = form(server.requests.last.body);
    expect(sent['grant_type'], 'refresh_token');
    expect(sent['refresh_token'], 'refresh-1');
    expect(store.token, 'refresh-2', reason: 'rotated refresh token replaces the old one');
  });

  test('concurrent refreshes share one request (a reused refresh token ends the sign-in)', () async {
    store.token = 'refresh-0';
    server.delay = const Duration(milliseconds: 20);
    final results = await Future.wait([
      tokens.validAccessToken(),
      tokens.validAccessToken(),
      tokens.refresh(),
    ]);
    expect(results, ['access-1', 'access-1', 'access-1']);
    expect(server.requests, hasLength(1));
  });

  test('a refused refresh token signs out and clears the device', () async {
    store.token = 'refresh-0';
    server.handler = (_, _) => (status: 400, body: {'error': 'invalid_grant'});
    var signedOut = false;
    tokens.signedOut.listen((_) => signedOut = true);
    await expectLater(tokens.validAccessToken(), throwsA(isA<SignedOutException>()));
    await Future<void>.delayed(Duration.zero);
    expect(signedOut, isTrue);
    expect(store.token, isNull);
  });

  test('a server error keeps the sign-in for a later try', () async {
    store.token = 'refresh-0';
    server.handler = (_, _) => (status: 503, body: null);
    await expectLater(tokens.validAccessToken(), throwsA(isA<DioException>()));
    expect(store.token, 'refresh-0');
  });

  test('no sign-in on the device gives no token and no request', () async {
    expect(await tokens.validAccessToken(), isNull);
    expect(server.requests, isEmpty);
  });

  test('sign out revokes on the server and forgets locally', () async {
    await tokens.exchangeCode(code: 'abc', codeVerifier: 'v' * 43);
    server.handler = (_, _) => (status: 200, body: null);
    await tokens.signOut();
    expect(server.paths.last, '/oauth/revoke');
    expect(form(server.requests.last.body)['token'], 'refresh-1');
    expect(store.token, isNull);
    expect(await tokens.validAccessToken(), isNull);
  });
}
