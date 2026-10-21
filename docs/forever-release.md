# Forever release checklist

What changes in this repo and its neighbours for World of Warcraft: Forever's live client, which Battle.net installed beside the beta on 14 Oct 2026.

## Live client values

Read off the disk on 14 Oct 2026:

| Value | Where | Live | Beta |
| --- | --- | --- | --- |
| Product code | `<install>\<flavour folder>\.flavor.info`, the line after `[Flavor]` (`WowInstalls` reads this) | `wowf` | `wow_classic_beta` |
| Flavour folder | the folder under the WoW root holding `.flavor.info`, `Interface\` and `WTF\` | `_forever_` | `_classic_beta_` |
| Client version | `.build.info` | `1.60.1.70245` | `1.60.1.70374` |
| Interface number | computed from the client version (`TocFile.InterfaceNumber`) | `16001` | `16001` |
| Client executable | the process under the flavour folder while the game runs | `Wow.exe` | `WowB.exe` |
| CurseForge version type | `curseforge.com/wow/search` game version filter, "Forever" | `88568` | `88568` |

Battle.net copied the beta's `WTF` into `_forever_` on install; `Interface\AddOns` started empty.

## This repo, done

1. `appsettings.json`: `wowf` in `SupportedProducts` ("World of Warcraft: Forever"), `RestedXp:ProductPrefixes` and `CurseForge:GameVersionTypes`. BugSack and BugGrabber keep their baked-in `88568`, since both clients share it.
2. `Assets/Products/wowf.png`, the picker icon, a copy of the beta's.
3. `ForeverLaunch.MoveFromBeta`: from 21 Oct 2026 a selection still on the beta moves to the live install once.
4. `ForeverCopy` and `MainViewModel.ForeverCopy.cs`: while a beta install is present, the live install is held out of `Installs` until the user copies from the beta or chooses "I'll do it myself" (`forever_copy_done`), and the Addons page header offers "Sync from beta" on the live install after that. The sync is newer-wins and never deletes: AddOns folders, account-level saved variables and settings under `WTF\Account`, and the video and sound CVars of `WTF\Config.wtf`, merged into the live file with every other line kept.

## This repo, when the beta is retired

1. Remove `wow_classic_beta` from the three `appsettings.json` maps; a beta install then drops out of discovery and the selection falls back to the live one.
2. `GuidesPreview` and `HomePage.xaml` copy, `docs/design/home-and-settings.md` and the mockups: replace `_classic_beta_` and "Forever - Beta".
3. Remove `ForeverLaunch` and `forever_selection_moved`.
4. Remove the gate, the copy wizard and Sync from beta (`ForeverCopy`, `MainViewModel.ForeverCopy.cs`, the dialog in `MainWindow.xaml.cs`, the header button in `HomePage.xaml`) and `forever_copy_done`.

## Neighbouring repos

- `hoobio/Steward` and `hoobio/HoobiScripts`: `## Interface: 16001` already matches. Confirm in game that the live client still loads `_Camelot`/`[AllowLoadGameType camelot]` TOCs. HoobiScripts' `tools\` scripts default to `_classic_beta_` paths.
- steward-server: `web/src/views/addons-bundle.tsx` tells Mac users to copy into `_classic_beta_`; `WowVersions.DiffProduct` and `tools\DumpRecipeIds.cs` read the beta.
- `hoobio/addons`: nothing keyed on the client.
- RestedXP: `RXPGuides` already lists `16001`.

## Not affected

The RestedXP API contract, the guide strings (bound to the BattleTag, not to a client), the sign-in flow, the Discord session, the MSI and the self-updater.
