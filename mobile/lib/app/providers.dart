import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/generated/soitho_api.dart';
import '../auth/auth_interceptor.dart';
import '../auth/authorizer.dart';
import '../auth/token_manager.dart';
import '../auth/token_store.dart';
import 'config.dart';

final configProvider = Provider<AppConfig>(
  (ref) => AppConfig.forPlatform(defaultTargetPlatform),
);

final tokenStoreProvider = Provider<TokenStore>((ref) => SecureTokenStore());

final tokenManagerProvider = Provider<TokenManager>((ref) {
  final config = ref.watch(configProvider);
  final manager = TokenManager(
    config: config,
    store: ref.watch(tokenStoreProvider),
    http: Dio(
      BaseOptions(
        connectTimeout: const Duration(seconds: 15),
        receiveTimeout: const Duration(seconds: 30),
      ),
    ),
  );
  ref.onDispose(manager.dispose);
  return manager;
});

final authorizerProvider = Provider<Authorizer>(
  (ref) => AppAuthAuthorizer(ref.watch(configProvider)),
);

/// Dio for /api/v1, with the bearer token added and refreshed as needed.
final dioProvider = Provider<Dio>((ref) {
  final config = ref.watch(configProvider);
  final dio = Dio(
    BaseOptions(
      baseUrl: config.server.toString(),
      connectTimeout: const Duration(seconds: 15),
      receiveTimeout: const Duration(seconds: 60),
    ),
  );
  dio.interceptors.add(AuthInterceptor(ref.watch(tokenManagerProvider), dio));
  return dio;
});

final apiProvider = Provider<ApiClient>(
  (ref) => SoithoApi(ref.watch(dioProvider)).api,
);
