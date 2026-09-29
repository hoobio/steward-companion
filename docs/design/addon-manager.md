# Addon manager design

The redesign of the Addons page from one expander per WoW install into a single table for one selected install, so the page works as a general addon manager rather than a view of five configured addons. Release channels move off the Settings page and onto the row.

Interactive mockup of every state: https://claude.ai/artifact/Vtzv3nY6bR3SEZpepFb6NX

Phase 1 is implemented and this doc stays the source of truth for it; in phase 2 the Get addons dialog and the CurseForge source are implemented (`curseforge.md` is the source of truth for everything CurseForge) and Wago is not. It replaces the Addons page and the Release channels card described in `home-and-settings.md`; everything else in that doc (the gate, the account flyout, the rest of Settings, checking and auto-apply) is unchanged. Colours, radii and control treatment follow the Design system section of `AGENTS.md` and the styles already in `App.xaml`; the mockup's pixel values are illustrative. The sections below describe the built page; the differences from the original agreed design are called out where they matter.

## Page layout

Top to bottom:

1. Header row, one line: `Addons` at 28/600 on the left and `Get addons` on the right (only with the `curseforge` feature and a game version CurseForge has a version type for). The install picker sits in the title bar (see Install picker), and Open AddOns folder is in its flyout.
2. `BannerList`, in its existing row between the header and the content.
3. Toolbar: a filter box, a `Segmented` of `All`, `Updates` and `Hidden` (the Hidden segment only while at least one row is hidden), a `Default order` button while a column sort is active, then right-aligned the refresh icon button and the `Update all` split button. Each count is a pill badge after its segment label: Updates in accent, shown only while non-zero; Hidden neutral. The refresh button's tooltip reads "Checked {relative}" (`CheckedText`); the same text shows inline to its left, in caution, only when the last manifest check failed or no check has succeeded for 5 minutes (five missed 1-minute passes, `CheckStaleAfter`), and never below 900px of window width.
4. The table.

The summary banner and the per-install expanders go. The `Hidden addons` toggle in the header goes, replaced by the Hidden segment.

## Install picker

The picker is a button showing the selected install's label, opening a flyout of every install. It sits in the title bar immediately left of the account button (`TitleBar.RightHeader` in `MainWindow.xaml`), hidden while signed out and while the Settings page is shown, and its selection scopes the Addons, Sync and Guides pages alike; background work (manifest checks, auto-update, character pushes, `StewardSync.lua` and guide writes, saved-variables watchers) still covers every install. A row holds more than a menu item can (icon, label, status line, edit button), so it is a custom `Flyout` matching `MenuFlyout`, the same treatment as the guild switcher: `ShouldConstrainToRootBounds="False"`, a `TintedAcrylicBackdrop`, the shared `TintedFlyoutPresenterStyle`, `AreOpenCloseAnimationsEnabled="False"`, `Placement="BottomEdgeAlignedRight"`. The open animation is off because a windowed `Flyout` animates its content inside a backdrop window already shown at full size: frames captured on 28 Sep 2026 showed an empty square box for about 90ms, then the content sliding down inside it, where a native `MenuFlyout` reveals backdrop and content together; with the animation off the flyout appears whole in one frame. Each flyout row is exactly two lines, vertically centred against the 32px game version icon: the label, then the status line (below), one `TextBlock` of `Run`s; the full flavour path is the row's tooltip. The selected row carries the accent selection bar, and a trailing subtle pencil button ("Edit install", accessible name "Edit {label}") closes the flyout and opens the edit dialog on the Settings page. Clicking the row itself selects the install. Below a separator: `Open AddOns folder` (the selected install's `AddOns` in Explorer), `Add install` (the existing folder picker, `AddedInstalls`) and `Manage installs` (navigates to the Installs card in Settings). While the selected install's client runs, the picker button carries a small caution dot before its chevron, hidden while the flyout is open; in the flyout each row whose own client runs shows the same caution dot in its trailing column, and the selected row is marked by the 3x16 accent pill at its left edge (WinUI's selection indicator).

**Label.** The install's `SupportedProducts` display name with the `World of Warcraft: ` prefix dropped, so `World of Warcraft: Forever - Beta` reads `Forever - Beta`. A user label replaces it and is used verbatim. When two or more unlabelled installs share a default name, the first in the install list keeps it and each later one reads `{name} (2)`, `(3)` and so on (`InstallNames.Resolve` in Core, recomputed from the current list whenever it or a label changes, never persisted); labelled installs take no part in the numbering. The resulting label is the install's name everywhere: the picker button and flyout rows, the Edit install name placeholder, the Settings Installs cards, the Guides install heading, and the uninstall and install-link dialog text. The flyout row's label line gains a dim ` · running` while `WowClient.IsRunning` holds. Its status line reads `{client version}`, and on the selected row also the flavour folder name, `1.60.1.70009 · _classic_beta_`; with a user label it leads with the game version, `Forever · 5.5.0.62422`, so a renamed install still says which game it runs. The install path is in the picker's and each flyout row's tooltip and in the edit dialog, not on the page.

**Icon.** Each install carries its game version's icon, `Assets/Products/{product code}.png` (`ProductIcon.For`), shown beside the install picker's own label, on each flyout row, in the edit dialog's game version options and on the Settings Installs cards. An install with no game version shows the caution glyph in its place instead.

**Game version is required.** Discovery reads it from `.flavor.info`. Add install accepts a folder that has no `.flavor.info`, as long as it looks like a flavour folder (an underscore-wrapped name holding `Interface\AddOns`); a folder added by hand with no `.flavor.info` has none: its flyout status line reads "Game version not set" in caution, and the page shows a centred prompt in place of the toolbar and table ("Choose a game version for this install", with a `Choose game version` button that opens the edit dialog on the Settings page) until one is set. The options are the `SupportedProducts` values with the prefix dropped. Manifests carry one build per addon today, so the game version drives the RestedXP guide product gate (`RestedXp:ProductPrefixes`) and the CurseForge version type (`CurseForge:GameVersionTypes`), which scopes Get addons, matching and CurseForge manifests.

**Selection** is one app-wide `MainViewModel.SelectedInstall`, remembered in `selected_install`, falling back to the first install.

### Missing installs

An install added by hand (`AddedInstalls`) whose folder is gone stays in the list, greyed, with "Folder not found" as its flyout status line and on its Settings card, rather than dropping out silently. `MissingInstalls.Detect` in Core runs on startup and Refresh (`MainViewModel.DetectMissingInstalls`, never the 1-minute background pass) with `Directory.Exists` and a drive-root check. It records when each path was first seen missing in `missing_since` (cleared when the folder returns) and reports the paths missing for at least `MissingInstalls.RemovalDelay` (14 days) whose drive root still exists as due; those are removed through `MainViewModel.RemoveInstall(path)`, logged at Information. A path whose drive root is absent (an unplugged or offline drive) is only ever shown as missing. `WowInstalls.MissingFromPath` builds the install with `WowInstall.IsMissing` set, and a Refresh rebuilds a row whose state changed in place.

A missing install has no addon rows, no local scan, no `StewardSync.lua` or guide writes (`GuildRosterSync.WriteIfChanged` returns for it), no character push and no RestedXP card. Selecting it on the Addons page shows a centred "Folder not found" message with a `Remove` button in place of the toolbar and table.

A local info banner reads "{label}'s folder no longer exists." with a `Remove` action, once per missing episode: closing it records `missing-install|{path}|{missing_since ticks}` in `dismissed_banners`, so it returns only after the folder reappears and goes missing again. Every removal, from that banner, the Addons message, the Settings card or the automatic 14-day rule, goes through `MainViewModel.RemoveInstall`, which calls `AppStateStore.RemoveInstall` (pruning every per-path key and both side files), selects another install when the removed one was selected and drops that path's banner. The user-driven ones confirm first with a `Remove {label}?` dialog through `AppDialogs`; nothing in the folder is deleted.

### Edit install dialog

A `ContentDialog` titled `Edit install`, hosted by the Settings page. The picker's pencil, the `Choose game version` prompt and each Settings Installs card's `Edit` button all call `MainViewModel.RequestEditInstall`, which records the install's flavour path, navigates to Settings and raises `EditInstallRequested`; the Settings page takes the pending request once it is loaded and shows the dialog, so there is one path that opens it:

- **Name**, a `TextBox` whose placeholder is the install's numbered default name (the game version name when a label is set or another game version is picked). "Shown in the install picker. Leave empty to use the game version name." Empty clears the label.
- **Game version**, a `ComboBox`. For a discovered install it is preselected with "Detected from .flavor.info. Change it only if detection is wrong." beneath. For an added install with none it starts empty with "Required. Decides which addon builds Steward installs here.", and Save with nothing chosen shows "Choose a game version so Steward installs the right addon builds." in critical under the field and keeps the dialog open.
- **Folder**, the flavour path read only, with `Change` opening the folder picker under the same validation as Add install.

Primary `Save`, close `Cancel`.

## Addons table

### Which rows show

- Every configured addon in `appsettings.json` that `VisibleAddons()` passes, on every install with a game version, whether installed or not. Feature gating, `WowInstallViewModel.SyncAddons` reconciliation and the `AutoInstall` rules are unchanged.
- Every other folder in the install's `AddOns` holding its own TOC (`<Folder>.toc`, or a flavour-suffixed `<Folder>_*.toc` when that is all it has), as a Local row. A configured addon's `FolderName` never gets a Local row, whether or not that addon is visible to the user, so feature gating stays exact. `StewardGuides` never gets one either: the app generates it and the Guides page owns it. A folder is folded into another row instead of getting its own when its TOC's `## Dependencies` or `## RequiredDeps` names another unmanaged folder that is present, resolved through chains to the root; the parent row's folder line then reads `DBM-Core + 12 folders`. A folder depending only on a configured addon keeps its own row: `HoobiVersions` depends on `Steward` but is a separate addon. The rule was checked against the Forever beta install on 28 Sep 2026, whose `AddOns` held `HoobiScripts`, `HoobiVersions`, `RXPGuides`, `Steward` and `StewardGuides`; the unmanaged-to-unmanaged folding case (a DBM-style suite) had no real folder there and is covered by tests.
- CurseForge addons, on the installs they were installed to or found on: those installed through Get addons, and Local folders identified as a CurseForge mod by declared id or fingerprint (`curseforge.md`) that the user adopted (see Adopting CurseForge matches). A CurseForge addon's sibling folders fold into its row, whose folder line reads `Questie + 1 folder`; its folders never get a Local row, with or without the feature. They sort with the Local rows by name after the configured addons.

### Columns

| Column | Header | Content |
| --- | --- | --- |
| Icon | hidden | The manifest icon for a configured addon, falling back to the decoded local TOC icon and then an initials tile; an initials tile for a Local row. See Icons below. |
| Name | `Name`, sortable, resizable | Display name, with the folder name in mono beneath, and the out of date glyph after the name when it applies. A row notice (below) takes the line under the folder name. |
| Version | `Version`, sortable by last updated, resizable | See Version cell. |
| Channel | `Channel`, resizable | A chip button opening the channel dialog, shown only when the addon has a release on two or more channels; with exactly one release, the channel name renders as plain text with a tooltip ("Only {channel} builds are published for {addon}.") and `Change channel` is left out of the overflow menu. A dim `-` for Local rows and addons with no release on any channel. |
| Source | `Source`, sortable, resizable | `Steward`, `GitHub`, `CurseForge` or `Local`. |
| Status | `Status`, sortable | The row's action button when it has one (`Update` and `Switch to {channel}` accent, `Install`, `Retry`, `Sign in to RestedXP`, and `Get on CurseForge` or `Update on CurseForge` for a CurseForge mod that does not allow distribution), otherwise its status: `Updating` in Text secondary, `Not installed` in Text mute when no `Install` is offered, `Update available` or `Switch to {channel} pending` in Accent hover for a row whose button is withheld (not an admin), `Up to date` in Text secondary, the amber `Adopt` button for a Local row matched to CurseForge (see Adopting CurseForge matches), the `Ignored` and `Hidden` pills, and a dim `-` for no releases and for Local rows (the Source column already says `Local`). Button or status is left-aligned under the header label and vertically centred; buttons are 32px. A failed row shows `Retry` here and its failure line under the folder name. Sorting still uses the state behind the cell. |
| Overflow | hidden | A fixed 32px column holding only the overflow button. |

**Widths.** Name, Version, Channel and Source take pixel widths, each with a `ColumnResizeGrip` on its right edge in the header: a focus stop named "Resize {column} column" with the SizeWestEast cursor and a hairline shown on hover, focus or drag. Dragging, or Left and Right arrows in 8px steps, changes only that column; nothing to its left moves, the columns to its right shift, and Status absorbs the difference, stopping when Status reaches its minimum. The grip's automation peer exposes `RangeValue` for the same resize. Double-click resets the column to its default. User widths persist in `table_column_widths` in `state.json`, column id to pixels. A column without a stored width takes its share of the table width at first layout by the ratios Name 3, Version 2, Channel 1.2, Source 1, Status 1.5. Status fills the rest up to the overflow column and has no grip; when the table narrows Status gives up space first, then the pixel columns shrink in proportion down to their minimums. Minimums are the header label plus sort arrow (Name 80, Version 64, Channel 64, Source 56); the Status minimum is the widest action button that can appear there (`Switch to pre-release`, `Update on CurseForge`, `Sign in to RestedXP`), measured at the accent button's font and padding, so a button or status never clips. Names and folder names narrower than their column trim with an ellipsis and keep the full name in a tooltip, while versions stack (below). The overflow column is a fixed 32px in every row and the header. The header row sits on `LayerFillColorAltBrush` with the card's top corners, so it reads as the table's title bar. Header and rows share one width set applied from the page (`HomePage.ApplyColumns`) to the header and every realized row, driven by the table's own width rather than a row's, since a `Grid` whose columns exceed its slot is arranged at its desired width.

**Narrow mode.** Below 760px of table width the Channel column and its grip collapse and the channel folds into the Version cell as a second line (the same chip, or the same plain text and tooltip; nothing for the dim `-` case). In that mode the "Released {relative}" line moves into the version tooltip, so the cell stays at two lines. Below 840px the Source column collapses.

**Stacked version pair.** A version is never truncated. When the one-line pair (`installed -> available` plus the changelog icon) is wider than the Version column, the pair stacks: the installed version (or "Not installed") on line 1, `-> available` and the changelog icon on line 2, each line one `TextBlock` of `Run`s. The decision is per row, from the pair's measured natural width against the Version column width `HomePage.ApplyColumns` sets, rechecked whenever that width or the version text changes, so it holds at any table width, including a user-widened Name column. A stacked pair also moves "Released {relative}" into the version tooltip. When the pair stacks while the channel is folded, the channel leaves the cell and its name (or the single-channel sentence) becomes the last tooltip line, so the cell never exceeds two lines; `Change channel` stays in the overflow menu.

Source for a configured addon comes from a new `Source` field on its `Addons` entry: `Steward` for the `addon.hoobi.io` manifests, `GitHub` for the gigagrug mirrors (RestedXP, BugSack, BugGrabber).

### Version cell

| State | Version cell | Status cell |
| --- | --- | --- |
| Update available | `installed -> available`, old in Text mute, new in Accent hover, and "Released {relative}" beneath from `AddonRelease.Released` | `Update`, accent (`Update available` without the button) |
| Recorded channel differs | the same pair | `Switch to {channel}`, accent (`Switch to {channel} pending` without the button) |
| Not installed | `Not installed -> available` | `Install` (`Not installed` without the button) |
| Current | the installed version in Text dim | success tick and `Up to date` |
| Ignored | `installed -> available`, both in Text mute | `Ignored` pill |
| Hidden | as its state | `Hidden` pill |
| No releases | "No releases yet" | dim `-` |
| Local | the TOC `## Version` | dim `-` |
| Updating | the target version, the 3px bar under the name | `Updating` |
| Failed | the failure line under the folder name | `Retry` |

The cell is a focus stop with a tooltip: "Updated {relative} ({d MMM yyyy})" from the install record's `InstalledAt`, or "Installed {relative} ({date})" from the last write time of the addon's `<Folder>.toc` (or its flavour-suffixed TOC) when there is no record, which is the manual install case and every Local row. A row that is not installed has no tooltip. The time is read when the installed version is refreshed, with the file stat on a background thread, never in a getter.

When the available release carries `notes`, a small changelog icon button (focus stop, tooltip "Changelog") sits beside the version and opens a `ContentDialog` titled "{addon} {version}" listing each note as a bullet line with a hanging indent, in a scroll area capped at 320px, close button `Close`. Manifests carry an optional `notes` string array, parsed into `AddonRelease.Notes`; without it there is no icon.

Versions are mono. The version pair is one `TextBlock` of `Run`s rather than separate blocks, so the mono and proportional parts share a baseline. The RestedXP row keeps its `Sign in to RestedXP` button in the action cell while RXPGuides is installed with no RestedXP session, per `guides-page.md`.

Row notices keep their current wording and move to the line under the folder name: the stale stored channel fallback, a newer build on a more stable channel, the `/reload` hint after an update applied while the client ran.

The Updates count and `Update all` cover the Update available and channel switch states only; a missing addon does not count towards either, and neither does a CurseForge mod that does not allow distribution, whose update happens on CurseForge. A matched CurseForge row with no install record and no `## Version` in its TOC shows `unknown` as its installed version.

### Icons

A configured addon's icon comes from its manifest, falling back on `ImageFailed` to the folder's own TOC `## IconTexture`, decoded from TGA or BLP2 by `AddonIcon` in Core; a Local row goes straight to the decoded TOC icon. Where neither resolves, the tile shows the display name's initials on the dark tile, coloured from a fixed palette by a stable hash of the folder name (`InitialsTile`).

### Out of date flag

A caution glyph after the name when the installed addon's TOC `## Interface:` holds no value equal to the client's interface number. The value can be a comma-separated list; any match counts. The client's number comes from `ClientVersion` as major × 10000 + minor × 100 + patch, so the Forever beta's `1.60.1.70009` is `16001`, the value every current addon TOC on that install carries. The TOC read is `<FolderName>.toc`, the same file the version fallback reads. The glyph is a focus stop with a `ToolTip`: "Out of date for this client. Built for interface 16000 (1.60.0). Forever - Beta runs 16001 (1.60.1). The game skips it unless Load out of date AddOns is ticked on the AddOns screen." `TocFile.ReadDirective` already reads the line. Not shown on a row that is not installed, and not shown when the TOC carries no `## Interface:` line at all, since there is nothing to compare against.

### Order and filter

The default order is the configured addons in `Addons` array order (Steward, HoobiScripts, RestedXP Guides, BugSack, BugGrabber), then every other row by name. Clicking `Name`, `Source`, `Status` or `Version` sorts by that column, first click then second click then back to the default, with the arrow glyph on the active header; `Default order` in the toolbar resets it. `Status` sorts Update available, Switch, Failed, Not installed, Up to date, Ignored, Hidden, Local on the first click and in reverse on the second. `Version` sorts by the last updated time behind its tooltip, newest first and then oldest first, rows with no time last either way. The sort lasts for the session. A row's status and time are captured when the sort, filter or install changes and reused until then (a new row gets its own on arrival), so a background pass never re-orders rows.

The filter box matches the display name, case-insensitive. `Updates` shows rows in the Update available or channel switch state. `Hidden` shows hidden rows only; `All` and `Updates` exclude them. A filter with no match shows one row reading "No addons match".

The table is full width, its edges aligned with the toolbar above it. Column widths and the narrow-mode collapse are applied from the table body's `SizeChanged` and from `ItemsRepeater.ElementPrepared`, since `AdaptiveTrigger` does not fire inside an `ItemsRepeater`.

## Row actions

The overflow menu is a `MenuFlyout` defined once in the row template's resources and used both as the overflow button's `Flyout` and as the row `Grid`'s `ContextFlyout`, so right-click, Shift+F10 and the context key open the same menu, on Local rows too. Items show only where they apply:

1. `Ignore updates` or `Resume updates`, on an installed addon with a release.
2. `Change channel`, opening the channel dialog.
3. `Hide addon` or `Show addon`.
4. `Open folder`.
5. `Adopt from CurseForge`, on a matched Local row kept local through `Adopt none`.
6. `Reinstall`, on an installed addon with a release.
7. A separator, then `Uninstall` in critical, on an installed row.

Neither Ignore nor Hide is offered on an `AutoInstall` addon.

**Ignore** is per install and new. The key is `AppStateStore.Key` (flavour path plus addon id) in `ignored_addons`. An ignored row is still checked, so its version cell shows what it is missing, and it is left out of the Updates count, `Update all` and `AutoApplyAsync`. `Resume updates` removes the key.

**Hide** keeps its current semantics (`hidden_addons`, global, excluded from counts, auto-apply and Update all); only its toggle moves to the Hidden segment.

**Uninstall** opens a confirmation `ContentDialog`, "Uninstall {name}?", body "This deletes {folders} from {install}'s AddOns folder.", primary `Uninstall`; `{folders}` names the first folder and "and n more folders" for a row folded from several unmanaged folders. It removes every folder of the addon (the manifest's `folders` for a CurseForge addon) under the rule that a folder is only deleted when it holds its own top-level TOC, `<Folder>.toc` or `<Folder>_<flavour>.toc`, refusing before deleting anything when any folder fails it, and drops the install record. A configured addon's row stays, with `Install`; a Local or CurseForge row goes.

Local rows are rescanned on startup, on Refresh, and after an install or uninstall on that install (any change to a configured row's installed version, or a Local row's own removal), not on the background pass, so a busy background pass never restructures the folded-folder rows under the user.

## Adopting CurseForge matches

With the `curseforge` feature, the Local scan's CurseForge matches are held in memory per install and never recorded on their own. A Local row whose folder, or one of its folded folders, has a match that is not already recorded shows the match's CurseForge name and icon and an `Adopt` button in the Status column: 32px, 4px corners, no border, `SystemFillColorCaution` amber (`CautionButtonBackground`, with lighter hover and pressed brushes in `App.xaml`) and dark text (`CautionButtonForeground`). Adopting records exactly what the match identifies (`CurseForgeAddons.Adopt`), turns the row into a CurseForge row and fetches its manifest at once.

While the selected install has an adoptable, unhidden row not kept local, the `Update all` slot shows an amber `Adopt all` split button instead: the main part adopts every such row on that install in one reconcile, and its chevron opens a `MenuFlyout` with `Adopt none`. `Adopt none` stores each of those rows as kept local, `AppStateStore.Key(flavourPath, primaryFolder)` in `kept_local_addons`; a kept-local row shows its status dash instead of the button, is left out of `Adopt all`, and offers `Adopt from CurseForge` in its overflow menu, which adopts it and removes the key. With nothing to adopt the slot shows `Update all` exactly as below. A recorded CurseForge addon is never un-adopted.

## Update all

Two accent `Button`s, hidden while no install has anything to update: the main part reads `Update all` (the count is on the Updates segment's badge) and updates the selected install's pending rows, opening the flyout instead when the selected install has none, and both are disabled while any row is busy; a second, narrower button with a chevron opens a `MenuFlyout` holding `Update all on {install} ({n})` and `Update all installs ({total})`. Built as two buttons rather than a `SplitButton`, because the `SplitButton`'s secondary half stayed transparent after `IsEnabled` toggled; the flyout's counts sit right-aligned in `KeyboardAcceleratorTextOverride`, with the full text carried in `AutomationProperties.Name`.

## Release channel dialog

A `ContentDialog` titled `Release channel`, opened from the channel chip or `Change channel`. It replaces the Release channels expander on the Settings page, which is removed along with its cards; `AddonChannels` and `AddonChannelViewModel` back the dialog instead.

- The addon's icon, name and folder under the title.
- `RadioButtons`, one per channel in `AddonChannelStatus.Ordered` (`release`, `pre-release`), each labelled with the channel name, a `Current` tag on the stored one, and `{version} · {released relative}` beneath in mono. A channel with no release is disabled with "No releases on {channel} yet".
- An enabled channel with a changelog gets a list-glyph button beside its detail line ("Changelog for {version}"); it shows that release's changelog in a scrollable panel below the options, captioned "{Channel} {version} changelog", and pressing it again hides the panel.
- A hint: "Applies to {addon} on {this install | both installs | all {n} installs}. The new version installs when you click Switch on the row."
- Primary button `Save` while the selection is unchanged and `Use {channel}` once it differs; close `Cancel`.

Saving writes `AppState.Channels` exactly as the Settings picker does today. Nothing downloads until the row's `Switch to {channel}` is clicked or auto-apply runs.

## Settings

The Release channels expander and its cards are removed. The Installs cards show the install's label as their title, with the game version beneath when a label is set, and an `Edit` button (accessible name "Edit {label}") beside `Remove` that opens the Edit install dialog for that install.

## Get addons (phase 2)

`curseforge.md` is the source of truth for the CurseForge source: key handling, the gigagrug routes, the terms constraints, matching existing folders by declared ID and fingerprint, and what the verified API allows.

`Get addons` in the header opens a `ContentDialog` (`GetAddonsDialog`): "Searching addons built for {game version}." above a search box and a source `ComboBox` (`All sources`, `CurseForge`). With an empty query it lists gigagrug's discover route, "Popular for {game version}"; otherwise, debounced 300ms, "{n} results for "{query}"". While search answers `search_unavailable`, "CurseForge search is not enabled for Steward yet. Showing popular addons." shows under the search box and the list stays on discover. Each result shows the icon, name with "by {author}" as one line of `Run`s, a one-line description, "CurseForge · {latest version}", and `Install`, `Installing`, an Installed tick, or `Available on CurseForge` linking to the project page when the mod does not allow distribution. Installing adds the row to the table behind the dialog without closing it; a failure shows under the result. No match reads "No addons for {game version} match. Check the spelling or try another source." Close button `Done`.

There is no add-by-URL field. The unauthenticated GitHub API allows 60 requests an hour per IP, which a 5-minute check across several addons exhausts; a GitHub-only addon is added to gigagrug's mirror, the way RestedXP, BugSack and BugGrabber are.

CurseForge addons are persisted in `provider_addons.json` beside `state.json` (see AGENTS.md "Managed addons"), per install, as `ManagedAddon`-shaped records with `Source: "CurseForge"`; they are shown only on the installs they were installed to or found on, and only while the user holds the `curseforge` feature.

## State

| `state.json` key | Shape | Use |
| --- | --- | --- |
| `install_labels` | flavour path to string | User label for an install. |
| `install_products` | flavour path to product code | Game version for an added install, or an override of the detected one; written only when the chosen product differs from the one `.flavor.info` detects, so a plain confirmation of the detected value writes nothing. |
| `selected_install` | flavour path | The install the page shows. |
| `missing_since` | flavour path to timestamp | When an added install's folder was first seen missing; cleared when it returns. |
| `ignored_addons` | list of `AppStateStore.Key` | Per-install ignored addons. |
| `kept_local_addons` | list of `AppStateStore.Key` (flavour path plus primary folder) | CurseForge matches the user chose to keep as Local rows through `Adopt none`. |
| `hidden_addons` | unchanged | |
| `channels` | unchanged | Written from the channel dialog. |
| `table_column_widths` | column id to pixels | User-resized widths of Name, Version, Channel and Source; a column without an entry keeps its proportional default. |
| `provider_addons` | flavour path to a list of `ProviderAddonRecord` | CurseForge addons installed through Get addons or adopted from a matched Local folder on that install. Held in memory on `AppState` but saved to `provider_addons.json`, not `state.json`. |

## Control mapping

| Element | Control | Note |
| --- | --- | --- |
| Install picker | `Button` with a chevron + custom `Flyout` | Guild switcher treatment: `ShouldConstrainToRootBounds="False"`, `TintedAcrylicBackdrop`, `TintedFlyoutPresenterStyle`, `AreOpenCloseAnimationsEnabled="False"`, explicit `Placement="BottomEdgeAlignedRight"`. A trailing pencil button per row. |
| Edit install, uninstall confirmation, changelog | `ContentDialog` | Edit install is hosted by the Settings page. The changelog is a XAML dialog (`ChangelogDialog`); the code-built uninstall confirmation sets `DefaultContentDialogStyle`, so every dialog shares one template. |
| Filter | `TextBox` with a search glyph | |
| All / Updates / Hidden | `toolkit:Segmented` | |
| Table | header `Grid` + `ItemsRepeater` of row `Grid`s whose `ColumnDefinitions` the page sets from one width set | Not a `ListView`: selection chrome fights the row buttons, as on the current page. |
| Sortable headers | `Button` in the subtle style | Arrow glyph on the active column. |
| Column resize | `ColumnResizeGrip` (a `ContentControl` with `ProtectedCursor`) | Focus stop, arrow keys, `RangeValue` automation, double-click reset. |
| Channel chip | `Button` in the chip style | |
| Changelog button, version cell, out of date glyph | `ToolTip` | All three are focus stops. |
| Row overflow | `MenuFlyout` in the row's resources | Button `Flyout` and row `ContextFlyout` share it. Ignore and Hide flip their label rather than using `ToggleMenuFlyoutItem`. |
| Update all | Two `Button`s, accent | A main button and a narrower chevron button opening a `MenuFlyout`, in place of a `SplitButton`. |
| Channel dialog | `ContentDialog` + `RadioButtons` | `IsEnabled` per item carries the no-release state. |
| Get addons | `ContentDialog` + `AutoSuggestBox` + `ItemsRepeater` | XAML dialog (`GetAddonsDialog`). Search debounced at 300ms. |

## Phases

**Phase 1, this repo only, implemented:** the header and install picker, the edit install dialog and its state, the table with every column and state above, Local rows, the out of date flag, ordering and filtering, Ignore, Hide moved to the segment, uninstall confirmation, the Update all buttons, the channel dialog, and the Release channels card removed from Settings. `AGENTS.md` and `home-and-settings.md` describe what is built.

**Phase 2, implemented for CurseForge:** Get addons, the CurseForge source with folder matching, and `notes` in the CurseForge manifests, which the changelog dialog renders. Wago is not built.

## Open questions

- Whether and when Wago follows CurseForge, given its API key terms.
