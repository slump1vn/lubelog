import 'package:dio/dio.dart';

import 'token_manager.dart';

/// Adds `Authorization: Bearer` to API calls. On 401 it gets a new access
/// token once and repeats the call; a second 401 is passed on.
class AuthInterceptor extends Interceptor {
  AuthInterceptor(this._tokens, this._dio);

  final TokenManager _tokens;
  final Dio _dio;
  static const _retried = 'soitho.retried';

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    try {
      final token = await _tokens.validAccessToken();
      if (token == null) {
        return handler.reject(
          DioException(
            requestOptions: options,
            error: const SignedOutException('no_session'),
          ),
        );
      }
      options.headers['Authorization'] = 'Bearer $token';
      handler.next(options);
    } on SignedOutException catch (e) {
      handler.reject(DioException(requestOptions: options, error: e));
    } on DioException catch (e) {
      handler.reject(e);
    }
  }

  @override
  Future<void> onError(
    DioException err,
    ErrorInterceptorHandler handler,
  ) async {
    final options = err.requestOptions;
    if (err.response?.statusCode != 401 || options.extra[_retried] == true) {
      return handler.next(err);
    }
    final sent = (options.headers['Authorization'] as String?)?.replaceFirst(
      'Bearer ',
      '',
    );
    try {
      final token = await _tokens.tokenAfterRejection(sent);
      if (token == null) return handler.next(err);
      options.headers['Authorization'] = 'Bearer $token';
      options.extra[_retried] = true;
      handler.resolve(await _dio.fetch<Object?>(options));
    } on SignedOutException catch (e) {
      handler.next(DioException(requestOptions: options, error: e));
    } on DioException catch (e) {
      handler.next(e);
    }
  }
}
