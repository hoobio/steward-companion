# Character sync

WoW characters observed by the Steward addon flow addon -> SavedVariables -> Steward desktop app -> steward-server, and render on the admin SPA roster as `<name> (<level>)` in place of `<class> (<spec>)`. This is the agreed contract between `hoobio/Steward`, this app and steward-server, and the source of truth for each side. It is a proof of concept for later client-to-server syncs (professions, known recipes, gear).

## Feature flag

`sync` is a flag in steward-server's `DesktopAccess.Flags` and in `DesktopAccess.OfficerFeatures` (`src/Steward.Data/DesktopAccess.cs`), so every guild seat holder and global admin holds it automatically, the same as `addons` and `guides`, and the flags page shows it ticked and disabled for them. It is also grantable to anyone else through a role or user target, for a later guildie self-sync that is not built. A non-officer holding `sync` gets it in `/api/admin/me`'s `features` but an empty `guilds`, and 403 on every `/api/admin/{guild_id}/` route, since they hold no seat; an officer of one guild gets 403 on another guild's routes.

steward-server enforces it with one helper, `GuildAccess.RequireFeature(context, "sync")` (`src/Steward.Data/GuildAccess.cs`): the existing guild-seat check (401 unauthenticated, 403 forbidden) plus 403 `{"error":"forbidden"}` when the flag is absent. Every endpoint below uses it except `POST roster`.

The app treats `sync` as not standalone: the empty-feature-set check that drops a user to the gate uses the intersection with `addons`, `guides` and `steward`. The push and every character UI element run and show only while `sync` is held, re-checked before each push. The SPA renders character UI only when `me.features` includes `sync`.

## Requests from the app

steward-server's `AdminAccess.IsSameSite` check (`src/Steward.Data/AdminAccess.cs`) rejects a non-GET with neither `Sec-Fetch-Site` nor an allowlisted `Origin`. `GigagrugClient` sends `Sec-Fetch-Site: none` on every request, a value browsers never let a page set and `IsSameSite` already accepts.

## Identity and scope

A character is identified by its WoW GUID (`Player-<serverId>-<8 hex>`), which survives renames and is unique across realms. Each observation carries its scope, `(realm, guildName)` from `GetGuildInfo("player")`, because beta realm and guild names change at release; the SPA picks a scope from a dropdown defaulting to the most recently observed one. `realm` is the guild's realm as a compact key (no spaces or hyphens, e.g. `ClassicBetaPvP2`): the fourth return of `GetGuildInfo("player")` when the guild is on another realm, else the player's own realm compacted. The optional `realmName` is the display name (`GetRealmName()`), written only when the guild is on the player's own realm, since another realm's display name is not available; it rides on each character entry and on `guildRanks`, is part of the push-gate fingerprint like `realm`, and is omitted from the request when absent.

Each Discord guild carries an allow-list of WoW guild names, sent as `sync_guild_names` (original casing) on that guild's `/api/me` entry. The server rejects a character from a non-allowed guild with the reason `guild not allowed` and ignores `guildRanks` for a non-allowed guild. The app routes each push by those lists: every Discord guild whose own resolved features hold `sync` and whose `sync_guild_names` is a non-empty list receives the characters whose trimmed `guild` matches a listed name case-insensitively, in that guild's own officer or professions-only mode, with `guildRanks` only for an officer guild that lists its guild (`CharacterPushRouting.Route`). A WoW guild on several lists goes to each, and a character on no list is not sent anywhere. Each guild's push-gate fingerprint covers only what was routed to it, and its push records, batch id, per-character outcomes and catalogue fingerprint are kept per Discord guild id and install, so the guilds gate and diff independently. When the selected guild's entry has no `sync_guild_names` (an older server), the whole batch goes to the selected guild only and the server decides. A `guild not allowed` rejection counts as skipped rather than rejected. The Sync page's "Your characters" row reads "{n} characters synced to {m} servers", or "{n} characters for {m} servers, sending shortly" while any is unsent, and "No characters on a server's guild list" when no list covers an observed character.

## Addon (`hoobio/Steward`)

`StewardDB` joins `## SavedVariables:`. Every roster rebuild writes the whole table:

```lua
StewardDB = {
  ["characters"] = {
    ["Player-4395-0A1B2C3D"] = {
      ["name"] = "Hoobi Furry", ["realm"] = "Nightslayer", ["realmName"] = "Nightslayer", ["guild"] = "Gigagrug",
      ["level"] = 60, ["classID"] = 1, ["raceID"] = 2, ["gender"] = 2, ["rankIndex"] = 1,
      ["lastOnline"] = 1758250000, ["linkedUserId"] = "123456789012345678",
      ["linkKnown"] = true, ["observedAt"] = 1758260000,
    },
  },
  ["guildRanks"] = {
    ["realm"] = "Nightslayer", ["realmName"] = "Nightslayer", ["guild"] = "Gigagrug", ["observedAt"] = 1758260000,
    ["ranks"] = { [1] = "Guild Master", [2] = "Officer", [3] = "Member" },
  },
}
```

- `guildRanks.ranks` is the rank name by index, from `GuildControlGetRankName` over `1..GuildControlGetNumRanks()`, the same index space as `rankIndex`; written alongside `characters` on every rebuild, only when non-empty.
- `observedAt` and `lastOnline` use `GetServerTime()`, the realm clock every officer shares, not `time()`.
- `linkedUserId` is `Match.For`'s result, extended to exact-match Discord members (nick or name to id), so a note of `xariahz` links a Discord member with no roster row. Nil when unlinked.
- `linkKnown` is `C_GuildInfo.CanViewOfficerNote()`: a player who cannot read officer notes reports links as unknown, never as unlinked.
- Notes are neither persisted nor sent.
- `ClubMemberInfo.guid` is nilable on this client: a member with a nil guid or one failing the format is skipped, and `/sw check` reports the skipped count.

## App (this repo)

- `StewardSavedVariables` maps `StewardDB.characters` to `CharacterObservation` records through the existing `LuaSavedVariables` parser, skipping and counting malformed records like the other datasets.
- The background pass, while `sync` is held, pushes the whole `characters` table for an install whenever its `StewardDB` file changed since the last successful push (file fingerprint per install in `state.json`), as one batch with a client-generated `batchId`. No per-character ack: the server discards unchanged observations, and a full push lets a voided batch heal on the next one.
- `GigagrugClient.PostCharacterSyncAsync(guildId, batch)`; the Sync page shows the last push's time and each character's outcome in the install's "Your characters" row. `Sync now` at the top of the page forces the push for every install even when the fingerprint matches the last attempt, unlike the background pass.

## steward-server

```sql
sync_batches(guild_id TEXT, batch_id TEXT, user_id TEXT, app_version TEXT, received_at INTEGER, voided_at INTEGER NULL, voided_by TEXT NULL, PRIMARY KEY(guild_id, batch_id))
character_observations(guild_id TEXT, batch_id TEXT, guid TEXT, realm TEXT, guild_name TEXT, name TEXT, level INTEGER, class_id INTEGER, race_id INTEGER, rank_index INTEGER, last_online INTEGER, linked_user_id TEXT NULL, link_known INTEGER, observed_at INTEGER, PRIMARY KEY(guild_id, batch_id, guid))
character_tombstones(guild_id TEXT, guid TEXT, deleted_at INTEGER, deleted_by TEXT, PRIMARY KEY(guild_id, guid))
```

An incoming record is stored only when its content differs from the current observation for that guid. The current character is the latest non-voided observation by `observed_at` per `(guild_id, guid)`, hidden when a tombstone exists with `deleted_at >= observed_at`; a newer observation (the character seen in the guild after the delete) shows it again, computed on read. The current link is the pin held in `character_links` for that guid, not an observation field; see Merge rules for how a guid gets pinned.

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

A roster row is only visible with a `name`, and `roster_set` does not write one, so manual add and auto-create both insert `name = nick or name` of the live Discord member. On sync, a `linked_user_id` that is a live Discord member with no `roster_members` row gets one with no build, so linked raiders without a sign-up reach the roster, the `/roster` pull and `StewardSync.lua`. A row with no sign-ups survives the recompute and a later Raid-Helper sign-up merges onto the same key.

The SPA roster build line shows `<name> (<level>)` of the person's current character in the selected scope whose class matches the primary sign-up class, else the highest level, ties to the latest `last_online`.

## Professions

The logged-in character's own professions and known recipes, readable only from that character's client, so a push covers the characters of the account that pushes.

The addon writes `StewardDB.professions[guid]` for the logged-in character, in the account file beside `characters`. Schema 2 (the only version this app or steward-server accepts): known recipes are bare recipe id arrays, and there is no per-recipe object or `difficulty` field anywhere in this dataset.

```lua
["professions"] = {
  ["Player-4395-0A1B2C3D"] = {
    ["observedAt"] = 1758260000,
    ["fp"] = "a1b2c3d4e5f6...",
    ["schema"] = 2,
    ["skills"] = {
      { ["name"] = "Alchemy", ["rank"] = 285, ["maxRank"] = 300, ["secondary"] = false },
      { ["name"] = "Cooking", ["rank"] = 150, ["maxRank"] = 225, ["secondary"] = true },
    },
    ["recipes"] = {
      ["Alchemy"] = { 11460, 11461 },
    },
  },
}
```

- The Forever client has no Classic tradeskill globals (`GetTradeSkillInfo` and the rest are absent from `D:\wow-ui-source` at `bd2470a`); professions run on the retail-style `C_TradeSkillUI`, whose recipe data arrives from the server only when a profession's window opens (`TRADE_SKILL_SHOW`, `TRADE_SKILL_LIST_UPDATE`). A recipe list is therefore captured the first time each profession's window opens and refreshed on every later opening; an addon cannot open the window itself outside a hardware event: `C_TradeSkillUI.OpenTradeSkill(185)` opens Cooking from `/run`, but the same call from a `C_Timer.After` callback raises `ADDON_ACTION_BLOCKED` (`ForceTaint_Strong`), verified in game on 26 Sep 2026, so it works only from a click or key handler.
- `skills` is read without a window, on login and whenever skill ranks change, from whatever the client's API offers for the character's professions and secondary skills; `secondary` marks a secondary skill.
- `recipes[profession]` is replaced whole on each capture: a Lua array of learned recipe ids (spell ids), ascending and unique. A profession never opened has no entry. Recipe names, headers, tools and reagents live only in the catalogue (`catalogue`/`StewardDB.catalogue`, unchanged); a reader wanting them resolves the id against it.
- `schema` is a required integer, currently always `2`; it exists so a client change to this shape is detected rather than read lossily. The app rejects (does not map or push) a professions entry whose `schema` is not `2`, or whose `recipes[profession]` values are not arrays of positive integers (including the pre-schema `{ scannedAt, list = { {recipeId, ...} } }` shape); the Sync page shows "Update the Steward addon" for the affected install instead of sending stale or malformed data. steward-server rejects a pushed professions entry whose `schema` is not `2` with a 4xx telling the officer to update both the addon and the app, surfaced verbatim on the push row.
- The app sends a character's `professions` object (same shape, camelCase) on that character's record in the sync batch, `fp` and `schema` included.
- steward-server stores it as `professions_json` on the observation, validated for shape and bounds (rank at most maxRank, maxRank at most 375, at most 1000 recipes per profession, `schema` exactly `2`). The current professions are the latest non-voided observation carrying `professions_json`, independent of the latest roster observation, as links are.

## Integrity

`fp` is a keyed fingerprint the addon computes over a fixed canonical form of a character's professions (the id arrays, not the old per-recipe objects) and sends alongside them; `StewardSavedVariables` parses it into `CharacterProfessions.Fp` and the app forwards it untouched, included in the push-gate fingerprint so a changed `fp` triggers a push like any other professions field. steward-server recomputes it on receipt and rejects that record's professions on a missing or mismatched value, DMing the character's owner. It is a deterrent against a hand-edited push, not security: the key and the exact algorithm are defined in the Steward addon and in steward-server, not here.

## Other members' professions

Verified in game on 26 Sep 2026: the client has no server-side guild professions. `C_TradeSkillUI.IsGuildTradeSkillsEnabled()` is `false`, the guild roster has no Professions view, and `C_GuildInfo.QueryGuildMembersForRecipe(186, 2657)` followed by `GetGuildRecipeInfoPostQuery()` answers `0 0 0`. A character's professions are known only from its own client, so guild-wide coverage waits for raider self-push. Profession links use `|cffffd000|Htrade:<guid>:<profession spell id>:<skill line id>|h[<name>]|h|r` (First Aid `3273:129`); a link printed locally for the player's own character opens, one for another character did not in the one test made (profession unverified), so whether the server serves other or offline characters is still open.

## Recipe catalogue

Verified in game on 26 Sep 2026: once a profession's window has opened, `C_TradeSkillUI.GetAllRecipeIDs()` lists every recipe of that profession, learned or not (32 for First Aid); `IsPlayerSpell(recipeId)` answers `true` for a learned recipe after a `/reload` with no window opened; recipe ids are Forever's own (`1230117` First Aid Kit), and Forever moves recipes between professions (Minor Healing Potion is First Aid), so no Classic data applies. Known recipes are therefore detected without a window against a catalogue the guild builds itself.

- The addon writes `StewardDB.catalogue[profession] = { ["scannedAt"], ["list"] = { recipe, ... } }` whenever a profession's window data arrives: every recipe `GetAllRecipeIDs` returns, learned or not, in the recipe shape above minus `difficulty`.
- Each catalogue recipe also carries, each absent until known:
  - `order`: the recipe's 1-based position in the in-game window. `GetAllRecipeIDs` answers recipes grouped by category in ascending `C_TradeSkillUI.GetCategoryInfo(categoryID).uiOrder` (confirmed in game on 28 Sep 2026: Engineering's Parts 10, Explosives 20, Ammunition 140, all children of the profession root 2428). The addon sorts by each category's `uiOrder` then name, walking `parentCategoryID` up through categories that themselves hold recipes as `Blizzard_ProfessionsTemplates\Blizzard_Professions.lua:767-842` does, then by position in `GetAllRecipeIDs`. Blizzard's learned and unlearned grouping is ignored, since learned differs per character and the catalogue is guild-wide.
  - `grey`: `recipeInfo.maxTrivialLevel`, the rank at which the recipe turns grey.
  - `orangeTo` (highest rank seen orange), `yellowFrom` (lowest rank seen yellow or easier), `yellowTo` (highest rank seen yellow or harder) and `greenFrom` (lowest rank seen green or grey), observed from `recipeInfo.relativeDifficulty` (`Enum.TradeskillRelativeDifficulty`: `Optimal` 0 orange, `Medium` 1 yellow, `Easy` 2 green, `Trivial` 3 grey, populated for unlearned recipes too) against the character's rank in that profession at capture time. `TradeSkillRecipeInfo` carries no yellow or green threshold (confirmed in game), which is why they are observed. Yellow starts in `orangeTo + 1 .. yellowFrom` and is pinned exactly when `yellowFrom == orangeTo + 1`; green likewise in `yellowTo + 1 .. greenFrom`. A capture merges the bounds with the previous `StewardDB` entry and the synced guild catalogue, never discarding them.
- The app sends each install's catalogue as a top-level `catalogue` object on the sync batch. steward-server keeps one row per `(guild_id, profession, recipe_id)`, replaced by a newer `scannedAt` except for the bounds, which merge across pushes (highest `orangeTo`/`yellowTo`, lowest `yellowFrom`/`greenFrom`, `order` and `grey` from the newer scan), and serves it at `GET /api/admin/{guild_id}/recipes/catalogue` (flag-gated) as `{catalogue: {<profession>: [recipe, ...]}}`, each profession's recipes in `order` (a recipe without one last, by name), the levels snake_case (`order`, `grey`, `orange_to`, `yellow_from`, `yellow_to`, `green_from`).
- The app writes that catalogue into `StewardSync.lua` as `["catalogue"]`, only while `sync` is held.
- At login the addon unions the synced and local catalogues and, for each profession the character has (`GetProfessions`), lists as known every recipe where `IsPlayerSpell(recipeId)` is true, writing `professions[guid].recipes[profession]` as that profession's id array. A window capture of the same profession rebuilds the same array from the window's own known recipes.

## Raider self-push

Agreed direction on 26 Sep 2026, built: raiders push their own characters' professions so the guild professions page covers everyone, not only officers.

- No separate `professions` flag: `sync` without an officer seat (`role` not `global`/`admin`) is the raider path. An officer's `sync` keeps the full push described above; a seatless holder gets the professions-only subset below.
- A seatless `sync` holder's push is accepted only for guids whose pinned link (`character_links`, see Merge rules) is that user, and only the `professions` object of each record; roster fields and any `guildRanks` on the batch are ignored, and `catalogue` is add-only as described below. Pins come from a confirmed officer observation, so the raider cannot claim a character.
- A record for any other guid is stripped from the batch, logged, and reported by the bot as a DM to the global admins (one DM per offending batch, naming the user and the stripped count).
- The app sends a seatless holder's records only for guids present in `StewardDB.professions` (the account's own characters), via `CharacterSyncMapping.FilterToProfessionsOnly`; the batch still carries each `CharacterSyncEntry`'s roster fields (name, realm, guild, level, classId), since the server's validation requires them on the shape, but the catalogue and guild ranks are sent null. An honest push never trips the alert; the addon's `characters` table holds the whole guild roster and would otherwise be stripped and reported on every push.
- Routing: a non-officer has no seat, so `_authorise` 403s every `/api/admin/{guild_id}/` route below `me`. `/api/admin/me` now returns the guilds a `sync` holder is a Discord member of even without a seat, so the app has a guild id to push and to read the catalogue against; `POST characters/sync` and `GET recipes/catalogue` carry an exception in `_authorise` for `sync` plus Discord membership of that guild.
- Catalogue: only officers update it; a seatless holder's push may add to it. Decided 27 Sep 2026, built in steward-server. Each profession in the seatless `catalogue` is validated like the officer path and kept only when its `fp` matches, whatever `Sync:RequireProfessionsFingerprint` says; a missing or mismatched `fp` drops that profession's additions, the rest of the push proceeds, and the global admins get the same throttled integrity DM with the reason `catalogue fingerprint missing` or `catalogue fingerprint mismatch` and the profession name. A kept recipe is inserted only when that `(guild, profession, recipeId)` is not already in the catalogue, recording the contributor's user id; an existing entry is never updated, replaced or deleted by a seatless push, whatever its `scannedAt`. The officer path is unchanged and ignores `fp`. The app does not send a seatless `catalogue` yet. `GET recipes/catalogue` opens to them so their addon gets the guild catalogue.
- Catalogue `fp`: keyed FNV-1a 32-bit exactly like the professions `fp` (same key, 8 lowercase hex) over UTF-8 text with `\n` after every line, the last included: `c2`, then the profession name, then one `<recipeId>|<name>|<itemId>|<reagents>|<order>|<grey>|<orangeTo>|<yellowFrom>|<yellowTo>|<greenFrom>` line per recipe in that profession's pushed `list`, sorted by `recipeId` ascending numerically. `itemId` and each level field are empty when absent; `reagents` is each reagent as `<itemId>:<count>` sorted by `itemId` ascending numerically, a reagent with no `itemId` rendering as `:<count>` and sorting first, comma-joined, empty when none. The raw pushed strings are used. A recipe with no `recipeId` renders with an empty `recipeId` and sorts first, though the server never stores it. Test vector: `Blacksmithing` with `2660` "Rough Sharpening Stone" (item `2862`, one `2835`, order 2, grey 55, orangeTo 14, yellowFrom 15, yellowTo 30) and `3115` "Rough Weightstone" (item `3239`, one `2835`, order 1, grey 65, greenFrom 45) has the canonical text `c2\nBlacksmithing\n2660|Rough Sharpening Stone|2862|2835:1|2|55|14|15|30|\n3115|Rough Weightstone|3239|2835:1|1|65||||45\n` and `fp` `8d7355e0`. steward-server still accepts the previous `c1` form from an out-of-date addon: the same lines without the six level fields under a `c1` header (the same two recipes give `c1\nBlacksmithing\n2660|Rough Sharpening Stone|2862|2835:1\n3115|Rough Weightstone|3239|2835:1\n`, `fp` `8744cb70`), and a profession signed that way is kept with its level fields stripped, since `c1` does not sign them.
- The app's roster pull (`/roster`, `/members`) and the event stream stay officer-only (`HasStewardFeature`); for a raider the catalogue fetch runs on its own.
- The app's access gate no longer needs `addons`, `guides` or `steward` alongside `sync`: `GigagrugClient.IsAuthorizing` treats `sync` alone as authorizing.

## Gear

The logged-in character's equipped items, bags and bank. The addon writes `StewardDB.gear[guid]` (schema 1, the only version this app accepts; any other `schema` skips the record) in the account file beside `professions`. The app parses it and forwards the raw values untouched as `gear` on that character's record, since the server recomputes `fp` over them: nothing signed is normalised, trimmed or reordered. `gear` is omitted when the character has none, and a raider's professions-only push carries it for the same guids as `professions`.

```
gear: {
  schema: 1,
  observedAt: <unix s>,
  level: <character level at capture>,
  equipped: {"<slot 1..19>": entry, ...},
  bags: [entry, ...],
  bank: {observedAt: <unix s>, items: [entry, ...]} | null,
  fp: "<8 lowercase hex>"
}
entry: {link, itemID, quality, enchantID?, suffixID?, count?, equipLoc?, ilvl?}
```

- `link` is the bare `item:...` string (the part inside `|H...|h`), never the coloured hyperlink. One entry per container slot; a stack carries `count`. Absent optional fields are omitted, not zero.
- `equipped` holds every equipped item at any quality. `bags` and `bank` are empty below `MAX_LEVEL` (60) and hold only entries with `quality >= 3` and an `equipLoc`. `bank` is null until the bank has been opened once with Steward running, and then keeps its last scan.
- `observedAt`, `level` and `schema` are not signed. A gear change is a changed push fingerprint (the record with `observedAt` left out), so it triggers a push on the full path and on the professions-only path.
- `fp` is the keyed FNV-1a 32-bit over the professions `FINGERPRINT_KEY` followed by the `g1` canonical text, as 8 lowercase hex characters. The canonical text is UTF-8 with `\n` after every line, over the raw pushed values: `g1`, the character guid, one `e|<slot>|<entry line>` per equipped slot by slot number ascending, one `b|<entry line>` per bags entry sorted by entry line in code-point order, then `k|-` when `bank` is null, else `k|<bank observedAt>` followed by one `k|<entry line>` per bank entry sorted by entry line. `<entry line>` is `<link>|<quality>|<count>|<equipLoc>|<ilvl>` with an empty string for an absent field.
- Test vector (guid `Player-5826-0A1B2C3D`), canonical text:

```
g1
Player-5826-0A1B2C3D
e|1|item:12640:1508:0:0:0:0:0:0:60:0:0:0:0|4||INVTYPE_HEAD|63
e|4|item:45:0:0:0:0:0:0:0:60:0:0:0:0|1||INVTYPE_BODY|1
e|16|item:19019:1900:0:0:0:0:0:0:60:0:0:0:0|5||INVTYPE_WEAPON|80
b|item:10247:0:0:0:0:0:1050:0:60:0:0:0:0|3||INVTYPE_HEAD|57
b|item:17063:0:0:0:0:0:0:0:60:0:0:0:0|4||INVTYPE_FINGER|71
k|1790900000
k|item:18813:0:0:0:0:0:0:0:60:0:0:0:0|4||INVTYPE_FINGER|71
```

  gives `fp` `8bb1ce6a`; the same with the three `k` lines replaced by `k|-` (bank null) gives `cd7ea981`. `GearFingerprintTests` pins both.

## Merge rules

- The newest `observed_at` wins per character; absence from a push never deletes.
- Links are pinned per character guid in steward-server's `character_links`, decided 27 Sep 2026 and built there. The first officer observation with `link_known` and a `linkedUserId` for a guid with no pin pins it to that user; after that no observation moves it, and an officer observation whose `link_known` user differs is recorded as a "guild note disagrees" conflict (the observed user and time) on the officer character page. An unpinned guid is linked to nobody, so every reader (the member routes, the raider professions push, roster mains, the `directory` data this app writes) reads the pin alone. Officers re-map a pin to another live member or roster person, unlink it (which blocks the automatic pin) or reset it (so the next confirmed observation pins again), each with a history row naming the officer. Existing links were pinned once by a migration. Voiding a batch never unpins.
- Rollback is voiding a batch, redo is unvoiding it, and restore is removing a tombstone; nothing is updated in place.
