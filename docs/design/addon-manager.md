# Addon manager design

The redesign of the Addons page from one expander per WoW install into a single table for one selected install, so the page works as a general addon manager rather than a view of five configured addons. Release channels move off the Settings page and onto the row.

Interactive mockup of every state: https://claude.ai/artifact/Vtzv3nY6bR3SEZpepFb6NX

Agreed, not implemented. It replaces the Addons page and the Release channels card described in `home-and-settings.md`; everything else in that doc (the gate, the account flyout, the rest of Settings, checking and auto-apply) is unchanged. Colours, radii and control treatment follow the Design system section of `AGENTS.md` and the styles already in `App.xaml`; the mockup's pixel values are illustrative.

## Page layout

Top to bottom:

1. Header row, one line: `Addons` at 28/600, the install picker, an edit button, and status text in Text mute at 12px on the left; "Checked {relative} ago", a refresh icon button and an open AddOns folder icon button on the right. Phase 2 adds `Get addons` after them.
2. `BannerList`, in its existing row between the header and the content.
3. Toolbar: a filter box, a `Segmented` of `All`, `Updates {n}` and `Hidden {n}` (the Hidden segment only while at least one row is hidden), a `Default order` button while a column sort is active, and the `Update all` split button right-aligned.
4. The table.

The summary banner and the per-install expanders go. The `Hidden addons` toggle in the header goes, replaced by the Hidden segment.

## Install picker

The picker is a button showing the selected install's label, opening a flyout of every install. A row holds more than a menu item can (label, game version, path), so it is a custom `Flyout` matching `MenuFlyout`, the same treatment as the guild switcher. Each flyout row shows the label, the game version beneath it when the install has a user label, and the flavour path in mono; the selected row carries the accent selection bar. Below a separator: `Add install` (the existing folder picker, `AddedInstalls`) and `Manage installs` (navigates to the Installs card in Settings).

**Label.** The install's `SupportedProducts` display name with the `World of Warcraft: ` prefix dropped, so `World of Warcraft: Forever - Beta` reads `Forever - Beta`. A user label replaces it. The status text beside the picker reads `{client version}` plus the running dot and "Running" while `WowClient.IsRunning` holds; with a user label it leads with the game version, `Forever · 5.5.0.62422`, so a renamed install still says which game it runs. The install path is in the picker's tooltip, the flyout and the edit dialog, not on the page.

**Game version is required.** Discovery reads it from `.flavor.info`. A folder added by hand with no `.flavor.info` has none: its status text reads "Game version not set" in caution, and the page shows a centred prompt in place of the toolbar and table ("Choose a game version for this install", with a `Choose game version` button opening the edit dialog) until one is set. The options are the `SupportedProducts` values with the prefix dropped. Manifests carry one build per addon today, so in phase 1 the game version drives the RestedXP guide product gate (`RestedXp:ProductPrefixes`) and nothing else; phase 2 provider search is scoped by it.

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
- Every other folder in the install's `AddOns` holding a `.toc`, as a Local row. A folder is folded into another row instead of getting its own when it is a configured addon's `FolderName`, or when its TOC's `## Dependencies` or `## RequiredDeps` names another unmanaged folder that is present; the parent row's folder line then reads `DBM-Core + 12 folders`.
- Phase 2: addons installed through Get addons, on the installs they were installed to.

### Columns

| Column | Header | Content |
| --- | --- | --- |
| Icon | hidden | The manifest icon for a configured or provider addon; an initials tile otherwise. |
| Name | `Name`, sortable | Display name, with the folder name in mono beneath, and the out of date glyph after the name when it applies. A row notice (below) takes the line under the folder name. |
| Version | `Version` | See Version cell. |
| Channel | `Channel` | A chip button reading the addon's channel, opening the channel dialog. A dim `-` for Local rows and addons with no release on any channel. |
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

Versions are mono. The RestedXP row keeps its `Sign in to RestedXP` button in the action cell while RXPGuides is installed with no RestedXP session, per `guides-page.md`.

Row notices keep their current wording and move to the line under the folder name: the stale stored channel fallback, a newer build on a more stable channel, the `/reload` hint after an update applied while the client ran.

The changelog tooltip on the available version is phase 2: `AddonRelease` has no notes field, so it needs a `notes` string array in the manifests first.

### Out of date flag

A caution glyph after the name when the installed addon's TOC `## Interface:` holds no value equal to the client's interface number. The value can be a comma-separated list; any match counts. The client's number comes from `ClientVersion` as major × 10000 + minor × 100 + patch, so `5.5.1.63538` is `50501`. The glyph is a focus stop with a `ToolTip`: "Out of date for this client. Built for interface 50500 (5.5.0). Forever - Beta runs 50501 (5.5.1). The game skips it unless Load out of date AddOns is ticked on the AddOns screen." `TocFile.ReadDirective` already reads the line. Not shown on a row that is not installed.

### Order and filter

The default order is the configured addons in `Addons` array order (Steward, HoobiScripts, RestedXP Guides, BugSack, BugGrabber), then every other row by name. Clicking `Name` or `Source` sorts by that column, ascending then descending then back to the default, with the arrow glyph on the active header; `Default order` in the toolbar resets it. The sort lasts for the session. A background pass never re-orders rows, as today.

The filter box matches the display name, case-insensitive. `Updates` shows rows in the Update available or channel switch state. `Hidden` shows hidden rows only; `All` and `Updates` exclude them. A filter with no match shows one row reading "No addons match".

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

**Uninstall** opens a confirmation `ContentDialog`, "Uninstall {name}?", body "This deletes {folders} from {install}'s AddOns folder.", primary `Uninstall`. It removes the folder under the existing rule that a folder is only deleted when it holds that addon's own `.toc`, and drops the install record. A configured addon's row stays, with `Install`; a Local row goes.

## Update all

A `SplitButton` in the accent style. The main part reads `Update all ({n})` and updates the selected install's pending rows, disabled at 0 and while any row is busy, as today. The flyout holds `Update all on {install} ({n})` and `Update all installs ({total})`.

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
| `install_products` | flavour path to product code | Game version for an added install, or an override of the detected one. |
| `selected_install` | flavour path | The install the page shows. |
| `ignored_addons` | list of `AppStateStore.Key` | Per-install ignored addons. |
| `hidden_addons` | unchanged | |
| `channels` | unchanged | Written from the channel dialog. |

## Control mapping

| Element | Control | Note |
| --- | --- | --- |
| Install picker | `Button` with a chevron + custom `Flyout` | Guild switcher treatment: `ShouldConstrainToRootBounds="False"`, `DesktopAcrylicBackdrop`, `MenuFlyoutPresenter*` brushes, explicit `Placement="BottomEdgeAlignedLeft"`. |
| Edit install, uninstall confirmation | `ContentDialog` | |
| Filter | `TextBox` with a search glyph | |
| All / Updates / Hidden | `toolkit:Segmented` | |
| Table | header `Grid` + `ItemsRepeater` of row `Grid`s sharing one set of `ColumnDefinitions` | Not a `ListView`: selection chrome fights the row buttons, as on the current page. |
| Sortable headers | `Button` in the subtle style | Arrow glyph on the active column. |
| Channel chip | `Button` in the chip style | |
| Changelog, out of date glyph | `ToolTip` | Both targets are focus stops. |
| Row overflow | `MenuFlyout` | Ignore and Hide flip their label rather than using `ToggleMenuFlyoutItem`. |
| Update all | `SplitButton`, accent | |
| Channel dialog | `ContentDialog` + `RadioButtons` | `IsEnabled` per item carries the no-release state. |
| Get addons | `ContentDialog` + `AutoSuggestBox` + `ItemsRepeater` | Phase 2. Search debounced at 300ms. |

## Phases

**Phase 1, this repo only:** the header and install picker, the edit install dialog and its state, the table with every column and state above, Local rows, the out of date flag, ordering and filtering, Ignore, Hide moved to the segment, uninstall confirmation, the Update all split button, the channel dialog, and the Release channels card removed from Settings. `AGENTS.md` and `home-and-settings.md` are updated to describe what is built.

**Phase 2, after gigagrug:** Get addons, CurseForge and Wago sources, the changelog tooltip.

## Open questions

- Which providers phase 2 searches, given the API key terms for CurseForge and Wago.
- Whether the Local folder-folding rule holds up against real `AddOns` folders; check it against the Forever beta install before building the table on it.
