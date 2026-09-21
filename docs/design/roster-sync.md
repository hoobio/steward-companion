# Roster sync between gigagrug, the desktop app and the addon

How the three halves of Steward move guild data between Discord and the game. Written at the start of the first roster sprint, 20 Sep 2026, so later sprints do not have to re-derive the contract.

## Participants

| Repo | Local clone | Role |
| --- | --- | --- |
| `hoobio/gigagrug` | `D:\gigagrug` | Python aiohttp API and React admin SPA. Holds the Discord-side truth: the roster, the Discord member list, sign-ups, attendance. Source of record for people. |
| `hoobio/steward-companion` | `D:\steward-companion` | .NET 10 WinUI 3 desktop app. The only party that can talk to both sides. Pulls from gigagrug over HTTP, writes files into the addon folder, reads files out of `WTF`. |
| `hoobio/Steward` | `D:\Steward` | The in-game addon. Renders what the app writes, records what happens in game. Source of record for characters. |

The addon and gigagrug never speak to each other. The WoW Lua sandbox has no sockets and no file IO, so every exchange goes through the desktop app.

## Direction of travel

```
gigagrug  --HTTP-->  desktop app  --StewardSync.lua-->  addon
gigagrug  <--HTTP--  desktop app  <--StewardDB--------  addon
```

Two files, one direction each. The rule exists because the client rewrites its whole saved-variables file from memory at logout, exit or `/reload`, so anything written into that file from outside is discarded on the next serialise.

| File | Written by | Read by | Path |
| --- | --- | --- | --- |
| `StewardSync.lua` | desktop app | addon | `Interface\AddOns\Steward\StewardSync.lua` |
| `Steward.lua` (`StewardDB`) | game client | desktop app | `WTF\Account\<ACCOUNT>\SavedVariables\Steward.lua` |

`StewardSync.lua` is a plain Lua file listed in the TOC. It calls `Steward.LoadSync({...})` rather than assigning a global, so the addon owns the shape of what arrives. The addon updater replaces the whole addon folder, so the desktop app rewrites this file immediately after every addon update.

## Images

The sandbox has no network, so an image reaches the game the same way data does: the desktop app fetches it, converts it, and writes it into the addon folder. The signed-in user's Discord avatar is the first of these, drawn in the window portrait circles.

`cdn.discordapp.com` serves `?size=64` PNG directly, so no resampling is needed, only a format conversion. A texture in an addon folder must be TGA or BLP, with power-of-two edges, 32-bit for alpha, and the one combination confirmed working on this client is 64x64 32-bit uncompressed true-colour, bottom-up, which is what `Icon.tga` already is.

The payload carries `["avatar"] = "Interface\\AddOns\\Steward\\Avatar.tga"` **only when the app actually wrote that file**, and omits the key otherwise. That rule is load-bearing rather than tidy: since patch 5.0.4 `GetTexture()` echoes back whatever string it was given whether or not the file loaded, and a bad path draws solid green instead of erroring, so **the addon has no way to detect a missing texture**. Presence of the key is the only signal it gets, which is why the app must never write the key optimistically.

A texture file written while the client is running is picked up on `/reload`, not immediately, the same boundary the rest of this system already lives with. The addon folder is replaced wholesale on update, so every written image is rewritten after an update alongside `StewardSync.lua`.

## Saved variables do not load after a client restart

Verified on 1.60.1.69913 and tracked as ClassicWoWCommunity/forever-bugs#34. The client still writes saved-variables files, but on the next launch it does not load them back, and the first `/reload` of that session writes defaults over whatever was there. `## SavedVariablesPerCharacter` loads across a `/reload` but not across a restart either.

Two consequences shape everything above:

- **The addon has no persistent store of its own.** It cannot remember a link, a setting or a decision between sessions.
- **`StewardDB` is a write-only outbox.** The addon fills it during a session and the client writes it out at logout. The addon never reads it back; the desktop app does.

A round trip therefore closes through the desktop app, not through addon storage: the addon writes a decision into the outbox, the client serialises it at logout, the app reads it on the next pass, the app pushes it to gigagrug, and the next `StewardSync.lua` carries the settled result back into the game.

Settings that must survive a restart go where HoobiScripts puts them, in a macro body (`Core/Store.lua` there), which is the one store the server holds for an addon. Retire this section once `/sw db` reports the saved-variable tables present after a full client restart.

## Identity and linking

gigagrug keys a person on `user_id`, the Discord snowflake. `roster_members` has a `character` column but it is dead: roster PATCH rejects writes to it and nothing populates it. So the character to person link does not exist server-side today.

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

The durable fix is to stop using guild notes as the store. A `roster_characters` table in gigagrug keyed on `user_id` needs no protected API, reaches every officer rather than only the one who clicked, and survives a rename or a transfer. Guild notes stay what they already are: a read source and the bootstrap `/janny` mines for suggestions.

A Discord snowflake as the stored value was rejected: 18 to 19 of the 31 characters, unreadable to the humans who also read the column, and the stability it buys is what a re-run of `/janny` gives more cheaply. Moving the link into gigagrug proper, as a characters table keyed on `user_id`, is the right long-term home and is deferred until the note matching has proved itself in use.

## Freshness

`StewardSync.lua` carries `writtenAt`, the unix second the desktop app wrote it. The addon reports its own staleness from that value and needs nothing else; there is no timestamp in any guild note.

The app judges the freshness of what the addon wrote by the saved-variables file's mtime against the running client's process start time, plus the `exportedAt` the addon stamps into the payload. `WowClient` finds a running client by enumerating processes whose main module sits under that flavour's folder, because the sandbox gives the addon no way to signal that it is running.

Data reaches a second officer's game client by the same route it reached the first: their desktop app pulls the same roster on the same timer and rewrites their own `StewardSync.lua`. There is no addon-to-addon gossip, and adding some would not help, because the only staleness window is between a rewrite and the next `/reload` and no addon message can make the client reload. If officers ask for it later, the cheap version is one number broadcast on `GUILD` saying a newer roster exists.

## gigagrug endpoints in use

No gigagrug change was needed for the first sprint. Both endpoints already existed for the admin SPA and the desktop client inherits their auth from `auth_middleware`.

| Endpoint | Gives |
| --- | --- |
| `GET /api/admin/me` | `{user: {id, name, username, avatar_url, role}, guilds: [...]}`. `role` is `global`, `admin` or null. The guild list supplies the `guild_id` the other two calls need. |

**`guilds` does not mean "the user's guilds".** For a `global` role it is every guild the bot is in, in Discord's own `client.guilds` order; only for an `admin` is it filtered to their seats (`routes.py:84-91`). Taking the first entry therefore picks an arbitrary server for exactly the people most likely to be running this, and the failure is quiet: `/members` answers with that server's real Discord members while `/roster` answers empty, so the sync looks like it worked. The guild is a persisted user setting (`guild_id` in `state.json`, chosen from a picker in the Sync page header showing each guild's Discord name and icon), never an index into that list. `name` and `icon_url` are null whenever the Discord client is not ready (`routes.py:92-95`), so the picker falls back to the raw id and no icon rather than rendering a blank row.
| `GET /api/admin/{guild_id}/roster` | `{members: [...], origins, specs, statuses, flags, roles, palette}`. A member carries `user_id`, `display_name`, `name`, `discord_tag`, `status`, `origin`, `flags`, `rating`, `notes` and derived primary and secondary builds. |
| `GET /api/admin/{guild_id}/members` | The full live non-bot Discord member list, `{id, name, nick, avatar_url}`. This is what `ginv` matches against, so a person who has never signed up is still invitable by Discord name. |

The origin is `https://api.hoobi.io/guild` (`Gigagrug:BaseUrl`). Never `guild.hoobi.io`: the Static Web App's navigation fallback answers every `/api/*` path with `index.html` and a 200, so a client pointed there parses HTML as JSON instead of seeing a 401.

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
| `hoobio/Steward` | push to `develop` only | develop channel publishes on every push. No pre-release, no release. |
| `hoobio/steward-companion` | push to `main` only | no release cut: leave the release-please PR unmerged. |
| `hoobio/gigagrug` | push direct to `main` if a change is needed | assume none is needed |

The companion repo uses no feature branches, no worktrees and no pull requests for its own work. The addon repo works day to day on `develop`, fast-forwards `main` for a pre-release and merges the release-please PR for a release; neither happens this sprint.

## Out of scope

- Pushing loot and attendance. The push direction of `IGuildSyncApi` stays unimplemented.
- A characters table in gigagrug.
- Addon-to-addon messages of any kind.
- Writing to the addon's own saved variables from the desktop app, in any circumstance.
