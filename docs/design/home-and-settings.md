# Home and settings design

The WinUI 3 design for the two pages the app has: a home page listing every WoW install and the managed addons in it, and a settings page. Everything is gated behind Discord sign-in.

Rendered mockups of every state: https://claude.ai/artifact/1Vyp5Qcg9KzbbtweuNNYRf

None of this is built. `MainWindow.xaml` today is a single grid with an install `ComboBox`, a channel `ComboBox` and a `ListView` of addon rows, and there is no settings page.

## Foundations

Fluent supplies the materials, controls and motion. The guild panel (`@hoobi/design`, Ayu Mirage) supplies the colour. Radius already agrees between them: 4px on controls, 8px on panels.

| Role | Hex | Maps to |
| --- | --- | --- |
| Ground | `#1f2430` | window, behind Mica |
| Surface | `#232834` | install cards, settings cards |
| Raised | `#2b3140` | summary banner, hover states |
| Chip | `#1a1f2b` | version pills, combo/segment track |
| Line | `#343b4c` | control borders only |
| Text | `#cbccc6` | `TextFillColorPrimary` |
| Text dim | `#8a9199` | `TextFillColorSecondary` |
| Text mute | `#707a8c` | `TextFillColorTertiary` |
| Accent | `#409fff` | `SystemAccentColor` |
| Accent hover | `#73d0ff` | `SystemAccentColorLight1` |
| Success | `#bae67e` | `SystemFillColorSuccess` |
| Caution | `#ffd580` | `SystemFillColorCaution` |
| Critical | `#f28779` | `SystemFillColorCritical` |

Override the accent and the three semantic fills in `App.xaml`, and set `RequestedTheme="Dark"` on the `Application`. Steward is dark only, matching the guild panel, which ships no light variant. Leave every other brush on stock Fluent so Mica, acrylic flyouts and high contrast keep working. The existing `Spacing*` doubles in `App.xaml` already carry the 4px scale; page gutters are 26px and cards are 10px apart.

Segoe UI Variable for the interface, Cascadia Mono for versions, hashes and paths. Hanken Grotesk and DankMono stay on the web side, where a font file can be served.

Surfaces are told apart by fill, not by borders:

- Mica stays on the window. Page content sits directly on it with no wrapping panel.
- Install cards and settings cards use `LayerFillColorDefault` at 8px with no stroke.
- Addon rows inside a card are divided by a 1px hairline of the ground colour.
- A stateful row or banner tints its whole surface at ~13% of the semantic colour instead of gaining a border.
- Borders remain on interactive controls: buttons, combo boxes, the segmented channel picker, text inputs.

## Channels

The channel is per addon, not per app. `hoobiscripts` and `steward` are separate addons on separate release cadences, and one picker over both is wrong. The channel applies to that addon across every install.

`state.json` carries `Channels`, a map of addon id to channel, replacing the single `Channel` string. On first load, an existing `Channel` value seeds every configured addon so nobody loses their setting.

**Availability.** A channel is offered for an addon only when `{ManifestBaseUrl}latest-{channel}.json` resolves to a release. Refresh probes all three per addon rather than only the selected one, so 3 fetches per addon at startup instead of 1. A channel with no release renders disabled in the picker with the tooltip "No releases on {channel} yet".

**Default.** With no stored choice for an addon, pick the highest channel that has a release, ordered `stable` then `beta` then `unstable`. Today that gives `hoobiscripts` beta, matching the current hardcoded default, and it gives `steward` nothing until it publishes. Once the user picks a channel it is stored and the default stops applying.

**Role.** `unstable` is absent from the picker unless `/api/admin/me` returns role exactly `global`. Absent for role and disabled for no releases are different states and read differently: the first is not shown at all, the second is shown greyed with the reason.

**No channel at all.** An addon with no releases on any channel the user can see shows "No releases yet" in place of the version pair, no picker, and no action button. `steward` is in this state today.

**Stored channel goes stale.** If the stored channel stops resolving, fall back to the default rule and say so once on the row: "No releases on beta any more. Showing stable."

## Checking

Steward checks at startup and every 15 minutes in the background, on the `DispatcherQueueTimer` in `MainViewModel` that already re-checks the role. One timer covering both jobs, split only if the two cadences ever need to differ. A manual check stays available on the refresh button in the page header.

Applying follows the game client. An install whose client is closed has its available updates applied by Steward on the pass that finds them, one row at a time, for a user with an admin role. While that install's client runs nothing is applied for it: the row keeps its Update button, and after a manual update the row carries "Type /reload in game to load the updated files" until the client stops or a later update runs. The green dot beside an install name is the running indicator.

A background pass must not disturb the page:

- A row that is busy is skipped entirely.
- Cards and rows never re-sort. A newly available update changes the row in place and raises the banner count.
- Nothing steals focus, and no dialog or toast appears.
- The header and banner carry a relative "checked {time} ago", which is what tells the user the timer is alive.

`README.md` says "It does not poll in the background or apply anything while you are not looking", and the `Outstanding work` section of `AGENTS.md` says there is no timer for addon updates. Both stop being true here, and both need correcting in the same PR.

## Window shell

Two pages, so no navigation pane.

- `ExtendsContentIntoTitleBar` with `SetTitleBar` over a 48px drag region. The app mark sits left; the account chip and the settings gear are interactive islands on the right, before the caption buttons.
- Root `Frame` holds Home and Settings. While Settings is shown the app mark is replaced by a back arrow.
- Default window 980x720, minimum 720x560.
- Signed out, the title bar carries the app mark and the caption buttons only.
- When roster, loot or attendance land, this becomes a `NavigationView` in `LeftCompact` mode with Settings on its footer item. Nothing else in this design changes.

## States

| State | Trigger |
| --- | --- |
| Gate | No token in `state.json`, or the persisted token was cleared |
| Signing in | Browser opened on the desktop sign-in URL, no code received yet |
| Sign-in failure | Browser timeout, expired session, or unreachable host |
| No installs | Discovery returned nothing |
| Member role | Session valid, role outside `global`/`admin` |
| Admin, updates available | Role `global`/`admin` and a manifest version differs from what is recorded |
| No releases | An addon has no manifest on any channel the user can see |
| Updating | `AddonRowViewModel.UpdateAsync` running |
| All current | Every managed addon matches its channel manifest |
| Running client | A process whose main module sits under the install's flavour folder |

### Gate

The signed-out window is the sign-in and nothing else. No install list, no settings gear, no account chip.

Centred stack: app mark at 56px, `Sign in to Steward` at 28/600, one line of body copy at 46ch, then the Discord button (`#5865f2`, white label, 38px tall). Below it, muted at 12px: "Opens your browser. Steward stores the session locally and never sees your password." Version and a Help link sit in the lower-left corner of the window, outside the centred stack.

This replaces the current behaviour, where `InitializeAsync` calls `SignInAsync` immediately and the user never sees a signed-out window.

### Signing in

The main window stays on the gate while the default browser handles Discord and lands on the loopback page. The heading changes to "Waiting for Discord", the button goes to its busy state with a spinner and keeps its label, and a Cancel button appears beside it.

### Sign-in failures

An `InfoBar` directly above the sign-in button, inside the centred stack. The button stays available.

- Browser timeout, caution: "Steward did not hear back from your browser. Sign in again." Raised when the loopback listener sees no code within 5 minutes or the tab was closed.
- Expired session, caution: "Your session expired. Sessions last 90 days from last use. Sign in again to carry on."
- Unreachable host, critical: "Could not reach guild.hoobi.io. Check your connection. Steward will not have current addon versions until it can." Action button retries.

### No installs

Centred empty state: folder glyph at 44px, "No World of Warcraft installs found", then "Steward looks for a flavour folder holding `.flavor.info`, such as `_retail_` or `_classic_era_`. Point it at one and it reads the patch version from there." Add install (accent) and Rescan.

The page header stays visible above it.

### Member role

Update, Update all and Install disappear rather than greying out. Versions, installs and channel pills stay readable, and a row with an update shows an `Update available` pill where the button was. The channel rows in Settings go read-only, showing the channel as text rather than a picker.

A window-level informational `InfoBar` sits above the cards: "Signed in as {name}. Applying addon updates needs an officer role on the guild panel. Ask an officer to raise yours." with an Open guild panel link.

### Admin, updates available

The primary state. Top to bottom:

1. Page header. `Addons` at 26/600, and under it "{n} World of Warcraft installs, last checked {relative time}".
2. Header actions, right-aligned on the same row: a refresh icon button and `Add install`. No channel picker, because the channel is per addon and lives in Settings.
3. Summary banner. Caution tint, download glyph, "{n} updates available" at 19/600, a second line naming the addon and versions, and `Update all` as the accent button on the right.
4. One `Expander` per install, expanded by default. Header carries the flavour name, the flavour path in mono at 11.5px, the client version as a chip, and a per-install count pill (`1 update` in caution, `Up to date` in success).
5. Addon rows inside each card, divided by hairlines.

An addon row is: 30px addon glyph, name at 13.5px with the addon id in mono beneath, then the version pair, then the channel pill, then the action.

- Update available: `0.4.9 -> 0.5.0-beta.1`, old in mute, new in accent hover, and an accent `Update` button.
- Current: the single version in dim, and a success tick with `Up to date`.
- Missing: `Not installed` in dim, and a secondary `Install` button.
- No releases: "No releases yet" in mute, no channel pill, no button.

The channel pill is read only here. The same addon appears once per install and they all share one channel, so a picker on the row would change three rows at once. The row's overflow menu carries `Change channel`, which opens Settings, alongside Open folder, Reinstall and Copy SHA-256.

Availability is string inequality against the recorded version, so a channel switch can move a version down. The button reads `Switch to {version}` rather than `Update` when the available version sorts below the installed one.

### Updating

The row owns the progress and the window does not block. The version pair is replaced by the target version and a state line ("Installing 0.5.0-beta.1, verifying download"), a 3px determinate `ProgressBar` bound to the existing `UpdateProgress` sits under the name, the percentage replaces the version text, and the button goes to a disabled busy state labelled `Updating`. `Update all` disables while any row is busy.

Row failures stay on the row, in critical text under the addon id, with a `Retry` button: "Update failed: the download did not match the manifest checksum. Nothing was written." The window-level `InfoBar` is for session and connectivity only.

### All current

The banner drops to the success tint, reads "Everything is up to date" with "{n} addons across {n} installs, last checked {relative time}", and `Update all` is replaced by `Check again`. Rows keep their success tick.

## Account flyout

Anchored to the account chip, acrylic by default. Avatar at 34px, display name at 14/600, `@handle` in mono beneath, and the role as an accent-tinted badge. Two items below a hairline: Open guild panel, and Sign out in critical.

Role comes from the last `/api/admin/me` response, which the 15-minute timer already refreshes.

## Settings page

Toolkit `SettingsCard` and `SettingsExpander` in a 1064px column with 4px between cards, matching the WinUI Gallery settings layout. Reached from the gear. Unreachable while signed out.

**Account**

- Identity card: avatar, display name, "Signed in with Discord, guild.hoobi.io", the role badge, and a danger-toned `Sign out`.
- Guild panel: opens `guild.hoobi.io`, with an open-in-new action icon.

**Release channels**

A `SettingsExpander` headed `Release channels`, described "Each addon follows its own channel", holding one child card per configured addon. Each card shows the addon glyph, its name, the addon id in mono, and a `Segmented` picker of the channels that addon has releases on.

- A channel with no release for that addon is disabled with the tooltip "No releases on {channel} yet". `steward` has all three disabled today, and its card reads "No releases yet" in place of the picker.
- `unstable` is absent for any role but `global`, rather than disabled.
- The card description carries the latest version on the selected channel, in mono, so the consequence of a switch is visible before making it: "beta, 0.5.0-beta.1".

There is no card explaining when Steward checks. The relative "checked {time} ago" in the page header and the summary banner says it.

**World of Warcraft installs**

A `SettingsExpander` headed `Installs`, described "{n} found, read from .flavor.info and .build.info", with `Rescan` and `Add install` in the header. One child card per install: flavour name, client version chip, path in mono, and `Remove`. An install added through the picker carries an `Added by you` pill.

Removing an install is new. It drops the install from the list and its records from `state.json`; it does not touch the addon folder on disk.

**About**

- Steward, with the version and "installed to `%LocalAppData%\Steward`", and a `Check for a new version` button pointing at GitHub releases.
- App data, showing `%LocalAppData%\Steward\state.json` in mono, with `Open folder`.
- Source and issues, opening `github.com/hoobio/steward-companion`.

## Control mapping

| Element | Control | Note |
| --- | --- | --- |
| Window chrome | `Window` + `MicaBackdrop` | Already set. Add `ExtendsContentIntoTitleBar` and `SetTitleBar`. |
| Page switching | `Frame` | Two pages. Back arrow swaps in for the app mark on Settings. |
| Account chip | `DropDownButton` + `PersonPicture` | Flyout gets acrylic by default. |
| Summary banner | `Border` on `LayerFillColorDefault` | Tone tint chosen by a converter on the update count. |
| Channel picker | `toolkit:Segmented` | One per addon, on the Settings page only. Items bind to that addon's available channels; `IsEnabled` per item carries the no-release state. |
| Channel pill on a row | `Border` + `TextBlock` | Read only. Editing lives in Settings. |
| Install card | `Expander` | Expanded by default. |
| Addon rows | `ItemsControl` | Not a `ListView`: rows are not selectable and selection chrome fights the row buttons. |
| Row progress | `ProgressBar` | Determinate, bound to `UpdateProgress`. |
| Session and network messages | `InfoBar` | Window level, bound to `StatusMessage`. |
| Settings rows | `toolkit:SettingsCard`, `toolkit:SettingsExpander` | Needs `CommunityToolkit.WinUI.Controls.SettingsControls`, which is not referenced today. |
| Empty state | `StackPanel` + `FontIcon` | Centred, 46ch copy width. |

## Scope

Already in the app and unchanged by this design: the default-browser sign-in and DPAPI token, the role re-check at startup and on the 15-minute timer, the `unstable` gate on role `global`, per-row update with progress and SHA-256 verification, install discovery and the folder picker.

New: the settings page and the gear that reaches it, the signed-out gate as a real state, the summary banner and `Update all`, all installs visible at once in place of the install dropdown, removing an install, and rescanning.

New and not only a view change, so worth doing first:

- **Per-addon channels.** `AppState.Channel` becomes `Channels`, a map of addon id to channel, with a one-time migration seeding every configured addon from the old value. `MainViewModel.SelectedChannel` and `OnSelectedChannelChanged` go away and `AddonRowViewModel` reads its own channel from the store.
- **Channel probing.** `AddonUpdater.GetLatestAsync` is called for all three channels per addon on refresh, not just the selected one, so the picker knows which channels have releases. A missing manifest is a normal outcome here, not an error to surface.
- **Default channel.** With nothing stored for an addon, take the highest channel that has a release, ordered `stable`, `beta`, `unstable`. Ordering is by stability, not by recency: the safest channel that actually has something wins.
- **Background checking.** The 15-minute timer refreshes manifests as well as the role, and a check runs at startup. Applying an update follows the game client. See [Checking](#checking).
- **Dark only.** `RequestedTheme` is forced to `Dark` in `App.xaml`, matching the guild panel. There is no theme setting and no light palette.

Deliberately absent:

- Start with Windows. Planned once and cancelled, and it stays cancelled. The tray icon, close to tray and minimise to tray are built on `H.NotifyIcon.WinUI`, under the `KeepInTray` setting on the Behaviour card in Settings.
- A check-interval setting. One interval, shared with the role timer, until there is a reason to split them.
- A navigation pane, until roster, loot or attendance give it a second destination.
- Light mode.
