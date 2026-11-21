import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// The last answer of a screen, kept encrypted on the phone so the app opens
/// with numbers right away and still shows them without a connection.
/// Cleared when signing out.
abstract class SnapshotCache {
  Future<Snapshot?> read(String key);
  Future<void> write(String key, Map<String, Object?> json, DateTime savedAt);
  Future<void> clear();
}

class Snapshot {
  const Snapshot(this.json, this.savedAt);
  final Map<String, Object?> json;
  final DateTime savedAt;
}

/// Uses the same encrypted store as the refresh token (Keystore / Keychain,
/// this device only). The few snapshots are small: a dashboard and a page of
/// transactions are a few kilobytes.
class SecureSnapshotCache implements SnapshotCache {
  SecureSnapshotCache([FlutterSecureStorage? storage])
    : _storage =
          storage ??
          const FlutterSecureStorage(
            iOptions: IOSOptions(
              accessibility: KeychainAccessibility.first_unlock_this_device,
            ),
          );

  final FlutterSecureStorage _storage;
  static const _prefix = 'snapshot.';
  static const keys = ['dashboard', 'transactions', 'budget'];

  @override
  Future<Snapshot?> read(String key) async {
    try {
      final raw = await _storage.read(key: '$_prefix$key');
      if (raw == null) return null;
      final decoded = jsonDecode(raw) as Map<String, Object?>;
      return Snapshot(
        decoded['json']! as Map<String, Object?>,
        DateTime.parse(decoded['savedAt']! as String),
      );
    } on Object {
      return null; // A broken snapshot is just a missing one.
    }
  }

  @override
  Future<void> write(String key, Map<String, Object?> json, DateTime savedAt) =>
      _storage.write(
        key: '$_prefix$key',
        value: jsonEncode({
          'savedAt': savedAt.toUtc().toIso8601String(),
          'json': json,
        }),
      );

  @override
  Future<void> clear() async {
    for (final key in keys) {
      await _storage.delete(key: '$_prefix$key');
    }
  }
}

class MemorySnapshotCache implements SnapshotCache {
  final store = <String, Snapshot>{};

  @override
  Future<Snapshot?> read(String key) async => store[key];

  @override
  Future<void> write(
    String key,
    Map<String, Object?> json,
    DateTime savedAt,
  ) async => store[key] = Snapshot(
    jsonDecode(jsonEncode(json)) as Map<String, Object?>,
    savedAt,
  );

  @override
  Future<void> clear() async => store.clear();
}
