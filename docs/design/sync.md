# Sync page design

The WinUI 3 design for a third page: moving roster, loot history and attendance between the WoW SavedVariables on this machine and the guild API, so several officers' records combine into one view.

Rendered mockups of every state: https://claude.ai/artifact/1Vyp5Qcg9KzbbtweuNNYRf

Built: the SavedVariables reader, the generated sync-file writer, the freshness judgement in `SavedVariablesFreshness`, the `NavigationView` shell and the page itself, against gigagrug's roster, professions and character-sync routes (`character-sync.md` and `roster-sync.md` are the source of truth for them). Not built: loot and attendance sync, which have no rows and no endpoints. The endpoint table and the fake API below are the original plan and no longer describe the code.

Foundations, palette, type and surfaces are unchanged from [home-and-settings.md](home-and-settings.md).

## API origin

`https://api.hoobi.io/guild` (`Gigagrug:BaseUrl`), the same origin and the same `gg_session` credential as `/api/admin/me`. Not `guild.hoobi.io`: that is the Static Web App hosting the SPA, and its navigation fallback answers every `/api/*` path with `index.html` and a 200, so a client pointed there parses HTML as JSON instead of seeing a 401.

A 401 on any sync call means the session is gone, and it is handled exactly as `/api/admin/me` handles it: clear the persisted token and drop to signed out.

## Endpoint contract

The shapes below are the original plan for loot and attendance, which have no endpoints. The routes the page uses today are in `character-sync.md` and `roster-sync.md`.

| Need | Planned shape |
| --- | --- |
| What the server holds per dataset, to compare against local | `GET /api/guild/sync/state` returning per-dataset record count and a server cursor |
| Push one dataset | `POST /api/guild/sync/{roster\|loot\|attendance}` with the parsed records and the addon's `exportedAt` |
| Pull the merged view | `GET /api/guild/sync/export` returning the merged datasets for writing into the generated Lua file |

The page ran against an in-memory fake (`InMemoryGuildSyncApi`, behind `IGuildSyncApi`) while the endpoints were absent; the fake was removed on 27 Sep 2026 and the page reads the real routes through `GigagrugGuildSyncApi`. The `Sample` pill, the `Scenario` property and the disabled "Available once the guild API ships" buttons went with it.

How the server merges overlapping records for loot and attendance is undecided. The page reports a merge result per dataset and never opens a conflict dialog, which assumes the server resolves overlaps and answers with what it took.

Access is gated on `/api/me`'s `user.features` rather than on role, per `AGENTS.md` (Auth): an officer holds every officer feature by default, and a user with `sync`, `roster` or `professions` and no seat reaches the same page with the rows their features allow.

## Reading and writing on disk

One direction per file, from the [saved variables write-up](../../AGENTS.md#saved-variables).

**Read.** The app reads the Steward addon's SavedVariables and never writes them. Those are one account-level file plus one per character.

**Write.** The app writes one generated Lua file in the addon folder, one the client never writes to, calling a function the addon exposes rather than assigning a raw global.

**Freshness.** The client serialises SavedVariables only at logout, exit or `/reload`, rewriting the whole file from memory. Anything on disk while the client is running is from before the current session. The app judges this from the saved-variables file's mtime against the running client's process start time, plus the `exportedAt` timestamp the addon writes into the file.

**Running client detection.** Enumerate OS processes whose main module path sits under that flavour's folder. The addon sandbox has no socket or file-IO channel to signal the app, so there is nothing to listen for.

## SavedVariables schema

The addon writes `characters`, `professions`, `catalogue` and `guildRanks` (see `character-sync.md`) and does not yet write the roster, loot and attendance tables below, so this is the contract it must write for those. `LuaSavedVariables` and `StewardSavedVariables` in `Steward.Core` read it, and the account file holds `StewardDB`:

```lua
StewardDB = {
["exportedAt"] = 1758260000,
["roster"] = { { ["name"] = "Hoobi", ["realm"] = "Nightslayer", ["class"] = "WARRIOR", ["level"] = 60, ["rank"] = "Officer", ["rankIndex"] = 1, ["note"] = "", ["officerNote"] = "", ["lastOnline"] = 1758250000 }, },
["loot"] = { { ["id"] = "3f2a...", ["at"] = 1758240000, ["player"] = "Hoobi", ["itemId"] = 19019, ["item"] = "Thunderfury", ["quality"] = 5, ["source"] = "Ragnaros", ["instance"] = "Molten Core" }, },
["attendance"] = { { ["id"] = "9c1b...", ["at"] = 1758230000, ["instance"] = "Molten Core", ["present"] = { "Hoobi", "Grug" } }, },
}
```

The per-character file holds `StewardCharDB` with `["exportedAt"]`, `["character"] = { ["name"], ["realm"], ["class"] }`, and `["loot"]` and `["attendance"]` arrays of the same record shapes, carrying what that character observed. Roster is account-level only.

`exportedAt` is unix seconds, at the file level and on `at` and `lastOnline`. Loot and attendance records carry an `id` string, and where the same `id` appears in more than one file the copy from the newest `exportedAt` wins. Reading is tolerant: a missing table gives an empty list, a record missing a required field is skipped and counted in the snapshot's `Skipped`, and an absent `exportedAt` gives null.

## Checking and acting

Reading is automatic, in three places: at startup, on the existing 15-minute `DispatcherQueueTimer` alongside the role and manifest refresh, and when a watched WoW process exits. The third one is the useful one, because a client exiting is exactly when fresh SavedVariables land.

Pushing and pulling stay on a click, matching how applying an addon update stays on a click.

The one automatic write is the generated Lua file immediately after an addon update. The updater replaces the whole addon folder, so the file the app previously wrote is gone and the TOC still lists it. Rewriting it is restoring what the app already put there, and the client reads it no earlier than the next `/reload`.

## Shell change

This is the second destination the home design was waiting for, so the shell moves to `NavigationView` in `LeftCompact` mode.

- Two menu items: `Addons` (the current home page) and `Sync`. `Settings` moves to the footer item.
- The settings gear leaves the `TitleBar.RightHeader`. The account chip stays.
- The `TitleBar` back button and `IsOnSettings` go away with it, since Settings is a destination rather than a pushed page.
- Signed out, the `NavigationView` is hidden along with the rest of the shell chrome. The gate is unchanged.
- The `Sync` item carries an infotip dot when a dataset is waiting to be pushed.

## States

| State | Trigger |
| --- | --- |
| Addon missing | No Steward addon folder in any install |
| Never exported | Addon present, no SavedVariables file yet |
| Client running | A WoW process is running from that flavour's folder |
| Ready | Fresh data on disk, differing from the server |
| In sync | Every dataset matches the server |
| Syncing | A push or pull running |
| Dataset failure | The server rejected one dataset |
| Unreachable | `api.hoobi.io` not answering |

### Addon missing

Centred empty state: addon glyph at 44px, "The Steward addon is not installed", then "Sync reads roster, loot and attendance out of the Steward addon's saved variables." A link to the guild panel sits below. The `steward` addon is a configured `Addons` entry, so the app installs it from the Addons page (automatically for a `sync` holder).

### Never exported

Addon installed, no saved-variables file. "Steward has not exported anything yet", then "Log in to a character. The addon writes its data when you log out, exit, or type `/reload`." The card for that install shows all three datasets as `Nothing yet` in mute, no buttons.

### Client running

A second line under the page title reads "WoW is running, /reload to load new data" in caution (the guild and last-synced line above it trims rather than wraps) only while `StewardSync.lua` was written after the selected install's client started and no saved variable has been written since (`SavedVariablesFreshness.AwaitsReload`), so it clears on the next `/reload` or logout; Guides makes the same check against its own `Guides.lua`. While the client runs, the install picker in the title bar carries a caution dot. There is no running banner and no `Running` pill.

Pulling is unaffected and says so on the pull action: "Writes now, read in game after `/reload`."

### Ready

The primary working state. Top to bottom:

1. Page header. `Sync` at 26/600 with the refresh icon button (re-reads the local files) and `Sync now` (accent) right-aligned on the same row. One line under the title, one `TextBlock` of `Run`s: the selected guild's name, " · last synced {relative time}", and the running note above while the selected install's client runs.
2. One card for the install selected in the title bar's install picker (see `addon-manager.md`), full width and edge-aligned like the Addons table, with no install heading or expander: the picker already names the install, and its flyout carries the client version and flavour folder. The empty states, the card and the nav badge read that install alone. Character pushes, `StewardSync.lua` writes and the saved-variables watchers still cover every install.
3. The card's rows, divided by hairlines in the page ground colour, the same for every role: "Guild roster" and "Guild professions" pulls, then a "Your characters" push that expands to one row per character, collapsed by default and remembered per install for the session. For an officer this expansion is scoped to the signed-in user's own characters (matched by Discord link, not the full guild-wide push), with "No characters of yours on this install yet." shown when none match. Loot and attendance have no rows until something syncs them.
4. The card's last row, "Guild data in game": `StewardSync.lua` in mono and " · written {relative time}" as one `TextBlock` of `Run`s, the full path in its tooltip, and `Write again`. After an addon update this row is the one that reports the rewrite. It shows whenever an install exists, including the addon-missing and never-exported states, where it is the card's only row.

A dataset row is: 30px glyph, the dataset name at 13.5px with the source file in mono beneath, then the record count and `exportedAt`, then the action.

- Waiting to send: "{n} records, {m} new" with the new count in accent hover, and an accent `Send` button.
- Matching: the count in dim, and a success tick with `In sync`.
- Nothing yet: `Nothing yet` in mute, no button.
- Pull pending: "Guild data written {relative time}" with a secondary `Update in game` button.

### Syncing

The row owns the progress, matching an addon update. The count is replaced by a state line ("Sending 812 loot events"), a 3px determinate `ProgressBar` sits under the name, and the button goes to a disabled busy state. `Sync now` disables while any row is busy.

### Dataset failure

Row-level, critical text under the source file, with `Retry`: "The server rejected this payload: 3 loot events reference an unknown character. Nothing was recorded." One dataset failing does not stop the others.

The window-level `InfoBar` stays reserved for session and connectivity, as on the Addons page.

### Unreachable

Same critical `InfoBar` as the Addons page, reworded: "Could not reach api.hoobi.io. Check your connection. Steward cannot send or pull guild data until it can." Local reading carries on, so the counts and timestamps stay truthful and only the actions disable.

## Control mapping

| Element | Control | Note |
| --- | --- | --- |
| Shell navigation | `NavigationView` | `LeftCompact`. Replaces the bare `Frame` in `MainWindow.xaml`. Settings becomes the footer item. |
| Dataset card | `Border` + hairline-divided rows | One card for the selected install; no install `Expander`. |
| Dataset rows | `ItemsControl` | Not selectable. "Your characters" is an `Expander` row. |
| Row progress | `ProgressBar` | Determinate, same treatment as an addon update. |
| Generated file row | `Grid` row, last in the dataset card | File name in mono, full path in the tooltip. |
| Session and network messages | `InfoBar` | Window level, shared with the Addons page. |
| Empty states | `StackPanel` + `FontIcon` | Centred, 46ch copy width. |

## Scope

New and not only a view change:

- **A SavedVariables reader.** Parsing the addon's Lua table dump into records. This is the largest piece and belongs in `Steward.Core` with its own tests, ahead of any UI. Built: `LuaSavedVariables` and `StewardSavedVariables`.
- **A generated Lua writer.** One file, calling a function the addon exposes. Rewritten after every addon update. Built: `StewardSyncFile`.
- **Running-client detection.** Process enumeration by main module path, per flavour folder, plus an exit hook that triggers a re-read. Built: `WowClient`, and the per-install watcher in `WowInstallViewModel`.
- **Sync client methods.** On `GigagrugClient`. Built for roster, members, professions and characters; not built for loot and attendance.
- **`NavigationView` shell.** See [Shell change](#shell-change). Built.
- **The page itself.** Built: `SyncViewModel` and `SyncPage`.

Deliberately absent:

- Unattended push. Reading moves to the timer; sending does not.
- A conflict resolution UI. The server merges and answers with what it took.
- Writing to the addon's own SavedVariables, in any circumstance.
- A sync history or audit log in the app. That belongs on the guild panel.
