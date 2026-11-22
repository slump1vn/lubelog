# Server side for the iPhone app (phase 0)

This is what the server offers the native iOS app. The app itself lives in `ios/` (phase 1).

## Signing in

The app uses OAuth 2.0 authorization code with PKCE, the way RFC 8252 asks native apps to sign in:

1. The app makes a random `code_verifier` (43 to 128 characters) and its S256 `code_challenge`.
2. It opens, in `ASWebAuthenticationSession` (never a web view inside the app):
   `https://finance.digifusion.vn/oauth/authorize?response_type=code&client_id=soitho-ios&redirect_uri=vn.digifusion.finance:/oauth/callback&state=<random>&code_challenge=<challenge>&code_challenge_method=S256`
3. The user signs in on the usual login page and taps **Cho phép** (Allow).
4. The browser returns to `vn.digifusion.finance:/oauth/callback?code=...&state=...`. The app checks `state`.
5. The app posts `grant_type=authorization_code`, `client_id`, `redirect_uri`, `code`, `code_verifier` and
   optionally `device_name` (for example "iPhone của Sói") to `/oauth/token`. The code works once, for one minute.
6. The answer holds an `access_token` (15 minutes) and a `refresh_token`. Keep the refresh token in the
   Keychain (`kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`), the access token in memory only.
7. Call the API with `Authorization: Bearer <access_token>`. On 401, post `grant_type=refresh_token`
   once; the answer has a **new** refresh token that replaces the old one. If a refresh token is ever
   used twice, the server ends that sign-in, so never retry a refresh with an old token.
8. Signing out in the app: post `token=<refresh_token>&client_id=soitho-ios` to `/oauth/revoke`.

`/.well-known/oauth-authorization-server` lists these endpoints (RFC 8414).

Each app sign-in shows up in **Tài khoản → Thiết bị đang đăng nhập** on the website, where it can be signed out.
A new password, a password reset or a role change signs out all other devices, the app included.

## API v1

The contract is [`wwwroot/api/openapi-v1.json`](../wwwroot/api/openapi-v1.json), also served at
`/api/openapi-v1.json`. A unit test fails when a field is sent but not documented.

| Call | What it gives |
|---|---|
| `GET /api/v1/me` | user, household |
| `GET /api/v1/finance/dashboard` | balance, money in and out this cycle, balance per day, coming up, last 8 transactions |
| `GET /api/v1/finance/transactions?limit=50&cursor=...` | transactions newest first; pass `nextCursor` back for older ones |
| `GET /api/v1/finance/budget` | paydays, funds, limits, debts (404 until set up on the website) |

Answers are never cached. Errors are RFC 9457 problem details (`application/problem+json`).

## Settings on the server

| Variable | Meaning |
|---|---|
| `LUBELOGGER_IOS_APP_ID` | `TEAMID.bundle.id` from the Apple Developer account. Turns on `/.well-known/apple-app-site-association` (passwords and passkeys shared between the site and the app). 404 while unset. |
| `LUBELOGGER_COOKIE_LIFESPAN` | days a "Remember me" login and an unused app sign-in last (default 30, max 90). |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` | needed behind the reverse proxy, so cookies are marked Secure and the rate limit sees the real address. |

## Security notes

- Login cookie: HttpOnly, SameSite=Lax, Secure over HTTPS, tied to a server-side session that can be ended.
  Cookies made before this version are not accepted: everyone signs in once after the update.
- Sign-in and password reset: 10 tries per address per 5 minutes; token endpoint 30. Then 429 with Retry-After.
- Only hashes (SHA-256) of session secrets and refresh tokens are stored. Access tokens are encrypted and
  signed with the server's Data Protection keys (the `keys` volume), so keep that volume backed up.
- Bearer tokens only work under `/api`; they never open web pages.
- `scripts/smoke-auth.sh` checks all of this against a running server and runs in CI on every push.
