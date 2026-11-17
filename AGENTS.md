# Steward

.NET 10 Avalonia desktop app for Windows, Linux and macOS, public repo. Windows ships through the Microsoft Store (x64 only), with an unpackaged self-contained MSI and the bare MSIX attached to every GitHub release; an MSI install updates itself from those releases. Every release from 0.24.1 also attaches a portable Windows zip, a deb and an rpm for Linux x64 and arm64 and a dmg for macOS arm64 and x64, and the same builds ship as previews; Linux installs come from a deb and rpm feed with a `stable` and a `preview` channel. The desktop half of Steward, a guild toolkit for World of Warcraft: it keeps the Steward addon current and will carry guild roster, loot history and attendance between the game and steward-server, combining several officers' records into one view.

Updating is the whole of it today. It manages a set of addons rather than one, against their published release channels, reading admin state from steward-server.

## Feature docs

Feature detail is in `docs/design/`, not in this file. Before work in an area, read its doc. A new or changed feature fact goes in that doc in the same change; this file holds only the index, build, delivery and rules. The docs have lines of up to 11 KB: search them with Grep (`-o` and a bounded pattern such as `.{0,300}term.{0,300}`), never a whole-file Read. The layout and index are in `docs/README.md`.

| Before touching | Read |
|---|---|
| `Views/MainWindow.axaml`, `Styles/Resources.axaml`, `AvaloniaPlatform`, `MainWindow.Unix.cs`, window chrome, flyouts, MSIX packaging (`scripts/Build-DesktopMsix.ps1`) | `docs/design/avalonia-shell.md` |
| Bundled fonts, `Styles/Glyphs.axaml`, scrolling styles, `tests/Steward.Desktop.Tests`, `UiPaths`, `Steward.ico` | `docs/design/avalonia-text-and-input.md` |
| `ManagedAddon`, the `/api/me` `addons` list, `Features`, `parent`, `AutoInstall`, `Requires`, ignored updates | `docs/design/managed-addons.md` (`docs/design/addon-manager.md` for the page) |
| Manifests, release channels, CurseForge, TukUI, the Local scan, Get addons, `curseforge://` | `docs/design/addon-sources.md` (`docs/design/curseforge.md` for CurseForge) |
| Sign-in, `gg_session`, `/api/me`, `user.features`, access loss, `410 Gone` | `docs/design/desktop-auth.md` |
| `WowInstalls`, `WowClient`, `SupportedProducts`, the beta banner | `docs/design/wow-install-discovery.md` |
| `AppUpdater`, `AppUpdateSources`, MSI self-update | `docs/design/self-update.md` |
| `WindowsStore`, the Store jobs in `build.yaml`, the single-instance mutex, package identity | `docs/design/microsoft-store.md` |
| `scripts/Install-DevBuild.ps1`, `.githooks/pre-push`, the side-by-side dev package | `docs/design/dev-install.md` |
| `.github/workflows`, release-please, the Linux package feed, Desktop Preview | `docs/design/releases.md` |
| Diagnostic logging, working set trimming | `docs/design/diagnostics.md` |
| Driving the app (`tools/UiDriver.psm1`), `--screenshot` | `docs/design/ui-verification.md` |
| Dialogs, button and box styles, page navigation, the settings and Guides pages | `docs/design/ui-design.md` (`docs/design/home-and-settings.md`, `docs/design/guides-page.md`, `docs/design/sync.md`) |
| The roster pull, the character push, `StewardSync.lua`, `people` avatars | `docs/design/roster-and-character-sync.md` (`docs/design/roster-sync.md`, `docs/design/character-sync.md`) |
| `LuaSavedVariables`, `SavedVariablesFreshness`, `StewardDB` | `docs/design/saved-variables.md` |
| RestedXP sign-in, `StewardGuides` | `docs/design/restedxp-client.md` (`docs/design/restedxp-guides.md`) |
| `SyncBannersAsync`, `BannerList`, products | `docs/design/banners-and-products.md` |
| The addon settings page | `docs/design/addon-settings-client.md` (`docs/design/addon-settings.md`) |
| The tray icon, the 1-minute check timer | `docs/design/tray-and-update-checks.md` |

## Project layout

`src/Steward.Core` holds the domain logic with no UI. `src/Steward.Presentation` holds the view models (`ViewModels/`), the UI-neutral services (`Services/`), `AppInfo` (build flags, train, `IsPackaged`, `IsWine`, `IsLinux`, `WindowTitle`) and `StartupRegistration`, and references no UI stack, and keeps the `Steward.App.*` namespaces (its `RootNamespace`).

`Steward.Core`, `Steward.Presentation`, `Steward.Desktop` and both test projects target plain `net10.0` (Linux and macOS) beside their Windows TFM: `net10.0-windows` for Core and its tests, `net10.0-windows10.0.26100.0` (the WinRT projection) for Presentation, Desktop and the Desktop tests. Per-OS code is chosen by file name: `Directory.Build.targets` compiles `*.Windows.cs` only for the Windows TFM and `*.Unix.cs` only for the others, each pair declaring the same public type and members, so callers need no `#if`; today the pairs are `WindowsStore`, `WindowsShell`, `CurseForgeDefaultQuery`, `InstanceCoordination` (the Windows file holds the named events and the link pipe; the Unix file uses a per-user mutex and a Unix domain socket per train, since named events do not exist off Windows) and `SecretProtection` (DPAPI on Windows; AES-GCM under a per-user key held in a keyring or an owner-only file off Windows, see docs/design/desktop-auth.md). A `*.Unix.cs` stub turns its feature off and logs it rather than throwing, and `.editorconfig` turns CA1822 off for those stubs, which keep an instance API. Code in a type that exists on every TFM (the registry, a P/Invoke) is guarded with `OperatingSystem.IsWindows()` instead; CA1416 is never suppressed, and it does not flag a P/Invoke or a named `EventWaitHandle` constructor, so those are confined by hand. `src/Steward.Desktop` is the one app: the views, `App.axaml`, `Program`, the tray icon and the Avalonia services (`AppDialogs`, `AvaloniaPlatform`). Every UI-neutral service registration (configuration, logging, the `Steward` and `Addon` HTTP clients, `AppStateStore`, `StewardClient`, `SessionService`, `AddonUpdater`, `AppUpdater`, the RestedXP services, `DiscordImage`, `StewardGuildSyncApi`, `MainViewModel` and `IUiPlatform`) is `StewardServices.AddStewardServices(platform, dataFolder)` in `src/Steward.Presentation/Hosting`, which the app calls with `StewardServices.DefaultDataFolder` (`%LocalAppData%\Steward`, holding `state.json` and `logs\`); that folder, and every default path under it (`AppStateStore`, `ManifestIcon`, `SecretProtection`, the instance socket folder and the pre-host log), is `LocalData.Folder` in Core: `Steward` under `SpecialFolder.LocalApplicationData`, which off Windows is read with `SpecialFolderOption.Create`, since .NET answers an empty string for a folder that does not exist yet and the data folder was then relative to the working directory. On Linux that is `$XDG_DATA_HOME` when it is absolute, else `~/.local/share` (created when missing); on macOS .NET 10 answers `NSApplicationSupportDirectory`, `~/Library/Application Support`, the platform convention, so nothing differs there; the app adds only its window, its pages and its concrete platform type. Presentation makes its internals visible to `Steward.Desktop` and `Steward.Desktop.Tests`.

A view model reaches the UI stack only through `IUiPlatform` (`src/Steward.Presentation/Services/IUiPlatform.cs`): the owner window handle, `HasThreadAccess` and `TryEnqueue` for the UI thread, `CreateTimer` (a `UiTimer` with `Interval`, `IsRepeating`, `IsRunning`, `Tick`, `Start` and `Stop`), images from a URI, a packaged asset, encoded bytes or premultiplied BGRA pixels, whether a packaged asset exists (`HasAsset`, which `ProductIcon` checks), `DecodeBgraAsync` (straight BGRA, optionally scaled to a square, for `DiscordImage` and `InGameIcon`), the clipboard and the folder picker. `AvaloniaPlatform` implements it (see docs/design/avalonia-shell.md); `MainViewModel` passes it to every view model it builds. An image property on a view model is typed `object` and is the platform's own image type. A visibility property is a `bool`, a brush or style is a resource key string, an initials tile colour is an ARGB `uint` and a hairline is a `bool`; the views convert them through `Views/BindConvert.cs` in `Steward.Desktop` (`Brush`, `Theme`, `ColorBrush`, `TopHairline`, `Image`, `Glyph`, `Initials`), where a null brush key is a transparent brush. A glyph on a view model (`BannerViewModel.IconGlyph`, `AddonRowViewModel.NoticeGlyph`, `FolderEntry.NoticeGlyph`, the four `*SortGlyph` headers) is a neutral name from `ViewModels/Glyphs.cs` (`Info`, `CheckMark`, `Warning`, `Error`, `Lock`, `ChevronUp`, `ChevronDown`, or empty for none), which `BindConvert.Glyph` maps to the icon font. `InstanceCoordination.ListenForSignals` takes the platform and the show, quit and link callbacks rather than the window.

## Git workflow

Commits go straight to `main` in the one working tree at `D:\steward-companion` and are pushed there; this repo uses no feature branches, no worktrees and no pull requests for its own work, and a subagent edits `main` in place. Conventional-commit subjects feed release-please, which opens the release PR itself. A session or subagent that does create a branch or worktree must delete it, locally and on origin, once its work is on `main` or abandoned and before it finishes; a branch or worktree found with no live owner is merged to `main` or deleted rather than left behind.

A push to `main` runs release-please and lint and tests only. A Store pre-release flight build for testers is a manual run of the `Build & Release` workflow (`gh workflow run build.yaml --ref main`), since each flight submission deletes the one still in certification and restarts certification from zero, which takes hours; a release-please release run builds and submits on its own. A change is built, tested and, where it is visible, UI-verified locally before it is pushed.

The CI, release and package feed workflows are in `docs/design/releases.md`.

## Build and test gotchas

Tests are fast by rule. The Core suite (about 800 tests) runs in about 2 seconds, and a new test keeps it there: no `Thread.Sleep`/`Task.Delay` waits, no real network, no real WoW or `%LocalAppData%` paths (temp directories only), and no per-test setup heavier than building the object under test. A test that needs time passing uses an injected clock or timestamp, not a wait. While iterating, run the affected test class (`--filter-class` or `--filter`); the whole suite runs once before pushing. steward-server's integration tests follow the same rule, set out in its AGENTS.md.

`Steward.slnx` defines no `Release|x64` solution configuration, so a solution-level `dotnet build` must not pass `-p:Platform=x64`; the CI workflow builds the solution without it and publishes `Steward.Desktop.csproj` at project level with `-r win-x64`.

`global.json` pins the SDK to `10.0.203` with `rollForward: disable`, because `DOTNET_SDK_VERSION` in the workflow only says what `setup-dotnet` installs and the runner image ships newer SDKs that `dotnet` would otherwise pick; it also sets `test.runner` to `Microsoft.Testing.Platform`. The publish step retries up to three times, since NuGet restore on the runner can fail with a bare MSB4181 "RestoreTask returned false but did not log an error" and no cause (NuGet/Home#13460), as it did twice on 20 Sep 2026. On .NET SDK 10.0.203, `dotnet test <directory>` fails under that runner; the working form is `dotnet test --project <csproj>`, which is what the CI workflow uses.

With two TFMs, `dotnet test --project` runs one test process per TFM at once, so a test never writes a fixed name under the temp folder without isolation: `tests/Steward.Core.Tests/TempFolderFixture.cs` points `TMP`, `TEMP` and `TMPDIR` at a folder of its own per process, since `AppUpdater.DownloadMsiAsync` writes `Steward-<version>.msi` there. `-f net10.0` runs one TFM. On Linux the Core suite runs with the pinned SDK installed by `dotnet-install.sh --version 10.0.203` and passes in full (1084 tests, 16 Oct 2026), and the Desktop suite runs there under Xvfb (`DISPLAY` set, `-p:EnableWindowsTargeting=true`).

`Microsoft.Win32.Registry` is already in-framework on `net10.0-windows` and compiles on `net10.0`, where it throws off Windows, so every registry call is guarded with `OperatingSystem.IsWindows()`; adding it as an explicit `PackageReference` fails restore with `NU1510`. `WowInstalls.cs` uses `Microsoft.Win32.Registry` directly, and neither `.csproj` in this repo references the package.

CommunityToolkit.Mvvm's field-backed `[ObservableProperty]` triggers diagnostic `MVVMTK0045` in a UI project; the ViewModels here declare partial properties instead (`[ObservableProperty] public partial string? Foo { get; set; }`).

## Related repos

Read `D:\Steward\AGENTS.md` and `D:\HoobiScripts\AGENTS.md` for the addon-side context before working on anything that crosses into the game. `D:\steward-server` is the source of the guild API, the website and the sync destination. The Forever client's `WTF` folder, where every saved-variables file lands, is `C:\Program Files (x86)\World of Warcraft\_forever_\WTF` (the beta's is `_classic_beta_\WTF`); search there directly, since a recursive search from a drive root takes minutes.

- `hoobio/HoobiScripts` (private, local clone `D:\HoobiScripts`): the quality-of-life addon. Its `AGENTS.md` carries the addon side of the integration and the release mechanics.
- `hoobio/Steward` (private, local clone `D:\Steward`): the roster, loot and attendance addon this app exists for. It has an `Addons` entry here (`steward`, `https://addon.hoobi.io/steward/`). The contract between it and this app is `docs/design/roster-sync.md`.
- `hoobio/gigagrug` (private, local clone `D:\gigagrug`): the Discord bot. It serves nothing this app uses; steward-server calls its internal control API for the bot controls only.
- `steward-server` (ADO Hoobi/Hoobi, local clone `D:\steward-server`): the .NET 10 services behind `api.hoobi.io/guild`, owning every route this app calls: auth, `/api/me`, the admin and guild routes, sync, banners, and (in `steward-integrations`) the CurseForge proxy and the RestedXP GitHub mirror. New server-side work for this app goes to steward-server. Read `D:\steward-server\AGENTS.md` before changing anything that crosses into it.

Both addon repos commit and push straight to `main`, where every push publishes a pre-release, and merge the release-please PR for a release; their version history was reset to 0.0.0 on 2026-09-20, so neither has a `release` build until a `feat` lands on `main` and its release PR is merged.
- `RestedXP/RXPGuides` (public GitHub): the levelling guide addon, managed through steward-server's mirror of its GitHub releases as described in docs/design/managed-addons.md. Their paid guide API is documented in `docs/design/restedxp-guides.md`, and the page that drives it in `docs/design/guides-page.md`.
- `hoobio/addons` (private, local clone `D:\addons`): builds the channel manifests and zips this app reads, and publishes them to the Static Web App. It also mirrors this repo's own releases as `/steward-companion/latest-{channel}.json`, which is where the self-update check looks.

Both addon clones live on `D:\` like every other repo, and the game loads whatever this app installs from the release channels, so a working tree is never what the client runs.

The `/<addon-id>/` path layout is a contract. Installed clients bake the manifest URL in, so it cannot be changed once anyone is running the app.
