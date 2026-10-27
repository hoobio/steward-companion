# Privacy Policy

**Steward**

*Last updated: 16 October 2026*

## Summary

Steward has no telemetry, analytics, advertising or crash reporting. It sends data only to do the jobs listed below: sign-in, addon installs and updates, and guild sync. The source code is public at [github.com/hoobio/steward-companion](https://github.com/hoobio/steward-companion), and every Microsoft Store release is built from that source by a public GitHub Actions run.

Steward does not read browser data, saved passwords, Battle.net credentials or your documents. It reads files only in your World of Warcraft folders and its own data folder, plus the few Windows settings and shortcuts listed under "What Steward reads on your PC".

## Sign-in

- You sign in with Discord in your own browser, through the Steward API (`api.hoobi.io`). Steward never sees your Discord password.
- The Steward API gives the app a session token. Steward keeps it encrypted with Windows DPAPI, so only your Windows account on this PC can decrypt it.
- Signing out deletes the token.

## Data sent to the Steward API

The Steward API at `api.hoobi.io` is run by the developer of Steward. Every request carries your session token, the app version and your IP address.

- **Your account:** your Discord user id, roles and the guild you select, to show what you have access to.
- **Character sync** (only if you have the sync feature and the Steward addon writes data): for each character the Steward addon has recorded, its name, id, realm, guild, guild rank, level, class, race, last online time, professions and recipes, and equipped, bag and bank items. It goes only to the guilds that list that character's WoW guild.
- **Journeys** (only if you have the sync feature and install the Steward: Journey addon): the file that addon writes for each character, which holds the route that character took (positions, zones and times).
- **CurseForge** (only while CurseForge management is on in Settings):
  - To find your installed addons on CurseForge, Steward sends the folder names and file fingerprints of the addons it does not manage. These are checksums of the addon files, not the files themselves.
  - It also sends your addon searches.
  - If a CurseForge download fails, it sends the addon id, version, download URL and the error, so the developer can see CDN problems.
- **Addon downloads:** requests for addon manifests and files, including protected addons.

The Steward API answers with your guild's roster, members, professions and banners, which Steward writes into the Steward addon's folder for the game to read.

## Other services

- **`addon.hoobi.io`:** addon manifests and zip files. These requests carry no account data.
- **TukUI** (`api.tukui.org`, `storage.tukui.org`): only while CurseForge management is on in Settings. Steward reads TukUI's addon list, changelogs, ElvUI and Tukui downloads and their logos directly. These requests carry no account data.
- **RestedXP** (`sso.restedxp.com`, `rxpbck.restedxp.com`, `rxpgcr.restedxp.com`): only if you sign in to RestedXP for levelling guides.
  - Your RestedXP username and password, and a two-factor code if asked, go to RestedXP only.
  - Steward does not store your password. It keeps the RestedXP session tokens encrypted with DPAPI.
- **GitHub** (`github.com`, `api.github.com`): the MSI build downloads its updates from this repository's releases and checks each one against GitHub's published checksum.
- **Microsoft Store:** the Store build checks for and installs its own updates through Windows.
- **Images:** addon icons and Discord avatars load from the addresses the Steward API and addon manifests give (for example Discord's and CurseForge's image servers). Those servers see your IP address.

## What Steward writes into your WoW folders

Steward writes only into `Interface\AddOns`, never into `WTF`, except for two things you ask for: the optional beta-to-live copy, which copies your own files from the beta install to the live install, and an addon settings import (below), which changes the HoobiScripts saved variables you confirm while the game is closed.

- Addon files it installs and updates.
- `Steward\StewardSync.lua` and avatar images, so the Steward addon can show your guild's data.
- `StewardGuides\Guides.lua`, the RestedXP guides you bought.
- `Core\Theme.lua` in protected addons, holding your Discord user id. If someone shares a copy of a protected addon, this id shows who downloaded it. The addon's notice in the app says this too.
- `StewardIcon.tga` and an icon line in the addon's TOC file, for addons that have no in-game icon.

## Addon settings and plans

"Edit on the website", "Plan talents" and "Plan action bars" in Settings open guild.hoobi.io in your browser with an export of one WoW account. The export holds the account folder name, the names of the installed addon folders, character names with their classes, realm names and the realm folder numbers, the HoobiScripts addon setting values in the saved variables (mail targets name characters), and your talent plans and action bar plans.

The export and the import link the website sends back are in the part of the address after `#`. Browsers never send that part to a server, so the data stays in the address and in your browser. Steward does not send it to the Steward API, and the website does not send it to a server.

An import changes only the settings and plans you confirm in Steward, and only while the game is closed.

## What Steward reads on your PC

- **WoW folders:** install details (`.build.info`, `.flavor.info`), addon folders and their TOC files, and these saved variables: `Steward.lua`, `StewardGuides.lua` and `StewardJourney.lua`. For an addon settings export or import, it also reads the saved variables of the HoobiScripts addons and the character folder names of that account. It also reads file dates under `WTF\Account` to tell when the game last saved.
- **Running programs:** whether WoW, CurseForge or WowUp is running. Steward waits for the game to close before some changes, and warns you before it adopts addons another manager handles.
- **Windows settings:** the Battle.net install location and the Blizzard install path, the startup entries and Startup-folder shortcuts for CurseForge and WowUp, and which app opens `curseforge://` links.

## Files on your PC

All in `%LocalAppData%\Steward`:

| File | What it holds |
|------|---------------|
| `state.json` | Settings, WoW install paths, addon install records, the selected guild, and the encrypted Steward and RestedXP sessions |
| `provider_addons.json` | CurseForge and TukUI addons you manage |
| `character_sync.json`, `journey_uploads.json` | What was last sent, so unchanged data is not sent again |
| `guides\` | Cached RestedXP guides and the BattleTag that bought them |
| `icons\` | Cached addon icons |
| `logs\` | Diagnostic logs, deleted after 14 days. They never hold your session token, passwords or sign-in codes |
| `update.log` | The installer log from the last MSI update |

Start with Windows adds a `Steward` entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` on the MSI build, or uses Windows' own startup task on the Store build.

To remove everything, uninstall Steward and delete `%LocalAppData%\Steward`.

## Linux and macOS

Steward collects and sends the same data on every system. These parts differ:

- **Data folder:** `~/.local/share/Steward` on Linux (or `$XDG_DATA_HOME/Steward`) and `~/Library/Application Support/Steward` on macOS, holding the same files as the table above.
- **Session encryption:** the key that encrypts the saved sessions is kept in the Secret Service keyring on Linux and the login Keychain on macOS. With no keyring, it is kept in `secrets/session.key` in the data folder, readable only by your user account.
- **Finding WoW:** Steward looks for World of Warcraft in Wine, Lutris, Heroic and Steam prefixes in your home folder, and reads Lutris game configs to find them.
- **Start on login:** an autostart entry in `~/.config/autostart` on Linux, or a LaunchAgent in `~/Library/LaunchAgents` on macOS.

## Contact

For questions, open an issue at [github.com/hoobio/steward-companion](https://github.com/hoobio/steward-companion/issues).
