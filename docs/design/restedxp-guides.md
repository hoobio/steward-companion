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

Steward signs in against Keycloak directly with an offline token, so a session survives the app being closed. Both grants are `application/x-www-form-urlencoded` POSTs to the realm's token endpoint, with no cookie on the request.

```
POST https://sso.restedxp.com/realms/RestedXP-Prod/protocol/openid-connect/token
grant_type=password&client_id=rxp-prod-login-client&scope=openid offline_access&username=...&password=...[&totp=<code>]

POST https://sso.restedxp.com/realms/RestedXP-Prod/protocol/openid-connect/token
grant_type=refresh_token&client_id=rxp-prod-login-client&refresh_token=<refresh_token>

200 {"access_token","refresh_token","expires_in","refresh_expires_in","token_type","scope"}
400 {"error":"invalid_grant","error_description":"Invalid user credentials"}
```

For an offline token `refresh_expires_in` is `0`, which means no expiry rather than an expiry now, so Steward stores a null refresh expiry and never computes `now + 0`. `invalid_grant` with description "Invalid user credentials" is a wrong username or password; an `invalid_grant` whose description mentions "otp" or "not fully set up" is the code step; any 400 on the refresh grant is a dead session. Every other failure keeps Keycloak's own `error_description`.

The stored `refresh_token` has JWT `typ` `Offline` and no `exp` claim. Each pass exchanges it for a 15-minute access token, which Steward wraps into the `rxp_cross_auth_token` cookie JSON for the two backends. The cookie's `expiresAt` is the refresh expiry, or 30 days out where there is none.

Probed on 20 Sep 2026: the realm's discovery document lists `password` among the grants and `offline_access` among the scopes; the password grant with `client_id=rxp-prod-login-client`, no client secret and `scope=openid offline_access` answers `invalid_grant` for wrong credentials rather than `unauthorized_client` or `invalid_scope`, so the client is public with direct grants on; and the authorization endpoint serves its login page for `scope=openid offline_access` while redirecting with an error for a made-up scope, so `offline_access` is assigned to that client. A user also needs the realm's `offline_access` role, which Keycloak grants to every user by default. An offline refresh token has no expiry of its own; Keycloak's offline session idle defaults to 30 days from last use, sliding on each refresh, with no absolute ceiling unless the realm sets one. That is the same shape as the 30-day sliding `gg_session`.

Verified from PowerShell with real credentials on 20 Sep 2026: the password grant with `scope=openid offline_access` succeeds and returns a refresh token whose JWT `typ` is `Offline` with no `exp` claim, the refresh grant returns a new access token with `expires_in` 900, and a cookie whose `token` is that access token and whose `user` is an empty object gets 200 from `/user-products` (454 bytes) and 200 from `/addon?guideName=Forever%20Leveling%20Guide%20-%20Both%20Factions&bundleIndex=0` (692,624 bytes). The backends read only `token` from the cookie.

With TOTP enabled on the account, the direct grant still answers 200 without a code and with a blank `totp` field: the realm's direct-grant flow has no OTP step, and Keycloak issues the token on the password alone today. That gap is RestedXP's to close and is reported to them. Steward's sign-in dialog keeps its code step, shown when Keycloak answers with an OTP description and sent as `totp` on a second password grant, so it works unchanged once they add OTP to that flow. No client secret is involved: the client is public, as a browser or desktop client has to be. Tell the developer before relying on it: a public client with direct grants and `offline_access` is a realm setting they can tighten at any time, and a dedicated public `steward` client id is the durable version of this.

Cadence: on startup, on a manual Refresh, and every 3 hours, not the 15-minute addon pass, plus on demand before a call whose access token has under a minute left. Each pass refreshes the offline session, so a Steward that runs at all in any 30-day window never asks for the password again.

The cookie itself is the SPA's. It writes `rxp_cross_auth_token` on `.restedxp.com` (`Secure`, `SameSite=Lax`, not `HttpOnly`, expiry = refresh token expiry), with the URL-encoded JSON `{"token":"<access_token>","refreshToken":"<refresh_token>","user":{<decoded JWT claims>},"expiresAt":<unix ms>}` as its value. Every account and guide call is made with `withCredentials` and no `Authorization` header, so that cookie is the credential the two backends read. Steward sends `Cookie: rxp_cross_auth_token=<encoded JSON>` on every call to `rxpbck` and `rxpgcr`.

### Previous design: the account backend's proxy

The first design ran Keycloak in Direct Access Grant mode through `rxpbck`, the way the login SPA does. It is recorded here for reference; Steward no longer calls these paths.

```
POST https://rxpbck.restedxp.com/login/keycloak
Content-Type: application/json
{"username":"<name or email>","password":"<password>"}

200 {"access_token","refresh_token","expires_in","refresh_expires_in","token_type","scope"}
200 {"mfaRequired":true,"sessionId":"..."}        when the account has MFA
```

MFA completes with `POST https://rxpbck.restedxp.com/login/verify-mfa` and body `{"sessionId","mfaToken","recovery":false}`, answering the same token shape. Refresh is `POST https://rxpbck.restedxp.com/login/keycloak/refresh` with `{"refresh_token":"<refresh_token>"}`.

Lifetimes, read from a live session on 20 Sep 2026: the access token lasts 15 minutes and the refresh token 30 minutes from issue, and the cookie expires with the refresh token. `TOKEN_EXPIRY: 32400` in the bundle is not what Keycloak issues. Through the backend proxy, a Steward closed for more than 30 minutes has a dead session and must ask for the password again, which is why the offline token replaced it. The proxy's `mfaRequired` step is the only MFA on this account today.

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

Route 2 was the first design and is dead on this client: the Forever beta (1.60.1 build 69913) does not restore account-level SavedVariables on login or `/reload` (ClassicWoWCommunity/forever-bugs#34, open as of 18 Sep 2026), so `RXPString` is never read, and `RXPDB` never comes back either. A write of the merged Forever plus Mists string into `RXPGuides.lua` on 20 Sep 2026 imported nothing. Route 2 also wipes `db.profile.guides` before importing, so it holds one product per account even where it works.

Route 3, the one Steward uses, is a generated addon. Steward writes `Interface\AddOns\StewardGuides\` with `StewardGuides.toc` (`## Category: Hoobi`, `## Dependencies: RXPGuides`, `## IconTexture: Interface\AddOns\StewardGuides\Icon`, notes "Purchased RestedXP guides, kept current by the Steward desktop app, with no settings of its own."), a copy of the Steward addon's `Icon.tga`, and `Guides.lua` holding each selected product's string as a Lua long-bracket literal (`[==[...]==]`; the strings are base64 plus `|:%` and cannot contain the terminator). On `PLAYER_ENTERING_WORLD` plus 3 seconds the file fetches the RXPGuides addon object through `LibStub("AceAddon-3.0"):GetAddon("RXPGuides", true)` and hands each string to `guideImporter:ImportString(text)`, the same path the in-game paste box uses, one at a time: a second call while an import is running resets the first one's buffer, so the file waits until `importCoroutine` is nil and `importBufferSize` is 0 before the next. It prints one `Steward` line per guide to chat. Each entry carries a `tag`, the purchaser's BattleTag from the cached download: the file reads the player's own through `BNGetInfo` (waiting 5 seconds at a time, six times, while Battle.net connects, as it does for an RXPGuides that has no `settings.profile` yet) and skips a guide bought on another tag with "bought on <tag>, you are <playerTag>; not imported", since the importer's own integrity check rejects it with nothing in chat. A finished import whose `importStatusHistory[1]` is not "Guides Loaded Successfully" is printed the same way and kept in `StewardGuidesDB.status[hash]` until a success clears it, so the desktop app can read the importer's own words off the saved variable; a status holding "restart your game client" is retried once after 10 seconds first. Verified on 20 Sep 2026 with Forever and Mists together: both products appeared in the guide menu with the free guides intact.

Consequences: several products per install; no client-closed rule, since the file is only read at login or `/reload`, so the row reads "Imports on next login or /reload"; the BattleTag check still runs inside the importer's integrity check, which derives its key from `BNGetInfo`, so Battle.net must be connected at the 3-second mark. While forever-bugs#34 stands the addon re-imports every login, a few seconds of work; once SavedVariables load again, a `StewardGuidesDB` saved variable records the hash of each string that finished with "Guides Loaded Successfully" and the file skips those.

The write follows `StewardSyncFile`: temp-then-move, the folder is only replaced when it holds Steward's own `StewardGuides.toc`, and the path never contains a `WTF` segment.

## Client gating

The API carries no expansion field; `productName` is the only handle. `appsettings.json` maps product-name prefixes to a client flavour: "Forever" to the Forever client, "Mists of Pandaria" to mists, "Cataclysm" to cata, "Classic Era", "Season of Mastery" and "Hardcore" to vanilla, "The Burning Crusade" to tbc, anything else to mainline. A product whose flavour is not the install's is listed on the Guides page but disabled with "Not for this client" and is never written to that install. Importing a Mists string on the Forever client parses every guide with "Invalid" zone errors in chat, which is what the gate prevents.

## What Steward needs

- A RestedXP sign-in card: username and password prompt, MFA code prompt, tokens persisted in `state.json` as the Discord session is, "Signed in as <BattleTag>" from `/user`.
- On the guide check: `/user-products`, then `/addon/get-all-timestamps`, compare per selected guide against the stored timestamp, download any newer string, regenerate `StewardGuides\Guides.lua` for that install, record the timestamp. One row per owned guide on the Guides page.
- The min-version check: parse the trailing number and hold the write until the installed RXPGuides TOC version satisfies it.

Open questions for the RestedXP developer, in priority order: whether handing strings to `guideImporter:ImportString` from a companion addon is an approach they will keep supporting once their own app ships, whether an expansion or client field can be added to `/user-products` or the timestamps so the name-prefix map can go, whether `rxpgcr` throttles per account or per IP, and whether the cross-auth cookie will remain the credential for `rxpgcr` or move to a bearer header.
