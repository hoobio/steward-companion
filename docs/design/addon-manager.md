# Addon manager design

The redesign of the Addons page from one expander per WoW install into a single table for one selected install, so the page works as a general addon manager rather than a view of five configured addons. Release channels move off the Settings page and onto the row.

Interactive mockup of every state: https://claude.ai/artifact/Vtzv3nY6bR3SEZpepFb6NX

Phase 1 is implemented and this doc stays the source of truth for it; phase 2 (Get addons, CurseForge and Wago sources) is blocked on gigagrug. It replaces the Addons page and the Release channels card described in `home-and-settings.md`; everything else in that doc (the gate, the account flyout, the rest of Settings, checking and auto-apply) is unchanged. Colours, radii and control treatment follow the Design system section of `AGENTS.md` and the styles already in `App.xaml`; the mockup's pixel values are illustrative. The sections below describe the built page; the differences from the original agreed design are called out where they matter.

## Page layout

Top to bottom:

1. Header row, one line: `Addons` at 28/600 alone on the left; on the right, in order, the selected install's status text, "Checked {relative} ago", the install picker, a refresh icon button and an open AddOns folder icon button. Phase 2 adds `Get addons` after them.
2. `BannerList`, in its existing row between the header and the content.
3. Toolbar: a filter box, a `Segmented` of `All`, `Updates {n}` and `Hidden {n}` (the Hidden segment only while at least one row is hidden), a `Default order` button while a column sort is active, and the `Update all` split button right-aligned.
4. The table.

The summary banner and the per-install expanders go. The `Hidden addons` toggle in the header goes, replaced by the Hidden segment.

## Install picker

The picker is a button showing the selected install's label, opening a flyout of every install. A row holds more than a menu item can (label, game version, path), so it is a custom `Flyout` matching `MenuFlyout`, the same treatment as the guild switcher: `ShouldConstrainToRootBounds="False"`, a `TintedAcrylicBackdrop`, the shared `TintedFlyoutPresenterStyle`, `AreOpenCloseAnimationsEnabled="False"`, `Placement="BottomEdgeAlignedRight"`. The open animation is off because a windowed `Flyout` animates its content inside a backdrop window already shown at full size: frames captured on 28 Sep 2026 showed an empty square box for about 90ms, then the content sliding down inside it, where a native `MenuFlyout` reveals backdrop and content together; with the animation off the flyout appears whole in one frame. Each flyout row shows the label, the game version beneath it when the install has a user label, and the flavour path in mono; the selected row carries the accent selection bar, and a trailing subtle pencil button ("Edit install", accessible name "Edit {label}") closes the flyout and opens the edit dialog on the Settings page. Clicking the row itself selects the install. Below a separator: `Add install` (the existing folder picker, `AddedInstalls`) and `Manage installs` (navigates to the Installs card in Settings).

**Label.** The install's `SupportedProducts` display name with the `World of Warcraft: ` prefix dropped, so `World of Warcraft: Forever - Beta` reads `Forever - Beta`. A user label replaces it. The status text beside the picker reads `{client version}` plus the running dot and "Running" while `WowClient.IsRunning` holds; with a user label it leads with the game version, `Forever · 5.5.0.62422`, so a renamed install still says which game it runs. The install path is in the picker's tooltip, the flyout and the edit dialog, not on the page.

**Icon.** Each install carries its game version's icon, `Assets/Products/{product code}.png` (`ProductIcon.For`), shown beside the install picker's own label, on each flyout row, in the edit dialog's game version options and on the Settings Installs cards. An install with no game version shows the caution glyph in its place instead.

**Game version is required.** Discovery reads it from `.flavor.info`. Add install accepts a folder that has no `.flavor.info`, as long as it looks like a flavour folder (an underscore-wrapped name holding `Interface\AddOns`); a folder added by hand with no `.flavor.info` has none: its status text reads "Game version not set" in caution, and the page shows a centred prompt in place of the toolbar and table ("Choose a game version for this install", with a `Choose game version` button that opens the edit dialog on the Settings page) until one is set. The options are the `SupportedProducts` values with the prefix dropped. Manifests carry one build per addon today, so in phase 1 the game version drives the RestedXP guide product gate (`RestedXp:ProductPrefixes`) and nothing else; phase 2 provider search is scoped by it.

**Selection** is remembered in `selected_install`, falling back to the first install.

### Edit install dialog

A `ContentDialog` titled `Edit install`, hosted by the Settings page. The picker's pencil, the `Choose game version` prompt and each Settings Installs card's `Edit` button all call `MainViewModel.RequestEditInstall`, which records the install's flavour path, navigates to Settings and raises `EditInstallRequested`; the Settings page takes the pending request once it is loaded and shows the dialog, so there is one path that opens it:

- **Name**, a `TextBox` whose placeholder is the game version name. "Shown in the install picker. Leave empty to use the game version name." Empty clears the label.
- **Game version**, a `ComboBox`. For a discovered install it is preselected with "Detected from .flavor.info. Change it only if detection is wrong." beneath. For an added install with none it starts empty with "Required. Decides which addon builds Steward installs here.", and Save with nothing chosen shows "Choose a game version so Steward installs the right addon builds." in critical under the field and keeps the dialog open.
- **Folder**, the flavour path read only, with `Change` opening the folder picker under the same validation as Add install.

Primary `Save`, close `Cancel`.

## Addons table

### Which rows show

- Every configured addon in `appsettings.json` that `VisibleAddons()` passes, on every install with a game version, whether installed or not. Feature gating, `WowInstallViewModel.SyncAddons` reconciliation and the `AutoInstall` rules are unchanged.
- Every other folder in the install's `AddOns` holding its own TOC (`<Folder>.toc`, or a flavour-suffixed `<Folder>_*.toc` when that is all it has), as a Local row. A configured addon's `FolderName` never gets a Local row, whether or not that addon is visible to the user, so feature gating stays exact. `StewardGuides` never gets one either: the app generates it and the Guides page owns it. A folder is folded into another row instead of getting its own when its TOC's `## Dependencies` or `## RequiredDeps` names another unmanaged folder that is present, resolved through chains to the root; the parent row's folder line then reads `DBM-Core + 12 folders`. A folder depending only on a configured addon keeps its own row: `HoobiVersions` depends on `Steward` but is a separate addon. The rule was checked against the Forever beta install on 28 Sep 2026, whose `AddOns` held `HoobiScripts`, `HoobiVersions`, `RXPGuides`, `Steward` and `StewardGuides`; the unmanaged-to-unmanaged folding case (a DBM-style suite) had no real folder there and is covered by tests.
- Phase 2: addons installed through Get addons, on the installs they were installed to.

### Columns

| Column | Header | Content |
| --- | --- | --- |
| Icon | hidden | The manifest icon for a configured addon, falling back to the decoded local TOC icon and then an initials tile; an initials tile for a Local row. See Icons below. |
| Name | `Name`, sortable, resizable | Display name, with the folder name in mono beneath, and the out of date glyph after the name when it applies. A row notice (below) takes the line under the folder name. |
| Version | `Version`, sortable by last updated, resizable | See Version cell. |
| Channel | `Channel`, resizable | A chip button opening the channel dialog, shown only when the addon has a release on two or more channels; with exactly one release, the channel name renders as plain text with a tooltip ("Only {channel} builds are published for {addon}.") and `Change channel` is left out of the overflow menu. A dim `-` for Local rows and addons with no release on any channel. |
| Source | `Source`, sortable, resizable | `Steward`, `GitHub`, `Local`, and in phase 2 `CurseForge` or `Wago`. |
| Status | `Status`, sortable | `Update available` and `Switch to {channel} pending` in Accent hover, `Updating` in Text secondary, `Failed` in critical, `Not installed` in Text mute, the success tick and `Up to date`, the `Ignored` and `Hidden` pills, and a dim `-` for no releases and for Local rows (the Source column already says `Local`). |
| Actions | hidden | The action button for the row's state (`Update`, `Switch to {channel}`, `Install`, `Retry`, `Sign in to RestedXP`), then the overflow button. |

**Widths.** Name, Version, Channel and Source take pixel widths, each with a `ColumnResizeGrip` on its right edge in the header: a focus stop named "Resize {column} column" with the SizeWestEast cursor and a hairline shown on hover, focus or drag. Dragging, or Left and Right arrows in 8px steps, changes only that column; nothing to its left moves, the columns to its right shift, and Status absorbs the difference, stopping when Status reaches its minimum. The grip's automation peer exposes `RangeValue` for the same resize. Double-click resets the column to its default. User widths persist in `table_column_widths` in `state.json`, column id to pixels. A column without a stored width takes its share of the table width at first layout by the ratios Name 3, Version 2, Channel 1.2, Source 1, Status 1.5. Status fills the rest up to the actions column and has no grip; when the table narrows Status gives up space first, then the pixel columns shrink in proportion down to their minimums. Minimums are the header label plus sort arrow (Name 80, Version 64, Channel 64, Source 56, Status 56); cell text narrower than its column trims with an ellipsis and keeps the full text in a tooltip. The actions column is `Auto` in every row, and the width set aside for it is the widest realized action cell, measured when an action cell changes size rather than on every layout pass, so a table whose rows show only the overflow button reserves only that button. The header row sits on `LayerFillColorAltBrush` with the card's top corners, so it reads as the table's title bar. Header and rows share one width set applied from the page (`HomePage.ApplyColumns`) to the header and every realized row, driven by the table's own width rather than a row's, since a `Grid` whose columns exceed its slot is arranged at its desired width.

**Narrow mode.** Below 760px of table width the Channel column and its grip collapse and the channel folds into the Version cell as a second line (the same chip, or the same plain text and tooltip; nothing for the dim `-` case). In that mode the "Released {relative}" line moves into the version tooltip, so the cell stays at two lines. Below 840px the Source column collapses.

Source for a configured addon comes from a new `Source` field on its `Addons` entry: `Steward` for the `addon.hoobi.io` manifests, `GitHub` for the gigagrug mirrors (RestedXP, BugSack, BugGrabber).

### Version cell

| State | Version cell | Status | Action |
| --- | --- | --- | --- |
| Update available | `installed -> available`, old in Text mute, new in Accent hover, and "Released {relative}" beneath from `AddonRelease.Released` | `Update available` | `Update`, accent |
| Recorded channel differs | the same pair | `Switch to {channel} pending` | `Switch to {channel}`, accent |
| Not installed | `Not installed -> available` | `Not installed` | `Install` |
| Current | the installed version in Text dim | success tick and `Up to date` | none |
| Ignored | `installed -> available`, both in Text mute | `Ignored` pill | none |
| Hidden | as its state | `Hidden` pill | none |
| No releases | "No releases yet" | dim `-` | none |
| Local | the TOC `## Version` | dim `-` | none |
| Updating | the target version, the 3px bar under the name | `Updating` | none |
| Failed | the failure line under the folder name | `Failed` | `Retry` |

The cell is a focus stop with a tooltip: "Updated {relative} ({d MMM yyyy})" from the install record's `InstalledAt`, or "Installed {relative} ({date})" from the last write time of the addon's `<Folder>.toc` (or its flavour-suffixed TOC) when there is no record, which is the manual install case and every Local row. A row that is not installed has no tooltip. The time is read when the installed version is refreshed, with the file stat on a background thread, never in a getter.

When the available release carries `notes`, a small changelog icon button (focus stop, tooltip "Changelog") sits beside the version and opens a `ContentDialog` titled "{addon} {version}" listing each note as a bullet line with a hanging indent, in a scroll area capped at 320px, close button `Close`. Manifests carry an optional `notes` string array, parsed into `AddonRelease.Notes`; without it there is no icon.

Versions are mono. The version pair is one `TextBlock` of `Run`s rather than separate blocks, so the mono and proportional parts share a baseline. The RestedXP row keeps its `Sign in to RestedXP` button in the action cell while RXPGuides is installed with no RestedXP session, per `guides-page.md`.

Row notices keep their current wording and move to the line under the folder name: the stale stored channel fallback, a newer build on a more stable channel, the `/reload` hint after an update applied while the client ran.

The Updates count and `Update all` cover the Update available and channel switch states only; a missing addon does not count towards either.

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
5. `Reinstall`, on an installed addon with a release.
6. A separator, then `Uninstall` in critical, on an installed row.

Neither Ignore nor Hide is offered on an `AutoInstall` addon.

**Ignore** is per install and new. The key is `AppStateStore.Key` (flavour path plus addon id) in `ignored_addons`. An ignored row is still checked, so its version cell shows what it is missing, and it is left out of the Updates count, `Update all` and `AutoApplyAsync`. `Resume updates` removes the key.

**Hide** keeps its current semantics (`hidden_addons`, global, excluded from counts, auto-apply and Update all); only its toggle moves to the Hidden segment.

**Uninstall** opens a confirmation `ContentDialog`, "Uninstall {name}?", body "This deletes {folders} from {install}'s AddOns folder.", primary `Uninstall`; `{folders}` names the first folder and "and n more folders" for a row folded from several unmanaged folders. It removes the folder under the existing rule that a folder is only deleted when it holds that addon's own `.toc`, and drops the install record. A configured addon's row stays, with `Install`; a Local row goes.

Local rows are rescanned on startup, on Refresh, and after an install or uninstall on that install (any change to a configured row's installed version, or a Local row's own removal), not on the background pass, so a busy background pass never restructures the folded-folder rows under the user.

## Update all

Two accent `Button`s: the main part reads `Update all ({n})` and updates the selected install's pending rows, disabled at 0 and while any row is busy; a second, narrower button with a chevron opens a `MenuFlyout` holding `Update all on {install} ({n})` and `Update all installs ({total})`. Built as two buttons rather than a `SplitButton`, because the `SplitButton`'s secondary half stayed transparent after `IsEnabled` toggled; the flyout's counts sit right-aligned in `KeyboardAcceleratorTextOverride`, with the full text carried in `AutomationProperties.Name`.

## Release channel dialog

A `ContentDialog` titled `Release channel`, opened from the channel chip or `Change channel`. It replaces the Release channels expander on the Settings page, which is removed along with its cards; `AddonChannels` and `AddonChannelViewModel` back the dialog instead.

- The addon's icon, name and folder under the title.
- `RadioButtons`, one per channel in `AddonChannelStatus.Ordered` (`release`, `pre-release`), each labelled with the channel name, a `Current` tag on the stored one, and `{version} · {released relative}` beneath in mono. A channel with no release is disabled with "No releases on {channel} yet".
- A hint: "Applies to {addon} on {this install | both installs | all {n} installs}. The new version installs when you click Switch on the row."
- Primary button `Save` while the selection is unchanged and `Use {channel}` once it differs; close `Cancel`.

Saving writes `AppState.Channels` exactly as the Settings picker does today. Nothing downloads until the row's `Switch to {channel}` is clicked or auto-apply runs.

## Settings

The Release channels expander and its cards are removed. The Installs cards show the install's label as their title, with the game version beneath when a label is set, and an `Edit` button (accessible name "Edit {label}") beside `Remove` that opens the Edit install dialog for that install.

## Get addons (phase 2)

`Get addons` in the header opens a `ContentDialog`: "Searching addons built for {game version}." above a search box and a source `ComboBox` (`All sources`, `CurseForge`, `Wago`). With an empty query it lists popular addons for that game version; otherwise "{n} results for "{query}"". Each result shows the icon, name, "by {author}", a one-line description, "{source} · {latest version}", and `Install` or an Installed tick. Installing adds the row to the table behind the dialog without closing it. No match reads "No addons for {game version} match. Check the spelling or try another source." Close button `Done`.

There is no add-by-URL field. The unauthenticated GitHub API allows 60 requests an hour per IP, which a 5-minute check across several addons exhausts; a GitHub-only addon is added to gigagrug's mirror, the way RestedXP, BugSack and BugGrabber are.

Blocked on gigagrug:

- A search endpoint over the chosen providers, scoped to a product code.
- A manifest per provider addon in the existing `latest-{channel}.json` shape, so `AddonUpdater` keeps one code path. CurseForge and Wago both need an API key issued to the app; serving them from gigagrug keeps the keys off the client and gives the caching the mirrors already have.
- `notes` in the manifests: the app already renders them in the changelog dialog once a manifest carries them.

App side: installed provider addons persisted in `state.json` as `ManagedAddon`-shaped entries with their `Source`, shown only on the installs they were installed to and not feature-gated.

## State

| `state.json` key | Shape | Use |
| --- | --- | --- |
| `install_labels` | flavour path to string | User label for an install. |
| `install_products` | flavour path to product code | Game version for an added install, or an override of the detected one; written only when the chosen product differs from the one `.flavor.info` detects, so a plain confirmation of the detected value writes nothing. |
| `selected_install` | flavour path | The install the page shows. |
| `ignored_addons` | list of `AppStateStore.Key` | Per-install ignored addons. |
| `hidden_addons` | unchanged | |
| `channels` | unchanged | Written from the channel dialog. |
| `table_column_widths` | column id to pixels | User-resized widths of Name, Version, Channel and Source; a column without an entry keeps its proportional default. |

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
| Get addons | `ContentDialog` + `AutoSuggestBox` + `ItemsRepeater` | Phase 2. Search debounced at 300ms. |

## Phases

**Phase 1, this repo only, implemented:** the header and install picker, the edit install dialog and its state, the table with every column and state above, Local rows, the out of date flag, ordering and filtering, Ignore, Hide moved to the segment, uninstall confirmation, the Update all buttons, the channel dialog, and the Release channels card removed from Settings. `AGENTS.md` and `home-and-settings.md` describe what is built.

**Phase 2, after gigagrug:** Get addons, CurseForge and Wago sources, and `notes` in the manifests, which the changelog dialog already renders.

## Open questions

- Which providers phase 2 searches, given the API key terms for CurseForge and Wago.
