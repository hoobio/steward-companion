# Addon manager design

The redesign of the Addons page from one expander per WoW install into a single table for one selected install, so the page works as a general addon manager rather than a view of five configured addons. Release channels move off the Settings page and onto the row.

Interactive mockup of every state: https://claude.ai/artifact/Vtzv3nY6bR3SEZpepFb6NX

Phase 1 is implemented and this doc stays the source of truth for it; phase 2 (Get addons, CurseForge and Wago sources) is blocked on gigagrug. It replaces the Addons page and the Release channels card described in `home-and-settings.md`; everything else in that doc (the gate, the account flyout, the rest of Settings, checking and auto-apply) is unchanged. Colours, radii and control treatment follow the Design system section of `AGENTS.md` and the styles already in `App.xaml`; the mockup's pixel values are illustrative. The sections below describe the built page; the differences from the original agreed design are called out where they matter.

## Page layout

Top to bottom:

1. Header row, one line: `Addons` at 28/600 alone on the left; on the right, in order, the selected install's status text, "Checked {relative} ago", the install picker, an edit button, a refresh icon button and an open AddOns folder icon button. Phase 2 adds `Get addons` after them.
2. `BannerList`, in its existing row between the header and the content.
3. Toolbar: a filter box, a `Segmented` of `All`, `Updates {n}` and `Hidden {n}` (the Hidden segment only while at least one row is hidden), a `Default order` button while a column sort is active, and the `Update all` split button right-aligned.
4. The table.

The summary banner and the per-install expanders go. The `Hidden addons` toggle in the header goes, replaced by the Hidden segment.

## Install picker

The picker is a button showing the selected install's label, opening a flyout of every install. A row holds more than a menu item can (label, game version, path), so it is a custom `Flyout` matching `MenuFlyout`, the same treatment as the guild switcher: `ShouldConstrainToRootBounds="False"`, a `DesktopAcrylicBackdrop`, the `MenuFlyoutPresenter*` brushes, `Placement="BottomEdgeAlignedRight"`. Each flyout row shows the label, the game version beneath it when the install has a user label, and the flavour path in mono; the selected row carries the accent selection bar. Below a separator: `Add install` (the existing folder picker, `AddedInstalls`) and `Manage installs` (navigates to the Installs card in Settings).

**Label.** The install's `SupportedProducts` display name with the `World of Warcraft: ` prefix dropped, so `World of Warcraft: Forever - Beta` reads `Forever - Beta`. A user label replaces it. The status text beside the picker reads `{client version}` plus the running dot and "Running" while `WowClient.IsRunning` holds; with a user label it leads with the game version, `Forever · 5.5.0.62422`, so a renamed install still says which game it runs. The install path is in the picker's tooltip, the flyout and the edit dialog, not on the page.

**Icon.** Each install carries its game version's icon, `Assets/Products/{product code}.png` (`ProductIcon.For`), shown beside the install picker's own label, on each flyout row, in the edit dialog's game version options and on the Settings Installs cards. An install with no game version shows the caution glyph in its place instead.

**Game version is required.** Discovery reads it from `.flavor.info`. Add install accepts a folder that has no `.flavor.info`, as long as it looks like a flavour folder (an underscore-wrapped name holding `Interface\AddOns`); a folder added by hand with no `.flavor.info` has none: its status text reads "Game version not set" in caution, and the page shows a centred prompt in place of the toolbar and table ("Choose a game version for this install", with a `Choose game version` button opening the edit dialog) until one is set. The options are the `SupportedProducts` values with the prefix dropped. Manifests carry one build per addon today, so in phase 1 the game version drives the RestedXP guide product gate (`RestedXp:ProductPrefixes`) and nothing else; phase 2 provider search is scoped by it.

**Selection** is remembered in `selected_install`, falling back to the first install.

### Edit install dialog

A `ContentDialog` titled `Edit install`, opened from the edit button beside the picker:

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
| Name | `Name`, sortable | Display name, with the folder name in mono beneath, and the out of date glyph after the name when it applies. A row notice (below) takes the line under the folder name. |
| Version | `Version` | See Version cell. |
| Channel | `Channel` | A chip button opening the channel dialog, shown only when the addon has a release on two or more channels; with exactly one release, the channel name renders as plain text with a tooltip ("Only {channel} builds are published for {addon}.") and `Change channel` is left out of the overflow menu. A dim `-` for Local rows and addons with no release on any channel. |
| Source | `Source`, sortable | `Steward`, `GitHub`, `Local`, and in phase 2 `CurseForge` or `Wago`. |
| Actions | hidden | The action for the row's state, then the overflow button. |

Source for a configured addon comes from a new `Source` field on its `Addons` entry: `Steward` for the `addon.hoobi.io` manifests, `GitHub` for the gigagrug mirrors (RestedXP, BugSack, BugGrabber).

### Version cell

| State | Version cell | Action |
| --- | --- | --- |
| Update available | `installed -> available`, old in Text mute, new in Accent hover, and "Released {relative}" beneath from `AddonRelease.Released` | `Update`, accent |
| Recorded channel differs | the same pair | `Switch to {channel}`, accent |
| Not installed | `Not installed -> available` | `Install` |
| Current | the installed version in Text dim | success tick and `Up to date` |
| Ignored | `installed -> available`, both in Text mute | `Ignored` pill |
| No releases | "No releases yet" | none |
| Local | the TOC `## Version` | none |
| Updating, failed | unchanged from today: target version, the 3px bar under the name, busy `Updating`; the failure line under the folder name and `Retry` | |

Versions are mono. The version pair is one `TextBlock` of `Run`s rather than separate blocks, so the mono and proportional parts share a baseline. The RestedXP row keeps its `Sign in to RestedXP` button in the action cell while RXPGuides is installed with no RestedXP session, per `guides-page.md`.

Row notices keep their current wording and move to the line under the folder name: the stale stored channel fallback, a newer build on a more stable channel, the `/reload` hint after an update applied while the client ran.

The changelog tooltip on the available version is phase 2: `AddonRelease` has no notes field, so it needs a `notes` string array in the manifests first.

The Updates count and `Update all` cover the Update available and channel switch states only; a missing addon does not count towards either.

### Icons

A configured addon's icon comes from its manifest, falling back on `ImageFailed` to the folder's own TOC `## IconTexture`, decoded from TGA or BLP2 by `AddonIcon` in Core; a Local row goes straight to the decoded TOC icon. Where neither resolves, the tile shows the display name's initials on the dark tile, coloured from a fixed palette by a stable hash of the folder name (`InitialsTile`).

### Out of date flag

A caution glyph after the name when the installed addon's TOC `## Interface:` holds no value equal to the client's interface number. The value can be a comma-separated list; any match counts. The client's number comes from `ClientVersion` as major × 10000 + minor × 100 + patch, so the Forever beta's `1.60.1.70009` is `16001`, the value every current addon TOC on that install carries. The TOC read is `<FolderName>.toc`, the same file the version fallback reads. The glyph is a focus stop with a `ToolTip`: "Out of date for this client. Built for interface 16000 (1.60.0). Forever - Beta runs 16001 (1.60.1). The game skips it unless Load out of date AddOns is ticked on the AddOns screen." `TocFile.ReadDirective` already reads the line. Not shown on a row that is not installed, and not shown when the TOC carries no `## Interface:` line at all, since there is nothing to compare against.

### Order and filter

The default order is the configured addons in `Addons` array order (Steward, HoobiScripts, RestedXP Guides, BugSack, BugGrabber), then every other row by name. Clicking `Name` or `Source` sorts by that column, ascending then descending then back to the default, with the arrow glyph on the active header; `Default order` in the toolbar resets it. The sort lasts for the session. A background pass never re-orders rows, as today.

The filter box matches the display name, case-insensitive. `Updates` shows rows in the Update available or channel switch state. `Hidden` shows hidden rows only; `All` and `Updates` exclude them. A filter with no match shows one row reading "No addons match".

The table is full width, its edges aligned with the toolbar above it. Each row raises `SizeChanged`, since `AdaptiveTrigger` does not fire inside an `ItemsRepeater`; the handler collapses the Channel column below 700px of row width and the Source column below 780px.

## Row actions

The overflow menu is a `MenuFlyout`, items shown only where they apply:

1. `Ignore updates` or `Resume updates`, on an installed addon with a release.
2. `Change channel`, opening the channel dialog.
3. `Hide addon` or `Show addon`.
4. `Open folder`.
5. `Reinstall` and `Copy SHA-256`, on an installed addon with a release.
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

The Release channels expander and its cards are removed. The Installs cards show the install's label as their title, with the game version beneath when a label is set.

## Get addons (phase 2)

`Get addons` in the header opens a `ContentDialog`: "Searching addons built for {game version}." above a search box and a source `ComboBox` (`All sources`, `CurseForge`, `Wago`). With an empty query it lists popular addons for that game version; otherwise "{n} results for "{query}"". Each result shows the icon, name, "by {author}", a one-line description, "{source} · {latest version}", and `Install` or an Installed tick. Installing adds the row to the table behind the dialog without closing it. No match reads "No addons for {game version} match. Check the spelling or try another source." Close button `Done`.

There is no add-by-URL field. The unauthenticated GitHub API allows 60 requests an hour per IP, which a 5-minute check across several addons exhausts; a GitHub-only addon is added to gigagrug's mirror, the way RestedXP, BugSack and BugGrabber are.

Blocked on gigagrug:

- A search endpoint over the chosen providers, scoped to a product code.
- A manifest per provider addon in the existing `latest-{channel}.json` shape, so `AddonUpdater` keeps one code path. CurseForge and Wago both need an API key issued to the app; serving them from gigagrug keeps the keys off the client and gives the caching the mirrors already have.
- `notes` in the manifests, for the changelog tooltip.

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

## Control mapping

| Element | Control | Note |
| --- | --- | --- |
| Install picker | `Button` with a chevron + custom `Flyout` | Guild switcher treatment: `ShouldConstrainToRootBounds="False"`, `DesktopAcrylicBackdrop`, `MenuFlyoutPresenter*` brushes, explicit `Placement="BottomEdgeAlignedRight"`. |
| Edit install, uninstall confirmation | `ContentDialog` | |
| Filter | `TextBox` with a search glyph | |
| All / Updates / Hidden | `toolkit:Segmented` | |
| Table | header `Grid` + `ItemsRepeater` of row `Grid`s sharing one set of `ColumnDefinitions` | Not a `ListView`: selection chrome fights the row buttons, as on the current page. |
| Sortable headers | `Button` in the subtle style | Arrow glyph on the active column. |
| Channel chip | `Button` in the chip style | |
| Changelog, out of date glyph | `ToolTip` | Both targets are focus stops. |
| Row overflow | `MenuFlyout` | Ignore and Hide flip their label rather than using `ToggleMenuFlyoutItem`. |
| Update all | Two `Button`s, accent | A main button and a narrower chevron button opening a `MenuFlyout`, in place of a `SplitButton`. |
| Channel dialog | `ContentDialog` + `RadioButtons` | `IsEnabled` per item carries the no-release state. |
| Get addons | `ContentDialog` + `AutoSuggestBox` + `ItemsRepeater` | Phase 2. Search debounced at 300ms. |

## Phases

**Phase 1, this repo only, implemented:** the header and install picker, the edit install dialog and its state, the table with every column and state above, Local rows, the out of date flag, ordering and filtering, Ignore, Hide moved to the segment, uninstall confirmation, the Update all buttons, the channel dialog, and the Release channels card removed from Settings. `AGENTS.md` and `home-and-settings.md` describe what is built.

**Phase 2, after gigagrug:** Get addons, CurseForge and Wago sources, the changelog tooltip.

## Open questions

- Which providers phase 2 searches, given the API key terms for CurseForge and Wago.
