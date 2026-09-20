# RestedXP paid guide sync

How the paid RestedXP guides reach the RXPGuides addon today, and the contract Steward can use to keep them current until RestedXP ship their own desktop app. Everything below was read from the production Angular bundles on `login.restedxp.com`, `account.restedxp.com` and `download.restedxp.com`, from live calls made under a signed-in session on 20 Sep 2026, and from the RXPGuides addon source on GitHub. Nothing is documented publicly; treat every path as subject to change without notice.

The free addon (`RestedXP/RXPGuides` on GitHub) is already managed by Steward. `download.restedxp.com` does nothing more than fetch `/repos/RestedXP/RXPGuides/releases/latest` and link the last `.zip` asset, so that page adds nothing.

## Hosts

| Host | Role |
| --- | --- |
| `login.restedxp.com` | Login SPA. Username or email plus password, optional MFA. |
| `sso.restedxp.com` | Keycloak, realm `RestedXP-Prod`, client `rxp-prod-login-client`. Never called by the browser directly; the backend proxies it. |
| `rxpbck.restedxp.com` | Account backend. Login, refresh, logout, `/user`, `/user-products`. |
| `rxpgcr.restedxp.com` | Guide content backend. Timestamps, published guides, guide download. |
| `shop.restedxp.com` | WooCommerce. Only its `wp-json/rxp-crossauth/v1` plugin is auth-related, and only for the shop itself. |

## Auth

The login SPA runs Keycloak in Direct Access Grant mode through the backend, so there is no browser redirect to complete and no OAuth callback to host. The whole flow is two JSON calls.

```
POST https://rxpbck.restedxp.com/login/keycloak
Content-Type: application/json
{"username":"<name or email>","password":"<password>"}

200 {"access_token","refresh_token","expires_in","refresh_expires_in","token_type","scope"}
200 {"mfaRequired":true,"sessionId":"..."}        when the account has MFA
```

MFA completes with `POST https://rxpbck.restedxp.com/login/verify-mfa` and body `{"sessionId","mfaToken","recovery":false}`, answering the same token shape.

Refresh:

```
POST https://rxpbck.restedxp.com/login/keycloak/refresh
{"refresh_token":"<refresh_token>"}
```

The SPA stores the tokens in `localStorage` and also writes one cookie, `rxp_cross_auth_token`, on `.restedxp.com` (`Secure`, `SameSite=Lax`, not `HttpOnly`, expiry = refresh token expiry). Its value is the URL-encoded JSON `{"token":"<access_token>","refreshToken":"<refresh_token>","user":{<decoded JWT claims>},"expiresAt":<unix ms>}`. Every account and guide call is made with `withCredentials` and no `Authorization` header, so that cookie is the credential the two backends read. For Steward: send `Cookie: rxp_cross_auth_token=<encoded JSON>` on every call to `rxpbck` and `rxpgcr`, refresh before `expiresAt`, and store only the refresh token at rest.

Lifetimes, read from a live session on 20 Sep 2026: the access token lasts 15 minutes and the refresh token 30 minutes from issue, and the cookie expires with the refresh token. `TOKEN_EXPIRY: 32400` in the bundle is not what Keycloak issues. Through the backend proxy, a Steward closed for more than 30 minutes has a dead session and must ask for the password again.

The way round that is an offline token, requested from Keycloak directly rather than through the proxy. Probed on 20 Sep 2026: the realm's discovery document lists `password` among the grants and `offline_access` among the scopes; a password grant against `https://sso.restedxp.com/realms/RestedXP-Prod/protocol/openid-connect/token` with `client_id=rxp-prod-login-client`, no client secret and `scope=openid offline_access` answers `invalid_grant` for wrong credentials rather than `unauthorized_client` or `invalid_scope`, so the client is public with direct grants on; and the authorization endpoint serves its login page for `scope=openid offline_access` while redirecting with an error for a made-up scope, so `offline_access` is assigned to that client. A user also needs the realm's `offline_access` role, which Keycloak grants to every user by default. An offline refresh token has no expiry of its own; Keycloak's offline session idle defaults to 30 days from last use, sliding on each refresh, with no absolute ceiling unless the realm sets one. That is the same shape as the 30-day sliding `gg_session`.

So Steward signs in once with

```
POST https://sso.restedxp.com/realms/RestedXP-Prod/protocol/openid-connect/token
grant_type=password&client_id=rxp-prod-login-client&scope=openid offline_access&username=...&password=...
```

stores the `refresh_token` (its JWT `typ` is `Offline`), and on each pass exchanges it with `grant_type=refresh_token` for a 15-minute access token, which it wraps into the `rxp_cross_auth_token` cookie JSON for the two backends.

Verified from PowerShell with real credentials on 20 Sep 2026: the password grant with `scope=openid offline_access` succeeds with no MFA step and returns a refresh token whose JWT `typ` is `Offline` with no `exp` claim, the refresh grant returns a new access token with `expires_in` 900, and a cookie whose `token` is that access token and whose `user` is an empty object gets 200 from `/user-products` (454 bytes) and 200 from `/addon?guideName=Forever%20Leveling%20Guide%20-%20Both%20Factions&bundleIndex=0` (692,624 bytes). The backends read only `token` from the cookie. With TOTP enabled on the account, the direct grant still answers 200 without a code and with a blank `totp` field, so the account backend's `mfaRequired` step is the only MFA there is; the realm's direct-grant flow has no OTP step and Keycloak issues tokens on the password alone. Steward's sign-in card keeps an optional code field sent as `totp`, shown when Keycloak asks for one, so it works unchanged if they add OTP to that flow. The gap itself is theirs to close and is reported to them. No client secret is involved: the client is public, as a browser or desktop client has to be. Tell the developer before relying on it: a public client with direct grants and `offline_access` is a realm setting they can tighten at any time, and a dedicated public `steward` client id is the durable version of this.

Cadence: on startup, on a manual Refresh, and every 3 hours, not the 15-minute addon pass. Each pass refreshes the offline session, so a Steward that runs at all in any 30-day window never asks for the password again.

Unauthenticated probes from PowerShell with a plain `User-Agent`: `GET /addon/get-all-timestamps` answers 200 with no cookie, so the version check needs no session; `GET /user-products` answers 401 `{"message":"Authentication required. Please login.","auth_source":"none"}`; `POST /login/keycloak` with wrong credentials answers 400 `{"success":false,"message":"Invalid user credentials","error":"invalid_grant"}`. Neither host rejects a non-browser client.

Steward must never hold the RestedXP password itself: the prompt is a one-off sign-in that exchanges it for tokens and discards it, the same posture as the Discord sign-in.

## Entitlements

```
GET https://rxpbck.restedxp.com/user-products
200 [{"productName":"Forever Leveling Guide - Both Factions","productImageUrl":"https://shop.restedxp.com/wp-content/uploads/2026/09/forever-book-both.webp"}, ...]
```

`productName` is the key into everything else. It is the WooCommerce product title, spaces and all.

## Latest version

There is no version number. Each guide has a publish timestamp in unix milliseconds:

```
GET https://rxpgcr.restedxp.com/addon/get-all-timestamps
200 {"timestamps":{"Forever Leveling Guide - Both Factions":1789767504421, ... 82 entries}}
```

The account page renders "Updated 1 day ago" from this. It lists every guide RestedXP publish, not only the ones owned, so intersect with `/user-products`. A guide is stale when its stored timestamp is older than this value.

`GET /addon/published-guides` answers `{"guides":[{"guideName","locale"}]}` and lists the non-English localisations only; it was empty at the time of writing. `GET /addon/get-bundle-info?guideName=...` answers the sub-guide names of a bundle product such as the Mists of Pandaria bundle, which the `bundleIndex` below selects from; it answered a CORS-blocked error for a non-bundle guide.

## Download

```
GET https://rxpgcr.restedxp.com/addon?guideName=Forever%20Leveling%20Guide%20-%20Both%20Factions&bundleIndex=0[&lang=xx]
200 {"encryptedGuides":[{"guideName":"...","guide":"<string>","bnetTag":"Hoobi#11438"}]}
```

`guide` is the full import string, 692 KB for the Forever guide. It is encrypted to the BattleTag on the RestedXP account and the addon checks the tag against the logged-in Battle.net account before importing, so a string is only good for that one BattleTag. The account page saves it verbatim as `<productName>!<bnetTag>.txt`. The page's own guard on failure reads "Try again in 10 seconds", so rate-limit retries to that.

The string's shape is `<count>|<hash>:<base64 payload>%|<minAddonVersion>`. The head for the Forever guide is `83|1084041902:...` and the tail is `...%|40000`. The addon rejects a string whose trailing number is below its own `addon.version` (40000 today), so an addon update can be a prerequisite of a guide update.

## Getting the string into the addon

The addon offers two routes, in `GuideLoader.lua`:

1. The in-game importer, where the player pastes the whole string into a text box. That is the documented path today and is what "Export" on the account page feeds.
2. The `RXPString` saved variable. `RXPGuides.toc` declares `## SavedVariables: RXPData, RXPDB, RXPSettings, RXPString`, and `addon.LoadCachedGuides()` reads `addon.string or RXPString` on load. If the string's header (`count`, `hash`, payload prefix) differs from what `RXPDB.profiles.<profile>.guideId/guideLength/guideContent` recorded from the last import, it runs `ImportString` on it synchronously and opens the importer window to show progress.

Route 2 is the one Steward uses. With the client closed, Steward writes `RXPString = "<string>"` into `WTF\Account\<account>\SavedVariables\RXPGuides.lua`, the same file layout `StewardSavedVariables` already walks for the Steward addon, using `LuaWriter`. On the next login the addon imports it with no player action. The file must be rewritten with the client closed: WoW writes SavedVariables on logout and would overwrite the change, and the existing close-then-apply rule for addon updates covers this. Hoobi's own `RXPGuides.lua` currently holds `RXPString = nil` and no imported guides, which is the clean starting state.

The string is bound to the BattleTag, and one `WTF\Account\<id>` folder is one Battle.net account, so the write goes to every account folder under the install; the addon rejects the string on a non-matching account and leaves the player's other guides alone. Once imported, the addon keeps the guides in `RXPDB` and the `RXPString` value is only re-read when its header changes, so leaving it in place is harmless and rewriting it with the same content is a no-op.

## What Steward needs

- A RestedXP sign-in card: username and password prompt, MFA code prompt, tokens persisted in `state.json` as the Discord session is, "Signed in as <BattleTag>" from `/user`.
- On the existing 15-minute pass: `/user-products`, then `/addon/get-all-timestamps`, compare per owned guide against the stored timestamp, download any newer string, write `RXPString` per account folder when the client is closed, record the timestamp. A row per owned guide under the RXPGuides addon row, in the style of the update rows.
- The min-version check: parse the trailing number and hold the write until the installed RXPGuides TOC version satisfies it.

Open questions for the RestedXP developer, in priority order: whether writing `RXPString` from outside the game is an approach they will keep supporting once their own app ships, whether `rxpgcr` throttles per account or per IP, and whether the cross-auth cookie will remain the credential for `rxpgcr` or move to a bearer header.
