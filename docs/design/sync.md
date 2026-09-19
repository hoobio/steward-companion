# Sync page design

The WinUI 3 design for a third page: moving roster, loot history and attendance between the WoW SavedVariables on this machine and the guild API, so several officers' records combine into one view.

Rendered mockups of every state: https://claude.ai/artifact/1Vyp5Qcg9KzbbtweuNNYRf

None of this is built, and neither is the half it depends on. The Steward addon publishes no manifests and has no `Addons` entry, so it cannot be installed by the app today, and the sync endpoints on the guild API do not exist yet. The page's honest default state is the one where the addon is missing.

Foundations, palette, type and surfaces are unchanged from [home-and-settings.md](home-and-settings.md).

## API origin

`https://api.hoobi.io/guild` (`Gigagrug:BaseUrl`), the same origin and the same `gg_session` credential as `/api/admin/me`. Not `guild.hoobi.io`: that is the Static Web App hosting the SPA, and its navigation fallback answers every `/api/*` path with `index.html` and a 200, so a client pointed there parses HTML as JSON instead of seeing a 401.

A 401 on any sync call means the session is gone, and it is handled exactly as `/api/admin/me` handles it: clear the persisted token and drop to signed out.

## Endpoint contract

Unconfirmed. The shapes below are what the page needs, not an agreed API. Get the real contract from gigagrug before writing any `GigagrugClient` method.

| Need | Provisional shape |
| --- | --- |
| What the server holds per dataset, to compare against local | `GET /api/guild/sync/state` returning per-dataset record count and a server cursor |
| Push one dataset | `POST /api/guild/sync/{roster\|loot\|attendance}` with the parsed records and the addon's `exportedAt` |
| Pull the merged view | `GET /api/guild/sync/export` returning the merged datasets for writing into the generated Lua file |

Two things to settle with the API owner before implementing:

- **Whether a member role may push.** This design gates pushing behind `global`/`admin`, matching every other privileged action in the app, and leaves pulling open to any signed-in user. If members are meant to contribute their own attendance, that inverts and the role gate moves off push entirely.
- **How the server merges overlapping records.** The page reports a merge result per dataset and never opens a conflict dialog, which assumes the server resolves overlaps and answers with what it took.

## Reading and writing on disk

One direction per file, from the [saved variables write-up](../../AGENTS.md#saved-variables).

**Read.** The app reads the Steward addon's SavedVariables and never writes them. Those are one account-level file plus one per character.

**Write.** The app writes one generated Lua file in the addon folder, one the client never writes to, calling a function the addon exposes rather than assigning a raw global.

**Freshness.** The client serialises SavedVariables only at logout, exit or `/reload`, rewriting the whole file from memory. Anything on disk while the client is running is from before the current session. The app judges this from the saved-variables file's mtime against the running client's process start time, plus the `exportedAt` timestamp the addon writes into the file.

**Running client detection.** Enumerate OS processes whose main module path sits under that flavour's folder. The addon sandbox has no socket or file-IO channel to signal the app, so there is nothing to listen for.

## SavedVariables schema

The addon has no Lua yet, so this is the contract it must write. `LuaSavedVariables` and `StewardSavedVariables` in `Steward.Core` read it, and the account file holds `StewardDB`:

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
| Member role | Role outside `global`/`admin` |
| Unreachable | `api.hoobi.io` not answering |

### Addon missing

The default state today, and the only one a user can reach right now.

Centred empty state: addon glyph at 44px, "The Steward addon is not installed", then "Sync reads roster, loot and attendance out of the Steward addon's saved variables. The addon has no releases yet, so there is nothing to install from here." No action button, because there is no manifest to install from. A link to the guild panel sits below.

Once the addon publishes, this state keeps its shape and gains an `Install` button pointing at the Addons page.

### Never exported

Addon installed, no saved-variables file. "Steward has not exported anything yet", then "Log in to a character. The addon writes its data when you log out, exit, or type `/reload`." The card for that install shows all three datasets as `Nothing yet` in mute, no buttons.

### Client running

The banner that matters most, caution tint: "World of Warcraft is running", second line "This data was exported {relative time}, before your current session. Log out or `/reload` in game to export again." `Sync now` stays available and reads `Sync what is on disk`.

Per install, the card header carries a caution `Running` pill beside the client version chip. A row whose data predates the running session shows its `exportedAt` in caution rather than dim.

Pulling is unaffected and says so on the pull action: "Writes now, read in game after `/reload`."

### Ready

The primary working state. Top to bottom:

1. Page header. `Sync` at 26/600, and under it "Last synced {relative time}".
2. Header actions: a refresh icon button that re-reads the local files, and `Sync now` as the accent button.
3. Summary banner, info tint: "{n} changes to send" at 19/600, a second line naming the datasets, and `Sync now` on the right.
4. One `Expander` per install holding the Steward addon, expanded by default. Header matches the Addons page: flavour name, path in mono at 11.5px, client version chip, and a per-install state pill.
5. Three dataset rows per card, divided by hairlines.

A dataset row is: 30px glyph, the dataset name at 13.5px with the source file in mono beneath, then the record count and `exportedAt`, then the action.

- Waiting to send: "{n} records, {m} new" with the new count in accent hover, and an accent `Send` button.
- Matching: the count in dim, and a success tick with `In sync`.
- Nothing yet: `Nothing yet` in mute, no button.
- Pull pending: "Guild data written {relative time}" with a secondary `Update in game` button.

Below the install cards, one card for the generated file: `Guild data in game`, the file path in mono, when it was last written, and `Write again`. After an addon update this row is the one that reports the rewrite.

### Syncing

The row owns the progress, matching an addon update. The count is replaced by a state line ("Sending 812 loot events"), a 3px determinate `ProgressBar` sits under the name, and the button goes to a disabled busy state. `Sync now` disables while any row is busy.

### Dataset failure

Row-level, critical text under the source file, with `Retry`: "The server rejected this payload: 3 loot events reference an unknown character. Nothing was recorded." One dataset failing does not stop the others.

The window-level `InfoBar` stays reserved for session and connectivity, as on the Addons page.

### Member role

`Send`, `Sync now` and the per-row send actions disappear rather than greying out. Counts, timestamps and the pull actions stay, since reading the merged guild data into the game is not a privileged action.

Window-level informational `InfoBar`: "Signed in as {name}. Sending guild records needs an officer role on the guild panel. You can still pull the guild view into the game." with an Open guild panel link.

### Unreachable

Same critical `InfoBar` as the Addons page, reworded: "Could not reach api.hoobi.io. Check your connection. Steward cannot send or pull guild data until it can." Local reading carries on, so the counts and timestamps stay truthful and only the actions disable.

## Control mapping

| Element | Control | Note |
| --- | --- | --- |
| Shell navigation | `NavigationView` | `LeftCompact`. Replaces the bare `Frame` in `MainWindow.xaml`. Settings becomes the footer item. |
| Install card | `Expander` | Expanded by default, matching the Addons page. |
| Dataset rows | `ItemsControl` | Three per card, not selectable. |
| Row progress | `ProgressBar` | Determinate, same treatment as an addon update. |
| Running-client pill | `Border` + `TextBlock` | Caution tone, beside the client version chip. |
| Generated file card | `toolkit:SettingsCard` | Already referenced for the Settings page. |
| Session and network messages | `InfoBar` | Window level, shared with the Addons page. |
| Empty states | `StackPanel` + `FontIcon` | Centred, 46ch copy width. |

## Scope

New and not only a view change:

- **A SavedVariables reader.** Parsing the addon's Lua table dump into records. This is the largest piece and belongs in `Steward.Core` with its own tests, ahead of any UI. Built: `LuaSavedVariables` and `StewardSavedVariables`.
- **A generated Lua writer.** One file, calling a function the addon exposes. Rewritten after every addon update.
- **Running-client detection.** Process enumeration by main module path, per flavour folder, plus an exit hook that triggers a re-read.
- **Sync client methods.** On `GigagrugClient`, against a contract that does not exist yet.
- **`NavigationView` shell.** See [Shell change](#shell-change).

Deliberately absent:

- Unattended push. Reading moves to the timer; sending does not.
- A conflict resolution UI. The server merges and answers with what it took.
- Per-character rows. The dataset is the unit; character detail lives in the record count.
- Writing to the addon's own SavedVariables, in any circumstance.
- A sync history or audit log in the app. That belongs on the guild panel.
