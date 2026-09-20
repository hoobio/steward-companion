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

A note is normalised before it is matched: any line whose trimmed content is exactly `Y` or `N` is dropped, then blank lines are trimmed. `"Y\nHoobi"` normalises to `"Hoobi"`. The normalised value is matched against the roster member's `display_name`, then sign-up `name`, then `discord_tag`, case-insensitively, then fuzzily.

Writing is `C_GuildInfo.SetNote(guid, note, isPublic)` (`GuildInfoDocumentation.lua:389-400`, `HasRestrictions = true`), with `isPublic = false` for the officer note, gated on `C_GuildInfo.CanEditOfficerNote()`. Both notes cap at 31 characters (`maxLetters` on the `SET_GUILD_COMMUNITIY_NOTE` dialog, `GameDialogDefs.lua:1999`), so a roster name longer than that cannot be stored and `/janny` says so rather than truncating.

The first iteration of `/janny` is dry run. It shows the write it would make and writes nothing, so the matching can be judged against the real guild before it touches guild data.

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
| `GET /api/admin/{guild_id}/roster` | `{members: [...], origins, specs, statuses, flags, roles, palette}`. A member carries `user_id`, `display_name`, `name`, `discord_tag`, `status`, `origin`, `flags`, `rating`, `notes` and derived primary and secondary builds. |
| `GET /api/admin/{guild_id}/members` | The full live non-bot Discord member list, `{id, name, nick, avatar_url}`. This is what `ginv` matches against, so a person who has never signed up is still invitable by Discord name. |

The origin is `https://api.hoobi.io/guild` (`Gigagrug:BaseUrl`). Never `guild.hoobi.io`: the Static Web App's navigation fallback answers every `/api/*` path with `index.html` and a 200, so a client pointed there parses HTML as JSON instead of seeing a 401.

## Client API facts this sprint established

The forever branch is not vanilla and it is not retail. Confirmed against `D:\wow-ui-source@forever`, commit 70ef1b2:

- `GetGuildRosterInfo`, `GuildRosterSetPublicNote` and `GuildRosterSetOfficerNote` are **absent**, and so is `C_Club.SetClubMemberNote`. The guild roster is a Communities club.
- Reading it: `C_Club.GetGuildClubId()` (`ClubDocumentation.lua:500`), then `CommunitiesUtil.GetMemberIdsSortedByName(clubId)` and one batch `CommunitiesUtil.GetMemberInfo(clubId, ids)`. `ClubMemberInfo` (`ClubDocumentation.lua:1808-1844`) already carries `memberNote`, `officerNote`, `guildRank`, `guildRankOrder`, `classID`, `level`, `zone`, `guid`, `name` and `lastOnlineYear/Month/Day/Hour`, so no second call per member is needed. There is `classID` but no class string. No field is flagged secret.
- Readiness is request-then-event, not synchronous. `C_GuildInfo.GuildRoster()` asks the server, `GUILD_ROSTER_UPDATE` and `CLUB_MEMBERS_UPDATED` signal a change, and `C_Club.AreMembersReady(clubId)` says whether the list is usable yet. `CommunitiesMemberList.lua:623-681` is the pattern to mirror.
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
| `hoobio/Steward` | push to `development` only | development channel publishes on every push. No pre-release, no release. |
| `hoobio/steward-companion` | push to `main` only | no release cut: leave the release-please PR unmerged. |
| `hoobio/gigagrug` | push direct to `main` if a change is needed | assume none is needed |

The companion repo uses no feature branches, no worktrees and no pull requests for its own work. The addon repo works day to day on `development`, fast-forwards `main` for a pre-release and merges the release-please PR for a release; neither happens this sprint.

## Out of scope

- Pushing loot and attendance. The push direction of `IGuildSyncApi` stays unimplemented.
- A characters table in gigagrug.
- Addon-to-addon messages of any kind.
- Writing to the addon's own saved variables from the desktop app, in any circumstance.
