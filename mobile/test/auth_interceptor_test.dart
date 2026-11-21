import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:soitho/auth/auth_interceptor.dart';
import 'package:soitho/auth/token_manager.dart';
import 'package:soitho/auth/token_store.dart';

import 'support/fake_server.dart';
import 'token_manager_test.dart' show config;

void main() {
  late FakeServer server;
  late MemoryTokenStore store;
  late Dio api;
  var issued = 0;
  var valid = <String>{};

  setUp(() {
    issued = 0;
    valid = {};
    store = MemoryTokenStore()..token = 'refresh-0';
    server = FakeServer((req, body) {
      if (req.uri.path == '/oauth/token') {
        issued++;
        valid = {'access-$issued'};
        return (
          status: 200,
          body: {
            'access_token': 'access-$issued',
            'expires_in': 900,
            'refresh_token': 'refresh-$issued',
          },
        );
      }
      final auth = req.headers['Authorization'] as String?;
      if (auth == null || !valid.contains(auth.substring(7))) {
        return (status: 401, body: {'title': 'Unauthorized'});
      }
      return (status: 200, body: {'ok': auth});
    });
    final tokens = TokenManager(
      config: config,
      store: store,
      http: Dio()..httpClientAdapter = server,
    );
    api = Dio(BaseOptions(baseUrl: 'https://finance.test'))
      ..httpClientAdapter = server;
    api.interceptors.add(AuthInterceptor(tokens, api));
  });

  test('adds the bearer token', () async {
    final res = await api.get<Map<String, Object?>>('/api/v1/me');
    expect(res.data!['ok'], 'Bearer access-1');
    expect(server.paths, ['/oauth/token', '/api/v1/me']);
  });

  test('on 401 refreshes once and repeats the call', () async {
    await api.get<Object?>('/api/v1/me');
    valid = {}; // server ended the access token (e.g. password changed)
    final res = await api.get<Map<String, Object?>>('/api/v1/me');
    expect(res.data!['ok'], 'Bearer access-2');
    expect(server.paths, [
      '/oauth/token',
      '/api/v1/me',
      '/api/v1/me',
      '/oauth/token',
      '/api/v1/me',
    ]);
  });

  test('a second 401 is passed on, not looped', () async {
    await api.get<Object?>('/api/v1/me');
    server.handler = (req, body) => req.uri.path == '/oauth/token'
        ? (status: 200, body: {'access_token': 'x', 'expires_in': 900, 'refresh_token': 'y'})
        : (status: 401, body: null);
    await expectLater(
      api.get<Object?>('/api/v1/me'),
      throwsA(isA<DioException>().having((e) => e.response?.statusCode, 'status', 401)),
    );
  });

  test('without a sign-in the call fails as signed out, nothing is sent', () async {
    store.token = null;
    await expectLater(
      api.get<Object?>('/api/v1/me'),
      throwsA(isA<DioException>().having((e) => e.error, 'error', isA<SignedOutException>())),
    );
    expect(server.requests, isEmpty);
  });
}
