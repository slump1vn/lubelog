import 'package:flutter/services.dart';
import 'package:flutter_appauth/flutter_appauth.dart';

import '../app/config.dart';

/// The result of the browser step of the sign-in.
typedef AuthorizationGrant = ({String code, String codeVerifier});

/// Opens the server's login page in the system browser
/// (ASWebAuthenticationSession on iPhone, Custom Tabs on Android), never in a
/// web view inside the app, as RFC 8252 asks. AppAuth makes the PKCE
/// verifier and S256 challenge and checks `state`.
abstract class Authorizer {
  /// Null when the user closed the browser.
  Future<AuthorizationGrant?> authorize();
}

class AppAuthAuthorizer implements Authorizer {
  AppAuthAuthorizer(this._config, [FlutterAppAuth? appAuth])
    : _appAuth = appAuth ?? const FlutterAppAuth();

  final AppConfig _config;
  final FlutterAppAuth _appAuth;

  @override
  Future<AuthorizationGrant?> authorize() async {
    try {
      final response = await _appAuth.authorize(
        AuthorizationRequest(
          _config.clientId,
          _config.redirectUri,
          serviceConfiguration: AuthorizationServiceConfiguration(
            authorizationEndpoint: _config.authorizeEndpoint.toString(),
            tokenEndpoint: _config.tokenEndpoint.toString(),
          ),
          // Not an OpenID Connect server: no scopes, no nonce needed.
          scopes: const [],
          // An ephemeral session would ask for the password every time;
          // the shared one lets the browser's saved password fill in.
          externalUserAgent: ExternalUserAgent.asWebAuthenticationSession,
        ),
      );
      final code = response.authorizationCode;
      final verifier = response.codeVerifier;
      if (code == null || verifier == null) return null;
      return (code: code, codeVerifier: verifier);
    } on FlutterAppAuthUserCancelledException {
      return null;
    } on PlatformException catch (e) {
      if (e.code.contains('cancel')) return null;
      rethrow;
    }
  }
}
