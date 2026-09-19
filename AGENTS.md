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

Discovery is filtered to `SupportedProducts` in `appsettings.json`, a product code to display name map, since the managed addons target World of Warcraft: Forever only. Today that map holds one entry, the beta's `wow_classic_beta`; the release build joins as a second entry once its product code is known.

## Branding

`src/Steward.App/Assets/Steward.ico` (16 through 256px, PNG-compressed frames) is the one source of the app's icon: `ApplicationIcon` stamps it on the exe, `AppWindow.SetIcon` puts it on both windows, and the wxs picks it out of the publish payload for `ARPPRODUCTICON` and the Start menu shortcut. WinUI 3 does not take the window icon from the exe on its own, so the `SetIcon` calls are load-bearing.

## Build and test gotchas

`Steward.slnx` defines no `Release|x64` solution configuration, so a solution-level `dotnet build` must not pass `-p:Platform=x64`; the CI workflow builds the solution without it and passes `-p:Platform=x64` only on the project-level `dotnet publish` of `Steward.App.csproj`.

`global.json` sets `test.runner` to `Microsoft.Testing.Platform`. On .NET SDK 10.0.203, `dotnet test <directory>` fails under that runner; the working form is `dotnet test --project <csproj>`, which is what the CI workflow uses.

`Microsoft.Win32.Registry` is already in-framework on `net10.0-windows`; adding it as an explicit `PackageReference` fails restore with `NU1510`. `WowInstalls.cs` uses `Microsoft.Win32.Registry` directly, and neither `.csproj` in this repo references the package.

CommunityToolkit.Mvvm's field-backed `[ObservableProperty]` triggers diagnostic `MVVMTK0045` under WinUI; the ViewModels here declare partial properties instead (`[ObservableProperty] public partial string? Foo { get; set; }`).

## Saved variables

Roster, loot and attendance read WoW SavedVariables files. Those files are one per addon per scope: everything under an addon's `## SavedVariables:` TOC directive lands in one shared account-level file at `WTF\Account\<ACCOUNT>\SavedVariables\<Addon>.lua` under the flavour folder, and `## SavedVariablesPerCharacter:` in a per-character file at `WTF\Account\<ACCOUNT>\<Realm>\<Character>\SavedVariables\<Addon>.lua`. Account folder names look like `54939295#1`, and the `.lua.bak` beside each file is ignored.

The reader is built, in `Steward.Core` with no UI on it yet. `LuaSavedVariables.Parse` is a recursive-descent parser for the client's own dump format: one global per `## SavedVariables:` entry, `["string"]`, `[123]` and bare identifier keys, strings with the client's escapes, numbers, booleans, `nil`, nested tables, array items, trailing commas and the `-- [n]` comments the client writes after array entries. Malformed input throws a `FormatException` naming the line and column. `StewardSavedVariables.FindFiles` locates the Steward files under a flavour path and `StewardSavedVariables.Read` maps them into a `SavedVariablesSnapshot` of roster, loot and attendance.

The Steward addon has no Lua yet, so the schema below is the contract the addon must write rather than a record of what it writes. The account file holds `StewardDB`:

```lua
StewardDB = {
["exportedAt"] = 1758260000,
["roster"] = { { ["name"] = "Hoobi", ["realm"] = "Nightslayer", ["class"] = "WARRIOR", ["level"] = 60, ["rank"] = "Officer", ["rankIndex"] = 1, ["note"] = "", ["officerNote"] = "", ["lastOnline"] = 1758250000 }, },
["loot"] = { { ["id"] = "3f2a...", ["at"] = 1758240000, ["player"] = "Hoobi", ["itemId"] = 19019, ["item"] = "Thunderfury", ["quality"] = 5, ["source"] = "Ragnaros", ["instance"] = "Molten Core" }, },
["attendance"] = { { ["id"] = "9c1b...", ["at"] = 1758230000, ["instance"] = "Molten Core", ["present"] = { "Hoobi", "Grug" } }, },
}
```

The per-character file holds `StewardCharDB` with `["exportedAt"]`, `["character"] = { ["name"], ["realm"], ["class"] }`, and `["loot"]` and `["attendance"]` arrays of the same record shapes, carrying what that character observed. Roster is account-level only and the reader takes it from `StewardDB` alone.

`exportedAt` is unix seconds, at the file level and on `at` and `lastOnline`. Loot and attendance records carry an `id` string, and where the same `id` appears in more than one file the copy from the newest `exportedAt` wins. Reading is tolerant: a missing table gives an empty list, a record missing a required field is skipped and counted in the snapshot's `Skipped`, and an absent `exportedAt` gives null.

The client only serialises saved variables at logout, exit or `/reload`, rewriting the whole file from memory, so any external write to that file while the client is running is lost on the next serialise. Data flow is therefore one direction per file: the app only reads the saved-variables file, and only writes a generated Lua file elsewhere in the addon folder, one the client never writes to, calling a function the addon exposes rather than assigning a raw global.

Still to come, in the commits after this one: the generated-file writer, the freshness judgement (the saved-variables mtime against the running client's process start time from `WowClient`, plus `exportedAt`), the rewrite of the generated file after an addon update since the updater replaces the whole addon folder, and the sync page itself. The addon side of the contract lives in `hoobio/Steward`'s `AGENTS.md`.

## UI design

`docs/design/home-and-settings.md` is the agreed design for the Addons page, the settings page and the shell, and it is implemented. Four things in it were behaviour rather than view, and the doc remains the source of truth for each: the release channel is per addon (`AppState.Channels`, with `LegacyChannel` carrying the one-time migration), refresh probes all three channels per addon so a channel with no release can be disabled, the default channel is the highest one that has a release ordered `stable`/`beta`/`unstable`, and the 15-minute timer refreshes manifests as well as the role. Applying an update follows the game client.

`docs/design/sync.md` is the agreed design for a third page, moving roster, loot and attendance between SavedVariables and the guild API. It is not implemented and it is blocked on two things outside this repo: the Steward addon publishes no manifests, and the sync endpoints do not exist. The endpoint table in that doc is provisional and marked as such. Its shell change replaces the `Frame` in `MainWindow.xaml` with a `NavigationView` in `LeftCompact` mode and moves Settings to the footer item.

## Outstanding work

The tray icon, close-to-tray and minimise-to-tray are built, on `H.NotifyIcon.WinUI`. The `TaskbarIcon` lives in `MainWindow.xaml`, takes its `IconSource` from the filesystem path `App.IconPath` because an unpackaged app cannot resolve `ms-appx:///`, and runs in `ContextMenuMode="SecondWindow"`. The `KeepInTray` setting on the Behaviour card in Settings, persisted as `keep_in_tray` in `state.json` and defaulting to true, decides whether the X button and minimise hide the window or quit. Start-with-Windows was planned, cancelled, and stays cancelled. A settings window was cancelled alongside it and has since been revived as a settings page: see `docs/design/home-and-settings.md`.

Checking runs at startup and on the 15-minute `DispatcherQueueTimer` in `MainViewModel`, which also re-checks `/api/admin/me`. A background pass skips busy rows and never re-orders `Installs` or `AddonRows`. Applying follows the game client. `WowClient.IsRunning` in Core judges whether an install's client is up, by matching any `Wow*` process whose main module sits under that install's flavour folder, and `MainViewModel.AutoApplyAsync` runs after every pass and on every tick: for an install with no client running, each eligible row's `UpdateCommand` is executed in turn, skipping busy rows and rows whose last attempt failed until a new resolution arrives. While an install's client is running its rows stay on the Update button and show a `/reload` hint after a manual update.

## Related repos

- `hoobio/HoobiScripts` (private, local clone `D:\HoobiScripts`): the quality-of-life addon, and the only entry in `appsettings.json` today. Its `AGENTS.md` carries the addon side of the integration and the release mechanics.
- `hoobio/Steward` (private, local clone `D:\Steward`): the roster, loot and attendance addon this app exists for. The repo holds an `AGENTS.md`, a TOC and its icon, and nothing else: no Lua, no manifests, and **no `Addons` entry here**. Do not assume it is wired up.
- `hoobio/addons` (private, local clone `D:\addons`): builds the channel manifests and zips this app reads, and publishes them to the Static Web App.

Both addon clones live on `D:\` like every other repo, and the game loads whatever this app installs from the release channels, so a working tree is never what the client runs.

The `/<addon-id>/` path layout is a contract. Installed clients bake the manifest URL in, so it cannot be changed once anyone is running the app.
