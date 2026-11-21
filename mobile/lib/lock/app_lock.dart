import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:local_auth/local_auth.dart';

import 'lock_policy.dart';

/// Asks the phone's own lock (Face ID, fingerprint, or device PIN).
abstract class DeviceUnlocker {
  /// False when the phone has no screen lock at all; the app then cannot lock.
  Future<bool> isAvailable();

  /// True when the person proved it is them.
  Future<bool> unlock();
}

class LocalAuthUnlocker implements DeviceUnlocker {
  final _auth = LocalAuthentication();

  @override
  Future<bool> isAvailable() async {
    try {
      return await _auth.isDeviceSupported();
    } on Object {
      return false;
    }
  }

  @override
  Future<bool> unlock() async {
    try {
      return await _auth.authenticate(
        localizedReason: 'Mở khóa Sói & Thỏ',
        // Device PIN is allowed as a fallback, like the banking apps.
        biometricOnly: false,
        persistAcrossBackgrounding: true,
      );
    } on LocalAuthException catch (e) {
      if (e.code == LocalAuthExceptionCode.noCredentialsSet) return true;
      return false;
    }
  }
}

final deviceUnlockerProvider = Provider<DeviceUnlocker>(
  (ref) => LocalAuthUnlocker(),
);

final lockPolicyProvider = Provider<LockPolicy>((ref) => const LockPolicy());

final clockProvider = Provider<DateTime Function()>((ref) => DateTime.now);

class LockState {
  const LockState({required this.locked, required this.obscured});

  /// The lock screen is shown and data is hidden.
  final bool locked;

  /// The app is in the app switcher: hide the numbers, no prompt.
  final bool obscured;

  LockState copyWith({bool? locked, bool? obscured}) => LockState(
    locked: locked ?? this.locked,
    obscured: obscured ?? this.obscured,
  );
}

final appLockProvider = NotifierProvider<AppLockController, LockState>(
  AppLockController.new,
);

class AppLockController extends Notifier<LockState> {
  DateTime? _backgroundAt;
  bool _unlocking = false;

  @override
  LockState build() => const LockState(locked: true, obscured: false);

  void onLifecycle(AppLifecycleState lifecycle) {
    switch (lifecycle) {
      case AppLifecycleState.inactive:
      case AppLifecycleState.hidden:
        state = state.copyWith(obscured: true);
      case AppLifecycleState.paused:
        _backgroundAt ??= ref.read(clockProvider)();
        state = state.copyWith(obscured: true);
      case AppLifecycleState.resumed:
        final lock = ref
            .read(lockPolicyProvider)
            .shouldLockOnResume(
              wentToBackgroundAt: _backgroundAt,
              now: ref.read(clockProvider)(),
            );
        _backgroundAt = null;
        state = LockState(locked: state.locked || lock, obscured: false);
      case AppLifecycleState.detached:
        break;
    }
  }

  Future<void> unlock() async {
    if (_unlocking || !state.locked) return;
    _unlocking = true;
    try {
      final unlocker = ref.read(deviceUnlockerProvider);
      final ok = !await unlocker.isAvailable() || await unlocker.unlock();
      if (ok) state = state.copyWith(locked: false);
    } finally {
      _unlocking = false;
    }
  }

  /// After a fresh sign-in the person just proved it is them.
  void markUnlocked() => state = state.copyWith(locked: false);
}
