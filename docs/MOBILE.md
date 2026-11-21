# Mobile app (Flutter)

One Flutter app for both phones, in `mobile/`. Android is built first; iOS follows once the Android
build runs well. Plan: [Kế hoạch app mobile Flutter](https://claude.ai/artifact/WDvyhPDSrfrqWvBnxwLdUY).

## Getting the Android app

1. Push to any branch that changes `mobile/**`, or run **Actions → Mobile app (Flutter) → Run workflow**.
2. Open the finished run and download **soi-tho-flutter-apk-N** (a zip holding the `.apk`).
3. Install it on the phone (allow "install unknown apps" for the browser or file manager once).

The APK is signed with the same release key as the old TWA app (GitHub secrets `ANDROID_KEYSTORE_*`,
see [ANDROID.md](ANDROID.md)). It has its own package `vn.digifusion.soitho`, so it installs next to
the TWA app instead of replacing it.

## What phase 0 does

- **Sign in** with OAuth 2.0 + PKCE in the system browser (Custom Tabs / `ASWebAuthenticationSession`),
  as RFC 8252 asks. The app never sees the password. See [IOS-API.md](IOS-API.md) for the server side.
- **Tokens**: the refresh token is kept in Android Keystore / iOS Keychain (this device only, never in
  backups); the 15-minute access token lives in memory. Refreshes are single-flight, because the server
  ends the sign-in when a refresh token is used twice. On 401 the call is repeated once with a new token.
- **App lock**: Face ID, fingerprint or the phone's PIN at every start and after 5 minutes in the
  background; the screen is covered in the app switcher.
- **Signing out** revokes the refresh token on the server (`/oauth/revoke`).
- Backups and device-to-device transfer are turned off for app data.
- Bottom tabs Tổng quan, Giao dịch, Ngân sách, Thêm. Tổng quan shows "Xin chào …" from `GET /api/v1/me`
  to prove the sign-in works; the real screens arrive in phase 1.

## Working on the code

Flutter 3.47.9 (pinned in `.github/workflows/mobile.yml`).

```sh
cd mobile
flutter pub get
flutter test
flutter run                                     # against https://finance.digifusion.vn
flutter run --dart-define=SOITHO_SERVER=http://192.168.100.121:8080   # another server
```

| Folder | Holds |
|---|---|
| `lib/app` | config, theme, router, Riverpod providers |
| `lib/auth` | sign-in (AppAuth), token manager, Dio interceptor, secure token store |
| `lib/lock` | app lock policy, lock screen |
| `lib/features` | one folder per screen |
| `lib/api/generated` | API client generated from `wwwroot/api/openapi-v1.json`; do not edit |

When the server's API changes, update `wwwroot/api/openapi-v1.json`, then run
`dart run swagger_parser` in `mobile/` and commit the result. CI fails when they disagree.
