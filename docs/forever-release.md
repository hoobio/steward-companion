# Forever release checklist

What changes in this repo and its neighbours when World of Warcraft: Forever ships its live client alongside, or instead of, the beta. Every entry is keyed on the beta today because the live client's product code, folder name and interface number are not published yet. The checklist is ordered so a single pre-release build can be tested at each step.

## Find the three values

Install the live client and read them off the disk, do not guess them:

| Value | Where | Beta value |
| --- | --- | --- |
| Product code | `<install>\<flavour folder>\.flavor.info`, the line after `[Flavor]` (`WowInstalls` reads this) | `wow_classic_beta` |
| Flavour folder | the folder under the WoW root holding `.flavor.info`, `Interface\` and `WTF\` | `_classic_beta_` |
| Interface number | `## Interface:` in the live RXPGuides TOC once RestedXP ship a build for it, or `/dump select(4, GetBuildInfo())` in game | `16001` |
| Client executable | the process name under the flavour folder while the game runs | `WowB.exe` |

The flavour folder needs no code change: discovery walks `.flavor.info`, never a folder name. The executable needs none either: `WowClient.IsRunning` matches any `Wow*` process whose main module sits under the flavour folder.

## This repo

1. `src/Steward.App/appsettings.json`, `SupportedProducts`: add `"<product code>": "World of Warcraft: Forever"` beside the beta entry. Without it the install is not discovered at all. Keep the beta entry until the beta client is retired.
2. `src/Steward.App/appsettings.json`, `RestedXp:ProductPrefixes`: add `"<product code>": [ "Forever" ]`. Without it the Guides page lists every product as "Not for this client" on the live install and writes nothing.
3. Generated `StewardGuides` addon: no change. Its TOC copies the `## Interface:` value from the installed RXPGuides TOC at write time, so it follows whatever RestedXP ship for the live client.
4. `docs/design/home-and-settings.md` and the preview scenarios in `GuidesPreview`: the sample install title reads "World of Warcraft: Forever - Beta"; update the copy when the beta entry goes.
5. `AGENTS.md`, "WoW install discovery": replace the sentence that says the release entry joins once its code is known.
6. Test: run a pre-release build on a machine with both clients installed; the Addons page shows two install cards, the Guides page allows the Forever products on both, and the beta card disappears when the beta entry is removed.

## Neighbouring repos

- `hoobio/Steward` and `hoobio/HoobiScripts`: `## Interface:` in each TOC must include the live number or the client marks the addon out of date; both repos' `AGENTS.md` carry their release mechanics. Ship those before the companion's config change, so the auto-installed Steward addon is not flagged on the first live login.
- `hoobio/addons`: nothing keyed on the client; the manifests are per channel, not per client.
- RestedXP: `RXPGuides` must list the live interface number in its own TOC (the beta's `16001` sits under `[AllowLoadGameType camelot]` today). Until it does, the generated addon inherits the beta number and the client may flag both as out of date; "Load out of date AddOns" in the addon list is the workaround.

## Not affected

The RestedXP API contract, the guide strings (bound to the BattleTag, not to a client), the sign-in flow, the Discord session, the MSI and the self-updater.
