# Steward

.NET 10 WinUI 3 desktop app, public repo, ships as an unpackaged self-contained MSI (x64 only). The desktop half of Steward, a guild toolkit for World of Warcraft: it keeps the Steward addon current and will carry guild roster, loot history and attendance between the game and gigagrug, combining several officers' records into one view.

Updating is the whole of it today. It manages a set of addons rather than one, against their published release channels, reading admin state from gigagrug.

## Managed addons

`appsettings.json` holds an `Addons` array of `{ Id, FolderName, ManifestBaseUrl }` (`ManagedAddon` in `Steward.Core`). The Steward addon does not exist yet, so the only entry shipped today is `hoobiscripts`, and Steward joins as a second entry once it publishes manifests of its own. Adding one is a config entry, nothing more: `AddonUpdater` takes the `ManagedAddon` and channel as parameters rather than hardcoding a folder name or manifest URL, and the UI iterates the configured list per WoW install.

The manifest for an addon+channel is `{ManifestBaseUrl}latest-{channel}.json`; the release zip resolves relative to that manifest URI. The manifest fetch sends `Cache-Control: no-cache` per request rather than trusting the Static Web App's own cache headers, since the SWA route's header behaviour for a nested path is unconfirmed. SHA-256 verification, the zip-slip guard, and the rule that an existing addon folder is only deleted when it holds that addon's own `.toc` are unchanged from the single-addon version.

## Auth: default-browser sign-in over a loopback callback

gigagrug accepts exactly one credential, the `gg_session` cookie (`auth_middleware`), with no bearer or API-key path, and exposes a desktop flow that hands that cookie's raw value to a local client. `SessionService` generates a verifier (base64url of 32 random bytes) and a challenge (lowercase hex SHA-256 of the ASCII verifier, matching Python's `hexdigest`), binds a listener, and opens `GET {BaseUrl}/api/auth/desktop?challenge=<challenge>&port=<port>` in the default browser. gigagrug runs the Discord OAuth exchange and redirects the browser to `http://127.0.0.1:<port>/?code=<one-time code>`. The app then POSTs `{"code","verifier"}` to `{BaseUrl}/api/auth/desktop/exchange` and receives `{"token": "<gg_session value>"}`; the code is single-use with a 120-second TTL.

The listener is a `TcpListener` on `IPAddress.Loopback` with port 0. `HttpListener` cannot bind port 0 and its HTTP.sys prefix semantics need a fixed registered prefix, and Kestrel pulls an ASP.NET framework reference into a self-contained WinUI publish, so neither is used. The whole flow times out after 5 minutes. Every accepted connection gets the same fixed 200 HTML page telling the user the tab can be closed, including the favicon request the browser makes alongside the callback; `LoopbackCallback.TryReadCode` in `Steward.Core` decides which request line carries a code.

gigagrug slides the session to 30 days from last use on every authenticated request, with no absolute ceiling, and never re-issues the token; the app's own `/api/admin/me` checks are what keep it alive. Removing a user's last seat on the guild panel deletes their sessions server-side, so a demotion reaches the app as a 401 on the next check rather than as `role: null`. The app persists the raw token itself (DPAPI-protected, `%LocalAppData%\Steward\state.json`) rather than relying on the cookie jar as the store; the browser flow is only how the token is acquired, once, at sign-in.

The API origin is `https://api.hoobi.io/guild` (`Gigagrug:BaseUrl`). `guild.hoobi.io` is the Static Web App hosting the SPA, and its navigation fallback answers every `/api/*` path with `index.html` and a 200, so a client pointed there sees HTML where it expects JSON rather than a 401.

Every privileged action re-checks `GET /api/admin/me`: at startup, immediately before an update, and on a 15-minute timer while the window is open. A 401 means the session is gone; the persisted token is cleared and the app drops to signed-out. An authenticated response with a role outside `global`/`admin` (a user who never held a seat) keeps the session but shows no update controls. This re-check keeps the UI truthful; it is not enforcement, since the zips themselves are served from a public Static Web App with no auth of their own.

## Channels

Three release channels: `stable`, `beta`, `unstable`. The selected channel is a persisted app setting, default `beta`, stored alongside the install-state records in the same `state.json`. `unstable` is offered only when `/api/admin/me` returns `role: "global"` exactly; `admin` sees stable and beta. That gate is client-side only, same caveat as the admin re-check above: nothing stops a direct fetch of the unstable manifest.

Each WoW install + addon pair records what was actually installed (version, channel, sha256), keyed by flavour path plus addon id. With no record, the addon's own `.toc` version is the fallback, which is the manual-install case. Update availability is plain string inequality between that recorded/TOC version and the channel manifest's version, not a semver comparison: versions like `0.5.0-beta.1` and `0.5.0-unstable.219da29` don't parse as `System.Version`, and switching channels has to be able to move the installed version in either direction.

## WoW install discovery

`WowInstalls` walks `.flavor.info` (product code) joined against the root `.build.info` (product to version) to find each flavour without a hardcoded product-to-folder map. The Blizzard registry key (`HKLM\...\World of Warcraft`) is not used as a discovery root by itself: on the dev machine it is hijacked by an unrelated Ascension install, so it is one candidate root among several, never the only one trusted.

## Branding

`src/Steward.App/Assets/Steward.ico` (16 through 256px, PNG-compressed frames) is the one source of the app's icon: `ApplicationIcon` stamps it on the exe, `AppWindow.SetIcon` puts it on both windows, and the wxs picks it out of the publish payload for `ARPPRODUCTICON` and the Start menu shortcut. WinUI 3 does not take the window icon from the exe on its own, so the `SetIcon` calls are load-bearing.

## Build and test gotchas

`Steward.slnx` defines no `Release|x64` solution configuration, so a solution-level `dotnet build` must not pass `-p:Platform=x64`; the CI workflow builds the solution without it and passes `-p:Platform=x64` only on the project-level `dotnet publish` of `Steward.App.csproj`.

`global.json` sets `test.runner` to `Microsoft.Testing.Platform`. On .NET SDK 10.0.203, `dotnet test <directory>` fails under that runner; the working form is `dotnet test --project <csproj>`, which is what the CI workflow uses.

`Microsoft.Win32.Registry` is already in-framework on `net10.0-windows`; adding it as an explicit `PackageReference` fails restore with `NU1510`. `WowInstalls.cs` uses `Microsoft.Win32.Registry` directly, and neither `.csproj` in this repo references the package.

CommunityToolkit.Mvvm's field-backed `[ObservableProperty]` triggers diagnostic `MVVMTK0045` under WinUI; the ViewModels here declare partial properties instead (`[ObservableProperty] public partial string? Foo { get; set; }`).

## Saved variables (planned)

Roster, loot and attendance will read WoW SavedVariables files. Those files are one per addon per scope: everything under an addon's `## SavedVariables:` TOC directive lands in one shared account-level file, `## SavedVariablesPerCharacter:` in a separate file per character. The client only serialises them at logout, exit or `/reload`, rewriting the whole file from memory, so any external write to that file while the client is running is lost on the next serialise. The intended data flow is one direction per file: the app only reads the saved-variables file, and only writes a generated Lua file elsewhere in the addon folder, one the client never writes to, calling a function the addon exposes rather than assigning a raw global. Since the sandbox gives the addon no socket or file-IO channel to signal the app, the app will judge whether a client is running by enumerating OS processes whose main module path sits under that WoW flavour's folder, and judge data freshness from the saved-variables file's mtime against that process's start time plus an `exportedAt` timestamp the addon writes. The addon updater replaces the whole addon folder on update, so the app will need to rewrite its generated sync file immediately after an update rather than rely on the extractor preserving it.

None of this is built yet. The full write-up lives in `hoobio/Steward`'s `AGENTS.md`.

## UI design

`docs/design/home-and-settings.md` is the agreed design for the Addons page, the settings page and the shell, and it is implemented. Four things in it were behaviour rather than view, and the doc remains the source of truth for each: the release channel is per addon (`AppState.Channels`, with `LegacyChannel` carrying the one-time migration), refresh probes all three channels per addon so a channel with no release can be disabled, the default channel is the highest one that has a release ordered `stable`/`beta`/`unstable`, and the 15-minute timer refreshes manifests as well as the role. Applying an update stays on the click.

`docs/design/sync.md` is the agreed design for a third page, moving roster, loot and attendance between SavedVariables and the guild API. It is not implemented and it is blocked on two things outside this repo: the Steward addon publishes no manifests, and the sync endpoints do not exist. The endpoint table in that doc is provisional and marked as such. Its shell change replaces the `Frame` in `MainWindow.xaml` with a `NavigationView` in `LeftCompact` mode and moves Settings to the footer item.

## Outstanding work

The tray icon, close-to-tray and minimise-to-tray are built, on `H.NotifyIcon.WinUI`. The `TaskbarIcon` lives in `MainWindow.xaml`, takes its `IconSource` from the filesystem path `App.IconPath` because an unpackaged app cannot resolve `ms-appx:///`, and runs in `ContextMenuMode="SecondWindow"`. The `KeepInTray` setting on the Behaviour card in Settings, persisted as `keep_in_tray` in `state.json` and defaulting to true, decides whether the X button and minimise hide the window or quit. Start-with-Windows was planned, cancelled, and stays cancelled. A settings window was cancelled alongside it and has since been revived as a settings page: see `docs/design/home-and-settings.md`.

Checking runs at startup and on the 15-minute `DispatcherQueueTimer` in `MainViewModel`, which also re-checks `/api/admin/me`. A background pass skips busy rows and never re-orders `Installs` or `AddonRows`. Applying remains user-initiated through `AddonRowViewModel.UpdateAsync`, a `RelayCommand` gated on `CanUpdate` and run from the row's own Update button; there is no unattended apply.

## Related repos

- `hoobio/HoobiScripts` (private, local clone `C:\Program Files (x86)\World of Warcraft\_classic_beta_\Interface\AddOns\HoobiScripts`): the quality-of-life addon, and the only entry in `appsettings.json` today. Its `AGENTS.md` carries the addon side of the integration and the release mechanics.
- `hoobio/Steward` (private, local clone `C:\Program Files (x86)\World of Warcraft\_classic_beta_\Interface\AddOns\Steward`): the roster, loot and attendance addon this app exists for. The repo holds an `AGENTS.md`, a TOC and its icon, and nothing else: no Lua, no manifests, and **no `Addons` entry here**. Do not assume it is wired up.
- `hoobio/addons` (private, local clone `D:\addons`): builds the channel manifests and zips this app reads, and publishes them to the Static Web App.

Both addons are cloned in place under the live WoW client rather than somewhere on `D:\`, so each working tree is what the game loads.

The `/<addon-id>/` path layout is a contract. Installed clients bake the manifest URL in, so it cannot be changed once anyone is running the app.
