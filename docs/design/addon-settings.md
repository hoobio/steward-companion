# Addon settings

The Addon settings page (nav tag `addonsettings`, shown while the `hoobiscripts` catalogue row is visible and HoobiScripts is installed on the selected install) shows each addon's in-game settings page and edits the SavedVariables files while the game is closed. The pages come from a settings schema that each addon build generates in CI, so a new or changed setting needs no Steward release. This document is the contract between `hoobio/HoobiScripts` (generates the schema), steward-server and `hoobio/addons` (store and serve it) and this app (renders it and writes the values).

## Scope

Every addon folder in `hoobio/HoobiScripts` with a settings page: HoobiScripts core (General, Quests, Items, Social, Mail, Beta) and each sub-addon page (Action Bars, GamerGuides, Profession Leveling, Sniper, Auctioning, Buff Manager, Journey). A page shows only when its addon is installed on the selected WoW install (the row's installed state, as `HasRestedXp` reads it) and its catalogue row is visible to the user. Buttons, help text and commands are not settings; the schema carries help text as `note` rows and leaves buttons out.

## Schema generation

`tools/export-settings-schema.lua` in `hoobio/HoobiScripts` runs under `lua5.1` with a stubbed client API (the same approach as `tools/tests.lua`). It loads each addon's settings code, runs the page builders and records every row in declaration order. It does not change how the addon declares settings.

- `Settings.RegisterAddOnSetting(category, variable, key, variableTbl, kind, name, default)` gives the key, the kind and the default. The harness maps `variableTbl` by identity to its saved-variable path (`HoobiScriptsDB.settings`, `HoobiScriptsDB.chars[<character>]`, `HoobiScripts_ActionBarsDB` and so on).
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

A schema with a `schemaVersion` higher than Steward knows, or a row type it does not know, shows that page or row read-only with "Update Steward to change this setting".

Steward refuses a whole schema, shows "Settings are not available for this version" and logs a Warning with the reason, unless `folder` equals the catalogue row's `folder_name` (case-insensitive), every row's `path[0]` matches `^[A-Za-z_][A-Za-z0-9_]*$` and every later path element is a non-empty string. No schema value is part of a file path.

## Publish and serve

The schema is stored per addon version, because the installed version can be behind the channel and a default can change between versions.

- Protected addons (`hoobiscripts*`, `steward-buffmanager`): `operations/scripts/publish-protected.sh` sends the schema as an optional `schema` part of the existing `PUT /api/addons/protected/{id}/{channel}/{version}`, with the same bearer token. steward-server keeps it per id and version and serves it at `GET /api/addons/protected/{id}/settings-schema/{version}.json`, inside the session group with the same `Visible` filter as the manifest, with an `ETag`.
- Public addons (Journey, Auctioning, GamerGuides and any other addon `hoobio/addons` builds): `hoobio/addons`' build runs the harness from the tagged HoobiScripts tree, when it has one, and writes `settings-schema/{version}.json` beside `latest-{channel}.json`.
- The app derives the URL as `{manifest_base_url}settings-schema/{version}.json` for both, fetched with the same client as the manifest (`ManifestNeedsSession`), through the `ManifestCache` revalidation. `{version}` is the installed version from the install record, else the TOC. A 404 shows the page as "Settings are not available for this version".

## Reading values

The value of a row is, in order: the value in the saved-variable file at `path`/`key`, else `defaultByClass` for the character's class, else `default`.

The file of a row comes from `path[0]`, not from the schema's `folder`. Steward reads the top-level TOC (`LocalAddons.TopLevelToc`) of each installed HoobiScripts-built addon on the install. The folder whose `## SavedVariables` lists the global owns it for `account`, `character` and `class` rows, and the file is `WTF\Account\<account>\SavedVariables\<Folder>.lua` under the flavour folder. The folder whose `## SavedVariablesPerCharacter` lists it owns it for `characterFile` rows. A row whose global no installed folder declares in the matching directive, or that two folders declare, is read-only with "No installed addon keeps this setting".

Steward never creates a SavedVariables file. A row whose file does not exist yet is read-only with "Log in once with <addon title> enabled before changing these settings", so the addon's own first-run setup (HoobiScripts' import offer) still runs.

On a `wowf` install, `HoobiScriptsDB.chars` keys on the beta realm (`ForeverLaunch.BetaRealm`, "Classic Beta PvP", case-insensitive), which the Forever copy wizard brings over, are left out of the character picker and of realm pairing. They stay in the file.

## Writing values

- Steward never writes while the install's WoW client is running (`WowClient.IsRunning`), checked when the page opens, when Save is pressed and again just before the move. The client rewrites the whole file at logout and `/reload`, so a write while it runs is lost.
- The file is parsed with `LuaSavedVariables.Parse`, the schema keys are changed in the parsed tree, and the tree is written back with `LuaWriter.Serialize`, temp file then move. Every key the schema does not name is kept exactly as parsed. A round-trip test over a real `HoobiScripts.lua` (numbers, escapes, empty tables, integer keys) guards this.
- Each file with a changed row is written once. A value equal to its default is removed from the table, as the addon does at logout, unless the row has `keepDefault`. A table left empty by a removal is kept.
- When Steward writes a file, it also writes every `keepDefault` row of that file whose key is absent, with its effective value, so a load-time migration (`Adopt` and `questMobSparkles`) cannot change it.
- A `text` value goes through its `rule` first; a value the rule refuses is not saved and the row shows why. The `string` rule trims the value, then refuses an empty one when `empty` is false, then lowercases it when `lower` is set. A `money` value is at most 9,999,999 gold.

## Unsaved changes

While the page has unsaved changes, a change of account, character or install, and New character, first ask "Save your changes?" (Save, Discard, Cancel). Cancel keeps the old selection. Leaving the page and coming back keeps the changed rows. When the install's WoW client stops, Steward reads the files again: with unsaved changes it asks first, with "Reload from the game" and "Keep my edits", since the game wrote newer values when it closed.

## Accounts and characters

The page has an account picker (the `WTF\Account` folders of the selected install, labelled with up to three character names as the Forever copy wizard does) and a character picker for `character` rows.

- The characters are the keys of `HoobiScriptsDB.chars` in that account's file, shown as `First Last (Realm)`.
- A realm is known when Steward can pair its folder id with its name: a character folder `<id>\First-Last` under the account whose name, with the hyphen read as a space, matches a `chars` key `First Last-<Realm>` gives `<id>` for `<Realm>`. The realm folder id (`91`) cannot be derived any other way; the realm ids in character guids (`4613`) differ from it.
- "New character" is enabled only while the account has at least one known realm, and otherwise disabled with "Log in to a character on this account once first". It asks for a first name, a last name, a class and one of the known realms (the class picks `defaultByClass`, `optionsByClass` and `class` rows, and is written as the entry's `class`, which `CarryOverSettings` also reads), creates the folder `<id>\First-Last` (the first letter of each name is capitalised and the rest kept as typed, since a name can carry inner capitals; letters, apostrophes and hyphens only) and writes `HoobiScriptsDB.chars["First Last-<Realm>"]` in the account file with the values the user changed.
- A pre-written entry stops HoobiScripts' `CarryOverSettings` for that character, which copies a same-named character's settings from another realm. The New character dialog says so.
