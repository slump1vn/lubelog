import 'dart:async';

import 'package:device_info_plus/device_info_plus.dart';
import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/generated/soitho_api.dart';
import '../app/providers.dart';
import '../lock/app_lock.dart';
import 'token_manager.dart';

sealed class AuthState {
  const AuthState();
}

/// Still reading the Keychain / Keystore at start-up.
class AuthChecking extends AuthState {
  const AuthChecking();
}

class SignedOut extends AuthState {
  const SignedOut({this.message, this.busy = false});

  /// Shown on the login screen, in Vietnamese.
  final String? message;
  final bool busy;
}

class SignedIn extends AuthState {
  const SignedIn(this.me);

  /// Null while offline: the sign-in is kept and data loads when back online.
  final Me? me;
}

/// Gives a readable name to this phone for "Thiết bị đang đăng nhập".
typedef DeviceNamer = Future<String?> Function();

final deviceNamerProvider = Provider<DeviceNamer>((ref) => _deviceName);

Future<String?> _deviceName() async {
  try {
    final info = DeviceInfoPlugin();
    if (defaultTargetPlatform == TargetPlatform.iOS) {
      final ios = await info.iosInfo;
      return ios.name;
    }
    if (defaultTargetPlatform == TargetPlatform.android) {
      final android = await info.androidInfo;
      return '${android.manufacturer} ${android.model}';
    }
  } on Object {
    // A missing name is fine; the server falls back to a generic one.
  }
  return null;
}

final authControllerProvider = NotifierProvider<AuthController, AuthState>(
  AuthController.new,
);

class AuthController extends Notifier<AuthState> {
  StreamSubscription<void>? _sub;

  @override
  AuthState build() {
    final tokens = ref.watch(tokenManagerProvider);
    _sub = tokens.signedOut.listen((_) {
      state = const SignedOut(
        message: 'Phiên đăng nhập đã hết hạn. Anh chị đăng nhập lại nhé.',
      );
    });
    ref.onDispose(() => _sub?.cancel());
    Future.microtask(_restore);
    return const AuthChecking();
  }

  Future<void> _restore() async {
    final tokens = ref.read(tokenManagerProvider);
    if (!await tokens.hasSession()) {
      state = const SignedOut();
      return;
    }
    await _loadMe(keepOffline: true);
  }

  Future<void> signIn() async {
    state = const SignedOut(busy: true);
    try {
      final grant = await ref.read(authorizerProvider).authorize();
      if (grant == null) {
        state = const SignedOut();
        return;
      }
      await ref
          .read(tokenManagerProvider)
          .exchangeCode(
            code: grant.code,
            codeVerifier: grant.codeVerifier,
            deviceName: await ref.read(deviceNamerProvider)(),
          );
      // The person just typed their password: no Face ID prompt on top.
      ref.read(appLockProvider.notifier).markUnlocked();
      await _loadMe(keepOffline: false);
    } on OAuthException catch (e) {
      state = SignedOut(message: _oauthMessage(e.error));
    } on DioException {
      state = const SignedOut(
        message: 'Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.',
      );
    } on Object catch (e) {
      state = SignedOut(message: 'Đăng nhập không thành công ($e).');
    }
  }

  Future<void> reload() => _loadMe(keepOffline: true);

  Future<void> signOut() async {
    await ref.read(tokenManagerProvider).signOut();
    state = const SignedOut();
  }

  Future<void> _loadMe({required bool keepOffline}) async {
    try {
      final me = await ref.read(apiProvider).getMe();
      state = SignedIn(me);
    } on DioException catch (e) {
      if (e.error is SignedOutException) {
        state = const SignedOut(
          message: 'Phiên đăng nhập đã hết hạn. Anh chị đăng nhập lại nhé.',
        );
      } else if (keepOffline && e.response == null) {
        state = const SignedIn(null);
      } else {
        state = const SignedOut(
          message: 'Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.',
        );
      }
    }
  }

  static String _oauthMessage(String error) => switch (error) {
    'invalid_grant' => 'Mã đăng nhập đã hết hạn. Anh chị thử lại nhé.',
    'invalid_client' => 'Máy chủ chưa nhận app này. Cần cập nhật máy chủ.',
    _ => 'Đăng nhập không thành công ($error).',
  };
}
