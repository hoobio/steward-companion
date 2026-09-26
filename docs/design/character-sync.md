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

## Professions

The logged-in character's own professions and known recipes, readable only from that character's client, so a push covers the characters of the account that pushes.

The addon writes `StewardDB.professions[guid]` for the logged-in character, in the account file beside `characters`:

```lua
["professions"] = {
  ["Player-4395-0A1B2C3D"] = {
    ["observedAt"] = 1758260000,
    ["skills"] = {
      { ["name"] = "Alchemy", ["rank"] = 285, ["maxRank"] = 300, ["secondary"] = false },
      { ["name"] = "Cooking", ["rank"] = 150, ["maxRank"] = 225, ["secondary"] = true },
    },
    ["recipes"] = {
      ["Alchemy"] = {
        ["scannedAt"] = 1758260000,
        ["list"] = {
          { ["name"] = "Major Healing Potion", ["header"] = "Potions", ["difficulty"] = "optimal", ["itemId"] = 13446,
            ["tools"] = "", ["reagents"] = { { ["itemId"] = 13464, ["name"] = "Golden Sansam", ["count"] = 2 } } },
        },
      },
    },
  },
}
```

- The Forever client has no Classic tradeskill globals (`GetTradeSkillInfo` and the rest are absent from `D:\wow-ui-source` at `bd2470a`); professions run on the retail-style `C_TradeSkillUI`, whose recipe data arrives from the server only when a profession's window opens (`TRADE_SKILL_SHOW`, `TRADE_SKILL_LIST_UPDATE`). A recipe list is therefore captured the first time each profession's window opens and refreshed on every later opening; an addon cannot open the window itself outside a hardware event: `C_TradeSkillUI.OpenTradeSkill(185)` opens Cooking from `/run`, but the same call from a `C_Timer.After` callback raises `ADDON_ACTION_BLOCKED` (`ForceTaint_Strong`), verified in game on 26 Sep 2026, so it works only from a click or key handler.
- `skills` is read without a window, on login and whenever skill ranks change, from whatever the client's API offers for the character's professions and secondary skills; `secondary` marks a secondary skill.
- `recipes[profession]` is replaced whole on each capture and holds learned recipes only, each also carrying its `recipeId` (spell id). A profession never opened has no entry. `difficulty` is the recipe's relative difficulty: `optimal`, `medium`, `easy` or `trivial`. Every field comes from APIs verified present in `D:\wow-ui-source`; a field the client cannot supply is omitted rather than guessed.
- The app sends a character's `professions` object (same shape, camelCase) on that character's record in the sync batch.
- gigagrug stores it as `professions_json` on the observation, validated for shape and bounds (rank at most maxRank, maxRank at most 375, at most 1000 recipes per profession, counts 1-100). The current professions are the latest non-voided observation carrying `professions_json`, independent of the latest roster observation, as links are.

## Recipe catalogue

Verified in game on 26 Sep 2026: once a profession's window has opened, `C_TradeSkillUI.GetAllRecipeIDs()` lists every recipe of that profession, learned or not (32 for First Aid); `IsPlayerSpell(recipeId)` answers `true` for a learned recipe after a `/reload` with no window opened; recipe ids are Forever's own (`1230117` First Aid Kit), and Forever moves recipes between professions (Minor Healing Potion is First Aid), so no Classic data applies. Known recipes are therefore detected without a window against a catalogue the guild builds itself.

- The addon writes `StewardDB.catalogue[profession] = { ["scannedAt"], ["list"] = { recipe, ... } }` whenever a profession's window data arrives: every recipe `GetAllRecipeIDs` returns, learned or not, in the recipe shape above minus `difficulty`.
- The app sends each install's catalogue as a top-level `catalogue` object on the sync batch. gigagrug keeps one row per `(guild_id, profession, recipe_id)`, replaced by a newer `scannedAt`, and serves it at `GET /api/admin/{guild_id}/recipes/catalogue` (flag-gated) as `{catalogue: {<profession>: [recipe, ...]}}`.
- The app writes that catalogue into `StewardSync.lua` as `["catalogue"]`, only while `sync` is held.
- At login the addon unions the synced and local catalogues and, for each profession the character has (`GetProfessions`), lists as known every recipe where `IsPlayerSpell(recipeId)` is true, writing `professions[guid].recipes[profession]` with `difficulty` omitted. A window capture of the same profession replaces it with the richer list, `difficulty` included.

## Raider self-push (not built)

Agreed direction on 26 Sep 2026, deferred: raiders push their own characters' professions so the guild professions page covers everyone, not only officers.

- A separate grantable flag, `professions`, open to any user; `sync` stays officer-only and remains the only way to push roster fields.
- A `professions` holder's push is accepted only for guids whose current link (latest non-voided officer observation with `link_known`) is that user, and only the `professions` object of each record; roster fields are ignored. Links come from officer-visible guild notes, so the raider cannot claim a character.
- A record for any other guid is stripped from the batch, logged, and reported by the bot as a DM to the global admins (one DM per offending batch, naming the user and the stripped count).
- The app sends a `professions`-only holder's records only for guids present in `StewardDB.professions` (the account's own characters), with no roster fields, so an honest push never trips the alert; the addon's `characters` table holds the whole guild roster and would otherwise be stripped and reported on every push.
- Routing: a non-officer has no seat, so `_authorise` 403s every `/api/admin/{guild_id}/` route, and `AdminMe.Guilds` is empty for them, so the app has no guild id. Both need solving: an exception in `_authorise` for `POST characters/sync` and `GET recipes/catalogue` (flag plus Discord membership of that guild), and a guild list in `/api/admin/me` for flag holders.
- Catalogue: only officers seed or update it; a `professions` holder's `catalogue` object is ignored. `GET recipes/catalogue` opens to them so their addon gets the guild catalogue.
- The app's roster pull (`/roster`, `/members`) stays officer-only; for a raider the catalogue fetch runs on its own.
- The app's access gate needs `addons`, `guides` or `steward` besides `professions`, as it does for `sync`.
- Signing (HMAC-SHA256 over each record with `sha2.lua`, verified by the app before upload) was agreed as a deterrent for raider pushes and lands with this work.

## Merge rules

- The newest `observed_at` wins per character; absence from a push never deletes.
- Links come only from observations with `link_known`.
- Rollback is voiding a batch, redo is unvoiding it, and restore is removing a tombstone; nothing is updated in place.
