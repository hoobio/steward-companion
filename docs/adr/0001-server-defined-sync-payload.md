# 0001 The server defines the Steward sync payload

Date: 2026-11-20

Status: Accepted

## Context

The app built `StewardSync.lua` itself. It called several steward-server routes (the officer roster and Discord members, the admin recipe catalogue, the member roster, professions and catalogue, and gear sets), applied its own feature gates and wrote each key with typed code in `StewardSyncFile`. A new key in the file needed a change to that code and a new app release. The app ships through the Microsoft Store, so a release takes certification time. steward-server's Mac addon bundle had a port of the same code, and the two ports drifted.

steward-server now serves the whole payload as one JSON document. The server decision is steward-server's `docs/adr/0011-server-defined-sync-payload.md`.

## Decision

- The app calls `GET /api/addons/sync/{guild_id}`, or `GET /api/addons/sync` with no guild, and nothing else for the file. There is no fallback to the old routes.
- A generic converter turns the JSON payload into Lua. It knows no key names. The rules are in `docs/design/roster-sync.md`, under Converter rules.
- The app owns only the overlay (`writtenAt`, `fingerprint`, `avatar` and the `avatar` key on `discord[]` and `directory.people[]` tables), the images and the sign-out transform.
- A document the converter rejects, a 404 or a 502 keeps the file on disk and writes nothing. A partial or wrong document must not overwrite good data.
- The pull runs on the existing triggers and on a `syncChanged` frame on `/api/events`, with the 15 minute fallback. It is never polled on the one-minute pass.
- The golden file the typed renderer wrote from steward-server's fixture inputs is kept in `tests/Fixtures`, and the converter is tested against it as Lua data.

## Consequences

- A new, removed or changed payload key is a server deploy only. This is meant to be the last app release for payload changes.
- A change to the overlay, the images, the directory keys the app reads for its own screens, the document `version` or a new `signOut` value still needs an app release.
- Two differences from the typed renderer are by design: an object member whose value is `null` is left out instead of written as `= nil`, and a gear set's `items` is written as positional entries with `nil` gaps instead of explicit integer keys. Both give the same Lua data.
- A string-keyed table whose only key is `$int` cannot be told from the integer-key wrapper.
- The typed request and response records, `RosterNotes` and the per-dataset client methods are deleted.
