# Roster sync between steward-server, the desktop app and the addon

How the three halves of Steward move guild data between Discord and the game.

## Participants

| Repo | Local clone | Role |
| --- | --- | --- |
| `steward-server` (ADO Hoobi/Hoobi) | `D:\steward-server` | .NET 10 services and the React web app. Holds the Discord-side truth: the roster, the Discord member list, sign-ups, attendance. Source of record for people. |
| `hoobio/steward-companion` | `D:\steward-companion` | .NET 10 WinUI 3 desktop app. The only party that can talk to both sides. Pulls from steward-server over HTTP, writes files into the addon folder, reads files out of `WTF`. |
| `hoobio/Steward` | `D:\Steward` | The in-game addon. Renders what the app writes, records what happens in game. Source of record for characters. |

The addon and steward-server never speak to each other. The WoW Lua sandbox has no sockets and no file IO, so every exchange goes through the desktop app.

## Direction of travel

```
steward-server  --HTTP-->  desktop app  --StewardSync.lua-->  addon
steward-server  <--HTTP--  desktop app  <--StewardDB--------  addon
```

Two files, one direction each. The rule exists because the client rewrites its whole saved-variables file from memory at logout, exit or `/reload`, so anything written into that file from outside is discarded on the next serialise.

| File | Written by | Read by | Path |
| --- | --- | --- | --- |
| `StewardSync.lua` | desktop app | addon | `Interface\AddOns\Steward\StewardSync.lua` |
| `Steward.lua` (`StewardDB`) | game client | desktop app | `WTF\Account\<ACCOUNT>\SavedVariables\Steward.lua` |

`StewardSync.lua` is a plain Lua file listed in the TOC. It calls `Steward.LoadSync({...})` rather than assigning a global, so the addon owns the shape of what arrives. The addon updater replaces the whole addon folder, so the desktop app rewrites this file immediately after every addon update.

## Sync document route

steward-server defines the payload. The app does not compose it. The decision is `docs/adr/0001-server-defined-sync-payload.md`, and the server side is steward-server's `docs/design/addon-bundle.md`, section "Sync document route".

`StewardClient.GetSyncDocumentAsync(guildId)` calls `GET /api/addons/sync/{guildId}` for the selected guild, or `GET /api/addons/sync` for a signed-in user with no guild. The answer is:

```json
{"version":1,"signOut":"dropMe"|"clear","payload":{...},"images":{"guild":url|null,"people":{id:url},"discord":{id:url}}}
```

`SyncDocument.Parse` turns it into a `SyncPayload`: the converted payload entries, the `signOut` value, the image URLs and the directory reader result. The server gates every key. The app applies no feature gate of its own to the file.

A failed pull keeps the file on disk and writes nothing:

| Answer | Result |
| --- | --- |
| 401 | Sign-out, the same as any other lost session. |
| 403 | Quiet. Nothing is logged. |
| 404, 502, a network failure | One log line. The same failure again is not logged again. The next trigger asks again. |
| A document the converter rejects | The same as 502. |

There is no fallback to the per-dataset routes.

## Converter rules

`SyncDocument.ToLua` converts JSON to Lua with no knowledge of the keys. `LuaWriter.Serialize` writes the result.

| JSON | Lua |
| --- | --- |
| Object | A keyed table. Each member is written `["name"] = v` in document order. The name goes through the Lua string serialiser, so a key is always data. |
| Object member with the value `null` | Left out. |
| Array | Positional entries in order. A `null` element is written as a positional `nil`, so later entries keep their positions. |
| String | The Lua string serialiser. |
| Number | `double` with the `R` format, the same as every other number in the file. A number that is not finite rejects the document. |
| `true`, `false` | `true`, `false`. |
| `[]` and `{}` | `{}`. |
| An object whose only member is `"$int"` with an object value | A table with integer keys: `{"$int":{"123":v}}` gives `{ [123] = v }`. A member name that is not a base-10 integer, or is outside plus or minus 2^53, rejects the document. |

No rule turns a digit string into a number, so Discord ids and guids stay strings. A string the serialiser cannot write (an unpaired surrogate) rejects the document. A document is also rejected when it is not a JSON object, when `version` is not the number 1, or when `payload` is not an object. A `signOut` value other than `dropMe` is read as `clear`.

## File layout

The file starts with the overlay, then the payload keys in document order:

1. `writtenAt`, the unix second of the write.
2. `fingerprint`.
3. `avatar`, only when `Avatar.tga` was written.

`ReadFingerprint` reads only the first 512 characters, so the fingerprint stays inside them.

`StewardSyncFile.Fingerprint` is SHA-256 over the rendered payload entries (no overlay), the downloaded guild icon's URL and the people and Discord image URLs sorted by id. `GuildRosterSync.WriteIfChanged` skips the write when the file's fingerprint matches, `Avatar.tga` exists when an icon was loaded, and no avatar file is still to write. `force` writes in every case.

## Images

The sandbox has no network, so an image reaches the game the same way data does: the desktop app fetches it, converts it, and writes it into the addon folder. The images are the guild icon (`Avatar.tga`) and the people and Discord member avatars (`Avatars\<id>.tga`).

`images` in the document gives the URLs. The guild icon and a person image are requested at 64 px and a Discord image at 32 px (`PersonAvatars.RequestUri`). A person image takes precedence over a Discord image for the same id. `cdn.discordapp.com` serves PNG at those sizes, so the app converts the format only. A texture in an addon folder must be TGA or BLP, with power-of-two edges, 32-bit for alpha, and the one combination confirmed working on this client is 32-bit uncompressed true-colour, bottom-up, which is what `AvatarTga.Encode` writes.

The payload carries `["avatar"] = "Interface\\AddOns\\Steward\\Avatar.tga"` **only when the app actually wrote that file**, and omits the key otherwise. That rule is load-bearing rather than tidy: since patch 5.0.4 `GetTexture()` echoes back whatever string it was given whether or not the file loaded, and a bad path draws solid green instead of erroring, so **the addon has no way to detect a missing texture**. Presence of the key is the only signal it gets, which is why the app must never write the key optimistically.

The same rule applies to each table in `payload.discord[]` and `payload.directory.people[]`: the app adds `avatar = "Interface\\AddOns\\Steward\\Avatars\\<id>.tga"` to a table whose `id` has a TGA on disk after the write. Only an all-digit id is downloaded or written, since the id becomes a file name. A failed download leaves the entry without `avatar` and never fails the sync. An avatar is downloaded again only when its URL changed or its file is gone: the URL last written per install and id is kept in `state.json` as `person_avatars`. Files for ids no longer in `images` are deleted on every write.

A texture file written while the client is running is picked up on `/reload`, not immediately, the same boundary the rest of this system already lives with. The addon folder is replaced wholesale on update, so every written image is rewritten after an update alongside `StewardSync.lua` (`RestoreSyncFileAfterInstallAsync`, from the last document when it is for the selected guild).

## Sign-out

The app keeps the last document. On sign-out it rewrites the file from that document's `signOut` (`SyncPayload.SignedOut`):

| `signOut` | File after sign-out |
| --- | --- |
| `dropMe` | The last payload without the top-level `me` key. The images stay. |
| `clear` | The overlay only. The avatar files are deleted. |
| No last document, the user held the `steward` feature | The file is not written. |
| No last document, the user did not hold the `steward` feature | The overlay only. |

The server sends `dropMe` when the caller gets the officer parts, so an officer keeps the roster data in game after sign-out. A non-officer with no document this session (a failed or 403 pull) loses any `me` and gear sets left from an earlier session.

## Keys the app reads

The app reads a small part of the payload for its own screens (`SyncDocument` builds a `SyncDirectory`):

| Payload key | Used for |
| --- | --- |
| `directory.people[]`: `id`, `name`, `mainGuid` | The not-linked rejection text. |
| `directory.characters[]`: `guid`, `name`, `level`, `classId`, `linkedUserId` | Which characters are the user's own, the not-linked text and the professions push selection. An absent `characters` key is not the same as an empty one. |
| `directory.professions` | The count on the Sync page. |
| `members` | The count on the Sync page for an officer. |
| `catalogue` | The profession count on the Sync page. |

The reader never throws. A missing or wrongly typed field gives null, an empty string or 0.

## Changes that need a companion release

A new payload key, a removed key or a changed shape is a server deploy only. A companion release is needed for:

- The overlay: `writtenAt`, `fingerprint`, `avatar` and the `avatar` key on `discord[]` and `directory.people[]` tables.
- The `images` maps and the image sizes.
- The directory keys in the table above.
- A new `version` or a new `signOut` value.

## Saved variables do not load after a client restart

Verified on 1.60.1.69913 and tracked as ClassicWoWCommunity/forever-bugs#34. The client still writes saved-variables files, but on the next launch it does not load them back, and the first `/reload` of that session writes defaults over whatever was there. `## SavedVariablesPerCharacter` loads across a `/reload` but not across a restart either.

Two consequences shape everything above:

- **The addon has no persistent store of its own.** It cannot remember a link, a setting or a decision between sessions.
- **`StewardDB` is a write-only outbox.** The addon fills it during a session and the client writes it out at logout. The addon never reads it back; the desktop app does.

A round trip therefore closes through the desktop app, not through addon storage: the addon writes a decision into the outbox, the client serialises it at logout, the app reads it on the next pass, the app pushes it to steward-server, and the next `StewardSync.lua` carries the settled result back into the game.

Settings that must survive a restart go where HoobiScripts puts them, in a macro body (`Core/Store.lua` there), which is the one store the server holds for an addon. Retire this section once `/sw db` reports the saved-variable tables present after a full client restart.

## Identity and linking

steward-server keys a person on `user_id`, the Discord snowflake. `roster_members` has a `character` column but it is dead: roster PATCH rejects writes to it and nothing populates it. So the character to person link does not exist server-side today.

The link is a **guild note holding the person's roster name**. That convention already exists in the guild: the public Note column holds people's names.

Both notes are read and they rank:

1. **Officer note.** The confirmed link. A player cannot edit their own, so a link recorded here stays put.
2. **Public note.** Treated as a suggestion, not a link. It is where the names already are, so it is what `/janny` mines to propose matches.
3. **Neither.** Unmatched. `/janny` falls back to fuzzy-matching the character name itself.

A note is normalised before it is matched: any line whose trimmed content is `Y` or `N`, in either case, is dropped, then blank lines are trimmed. `"Y\nHoobi"` normalises to `"Hoobi"`. The normalised value is matched against the roster member's `display_name`, then sign-up `name`, then `discord_tag`, case-insensitively, then fuzzily.

## What an addon may and may not do to Blizzard's guild list

The native guild window is the daily driver and Steward extends it, so the boundary is worth stating precisely. Tested in game, not reasoned:

**Works, and is what shipped.** Hanging an addon-created pane off `GuildMemberDetailFrame` (`Modules\MemberDetail.lua`) and anchoring one to a `StaticPopup` without parenting to it (`Modules\NoteSuggest.lua`). Both only draw beside Blizzard's UI.

**Worked, but was removed.** A roster-name column inserted between Class and Character: our own font strings on the rows, our own header button in the column display, Blizzard's row widgets and header buttons repositioned with `SetPoint`, and the `Name` header relabelled to `Character` with `SetText`. All of that functioned. It was taken out because a column you cannot sort or filter earns less than it costs in surface area against Blizzard's layout, and because the same information is one click away in the detail pane. `Modules\GuildColumn.lua`, removed in `1c98119`.

**Does not work: sorting the list.** Reordering means `table.sort` on `CommunitiesFrame.MemberList.sortedMemberList`, and there is no route around it. Assigning a fresh list taints the frame; substituting a DataProvider taints the container every `elementData` is read from. The tainted slots reach `RefreshListDisplay` (`CommunitiesMemberList.lua:313-322`), then `SetMember`, `OnClubMemberButtonClicked` (`:764`), `DisplayMember`, `self.memberId` (`GuildRoster.lua:97`) and `SetupRankDropdown` (`:84-89`), so `C_GuildInfo.SetGuildRankOrder` (`:38`) refuses rank changes until a reload. Built, shipped behind a flag, confirmed broken in game on 21 Sep 2026, reverted.

The rule that separates the two: **drawing on Blizzard's UI is fine, and so is moving what it has already drawn. Putting values into the data it reads back is not.** Sorting fails because the list order *is* data Blizzard reads back, while a font string and a `SetPoint` are not.

Cells are aligned by measuring each header's position rather than by matching constants, since the header chain and the row chain use different widths and drift apart otherwise.

## An addon cannot write a guild note

Established the hard way on 21 Sep 2026, after `/janny`'s Link button shipped, appeared to work, and wrote nothing.

Blizzard removed addon note writing in patch 12.0.1, and this client is at that parity. `GuildRosterSetPublicNote` and `GuildRosterSetOfficerNote` are gone, confirmed nil by `/dump` in game. `C_GuildInfo.SetNote` and `C_Club.SetClubMemberNote` survive and are protected: calling either raises `ADDON_ACTION_FORBIDDEN`, writes nothing, and pops Blizzard's offer to disable the addon, which cannot be suppressed. Guild Roster Manager's changelog of 12 Mar 2026 puts it plainly: "Blizzard has removed the ability of addons to edit Public or Officer notes… These cannot be circumvented."

Every other route was checked and closed. `StaticPopup_Show` from addon code carries the taint into Blizzard's own `OnAccept` (`StaticPopup.lua:407` stores the tainted data, `:727` reads it back). `SecureActionButtonTemplate` has no note action among its 30 types (`SecureTemplates.lua:262-650`), and its `macrotext` escape needs a slash command that sets a note, which has never existed. Pre-filling the note box the user opened taints the widget.

Two pieces of reasoning were wrong along the way and are recorded so they are not repeated:

- **`HasRestrictions = true` is not the marker that proves protection.** 282 functions carry it, `C_GuildInfo.GetMOTD` among them, which addons read normally. A first pass read it as inert documentation metadata and a second read it as proof of protection; both were guessing from the same flag. The standard in HoobiScripts' `AGENTS.md` applies: protection is not provable from the source either way, so call the function and watch `ADDON_ACTION_BLOCKED` / `ADDON_ACTION_FORBIDDEN`.
- **It was never about the guid being a secret value.** A secret-value violation raises an immediate Lua error, not an event, so receiving `ADDON_ACTION_FORBIDDEN` proves the function was refused rather than its argument. `C_Club.GetMemberInfo` is secret only in encounter, Mythic+, PvP and restricted-map states anyway.

So the addon **reads** both notes and never writes either. `/janny` is a finder: it resolves who each character is, ranks candidates and lets an officer pick one by hand, and the officer types the name into Blizzard's own dialog. The 31-character cap still governs what can be suggested (`maxLetters` on `SET_GUILD_COMMUNITIY_NOTE`, `GameDialogDefs.lua:1999`), so a longer label is reported rather than offered. `ginv` records no pending link.

The durable fix is to stop using guild notes as the store. A `roster_characters` table in steward-server keyed on `user_id` needs no protected API, reaches every officer rather than only the one who clicked, and survives a rename or a transfer. Guild notes stay what they already are: a read source and the bootstrap `/janny` mines for suggestions.

A Discord snowflake as the stored value was rejected: 18 to 19 of the 31 characters, unreadable to the humans who also read the column, and the stability it buys is what a re-run of `/janny` gives more cheaply. Moving the link into steward-server proper, as a characters table keyed on `user_id`, is the right long-term home and is deferred until the note matching has proved itself in use.

## Freshness

`StewardSync.lua` carries `writtenAt`, the unix second the desktop app wrote it. The addon reports its own staleness from that value and needs nothing else; there is no timestamp in any guild note.

The app judges the freshness of what the addon wrote by the saved-variables file's mtime against the running client's process start time, plus the `exportedAt` the addon stamps into the payload. `WowClient` finds a running client by enumerating processes whose main module sits under that flavour's folder, because the sandbox gives the addon no way to signal that it is running.

`MainViewModel.SyncFileAsync` pulls the sync document and writes the file for every install. It runs on these triggers:

- Startup and Refresh.
- A guild change.
- A character push that reached steward-server.
- The Sync page's write and rewrite buttons, and the restore after a Steward addon install.
- A changed user, role or feature set for a user without `steward` (through `QueueGuildPull`).
- An event on the officer stream `/api/admin/{guild_id}/events` (through `QueueGuildPull`).
- A `syncChanged` frame on `/api/events` (through `QueueGuildPull`). steward-server sends it to the one user whose gear set changed.
- The background pass, at most every 15 minutes (`GuildSyncFallbackInterval`).

`QueueGuildPull` debounces for 2 seconds and pulls the selected guild for every user, officer or not. The route is never polled on the one-minute pass. A result is dropped when the user or the selected guild changed while it was in flight.

Data reaches a second officer's game client by the same route it reached the first: their desktop app pulls the same document on the same steward-server change event, or on its fallback timer, and rewrites their own `StewardSync.lua`. There is no addon-to-addon gossip, and adding some would not help, because the only staleness window is between a rewrite and the next `/reload` and no addon message can make the client reload.

## The `me` dataset

The server's payload carries a `me` table for every signed-in user: `{ id, role, features }`, `role` left out when null. Sign-out removes it, as described under Sign-out. It is a display gate only: the file is locally editable, so `Steward.IsOfficer()` uses `me.role` to hide officer-only windows and columns for a raider rather than to enforce anything; steward-server and the game's own permissions are the real enforcement, and a client with no `me` (signed out, or an older `StewardSync.lua`) falls back to the guild rank check it already had.

## Payload keys

steward-server's `StewardSyncFile.ToJson` defines the keys and their order, and steward-server's `docs/design/addon-bundle.md` gives the gates. Today the keys are `roster`, `loot`, `attendance`, `members`, `discord`, `statuses`, `origins`, `catalogue`, `me`, `directory`, `links`, `rankTargets`, `rankLocked`, `kos`, `attunements` and `gearSets`. `links` maps a character guid to the linked Discord user id, from the link an officer pins in steward-server. `hoobio/Steward` reads `links` in preference to note matching, so a pin settles a link without the guild note convention described under Identity and linking. A gear set's `items` is an array by slot position (Head to Ranged), with `nil` in the empty slots.

## steward-server endpoints in use

Every route here sits behind `SessionAuthentication` (`src/Steward.Data/AdminAccess.cs`), the desktop client's own session cookie.

| Endpoint | Gives |
| --- | --- |
| `GET /api/me` | `{user: {id, name, username, avatar_url, role, features}, guilds: [...]}`. `role` is `global`, `admin`, `raider` or null; `raider` means a seatless caller holding at least one member-view flag (`sync`, `roster`, `professions`, `signups`) in at least one guild. Each guild entry carries its own `features`, that guild's resolved flags, since an officer of one guild is a plain member of another. The guild list supplies the `guild_id` the other calls need. |

**`guilds` does not mean "the user's guilds".** For a `global` role it is every guild in the `discord_guilds` mirror table, in guild id order with the last guild saved through `PUT /api/me/guild` moved to the front (`Mirror.Guilds`, `Me.Get`); only for an `admin` is it filtered to their seats (`CallerAccess.Scope` in `src/Steward.Data/CallerAccess.cs`); a seatless `raider` gets only the guilds where they are a live Discord member holding a member-view flag. Taking the first entry therefore picks an arbitrary server for exactly the people most likely to be running this, and the failure is quiet: `/members` answers with that server's real Discord members while `/roster` answers empty, so the sync looks like it worked. The guild is a persisted user setting (`guild_id` in `state.json`, chosen from a picker in the Sync page header showing each guild's Discord name and icon), never an index into that list. `icon_url` is null for a guild with no Discord icon, so the picker shows no icon rather than a broken image, and a null `name` (from an older server) falls back to the raw id.
| `GET /api/addons/sync/{guild_id}`, `GET /api/addons/sync` | The sync document, described under Sync document route. |
| `GET /api/admin/{guild_id}/events` | A `text/event-stream` of change notices. `event: ready` on connect, a `: ping` comment every 25 seconds, and `event: roster-changed`, `members-changed` or `characters-changed` with `data: {"guild":"<id>"}`. Events carry no data; the app pulls the sync document again. Same `gg_session` auth: 401 is a lost session, 403 no access to that guild. Officer-only. |
| `GET /api/events` | The stream every signed-in app holds. A `syncChanged` frame queues a pull of the sync document. |

The officer event stream is held open while the user holds `steward`. Each event queues a pull, debounced 2 seconds. A dropped connection reconnects after 5 seconds, doubling to 5 minutes and reset by the next `ready`, and 60 seconds with no line, pings included, counts as dropped. A 404 is a server without the route: the app stops trying for that run and relies on its timer. The character push runs on every one-minute pass while the stream is down and every 15 minutes while it is connected. The sync document is pulled on the background pass at most every 15 minutes. The push direction is event-driven too: a write to `Steward.lua` under `WTF\Account` triggers the character push, debounced 1.5 seconds.

The origin is `https://api.hoobi.io/guild` (`Steward:BaseUrl`). Never `guild.hoobi.io`: the Static Web App's navigation fallback answers every `/api/*` path with `index.html` and a 200, so a client pointed there parses HTML as JSON instead of seeing a 401.

## Client API facts this sprint established

The forever branch is not vanilla and it is not retail. Confirmed against `D:\wow-ui-source@forever`, commit 70ef1b2:

- `GetGuildRosterInfo`, `GuildRosterSetPublicNote` and `GuildRosterSetOfficerNote` are **absent**, and so is `C_Club.SetClubMemberNote`. The guild roster is a Communities club.
- Reading it: `C_Club.GetGuildClubId()` (`ClubDocumentation.lua:500`), then `CommunitiesUtil.GetMemberIdsSortedByName(clubId)` and one batch `CommunitiesUtil.GetMemberInfo(clubId, ids)`. `ClubMemberInfo` (`ClubDocumentation.lua:1808-1844`) already carries `memberNote`, `officerNote`, `guildRank`, `guildRankOrder`, `classID`, `level`, `zone`, `guid`, `name` and `lastOnlineYear/Month/Day/Hour`, so no second call per member is needed. There is `classID` but no class string.
- **No field of `ClubMemberInfo` is flagged secret, but `C_Club.GetClubMembers` (`ClubDocumentation.lua:435`) and `C_Club.GetMemberInfo` (`:613`) both are.** Reading the struct is not enough to be safe: under chat messaging lockdown the `name`, `memberNote`, `officerNote` **and `guid`** that come back are secret strings, so every one of them is guarded with `canaccessvalue` and the member is skipped when the guard fails, and none is stored. Checking the struct alone and concluding the path was safe is a mistake this contract already made once, and it named three of the four fields the second time. These are secret only in encounter, Mythic+, PvP and restricted-map states, so the guard matters in a raid and never in a city.
- Readiness is request-then-event, not synchronous. `C_GuildInfo.GuildRoster()` asks the server and **`C_Club.FocusMembers(clubId)` is what actually makes the club's members load**; without it `C_Club.AreMembersReady(clubId)` never turns true unless the player opens the guild frame, and the roster silently stays empty. `CommunitiesMemberList.lua:505` calls it immediately before the same readiness check and is the only guild-club caller in the whole UI source. `GUILD_ROSTER_UPDATE` and `CLUB_MEMBERS_UPDATED` signal a change; filter the latter on the guild clubId, since it fires for every club. `CommunitiesMemberList.lua:623-681` is the pattern to mirror.
- `C_GuildInfo.Invite(name)` is the guild invite. `GuildInvite` is absent. `CanGuildInvite()` and `CanEditPublicNote()` are undocumented bare globals that the Blizzard UI itself calls; `C_GuildInfo.CanEditOfficerNote()` is documented.
- `CHAT_MSG_WHISPER`, `CHAT_MSG_GUILD`, `CHAT_MSG_OFFICER` and `CHAT_MSG_SYSTEM` are `SecretInChatMessagingLockdown = true`. Under that lockdown their `text` is a secret string and any method on it errors, so every chat handler is guarded with `canaccessvalue(text)` and degrades to doing nothing.
- `CHAT_MSG_ADDON` carries **no** lockdown flag, so addon messages remain readable whatever the chat state.
- `StaticPopup` has no combat check, and `C_GuildInfo.Invite` carries no restriction marker. Deferring a prompt out of combat is a choice about interruption, not a technical requirement.

- Lists scroll on the ScrollBox system, not the legacy ones. `WowScrollBoxList` (`ScrollTemplates.xml:4`) plus `MinimalScrollBar`, a view from `CreateScrollBoxListLinearView()` (`ScrollBoxLinearView.lua:246`), wired by `ScrollUtil.InitScrollBoxListWithScrollBar` (`ScrollUtil.lua:137`) and fed a `CreateDataProvider()`. `FauxScrollFrameTemplate` and `HybridScrollFrameTemplate` exist but only legacy windows use them.
- `AddonCompartmentFrame` does **not** load here: its TOC line is gated `[AllowLoadGameType mainline]` (`Blizzard_Minimap.toc:32-33`) and this client's gametype is `camelot`. `MiniMapButtonTemplate` (`Blizzard_Minimap\Shared\MinimapButtonTemplate.xml:3`) does load on `classic, camelot`, but nothing in the Blizzard UI inherits it, so it is untested. A minimap button tries that template first and falls back to a plain `Button` on `Minimap`.
- `hooksecurefunc(frame, "Method", handler)` on a live frame instance is a pattern Blizzard's own code uses (`Blizzard_PTRFeedback_Tooltips.lua:30`). It is the way into `CommunitiesFrame.GuildMemberDetailFrame.DisplayMember`, because `Mixin()` copies methods at frame creation and patching `CommunitiesGuildMemberDetailMixin` afterwards does nothing to the frame that already exists.
- `CommunitiesFrame.GuildMemberDetailFrame` is anchored once in XML (`CommunitiesFrame.xml:615`) and never repositioned at runtime, so a pane anchored below it stays put.

The general rule from HoobiScripts' `AGENTS.md` holds: a familiar bare global is grepped on the branch before it is called, because half of them moved to a `C_*` table and the failure is a nil call inside a feature that otherwise looks correct.

## Branch and release rules

| Repo | Branch | Release |
| --- | --- | --- |
| `hoobio/Steward` | push to `develop` only | the `develop` channel is gone; a push to `develop` alone publishes nothing. No pre-release, no release. |
| `hoobio/steward-companion` | push to `main` only | no release cut: leave the release-please PR unmerged. |
| `steward-server` | push to `main` | the `steward-server` pipeline builds and deploys on a push |

The companion repo uses no feature branches, no worktrees and no pull requests for its own work. The addon repo works day to day on `develop`, fast-forwards `main` for a pre-release and merges the release-please PR for a release; neither happens this sprint.

## Out of scope

- Pushing loot and attendance. No loot or attendance route exists in `StewardGuildSyncApi`.
- A characters table in steward-server.
- Addon-to-addon messages of any kind.
- Writing to the addon's own saved variables from the desktop app, in any circumstance.
