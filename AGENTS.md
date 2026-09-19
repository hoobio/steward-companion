# Steward

.NET 10 WinUI 3 desktop app, public repo, ships as an unpackaged self-contained MSI (x64 only). The desktop half of Steward, a guild toolkit for World of Warcraft: it keeps the Steward addon current and will carry guild roster, loot history and attendance between the game and gigagrug, combining several officers' records into one view.

Updating is the whole of it today. It manages a set of addons rather than one, against their published release channels, reading admin state from gigagrug.

## Managed addons

`appsettings.json` holds an `Addons` array of `{ Id, FolderName, ManifestBaseUrl }` (`ManagedAddon` in `Steward.Core`). The Steward addon does not exist yet, so the only entry shipped today is `hoobiscripts`, and Steward joins as a second entry once it publishes manifests of its own. Adding one is a config entry, nothing more: `AddonUpdater` takes the `ManagedAddon` and channel as parameters rather than hardcoding a folder name or manifest URL, and the UI iterates the configured list per WoW install.

The manifest for an addon+channel is `{ManifestBaseUrl}latest-{channel}.json`; the release zip resolves relative to that manifest URI. The manifest fetch sends `Cache-Control: no-cache` per request rather than trusting the Static Web App's own cache headers, since the SWA route's header behaviour for a nested path is unconfirmed. SHA-256 verification, the zip-slip guard, and the rule that an existing addon folder is only deleted when it holds that addon's own `.toc` are unchanged from the single-addon version.

## Auth: WebView2 cookie lift, not native OAuth

gigagrug accepts exactly one credential, the `gg_session` cookie (`auth_middleware`), with no bearer or API-key path. Its `/api/auth/login` derives `redirect_uri` from the request's own `Host` header, so the registered local redirect URI cannot be borrowed by a desktop client talking to prod, and the callback that mints the session holds the client secret anyway. A native loopback OAuth flow is therefore not possible without a server change. Instead the app hosts a WebView2 against gigagrug's own login page, lets the ordinary browser flow run, and reads `gg_session` out of the WebView2 cookie jar.

The cookie's own `Max-Age` is a fixed 30 days, but gigagrug slides the session to 90 days from last use server-side and never re-issues the cookie. So the app persists the raw token itself (DPAPI-protected, `%LocalAppData%\Steward\state.json`) rather than relying on the cookie jar as the store; WebView2 is only how the token is acquired, once, at sign-in.

Every privileged action re-checks `GET /api/admin/me`: at startup, immediately before an update, and on a 15-minute timer while the window is open. A 401 means the session is gone; the persisted token is cleared and the app drops to signed-out. An authenticated response with a role outside `global`/`admin` keeps the session but shows no update controls. This re-check keeps the UI truthful; it is not enforcement, since the zips themselves are served from a public Static Web App with no auth of their own.

## Channels

Three release channels: `stable`, `beta`, `unstable`. The selected channel is a persisted app setting, default `beta`, stored alongside the install-state records in the same `state.json`. `unstable` is offered only when `/api/admin/me` returns `role: "global"` exactly; `admin` sees stable and beta. That gate is client-side only, same caveat as the admin re-check above: nothing stops a direct fetch of the unstable manifest.

Each WoW install + addon pair records what was actually installed (version, channel, sha256), keyed by flavour path plus addon id. With no record, the addon's own `.toc` version is the fallback, which is the manual-install case. Update availability is plain string inequality between that recorded/TOC version and the channel manifest's version, not a semver comparison: versions like `0.5.0-beta.1` and `0.5.0-unstable.219da29` don't parse as `System.Version`, and switching channels has to be able to move the installed version in either direction.

## WoW install discovery

`WowInstalls` walks `.flavor.info` (product code) joined against the root `.build.info` (product to version) to find each flavour without a hardcoded product-to-folder map. The Blizzard registry key (`HKLM\...\World of Warcraft`) is not used as a discovery root by itself: on the dev machine it is hijacked by an unrelated Ascension install, so it is one candidate root among several, never the only one trusted.

## WebView2 API deviations (verified live)

`CoreWebView2Environment.GetAvailableBrowserVersionString` surfaces a missing Evergreen Runtime as a bare `COMException` with `HRESULT 0x80070002` (`ERROR_FILE_NOT_FOUND`) on the WinUI 3 projection, not the classic `WebView2RuntimeNotFoundException` the WPF/WinForms wrapper throws. `SessionService` catches that HRESULT specifically and rewords it.

## Addon repos

The Steward addon is the app's reason to exist and has not been built yet. It gets its own private repo, and becomes a second `Addons` entry once it publishes manifests.

`HoobiScripts` (private, `hoobio/HoobiScripts`, cloned in place under the live client at `C:\Program Files (x86)\World of Warcraft\_classic_beta_\Interface\AddOns\HoobiScripts`) is the addon currently configured. Its `AGENTS.md` documents the addon side of the integration and the release-please channel mechanics.

`hoobio/addons` (private) builds the channel manifests and zips this app reads and publishes them to the Static Web App. The `/<addon-id>/` path layout there is a contract: installed clients bake the manifest URL in, so it cannot be changed once anyone is running the app.
