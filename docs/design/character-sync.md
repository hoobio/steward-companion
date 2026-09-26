# Character sync

WoW characters observed by the Steward addon flow addon -> SavedVariables -> Steward desktop app -> gigagrug, and render on the admin SPA roster as `<name> (<level>)` in place of `<class> (<spec>)`. This is the agreed contract between `hoobio/Steward`, this app and `hoobio/gigagrug`, and the source of truth for each side. It is a proof of concept for later client-to-server syncs (professions, known recipes, gear).

## Feature flag

`sync` is a grantable flag in gigagrug's `desktop_flags.FLAGS`, never in `OFFICER_FEATURES`, granted per user through the existing flag-targets admin UI. It is only grantable to a user who holds a guild seat or is a global admin: `PUT /desktop-flags/sync` answers 400 for any other user target and for any role target, the flags page disables the control for those users, and `sync` is only ever returned to a user who is currently an officer or global admin, so a demotion drops it without the targets changing. `handle_me` returns officers the sorted union of `OFFICER_FEATURES` and `features_for_user(user_id)`, so a grant to an officer or global admin takes effect; `test_me_features_for_officers_include_every_flag` changes with it.

gigagrug enforces it with one helper, `require_feature(request, guild_id, "sync")`: the existing guild-seat `_authorise` (401 unauthenticated, 403 forbidden) plus 403 `{"error":"forbidden"}` when the flag is absent. Every endpoint below uses it except `POST roster`.

The app treats `sync` as not standalone: the empty-feature-set check that drops a user to the gate uses the intersection with `addons`, `guides` and `steward`. The push and every character UI element run and show only while `sync` is held, re-checked before each push. The SPA renders character UI only when `me.features` includes `sync`.

## Requests from the app

gigagrug's `_same_site` check (`auth.py:146-153`, `255-256`) rejects a non-GET with neither `Sec-Fetch-Site` nor an allowlisted `Origin`. `GigagrugClient` sends `Sec-Fetch-Site: none` on every request, a value browsers never let a page set and `_same_site` already accepts; gigagrug's auth tests carry a case for it.

## Identity and scope

A character is identified by its WoW GUID (`Player-<serverId>-<8 hex>`), which survives renames and is unique across realms. Each observation carries its scope, `(realm, guildName)` from `GetRealmName()` and `GetGuildInfo("player")`, because beta realm and guild names change at release; the SPA picks a scope from a dropdown defaulting to the most recently observed one.

## Addon (`hoobio/Steward`)

`StewardDB` joins `## SavedVariables:`. Every roster rebuild writes the whole table:

```lua
StewardDB = {
  ["characters"] = {
    ["Player-4395-0A1B2C3D"] = {
      ["name"] = "Hoobi Furry", ["realm"] = "Nightslayer", ["guild"] = "Gigagrug",
      ["level"] = 60, ["classID"] = 1, ["raceID"] = 2, ["rankIndex"] = 1,
      ["lastOnline"] = 1758250000, ["linkedUserId"] = "123456789012345678",
      ["linkKnown"] = true, ["observedAt"] = 1758260000,
    },
  },
}
```

- `observedAt` and `lastOnline` use `GetServerTime()`, the realm clock every officer shares, not `time()`.
- `linkedUserId` is `Match.For`'s result, extended to exact-match Discord members (nick or name to id), so a note of `xariahz` links a Discord member with no roster row. Nil when unlinked.
- `linkKnown` is `C_GuildInfo.CanViewOfficerNote()`: a player who cannot read officer notes reports links as unknown, never as unlinked.
- Notes are neither persisted nor sent.
- `ClubMemberInfo.guid` is nilable on this client: a member with a nil guid or one failing the format is skipped, and `/sw check` reports the skipped count.

## App (this repo)

- `StewardSavedVariables` maps `StewardDB.characters` to `CharacterObservation` records through the existing `LuaSavedVariables` parser, skipping and counting malformed records like the other datasets.
- The background pass, while `sync` is held, pushes the whole `characters` table for an install whenever its `StewardDB` file changed since the last successful push (file fingerprint per install in `state.json`), as one batch with a client-generated `batchId`. No per-character ack: the server discards unchanged observations, and a full push lets a voided batch heal on the next one.
- `GigagrugClient.PostCharacterSyncAsync(guildId, batch)`; the Sync page shows the last push's time, accepted count and rejected records.

## gigagrug

```sql
sync_batches(guild_id TEXT, batch_id TEXT, user_id TEXT, app_version TEXT, received_at INTEGER, voided_at INTEGER NULL, voided_by TEXT NULL, PRIMARY KEY(guild_id, batch_id))
character_observations(guild_id TEXT, batch_id TEXT, guid TEXT, realm TEXT, guild_name TEXT, name TEXT, level INTEGER, class_id INTEGER, race_id INTEGER, rank_index INTEGER, last_online INTEGER, linked_user_id TEXT NULL, link_known INTEGER, observed_at INTEGER, PRIMARY KEY(guild_id, batch_id, guid))
character_tombstones(guild_id TEXT, guid TEXT, deleted_at INTEGER, deleted_by TEXT, PRIMARY KEY(guild_id, guid))
```

An incoming record is stored only when its content differs from the current observation for that guid. The current character is the latest non-voided observation by `observed_at` per `(guild_id, guid)`, hidden when a tombstone exists with `deleted_at >= observed_at`; a newer observation (the character seen in the guild after the delete) shows it again. The current link is the latest non-voided observation with `link_known = 1`. Both are computed on read.

| Method | Path | Behaviour |
|---|---|---|
| POST | `characters/sync` | `{batchId, appVersion, characters:[...]}`; repeat of a stored `(guild_id, batchId)` is a 200 no-op, 409 when the stored sender differs; validates per record and answers `{"accepted":n,"rejected":[{"guid","reason"}]}` |
| GET | `characters?realm=&guild=` | current characters for the scope with linked user, plus `scopes` (distinct realm and guild with last `observed_at`); no scope given means the most recent |
| DELETE | `characters/{guid}` | tombstone at now |
| POST | `characters/{guid}/restore` | removes the tombstone |
| GET | `sync/batches` | recent batches: sender, time, stored count, voided |
| POST | `sync/batches/{id}/void`, `sync/batches/{id}/unvoid` | rollback and redo of one push |
| POST | `roster` | manual raider add `{user_id}`; officer seat only, no flag; 404 unless a live Discord member |

All paths sit under `/api/admin/{guild_id}/`.

Validation per record: guid `^Player-\d+-[0-9A-F]{8}$`; name trimmed, 1-32 characters, no control characters; level 1-80; class_id 1-13; realm and guild non-empty, at most 64 characters; observed_at within now minus 30 days and now plus 10 minutes; linked_user_id null or a snowflake. At most 1000 records per batch, otherwise 400.

A roster row is only visible with a `name` (`roster.py:284-288`), and `roster_set` does not write one, so manual add and auto-create both insert `name = nick or name` of the live Discord member. On sync, a `linked_user_id` that is a live Discord member with no `roster_members` row gets one with no build, so linked raiders without a sign-up reach the roster, the `/roster` pull and `StewardSync.lua`. A row with no sign-ups survives the recompute and a later Raid-Helper sign-up merges onto the same key.

The SPA roster build line shows `<name> (<level>)` of the person's current character in the selected scope whose class matches the primary sign-up class, else the highest level, ties to the latest `last_online`.

## Merge rules

- The newest `observed_at` wins per character; absence from a push never deletes.
- Links come only from observations with `link_known`.
- Rollback is voiding a batch, redo is unvoiding it, and restore is removing a tombstone; nothing is updated in place.
