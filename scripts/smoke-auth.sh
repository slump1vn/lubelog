#!/usr/bin/env bash
# End-to-end check of sign-in, device sessions, the app's OAuth flow and API v1 against a running
# server with a fresh data folder. Used by CI; run locally with: scripts/smoke-auth.sh http://127.0.0.1:5080
set -euo pipefail
BASE="${1:-http://127.0.0.1:5080}"
WORK="$(mktemp -d)"
WEB="$WORK/web.txt"
CLIENT=soitho-ios
REDIRECT='vn.digifusion.finance:/oauth/callback'
USER_NAME=smoke-admin
USER_PASS="$(python3 -c 'import secrets; print(secrets.token_urlsafe(18))')"

fail() { echo "FAIL: $*" >&2; exit 1; }
pass() { echo "ok - $*"; }
json() { python3 -c "import json,sys; d=json.load(sys.stdin); print(eval(sys.argv[1], {'d': d}))" "$1"; }
status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
location() { tr -d '\r' | sed -n 's/^[Ll]ocation: //p'; }

# 1. With no login set up every visitor is the admin; create the login, which turns sign-in on.
curl -sf -X POST "$BASE/Login/CreateLoginCreds" --data-urlencode "userName=$USER_NAME" --data-urlencode "password=$USER_PASS" | grep -q true || fail "create login"
sleep 2 # the server reloads its configuration file
[ "$(status "$BASE/api/v1/me")" = 401 ] || fail "API without a token must be 401"
curl -s "$BASE/api/v1/me" -o "$WORK/401.json" -w '%{content_type}' | grep -q 'application/problem+json' || fail "401 must be problem+json"
pass "sign-in is required and API errors are problem details"

# 2. Cookie login: HttpOnly + SameSite, and a wrong password is refused.
curl -s -X POST "$BASE/Login/Login" --data-urlencode "userName=$USER_NAME" --data-urlencode "password=wrong" | json 'd["success"]' | grep -q False || fail "wrong password accepted"
curl -s -c "$WEB" -D "$WORK/login-headers.txt" -X POST "$BASE/Login/Login" --data-urlencode "userName=$USER_NAME" --data-urlencode "password=$USER_PASS" | json 'd["success"]' | grep -q True || fail "login"
grep -i '^set-cookie: ACCESS_TOKEN=' "$WORK/login-headers.txt" | grep -iq 'httponly' || fail "login cookie not HttpOnly"
grep -i '^set-cookie: ACCESS_TOKEN=' "$WORK/login-headers.txt" | grep -iq 'samesite=lax' || fail "login cookie not SameSite=Lax"
[ "$(status -b "$WEB" "$BASE/Home/GetUserSessionsModal")" = 200 ] || fail "device list"
pass "cookie login with HttpOnly and SameSite=Lax"

# 3. The login page refuses to send people to another site afterwards.
curl -s "$BASE/Login/Index?redirectURL=https://example.com/" | grep -q "example.com" && fail "open redirect"
pass "no redirect to other sites"

# 4. OAuth: authorize (consent page), allow, exchange the code with PKCE.
VERIFIER="$(python3 -c 'import secrets; print(secrets.token_urlsafe(48))')"
CHALLENGE="$(python3 -c 'import sys,hashlib,base64; print(base64.urlsafe_b64encode(hashlib.sha256(sys.argv[1].encode()).digest()).decode().rstrip("="))' "$VERIFIER")"
AUTH_URL="$BASE/oauth/authorize?response_type=code&client_id=$CLIENT&redirect_uri=$(python3 -c 'import urllib.parse,sys; print(urllib.parse.quote(sys.argv[1], safe=""))' "$REDIRECT")&state=xyz&code_challenge=$CHALLENGE&code_challenge_method=S256"
[ "$(status "$AUTH_URL")" = 302 ] || fail "authorize without login must send to the login page"
curl -s -b "$WEB" -c "$WEB" "$AUTH_URL" -o "$WORK/consent.html"
TOKEN_FIELD="$(python3 -c 'import re,sys; print(re.search(r"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", open(sys.argv[1]).read()).group(1))' "$WORK/consent.html")" || fail "consent page"
[ "$(status -b "$WEB" -X POST "$BASE/oauth/authorize" --data-urlencode "client_id=$CLIENT" --data-urlencode "redirect_uri=$REDIRECT" --data-urlencode "code_challenge=$CHALLENGE" --data-urlencode "decision=allow")" = 400 ] || fail "consent without the antiforgery token must fail"
LOCATION="$(curl -s -b "$WEB" -c "$WEB" -o /dev/null -D - -X POST "$BASE/oauth/authorize" \
  --data-urlencode "__RequestVerificationToken=$TOKEN_FIELD" --data-urlencode "client_id=$CLIENT" --data-urlencode "redirect_uri=$REDIRECT" \
  --data-urlencode "state=xyz" --data-urlencode "code_challenge=$CHALLENGE" --data-urlencode "decision=allow" | location)"
case "$LOCATION" in "$REDIRECT?code="*"&state=xyz") ;; *) fail "consent redirect: $LOCATION" ;; esac
CODE="$(python3 -c 'import sys,urllib.parse; print(urllib.parse.parse_qs(urllib.parse.urlsplit(sys.argv[1]).query)["code"][0])' "$LOCATION")"
[ "$(status -X POST "$BASE/oauth/token" -d grant_type=authorization_code -d client_id=$CLIENT --data-urlencode "redirect_uri=$REDIRECT" --data-urlencode "code=$CODE" --data-urlencode "code_verifier=${VERIFIER}x")" = 400 ] || fail "wrong verifier accepted"
pass "consent needs login and antiforgery; a wrong PKCE verifier is refused"

# the code was spent by the failed try above, so start again.
curl -s -b "$WEB" -c "$WEB" "$AUTH_URL" -o "$WORK/consent.html"
TOKEN_FIELD="$(python3 -c 'import re,sys; print(re.search(r"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", open(sys.argv[1]).read()).group(1))' "$WORK/consent.html")"
LOCATION="$(curl -s -b "$WEB" -c "$WEB" -o /dev/null -D - -X POST "$BASE/oauth/authorize" \
  --data-urlencode "__RequestVerificationToken=$TOKEN_FIELD" --data-urlencode "client_id=$CLIENT" --data-urlencode "redirect_uri=$REDIRECT" \
  --data-urlencode "state=xyz" --data-urlencode "code_challenge=$CHALLENGE" --data-urlencode "decision=allow" | location)"
CODE="$(python3 -c 'import sys,urllib.parse; print(urllib.parse.parse_qs(urllib.parse.urlsplit(sys.argv[1]).query)["code"][0])' "$LOCATION")"
curl -s -D "$WORK/token-headers.txt" -X POST "$BASE/oauth/token" -d grant_type=authorization_code -d client_id=$CLIENT --data-urlencode "redirect_uri=$REDIRECT" \
  --data-urlencode "code=$CODE" --data-urlencode "code_verifier=$VERIFIER" --data-urlencode "device_name=CI iPhone" -o "$WORK/token1.json"
grep -iq '^cache-control: no-store' "$WORK/token-headers.txt" || fail "token answer must not be cached"
ACCESS="$(json 'd["access_token"]' < "$WORK/token1.json")"; REFRESH1="$(json 'd["refresh_token"]' < "$WORK/token1.json")"
[ "$(json 'd["expires_in"]' < "$WORK/token1.json")" = 900 ] || fail "access token lifetime"
[ "$(status -X POST "$BASE/oauth/token" -d grant_type=authorization_code -d client_id=$CLIENT --data-urlencode "redirect_uri=$REDIRECT" --data-urlencode "code=$CODE" --data-urlencode "code_verifier=$VERIFIER")" = 400 ] || fail "a code worked twice"
pass "code exchanged once for tokens"

# 5. API v1 with the access token; tokens do not open web pages.
curl -sf -H "Authorization: Bearer $ACCESS" "$BASE/api/v1/me" | json 'd["userName"]' | grep -q "$USER_NAME" || fail "me"
curl -sf -H "Authorization: Bearer $ACCESS" "$BASE/api/v1/finance/dashboard" | json '"cycle" in d and "account" in d' | grep -q True || fail "dashboard"
curl -sf -H "Authorization: Bearer $ACCESS" "$BASE/api/v1/finance/transactions?limit=5" | json '"items" in d' | grep -q True || fail "transactions"
[ "$(status -H "Authorization: Bearer $ACCESS" "$BASE/api/v1/finance/transactions?cursor=nonsense")" = 400 ] || fail "bad cursor"
[ "$(status -H "Authorization: Bearer $ACCESS" "$BASE/Home/GetUserSessionsModal")" != 200 ] || fail "a token opened a web page"
[ "$(status -H "Authorization: Bearer not-a-token" "$BASE/api/v1/me")" = 401 ] || fail "forged token"
pass "API v1 answers with the token, and only the API does"

# 6. Refresh rotates; the old refresh token coming back ends the app session.
curl -sf -X POST "$BASE/oauth/token" -d grant_type=refresh_token -d client_id=$CLIENT --data-urlencode "refresh_token=$REFRESH1" -o "$WORK/token2.json" || fail "refresh"
ACCESS2="$(json 'd["access_token"]' < "$WORK/token2.json")"; REFRESH2="$(json 'd["refresh_token"]' < "$WORK/token2.json")"
[ "$REFRESH1" != "$REFRESH2" ] || fail "refresh token did not rotate"
[ "$(status -H "Authorization: Bearer $ACCESS2" "$BASE/api/v1/me")" = 200 ] || fail "new access token"
[ "$(status -X POST "$BASE/oauth/token" -d grant_type=refresh_token -d client_id=$CLIENT --data-urlencode "refresh_token=$REFRESH1")" = 400 ] || fail "old refresh token accepted"
[ "$(status -H "Authorization: Bearer $ACCESS2" "$BASE/api/v1/me")" = 401 ] || fail "session not ended after refresh token reuse"
[ "$(status -X POST "$BASE/oauth/token" -d grant_type=refresh_token -d client_id=$CLIENT --data-urlencode "refresh_token=$REFRESH2")" = 400 ] || fail "newest refresh token still works after reuse"
pass "refresh tokens rotate and reuse ends the session"

# 7. Signing out the web session on the server: the old cookie stops working.
curl -s -b "$WEB" -X POST "$BASE/Login/LogOut" -o /dev/null
[ "$(status -b "$WEB" "$BASE/Home/GetUserSessionsModal")" = 302 ] || fail "cookie still works after logout"
pass "logout ends the session on the server"

# 8. Rate limit on the sign-in.
for i in $(seq 1 12); do code="$(status -X POST "$BASE/Login/Login" --data-urlencode "userName=$USER_NAME" --data-urlencode "password=wrong$i")"; done
[ "$code" = 429 ] || fail "no rate limit on sign-in (last answer $code)"
pass "sign-in is rate limited"
# 9. Discovery documents.
curl -sf "$BASE/.well-known/oauth-authorization-server" | json 'd["code_challenge_methods_supported"]' | grep -q S256 || fail "OAuth metadata"
curl -sf "$BASE/api/openapi-v1.json" | json 'd["openapi"]' | grep -q '^3\.1' || fail "OpenAPI file"
[ "$(status "$BASE/.well-known/apple-app-site-association")" = 404 ] || fail "apple-app-site-association must be 404 while LUBELOGGER_IOS_APP_ID is unset"
pass "OAuth metadata, OpenAPI and apple-app-site-association"
echo "all checks passed"
