import 'package:flutter/foundation.dart';

/// Where the app talks to and how it signs in.
///
/// The server can be changed at build time for testing:
/// `flutter run --dart-define=SOITHO_SERVER=https://test.example`
class AppConfig {
  const AppConfig({
    required this.server,
    required this.clientId,
    required this.redirectUri,
  });

  /// Base address of the family finance server, without a trailing slash.
  final Uri server;

  /// OAuth client registered on the server (Logic/OAuthRules.cs).
  final String clientId;

  /// Private-use scheme redirect (RFC 8252 section 7.1), registered for [clientId].
  final String redirectUri;

  Uri get authorizeEndpoint => server.resolve('/oauth/authorize');
  Uri get tokenEndpoint => server.resolve('/oauth/token');
  Uri get revokeEndpoint => server.resolve('/oauth/revoke');

  static const _server = String.fromEnvironment(
    'SOITHO_SERVER',
    defaultValue: 'https://finance.digifusion.vn',
  );

  static AppConfig forPlatform(TargetPlatform platform) {
    final server = Uri.parse(_server);
    if (platform == TargetPlatform.iOS) {
      return AppConfig(
        server: server,
        clientId: 'soitho-ios',
        redirectUri: 'vn.digifusion.finance:/oauth/callback',
      );
    }
    return AppConfig(
      server: server,
      clientId: 'soitho-android',
      redirectUri: 'vn.digifusion.soitho:/oauth/callback',
    );
  }
}
