# CurseForge integration design

Phase 2 of `addon-manager.md`: the Get addons dialog and CurseForge as an addon source, across gigagrug (`D:\gigagrug`) and this app. This doc is the source of truth for the CurseForge parts; `addon-manager.md` stays the source of truth for the page, the table and the dialog layout.

Visual reference for the Get addons dialog, the search, result rows and the Installed state: https://claude.ai/artifact/Vtzv3nY6bR3SEZpepFb6NX (the interactive mockup behind `addon-manager.md`). Where the mockup and a doc disagree, the doc wins, and this doc wins over `addon-manager.md` on anything CurseForge-specific below.

Status: agreed direction, not implemented. Everything under "Verified API behaviour" was exercised against the live API with Steward's key on 29 Sep 2026.

## Access

- The key is a CurseForge 3rd-party API key, approved for the application "Steward" on 29 Sep 2026, and stored in Key Vault `hbicfgkvtaus01` as `curseforge-api-key`. It is not yet mapped into any variable group or container; wire it with the `hoobi-secret` skill (variable group `hoobi-podman` id 10, env var `CURSEFORGE_API_KEY` on the gigagrug service, `D:\hoobi-podman` files per that skill) in the same change that adds the code reading it.
- The key lives in gigagrug only and never ships in the app. Terms section 2.2: the key "is non-transferable and may not be shared with any third party", and a key compiled into a .NET Store package is recoverable with any decompiler whatever the obfuscation, so embedding it in Steward was rejected. (WowUp ships its key in its client, `AppConfig.curseforge.apiKey`; its agreement with CurseForge is not known here, so it is not a precedent for Steward's key.)
- Every CurseForge call goes `Steward -> gigagrug -> api.curseforge.com` with the `x-api-key` header, except file downloads, which the app takes straight from the CDN URL gigagrug returns (the CDN needs no key).

## Feature gate

Everything CurseForge sits behind a `curseforge` feature flag, held by default only by users with role `global` (global admin) and grantable to individuals like `hoobiscripts`. It arrives in `/api/me` `user.features` and is user-level, not guild-scoped.

- gigagrug: every `/api/addons/curseforge/*` route requires a signed-in `gg_session` and the `curseforge` feature; 401 without a session, 403 without the feature.
- App: `MainViewModel` exposes `HasCurseForgeFeature` from the user-level `_features`. Without it there is no `Get addons` button, no CurseForge matching (folders that would match stay Local rows), no CurseForge rows, and no CurseForge manifest fetches. CurseForge addons already recorded in `state.json` behave like a feature-hidden configured addon: no row, never updated, auto-applied or removed, their folders untouched on disk. `curseforge` alone does not count towards the app's access gate (`GigagrugClient.IsAuthorizing`), since every default holder already has access. Gaining or losing the feature mid-session goes through `RecheckAuthorizationAsync` and `ReconcileFeatureGating` like every other feature, including the `accessChanged` event stream.

## Terms constraints

From the CurseForge 3rd Party API Terms (https://support.curseforge.com/support/solutions/articles/9000207405-curse-forge-3rd-party-api-terms-and-conditions), quoted:

- 3.1(e): the developer shall not "save or cache any data obtained through the API or SDK". gigagrug therefore holds no CurseForge data: no database rows, no files, no in-memory cache. Each request to a gigagrug CurseForge route makes its CurseForge calls live and returns. This is the opposite of the GitHub mirror (`src/gigagrug/services/github_mirror/service.py`), which is why CurseForge is a separate service rather than another mirror. The app persists only what it needs to manage an install the user chose: the mod id, the installed file id, version and hash, in `state.json`, the same record every managed addon keeps. Whether that per-install record, or a short in-memory TTL on gigagrug, counts as caching is an open question for CurseForge (below).
- 3.1(e) also forbids concealing identity "through a proxy server or VPN". gigagrug calls CurseForge as itself with Steward's own key, so it identifies the developer rather than concealing it.
- 3.1: no "product or service that competes, directly or indirectly, with CF, CurseForge for Studios, or the Platform". Steward is a guild toolkit whose addon manager serves one game client; this is an open question for CurseForge before release (below).
- 11: Overwolf may reference the developer, its name, logo and app on its website. No attribution requirement on the developer was found; the app still labels CurseForge rows with the `CurseForge` source and links to each project page.
- No clause covers rate limits or `allowModDistribution`; the behaviour below follows the API itself.

## Verified API behaviour

Base `https://api.curseforge.com`, header `x-api-key`. World of Warcraft is `gameId=1`.

| Call | Result with Steward's key |
| --- | --- |
| `GET /v1/games`, `/v1/games/1`, `/v1/games/1/version-types`, `/v1/categories?gameId=1` | 200 (403 without the key) |
| `GET /v1/mods/search?...` | 200 since 29 Sep 2026 (the same day), with WowUp's request shape (`gameId=1&searchFilter=...&sortField=1&sortOrder=desc&index=0&gameVersionTypeId=...`, from `wowup-electron/src/app/addon-providers/curse-addon-provider.ts` `getSearchResults`). Earlier that day it answered 403 "API Key missing or invalid" for every shape, right after the key was approved; gigagrug keeps mapping a CurseForge 403 on search to `search_unavailable` in case that recurs. |
| `POST /v1/mods/featured` `{"gameId":1,"excludedModIds":[],"gameVersionTypeId":88568}` | 200: `featured` 0, `popular` 10, `recentlyUpdated` 10, scoped to Forever |
| `GET /v1/mods/{modId}`, `POST /v1/mods` `{"modIds":[...]}` | 200 |
| `GET /v1/mods/{modId}/files?gameVersionTypeId=88568` | 200, Forever builds only (DBM: 36) |
| `GET /v1/mods/{modId}/files/{fileId}/changelog` | 200, HTML string |
| `GET /v1/mods/{modId}/files/{fileId}/download-url` | 200 when the mod allows distribution, 403 otherwise |
| `POST /v1/fingerprints/1` | 200 |

WoW version types (`/v1/games/1/version-types`): `88568` WoW Forever (`wow-forever`), `517` Retail, `67408` Classic, `73246` Burning Crusade Classic, `73713` Wrath Classic, `77522` Cataclysm Classic, `79434` Mists of Pandaria Classic, `81212` Wrath Titan Reforged. The mapping from Steward's product code is config: `wow_classic_beta` to `88568`. A Forever file's `gameVersions` includes the client version (`1.60.1`).

File fields that matter:

- `releaseType` 1 release, 2 beta, 3 alpha.
- `hashes`: `algo` 1 is SHA-1 (40 hex), `algo` 2 is MD5 (32 hex). There is no SHA-256.
- `downloadUrl` on `edge.forgecdn.net`, fetchable without a key; `null` when the mod's `allowModDistribution` is false.
- `modules[].name`: the top-level folders the zip installs (DBM ships 9: `DBM-Core`, `DBM-GUI`, ...; Questie ships `QuestieDB` and `Questie`).
- `dependencies[]` of `{modId, relationType}`; relationType values follow the CurseForge docs (3 is a required dependency); confirm against https://docs.curseforge.com/rest-api/ before relying on them.
- `fileDate`, `fileLength`, `displayName`, `fileName`.

`allowModDistribution` is false for several of the most popular Forever addons (DBM, Details!, Auctionator) and true for others (Questie, Plater). The app cannot download a non-distributable mod.

## gigagrug service

A new `curseforge` service in `D:\gigagrug`, beside `github_mirror`, reading `CURSEFORGE_API_KEY`. No persistence of any CurseForge response (3.1(e)). Every route requires a signed-in `gg_session` like `/api/me`, so the key's quota is only spent for Steward users, and gigagrug rate-limits per user.

| Route | Does |
| --- | --- |
| `GET /api/addons/curseforge/discover?versionType=88568` | `POST /v1/mods/featured`, returns `popular` and `recentlyUpdated` mapped to result rows |
| `GET /api/addons/curseforge/search?versionType=88568&q=...` | `/v1/mods/search` with `gameId=1`, `gameVersionTypeId`, `searchFilter`, `sortField` popularity. Returns 503 `{"error":"search_unavailable"}` while CurseForge answers 403, so the app can say so rather than fail. |
| `GET /api/addons/curseforge/{modId}/{versionType}/latest-{channel}.json` | The manifest, in the `latest-{channel}.json` shape every other addon uses, so `AddonUpdater` keeps one code path. The `/<source>/<id>/.../latest-{channel}.json` path is a contract once shipped. |
| `GET /api/addons/curseforge/{modId}/{versionType}/icon.png` | Redirect to the mod's `logo.thumbnailUrl`, matching the mirrors' icon route |
| `POST /api/addons/curseforge/match?versionType=88568` | Body `{ "declared": [{ "folder", "modId" }], "fingerprints": [{ "folder", "fingerprint" }] }`. Confirms declared IDs through `POST /v1/mods` and resolves fingerprints through `POST /v1/fingerprints/1` (exact matches only), returning `[{ folder, modId, fileId, folders }]` for each match: `fileId` is the installed file when a fingerprint identified it, and `folders` are that mod's modules, so the app folds sibling folders into the one row. |

Result row shape: `{ id, name, summary, author, iconUrl, websiteUrl, downloadCount, latestVersion, allowDistribution }`.

Manifest selection mirrors the GitHub mirror's rule and its lesson: filter files by `gameVersionTypeId`, then `release` is the `releaseType` 1 file with the latest `fileDate` and `pre-release` the file with the latest `fileDate` among `releaseType` 2 and 3, never the API's list order. Manifest fields:

```json
{
  "version": "<displayName>",
  "zip": "<downloadUrl, absolute>",
  "sha1": "<hashes[algo=1].value>",
  "size": 91715613,
  "released": "<fileDate>",
  "folders": ["QuestieDB", "Questie"],
  "notes": ["<changelog HTML flattened to lines>"],
  "website": "https://www.curseforge.com/wow/addons/questie",
  "distributable": true
}
```

A non-distributable mod's manifest has `"distributable": false` and no `zip`/`sha1`. A mod with no file for that version type on a channel answers 404, which the app already reads as no release on that channel.

## App changes

- **Manifest model** (`AddonRelease` in `Models.cs`): optional `sha1`, `folders`, `website` and `distributable` (default true) beside the existing fields; `sha256` becomes optional. `AddonUpdater` verifies SHA-256 when present, otherwise SHA-1, and refuses a manifest with neither. Tests for each case.
- **Multi-folder installs:** `AddonUpdater.InstallAsync` removes and extracts every folder in `folders` (falling back to `FolderName` alone when absent), each through the unchanged `RemoveExistingInstall` TOC guard, and the zip-slip guard still applies. Uninstall removes the same set. A `folders` entry becomes a folded folder for the Local scan so it never shows as its own Local row.
- **Non-distributable mods:** the row's action is `Get on CurseForge` (opens `website`), or `Update on CurseForge` when a newer version exists; auto-apply and Update all skip it; Get addons shows `Available on CurseForge` with the same link instead of `Install`.
- **Session on manifest fetches:** CurseForge manifests are fetched with the `gg_session` token (the mirrors stay unauthenticated). A 401 follows the existing signed-out path.
- **Check interval:** with no caching allowed on gigagrug, CurseForge manifests are checked on startup, on Refresh and at most every 30 minutes from the background pass (wall-clock, like `_lastGuideCheck`), not every minute like the Steward manifests.
- **Installed provider addons:** persisted in `state.json` as `ManagedAddon`-shaped entries with `Source: "CurseForge"`, the mod id and the version type, keyed per install and shown only on the installs they were installed to; not feature-gated. They take part in channels, Ignore, Hide, sort, filter and Update all like configured addons.
- **Version type mapping:** `appsettings.json` gains `CurseForge:GameVersionTypes`, `{ "wow_classic_beta": 88568 }`, read from the install's effective product code. An install whose product has no mapping shows no CurseForge results.
- **Get addons dialog** per `addon-manager.md` "Get addons (phase 2)" and the mockup: with an empty query it lists the discover route's `popular` then `recentlyUpdated` (de-duplicated); with a query it calls search, debounced 300ms. While search answers `search_unavailable`, the search box stays but shows "CurseForge search is not enabled for Steward yet. Showing popular addons." and the list stays on discover. The source `ComboBox` offers `All sources` and `CurseForge` until Wago lands.
- **Changelog:** the manifest's `notes` feed the existing changelog button and dialog (`notes` support shipped in 2a9af26).
- **Existing folders matched to CurseForge:** a Local row whose folder is identified as a CurseForge mod becomes a CurseForge row (Source `CurseForge`, updates, channels, changelog) instead of Local, with nothing downloaded until the user updates. Matching is exact, never by name or title:
  1. **Declared ID.** The folder's own top-level TOC (`<Folder>.toc`, or the flavour-suffixed one the Local scan already picks) carries `## X-Curse-Project-ID: <modId>`. Only the top-level TOC counts: a nested one, like `RXPGuides\libs\HereBeDragons\HereBeDragons.toc` (`94348`, an embedded library), is ignored. Verified on the Forever install 29 Sep 2026: `BugSack.toc` declares `6273` and `RXPGuides.toc` `486246`, each beside `## X-Wago-ID` (and BugSack `## X-WoWI-ID`), so the same read serves Wago later. The ID is confirmed by `POST /v1/mods` returning that mod with a file for the install's version type, whose `modules` include the folder.
  2. **Fingerprint.** For a folder with no declared ID, gigagrug gets its fingerprint from the app and calls `POST /v1/fingerprints/1`; only an `exactMatches` entry whose file's `modules[]` holds a module with that exact fingerprint and folder name counts. `partialMatches` are not used (WowUp falls back to them; this design does not).
  3. Anything else stays Local.
  The fingerprint follows WowUp's `wowup-electron/app/curse-folder-scanner.ts`: the files are the folder's TOC files (`<Folder>.toc` and `<Folder>[-_](mainline|bcc|tbc|classic|vanilla|wrath|wotlkc|cata|mists|forever).toc`, case-insensitive; `forever` is not in WowUp's list and is required for Forever builds: QuestieDB ships only flavour TOCs including `_Forever` and `_Camelot`, and matched exactly only with `forever` added, while adding `camelot` as well broke both Questie folders, verified 29 Sep 2026) plus `Bindings.xml`, then every `.lua`/`.xml` a TOC lists (comments `#...` stripped, `..` paths refused) and every `<Include file>`/`<Script file>` an included `.xml` references (XML comments stripped), recursively; each file is hashed, the hashes are sorted numerically, concatenated as decimal strings, and that ASCII string is hashed again. The hash is WowUp's native `computeHash`, CurseForge's MurmurHash2 variant (seed 1, whitespace bytes 9, 10, 13 and 32 skipped). Implement it in `Steward.Core` with tests, and before relying on it confirm it end to end: a CurseForge-installed addon's folder fingerprint, sent to `POST /v1/fingerprints/1`, must come back as an `exactMatches` entry.
  A configured addon (`Addons` in `appsettings.json`) always keeps its configured source even when its TOC declares a CurseForge ID: BugSack and RXPGuides stay on gigagrug's GitHub mirrors. The match runs with the Local scan (startup, Refresh, after an install or uninstall), never on the background pass, and sends only mod IDs and fingerprints to gigagrug, no file contents.

## Install links from curseforge.com

Built as below; `AGENTS.md` (Managed addons, Microsoft Store) records how. The same-train hand-off is a per-train named pipe beside the existing `InstanceCoordination` events rather than `AppInstance` redirection, and the confirmation dialog's wording sits in its title with a supporting line underneath.

The Install button on a curseforge.com addon page (for example https://www.curseforge.com/wow/addons/atlasloot-forever/install/9001874) launches `curseforge://install?addonId=<modId>&fileId=<fileId>` from JavaScript; WowUp registers and parses the same scheme (`wowup-electron/package.json` test script `curseforge://install?addonId=3358&fileId=3240590`, `parseProtocol` in `curse-addon-provider.ts`). Steward registers it too:

- `Package.appxmanifest` declares a `windows.protocol` extension named `curseforge`, so the Store, flight and dev packages receive these links. With the CurseForge app also installed, Windows asks the user which app opens them. The unpackaged Debug build does not register it.
- A link starts a second process. It hands the URI to the running instance of the same train instead of only asking it to show itself (Windows App SDK `AppInstance` redirection, or the existing `InstanceCoordination` channel extended to carry the URI), then exits. A cold start handles the URI once signed in and the first pass has finished.
- The running app parses `addonId` and `fileId` (both positive integers, anything else ignored with a log line). Without a session or without the `curseforge` feature it shows a dismissible info bar ("CurseForge installs are not enabled for your account.") and does nothing else.
- gigagrug `GET /api/addons/curseforge/{modId}/files/{fileId}` (session and `curseforge` required, nothing cached) returns the mod's name, icon, website and that file in the manifest shape plus its `gameVersionTypeIds`.
- The app shows a confirmation `ContentDialog`, "Install {name} {version} on {install}?", on the selected install. A file without the install's version type reads "{name} {version} is not built for {game version}." with only Close; a non-distributable mod offers "Open on CurseForge". Confirming installs that file, adds the CurseForge row (channel `release`, or `pre-release` when the file's `releaseType` is 2 or 3) and navigates to Addons. An addon already installed there reads "{name} is already installed" with Update when the link's file is newer.

## Open questions for CurseForge

Search is required for the Get addons dialog, so question 1 gates the release of that dialog; matching existing folders, updates and installing from the discover list work without it. These go to the CurseForge API team before release, from Hoobi. A draft for Hoobi to review and send:

> Hi, thanks for approving Steward's API key. Steward is a free Windows companion app for a World of Warcraft guild toolkit; it installs and updates the addons a guild uses on WoW Forever, calling the API from our own server with our key. Three questions: (1) our key returns 403 on `/v1/mods/search` while every other endpoint works; can search be enabled for it? (2) Does 3.1(e) allow our server a short in-memory cache (a few minutes) of mod and file metadata, and does storing the mod id, file id and version of an addon the user installed count as caching? (3) We read 3.1 as allowing an addon manager inside a guild tool for one game; please tell us if you see it otherwise. Thanks, Alex

## Build order

1. gigagrug: the `curseforge` service and routes (including `match`), key wired through `hoobi-secret`, tests with recorded CurseForge responses (no live calls in tests).
2. App Core: `AddonRelease` fields, SHA-1 verification, multi-folder install and uninstall, the declared-ID read and the CurseForge folder fingerprint, tests.
3. App: matching existing folders into CurseForge rows, provider addons in `state.json`, the non-distributable row actions, the 30-minute check interval.
4. App: the Get addons dialog, discover first; search switches on once CurseForge enables it for the key.
5. Docs: `AGENTS.md` (Managed addons), `addon-manager.md` (phase 2 marked built, pointing here).

Wago Addons is a later, separate source: its API keys are self-serve at https://addons.wago.io/account/apikeys with no approval step, and its terms have not been reviewed.

## gigagrug status

Build order step 1 is deployed (gigagrug d859c69 and 6229444, 29 Sep 2026). The routes above live under `https://api.hoobi.io/guild/api/addons/curseforge/`, need a `gg_session` and the `curseforge` feature (global admins by default, grantable to anyone), and are limited to 300 requests per user per 5 minutes (429 beyond). Search answers `{"results": [...]}`, discover `{"popular", "recentlyUpdated"}` and match a bare array of `{ folder, modId, fileId, folders, name, websiteUrl }`, `name` being the mod's CurseForge display name, which the app shows instead of the folder's TOC title (Questie's plain `Questie.toc` is a stub reading "game client not supported"). CurseForge 403 is 502 `curseforge_denied`, a timeout or 5xx is 503 `curseforge_unavailable`. Search answered 200 with Steward's key on 29 Sep 2026, so the 403 recorded under Verified API behaviour no longer holds; the `search_unavailable` mapping stays for the case it returns. An exact fingerprint match needs every module fingerprint of the file in the same request (Questie's `Questie` fingerprint alone is only a partial match), so the app sends all its folders' fingerprints in one `match` call.
