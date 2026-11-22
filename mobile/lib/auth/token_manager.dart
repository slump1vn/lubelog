import 'dart:async';

import 'package:dio/dio.dart';

import '../app/config.dart';
import 'token_store.dart';

/// The server ended this sign-in (refresh token revoked, expired or reused).
/// The user has to sign in again.
class SignedOutException implements Exception {
  const SignedOutException([this.reason]);
  final String? reason;
  @override
  String toString() => 'SignedOutException($reason)';
}

/// The token endpoint answered with an OAuth error during sign-in.
class OAuthException implements Exception {
  const OAuthException(this.error, [this.description]);
  final String error;
  final String? description;
  @override
  String toString() => 'OAuthException($error, $description)';
}

/// Holds the short-lived access token in memory and the refresh token in
/// [TokenStore], and talks to /oauth/token and /oauth/revoke.
///
/// Rules from the server (docs/IOS-API.md):
/// - access tokens last 15 minutes;
/// - every refresh returns a NEW refresh token that replaces the old one;
/// - a refresh token used twice ends the whole sign-in.
/// So refreshes are single-flight: callers that need a new token at the
/// same time all wait for one request.
class TokenManager {
  TokenManager({
    required this.config,
    required this.store,
    Dio? http,
    DateTime Function()? clock,
  }) : _http = http ?? Dio(),
       _now = clock ?? DateTime.now;

  final AppConfig config;
  final TokenStore store;
  final Dio _http;
  final DateTime Function() _now;

  /// Refresh this long before the access token runs out.
  static const skew = Duration(seconds: 30);

  String? _access;
  DateTime? _expiresAt;
  Future<String?>? _refreshing;
  final _signedOut = StreamController<void>.broadcast();

  /// Fires when the server no longer accepts this sign-in.
  Stream<void> get signedOut => _signedOut.stream;

  String? get currentAccessToken => _access;

  Future<bool> hasSession() async => (await store.readRefreshToken()) != null;

  /// Step 5 of the sign-in: trade the one-time code for tokens.
  Future<void> exchangeCode({
    required String code,
    required String codeVerifier,
    String? deviceName,
  }) async {
    final body = await _post(config.tokenEndpoint, {
      'grant_type': 'authorization_code',
      'client_id': config.clientId,
      'redirect_uri': config.redirectUri,
      'code': code,
      'code_verifier': codeVerifier,
      if (deviceName != null && deviceName.isNotEmpty)
        'device_name': deviceName,
    });
    if (body.status != 200) {
      throw OAuthException(
        body.json['error'] as String? ?? 'http_${body.status}',
        body.json['error_description'] as String?,
      );
    }
    await _accept(body.json);
  }

  /// A usable access token, refreshing first when it is missing or about to
  /// run out. Null when there is no sign-in on this device.
  Future<String?> validAccessToken() async {
    final expiresAt = _expiresAt;
    if (_access != null &&
        expiresAt != null &&
        _now().isBefore(expiresAt.subtract(skew))) {
      return _access;
    }
    return refresh();
  }

  /// Called after the API answered 401 for [rejected]. When another request
  /// already got a newer token, reuse it instead of refreshing again.
  Future<String?> tokenAfterRejection(String? rejected) async {
    if (_access != null && _access != rejected) return _access;
    _access = null;
    _expiresAt = null;
    return refresh();
  }

  /// Gets a new access token with the stored refresh token. Concurrent calls
  /// share one request. Throws [SignedOutException] when the server refuses
  /// the refresh token; network errors are rethrown and keep the sign-in.
  Future<String?> refresh() {
    return _refreshing ??= _refresh().whenComplete(() => _refreshing = null);
  }

  Future<String?> _refresh() async {
    final refreshToken = await store.readRefreshToken();
    if (refreshToken == null) return null;
    final body = await _post(config.tokenEndpoint, {
      'grant_type': 'refresh_token',
      'client_id': config.clientId,
      'refresh_token': refreshToken,
    });
    if (body.status == 200) {
      await _accept(body.json);
      return _access;
    }
    if (body.status == 400 || body.status == 401) {
      await _forget();
      final reason = body.json['error'] as String?;
      _signedOut.add(null);
      throw SignedOutException(reason);
    }
    throw DioException(
      requestOptions: RequestOptions(path: config.tokenEndpoint.toString()),
      message: 'Token endpoint answered ${body.status}',
    );
  }

  /// Signs out on the server (best effort) and forgets the tokens here.
  Future<void> signOut() async {
    final refreshToken = await store.readRefreshToken();
    await _forget();
    if (refreshToken == null) return;
    try {
      await _post(config.revokeEndpoint, {
        'token': refreshToken,
        'client_id': config.clientId,
      });
    } on DioException {
      // Offline: the token stays valid on the server until it expires, and
      // can be ended from "Thiết bị đang đăng nhập" on the website.
    }
  }

  Future<void> _accept(Map<String, Object?> json) async {
    final access = json['access_token'] as String?;
    final refresh = json['refresh_token'] as String?;
    final expiresIn = (json['expires_in'] as num?)?.toInt() ?? 900;
    if (access == null || refresh == null) {
      throw const OAuthException('invalid_response');
    }
    // Store the new refresh token before using the access token, so a crash
    // right here never leaves the old (now spent) one on the device.
    await store.writeRefreshToken(refresh);
    _access = access;
    _expiresAt = _now().add(Duration(seconds: expiresIn));
  }

  Future<void> _forget() async {
    _access = null;
    _expiresAt = null;
    await store.clear();
  }

  Future<({int status, Map<String, Object?> json})> _post(
    Uri url,
    Map<String, String> form,
  ) async {
    final response = await _http.postUri<Object?>(
      url,
      data: form,
      options: Options(
        contentType: Headers.formUrlEncodedContentType,
        responseType: ResponseType.json,
        validateStatus: (_) => true,
        headers: {'Accept': 'application/json'},
      ),
    );
    final data = response.data;
    return (
      status: response.statusCode ?? 0,
      json: data is Map<String, Object?> ? data : const <String, Object?>{},
    );
  }

  void dispose() => _signedOut.close();
}
