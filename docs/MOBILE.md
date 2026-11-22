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
- Bottom tabs Tổng quan, Giao dịch, Ngân sách, Thêm.

## What phase 1 adds (read-only screens)

- **Tổng quan**: joint-account balance and last bank sync, balance per day, money in/out for the budget
  cycle (5th to 4th) with the part inferred from balance gaps, projected spending, upcoming items, the
  last transactions.
- **Giao dịch**: all joint-account transactions grouped by day, older pages load while scrolling
  (`nextCursor`), pull to refresh.
- **Ngân sách**: the plan from `GET /api/v1/finance/budget` (paydays and where the money goes, funds and
  limits, debts).
- **Offline**: each screen keeps its last answer encrypted on the phone (same store as the refresh
  token) and shows it at once, then refreshes; without a connection a note says how old the numbers are.
  Signing out, or the server ending the sign-in, deletes these snapshots.

## What phase 2 adds (writing from the phone)

- **Đối soát** (Thêm → Đối soát, or "Ghi rõ các khoản này" on Tổng quan): the balance changes the bank
  never reported as a transaction. Tap one to say what it was (amount, date, category, note); swipe an
  entry made by hand to delete it. Each entry carries its own `Idempotency-Key`, so a retry after a
  dropped connection never adds it twice.
- **Chụp hóa đơn** (on Đối soát): photo of a receipt or a transfer, the home AI reads amount and date,
  the family picks which gap it is, and the entry form opens filled in. If the bank already reported the
  payment, the app says so. Photos are made smaller on the phone (2000 px) and are not stored.
- **Đồng bộ ngay** (the ⟳ button on Tổng quan and Giao dịch): asks SePay for new transactions now.
- **Sửa ngân sách** (✎ on Ngân sách): amounts, days, limits and debt balances, with live sums of what
  each payday and fund still has to give out. Adding or removing funds, incomes and debts stays on the
  website. When the partner saved first, the app loads their version instead of overwriting it.
- **Nhãn thực phẩm** (Thêm): 1 to 3 photos, check the numbers the AI read (uncertain ones are marked),
  then the same rating as the website, saved to the household history.
- **Thiết bị đang đăng nhập** (Thêm): every phone and browser signed in to the account; sign others out.
- Server messages arrive as codes and are shown in Vietnamese (`lib/shared/issues.dart`).

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

## Known pitfalls

- `MainActivity` must not have `android:taskAffinity=""` (the Flutter template adds it). With it, the
  browser's redirect to `vn.digifusion.soitho:/oauth/callback` never returns to the app after **Cho phép**
  ([flutter_appauth issue 503](https://github.com/MaikuB/flutter_appauth/issues/503)).
