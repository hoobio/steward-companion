# Addon settings

Addon settings, talent plans and action bar plans are edited on the Steward website (guild.hoobi.io). The app only moves data between the game's SavedVariables files and the website: Export opens the website with the game's current values, and Import writes the values the website sends back, while the game is closed. The website renders the settings pages from a settings schema that each addon build generates in CI, so a new or changed setting needs neither a Steward release nor a website release. This document is the contract between `hoobio/HoobiScripts` (generates the schema), steward-server and `hoobio/addons` (store and serve the schema, host the editor) and this app (reads, validates and writes the files).

## Scope

Every addon folder in `hoobio/HoobiScripts` with a settings page: HoobiScripts core (General, Quests, Items, Social, Mail, Beta) and each sub-addon page (Action Bars, GamerGuides, Sniper, Auctioning, Buff Manager, Journey). The website shows a page only for an addon the export lists as installed. Buttons, help text and commands are not settings; the schema carries help text as `note` rows and leaves buttons out.

Talent plans (`HoobiScriptsDB.talentPlans`) and action bar plans (`HoobiScripts_ActionBarsDB`) are datasets beside the settings in the same export and import, edited in the website's builder (talents) and a new action bar planner. Their formats are in "Plan datasets" below.

## Schema generation

`tools/export-settings-schema.lua` in `hoobio/HoobiScripts` runs under `lua5.1` with a stubbed client API (the same approach as `tools/tests.lua`). It loads each addon's settings code, runs the page builders and records every row in declaration order. It does not change how the addon declares settings.

- `Settings.RegisterAddOnSetting(category, variable, key, variableTbl, kind, name, default)` gives the key, the kind and the default. The harness maps `variableTbl` by identity to its saved-variable path (`HoobiScriptsDB.settings`, `HoobiScriptsDB.chars[<character>]`, `HoobiScripts_ActionBarsDB` and so on).
- A row that an `IsAddOnLoadable` branch adds or removes gets its condition derived: the harness records every addon the code asks about and is not loading, builds the pages again with each one made loadable, and gives a row present only without it `hiddenWhenLoaded` and a row present only with it `visibleWhenLoaded`.
- Closures cannot be exported, so the harness maps each one by function identity: a `normalize` function to a named `rule`, a `locked` predicate to `lockedWhenLoaded`, a `visible` predicate to `visibleWhen`. An unmapped closure fails the CI run, so a new one is never shipped without a mapping.
- One schema file is written per addon folder.

## Schema format

```json
{
  "schemaVersion": 1,
  "addon": "hoobiscripts",
  "folder": "HoobiScripts",
  "version": "0.42.0",
  "pages": [
    {
      "title": "General",
      "sections": [
        {
          "title": "Nameplates",
          "column": 1,
          "rows": [
            { "type": "checkbox", "key": "autoRepair", "label": "Auto repair", "tooltip": "...", "scope": "account",
              "path": ["HoobiScriptsDB", "settings"], "default": true },
            { "type": "text", "key": "reenableEnemyNameplates", "label": "Re-enable enemy nameplates after (seconds)",
              "scope": "account", "path": ["HoobiScriptsDB", "settings"], "default": "",
              "maxLetters": 6, "placeholder": "Off", "rule": { "kind": "integer", "min": 1, "max": 3600, "empty": true } }
          ]
        }
      ]
    }
  ]
}
```

Row fields:

- `type`: `checkbox`, `dropdown` (one value from `options`, each `{ "label", "value" }`), `bitmask` (a number, `options` each `{ "label", "bit" }` where `bit` is 0-based), `text`, `money` (copper, a number), `mailTarget` (a string: empty follows `parent`, `-` is never, else a character name), `note` (`text` only, no key).
- `key`, `label`, `tooltip`, `placeholder`, `maxLetters`.
- `scope`:
  - `account`: in the account file.
  - `character`: in the account file under a character's entry; `path` holds the token `{character}`, for example `["HoobiScriptsDB", "chars", "{character}"]`.
  - `characterFile`: in the character's own file, `WTF\Account\<account>\<realm id>\<First-Last>\SavedVariables\<Folder>.lua` (Journey's `StewardJourneyDB`, a `## SavedVariablesPerCharacter` table).
  - `class`: in the account file, keyed by the selected character's class token; `key` is the token `{class}` (Action Bars' `classEnabled[CLASS]`). With no class known the row is read-only.
- `optionsByClass`: class token to an options list, in place of `options`, for lists that differ by class (spec, stat, role).
- `dynamicOptions`: true when the addon builds the options from the player's own game data (edit mode layouts, attack spells, sounds). Steward shows the stored value read-only.
- `visibleWhenLoaded`: addon folder names. The row shows only while one of them is installed on the install.
- `hiddenWhenLoaded`: addon folder names. The row is hidden while any of them is installed on the install (core's fallback Auto enabled Action Bars row, which the Action Bars page holds when that addon is installed).
- `path`: the saved-variable global followed by the table keys that hold `key`. The global can belong to another addon (the Action Bars page's `actionBars` row is in `HoobiScriptsDB.settings`).
- `default`: the value the addon uses when the key is absent. `defaultByClass` (class token to value) replaces it for a class-seeded key (`vendorPlainWater`, `vendorPlainFood`); with no class known the key is always written explicitly.
- `parent`: the key of a checkbox row. The row is indented, and disabled while that checkbox is off.
- `lockedWhenLoaded`: addon folder names. The row reads off and is disabled while any of them is installed on the install. A name ending in `*` matches by prefix, ignoring case (`Ellesmere*`, since the addon locks on any folder starting with "ellesmere").
- `visibleWhen`: `{ "key", "equals" }`. The row is hidden unless that key's effective value equals `equals`.
- `rule`: the `normalize` function as data: `{ "kind": "integer", "min", "max", "empty" }`, `{ "kind": "characterName" }` (trimmed), `{ "kind": "string", "lower", "empty" }` (`lower` lowercases, `empty: false` refuses an empty value). Steward applies it before it writes, since the game does not validate stored values.
- `keepDefault`: true when the key is never removed at its default, because a load-time migration reads the absent key (`questMobSparkles`, which `Adopt` sets to false while `worldSparkles` is false).

A schema with a `schemaVersion` higher than the app knows, or a row type it does not know, makes the app export those rows as read-only and refuse an import of them ("Update Steward to change this setting"). The website shows them read-only too.

The app refuses a whole schema, exports that addon's rows as unavailable and logs a Warning with the reason, unless `folder` equals the catalogue row's `folder_name` (case-insensitive), every row's `path[0]` matches `^[A-Za-z_][A-Za-z0-9_]*$` and every later path element is a non-empty string. No schema value is part of a file path.

## Publish and serve

The schema is stored per addon version, because the installed version can be behind the channel and a default can change between versions.

- Protected addons (`hoobiscripts*`, `steward-buffmanager`): `operations/scripts/publish-protected.sh` sends the schema as an optional `schema` part of the existing `PUT /api/addons/protected/{id}/{channel}/{version}`, with the same bearer token. steward-server keeps it per id and version and serves it at `GET /api/addons/protected/{id}/settings-schema/{version}.json`, inside the session group with the same `Visible` filter as the manifest, with an `ETag`.
- Public addons (Journey, Auctioning, GamerGuides and any other addon `hoobio/addons` builds): `hoobio/addons`' build runs the harness from the tagged HoobiScripts tree, when it has one, and writes `settings-schema/{version}.json` beside `latest-{channel}.json`.
- The app derives the URL as `{manifest_base_url}settings-schema/{version}.json` for both, fetched with the same client as the manifest (`ManifestNeedsSession`), through the `ManifestCache` revalidation. `{version}` is the installed version from the install record, else the TOC. A 404 leaves that addon out of the export.
- The website fetches the same schema for the version the export names, with the signed-in user's session (protected) or from addon.hoobi.io (public).

## Export and import

There is no server-side copy of anyone's game files. Everything travels in the URL fragment (after `#`), which the browser never sends to a server, so no request log, steward-server or the Static Web App sees it.

- **Payload:** UTF-8 JSON (below), compressed with raw DEFLATE (`System.IO.Compression.DeflateStream`; the website reads it with the browser's `DecompressionStream("deflate-raw")`), then base64url without padding.
- **Export:** the app opens `https://guild.hoobi.io/addon-settings#v1.<payload>` in the default browser through `ShellOpen`. The website decodes the fragment in the page, fetches each schema, and shows the settings pages, talent plans and action bar plans with the exported values. The app refuses an export whose URL is over 32,000 characters ("Too much to export at once; choose one account") rather than truncate it.
- **Import:** the website's "Send to Steward" button opens `steward://import#v1.<payload>`. The app registers the `steward` scheme beside `curseforge` (the MSIX manifest's protocol, the Linux desktop entry's `MimeType=x-scheme-handler/steward;`, macOS `CFBundleURLTypes`), and a launch with that link is handed to the running instance through the existing link pipe or socket, as a CurseForge link is. The website also offers the same link as text to copy, and the app has "Paste from website" for a browser that blocks the scheme.
- **Test vector:** both sides decode this to exactly `{"v":1,"product":"wowf","account":"54939295#1","basedOn":1761550000,"changes":[{"addon":"HoobiScripts","scope":"account","key":"autoRepair","from":false,"to":true}],"newCharacters":[],"plans":{"talents":[],"actionBars":[]}}`: `v1.PY2xasNAEAX_5bndQkoiB22ZNOkCdmlcrE-rWES5Pe5WFkbo34NwSDnD8N6CG7gmpGzdFByM2eYeBAnBpriJ5qV9bp_aZleDcJGi3WcE16_7ummqqqoI4SrxSwv4tEC6ziIYH2aX4RjykLyAUIIlBf_PEr71vvHkdtAkQwahz_YD7mUsSnADe550PROizu9XyRJc83ZzJqRRYgEvcBk1-p-V4IPFN3lU6_oL` (made by .NET's `DeflateStream`). Each side's tests also encode and decode a payload of its own, and decode the other side's checked-in vector.
- **Confirmation:** an import never writes on its own. The app shows what will change (addon, page, row label, old value, new value, and for plans the class and spec) and writes only after the user presses Apply, as a CurseForge link install asks first.

### Export payload

```json
{
  "v": 1,
  "product": "wowf",
  "install": "World of Warcraft: Forever",
  "account": "54939295#1",
  "exportedAt": 1761550000,
  "addons": [{ "id": "hoobiscripts", "folder": "HoobiScripts", "version": "0.0.0-pre-release.15e85ca" }],
  "installed": ["HoobiScripts", "HoobiScripts_ActionBars", "RXPGuides", "EllesmereUI"],
  "realms": [{ "name": "Classic Beta PvP", "id": "91" }],
  "characters": [{ "key": "Hoobi Drood-Classic Beta PvP", "class": "DRUID" }],
  "values": { "HoobiScripts": { "account": { "autoRepair": false }, "character": { "Hoobi Drood-Classic Beta PvP": { "spec": "Feral" } } } },
  "plans": { "talents": {}, "actionBars": {} }
}
```

- One export covers one WoW account folder of one install.
- `values` holds only keys the schema names and only keys present in the file. An absent key is left out, so "absent" (the addon's default) and "set to the default" stay different.
- `installed` is every folder on the install (for `visibleWhenLoaded`, `hiddenWhenLoaded`, `lockedWhenLoaded`).
- `realms` are the known realms (realm pairing below), and `characters` the `HoobiScriptsDB.chars` keys with their class, both with the beta-realm filter applied before export on a `wowf` install.

### Import payload

```json
{
  "v": 1,
  "product": "wowf",
  "account": "54939295#1",
  "basedOn": 1761550000,
  "changes": [
    { "addon": "HoobiScripts", "scope": "account", "key": "autoRepair", "from": false, "to": true },
    { "addon": "HoobiScripts", "scope": "character", "character": "Hoobi Drood-Classic Beta PvP", "key": "spec", "from": "Feral", "to": null }
  ],
  "newCharacters": [{ "first": "Grug", "last": "Aa", "class": "WARRIOR", "realm": "Classic Beta PvP" }],
  "plans": { "talents": [], "actionBars": [] }
}
```

- A change names a schema row by addon folder, scope, key and (for `character` and `characterFile` rows) the character key, never by path. `to: null` means remove the key (back to the addon's default).
- `from` is the value the website saw in the export, `null` for absent.

### Applying an import

- The app applies an import only to the install whose product and account match, only while that install's WoW client is not running (checked before the confirmation and again just before each move), and only through the local schema for the installed version.
- **Trust boundary:** every value comes from the browser and is checked against the local schema before anything is written: the row exists and is not read-only, `to` has the row's type, a `dropdown` value is in its options, a `bitmask` value fits its bits, `text` passes its `rule` and `maxLetters`, `money` is within range. File paths and table paths come only from the local schema and the local realm pairing, never from the payload. A change that fails is listed as refused in the confirmation and is not written; the rest still apply.
- **Conflicts:** a change applies only when the file still holds `from`. When the game has changed that value since the export, the change is listed as "Changed in game since the export" and is not written unless the user ticks it in the confirmation.
- A new character is applied only for a realm the app knows on that account, as in "Accounts and characters".
- After an import, the app offers Export again so the website shows the result.

## Plan datasets

Plans travel in `plans` in both payloads with the same rules as settings: an import change carries `from` (what the export held) and `to`, is validated locally, and is applied only through the confirmation.

### Talent plans

- **In game:** `HoobiScriptsDB.talentPlans[CLASS][group]`, a flat array of C_Traits trait node ids, one entry per point in purchase order (a rank 5 node appears 5 times), at most 51. `group` is the dual-spec group, 1 or 2 (`TalentPlan.lua`). No other fields.
- **On the website:** the builder's talents are `{ plan1, plan2, legacy? }` with the same node ids (`TalentNodes.json`, mapped from definition ids; the Druid ids in a real save file all appear there), the same 51-point cap and 5-per-row gate, and the same `HSTP1` text format. `plan1` is group 1 and `plan2` group 2. `legacy` has no addon equivalent and is not imported.
- **Export:** `plans.talents` is `{ "<CLASS>": { "1": [ids], "2": [ids] } }` for every class in the file. The website offers "Open in builder" per class.
- **Import:** `plans.talents` is a list of `{ "class", "group", "from": [ids] | null, "to": [ids] }`. The app checks: `class` is a class token, `group` is 1 or 2, every id is a positive integer, at most 51 entries. It replaces that one array.

### Action bar plans

- **In game:** `HoobiScripts_ActionBarsDB.plans[CLASS]` (one plan per class) and `HoobiScripts_ActionBarsDB.account` (applied to every class, overriding the class plan). A plan's numeric keys are slots (`slot = button + (page - 1) * 12`; pages: bar 1 is 1, bar 1 page 2 is 2, right 3, left 4, bottom right 5, bottom left 6, bars 6 to 8 are 13 to 15, a stance or form bar is `6 + offset` with offsets Warrior Battle 1, Defensive 2, Berserker 3, Druid Cat 1, Prowl 2, Bear 3, Moonkin 4, Rogue Stealth 1, Priest Shadowform 1). A slot holds one of `{ spell, fixed? }` (a rank spell id; without `fixed` the addon moves it to the highest known rank), `{ macro }` (a name defined in the plan's `macros`), `{ item }`, `{ profession }` (1 and 2 the two professions, 3 First Aid, 5 Cooking) or `{ list = { entry, ... } }` (fallbacks, the first that resolves is placed). `macros[name]` is `{ perChar, icon?, variants = { { level, body, icon?, spells? } } }`, variant 1 at level 1, `body` at most 255 characters, `spells` spell names.
- Never written by the app: `chars` (apply bookkeeping), `stanceNames`, `classEnabled` and the addon-wide options (`clearUnplanned`, `blockAutoPush`, `applyAccount`, `formsMacro`, which are settings rows), and the generated macro names `PowerShift`, `HoobiTargets`, `HoobiWater`, `HoobiFood`, `HoobiBandage`, `HoobiHealthPotion` and `HoobiManaPotion` in `macros` (a slot may still name them).
- **Export:** `plans.actionBars` is `{ "classes": { "<CLASS>": <plan> }, "account": <plan> }` as JSON, slots as string keys.
- **Import:** `plans.actionBars` is a list of `{ "target": "<CLASS>" | "account", "from": <plan> | null, "to": <plan> }`. The app checks every slot key is an integer from 1 to 180, every entry has exactly one of the five kinds with the right types, every macro a slot names is defined in `to.macros` or is a generated name, no generated name is defined, bodies are at most 255 characters and every variant has a positive integer level. It replaces that one plan table.
- The client writes slots 1 to n as positional array items with `nil` gaps; the reader handles both forms and the writer writes `[n] =` keys.

### Spell data for the action bar planner

The website needs each class's spells, ranks and icons. `tools/DumpClassSpells.cs` in `hoobio/HoobiScripts` (which writes `ClassSpells.lua` from the install) also writes `web/src/lib/class-spells.json` in steward-server, the way `tools/ExtractLegacyTalents.cs` writes `legacy-talents.json`. Icons and tooltips come from the existing `/api/wowhead/icons` route and `power.js`, as the builder's talents do.

## Reading values

The value of a row is, in order: the value in the saved-variable file at `path`/`key`, else `defaultByClass` for the character's class, else `default`.

The file of a row comes from `path[0]`, not from the schema's `folder`. Steward reads the top-level TOC (`LocalAddons.TopLevelToc`) of each installed HoobiScripts-built addon on the install. The folder whose `## SavedVariables` lists the global owns it for `account`, `character` and `class` rows, and the file is `WTF\Account\<account>\SavedVariables\<Folder>.lua` under the flavour folder. The folder whose `## SavedVariablesPerCharacter` lists it owns it for `characterFile` rows. A row whose global no installed folder declares in the matching directive, or that two folders declare, is read-only with "No installed addon keeps this setting".

Steward never creates a SavedVariables file. A row whose file does not exist yet is read-only with "Log in once with <addon title> enabled before changing these settings", so the addon's own first-run setup (HoobiScripts' import offer) still runs.

On a `wowf` install, `HoobiScriptsDB.chars` keys on the beta realm (`ForeverLaunch.BetaRealm`, "Classic Beta PvP", case-insensitive), which the Forever copy wizard brings over, are left out of the character picker and of realm pairing. They stay in the file.

## Writing values

- Steward never writes while the install's WoW client is running (`WowClient.IsRunning`), checked before the import confirmation and again just before the move. The client rewrites the whole file at logout and `/reload`, so a write while it runs is lost.
- The file is parsed with `LuaSavedVariables.Parse`, the schema keys are changed in the parsed tree, and the tree is written back with `LuaWriter.Serialize`, temp file then move. Every key the schema does not name is kept exactly as parsed. A round-trip test over a real `HoobiScripts.lua` (numbers, escapes, empty tables, integer keys) guards this.
- Each file with a changed row is written once. A value equal to its default is removed from the table, as the addon does at logout, unless the row has `keepDefault`. A table left empty by a removal is kept.
- When Steward writes a file, it also writes every `keepDefault` row of that file whose key is absent, with its effective value, so a load-time migration (`Adopt` and `questMobSparkles`) cannot change it.
- A `text` value goes through its `rule` first; a value the rule refuses is not saved and the row shows why. The `string` rule trims the value, then refuses an empty one when `empty` is false, then lowercases it when `lower` is set. A `money` value is at most 9,999,999 gold.

## App UI

The app has no settings editor. A card group on the Settings page, "Addon settings and plans", shown while the `hoobiscripts` catalogue row is visible and HoobiScripts is installed on the selected install, holds:

- An account picker (the `WTF\Account` folders of the selected install, labelled with up to three character names as the Forever copy wizard does) and "Edit on the website", which exports that account.
- "Plan talents" and "Plan action bars", which open the website's builder and action bar planner with the same export.
- "Paste from website", which imports a copied link.

An import link arriving while the app runs (or starting it) shows the confirmation dialog over whatever page is open.

## Accounts and characters

- The characters are the keys of `HoobiScriptsDB.chars` in the account's file, exported as `First Last-Realm` with their class.
- A realm is known when Steward can pair its folder id with its name: a character folder `<id>\First-Last` under the account whose name, with the hyphen read as a space, matches a `chars` key `First Last-<Realm>` gives `<id>` for `<Realm>`. The realm folder id (`91`) cannot be derived any other way; the realm ids in character guids (`4613`) differ from it.
- The website's "New character" is enabled only while the export lists at least one known realm, and otherwise disabled with "Log in to a character on this account once first". It asks for a first name, a last name, a class and one of the exported realms. On import the app creates the folder `<id>\First-Last` and writes `HoobiScriptsDB.chars["First Last-<Realm>"]` with `class` and the changed values. Each name part has its first letter capitalised and the rest kept as typed (a name can carry inner capitals), and holds letters and apostrophes only: a hyphen is refused, since every `chars` key is split at its first hyphen into name and realm.
- A pre-written entry stops HoobiScripts' `CarryOverSettings` for that character, which copies a same-named character's settings from another realm. The website's New character form says so.

## Privacy

The export and import payloads hold WoW account folder names, character names, realm names and setting values (mail targets name characters). They stay in the URL fragment and the user's browser and are never sent to steward-server. `PRIVACY.md` says so.
