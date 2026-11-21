import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  final manifest = File('android/app/src/main/AndroidManifest.xml').readAsStringSync();

  test('MainActivity keeps the default task affinity, so the sign-in returns to the app', () {
    // taskAffinity="" stops AppAuth's redirect from coming back after "Cho phép".
    expect(manifest.contains('android:taskAffinity'), isFalse);
  });

  test('FlutterFragmentActivity and biometric permission for the app lock', () {
    final activity = File(
      'android/app/src/main/kotlin/vn/digifusion/soitho/MainActivity.kt',
    ).readAsStringSync();
    expect(activity, contains('FlutterFragmentActivity'));
    expect(manifest, contains('android.permission.USE_BIOMETRIC'));
  });

  test('OAuth redirect scheme matches the server client soitho-android', () {
    final gradle = File('android/app/build.gradle.kts').readAsStringSync();
    expect(
      gradle,
      contains('manifestPlaceholders["appAuthRedirectScheme"] = "vn.digifusion.soitho"'),
    );
  });
}
