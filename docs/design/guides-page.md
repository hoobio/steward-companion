# Guides page design

The front end for the RestedXP guides feature: the page, its states, and the sign-in dialog. The API, the token handling and the `RXPString` write are in [restedxp-guides.md](restedxp-guides.md), which is the source of truth for everything this page talks to.

Rendered mockups of every state: https://claude.ai/artifact/2o3JamD7X4xRvjWRiDRnTL

Fourteen frames at 1100x720 plus a notes frame, in the order below. Foundations, palette, type and surfaces are unchanged from [home-and-settings.md](home-and-settings.md), and the frames carry no colour outside that palette.

## Shell

A third `NavigationView` menu item, `Guides`, after `Addons` and `Sync`, with `Settings` staying on the footer. The title bar account chip is the gigagrug identity and does not change; the RestedXP identity is a separate account and lives in the page's account strip, so the two are never confused.

## Layout

Top to bottom:

1. Page header. `Guides` at 26/600, with a `Refresh` button right-aligned on the same row.
2. Account strip. A Surface card holding "Signed in as {email} · {BattleTag}" with the BattleTag in mono, and `Sign out` as a hyperlink button. Signed out, it reads "Not signed in" with an accent `Sign in` button. No avatar.
3. One card per WoW install. Header carries the install title, the path in mono at 11.5px, and a caution `Running` dot and label when a client is running from that folder.
4. A `Keep in game` column header above the rows, once per card.
5. One row per owned product: checkbox, product name at 13.5px, "Updated {relative time}" in dim beneath, and a status pill at the right.

`productName` from `/user-products` is the row's name, and the relative time comes from that product's entry in `/addon/get-all-timestamps`.

## Multi-select, unverified

The frames assume several products can be kept in game at once, so the row control is a checkbox and each product carries its own state. **Verify this before building against it.**

`RXPString` is a single account-level SavedVariable, read once in `LoadCachedGuides`, and a valid import wipes `addon.guides`, `guideList`, `guideIds`, `guideCache` and `db.profile.guides`. On that reading, writing product B's string discards product A's guides. Against it: a string's header is `n|hash:payload` where `n` is a guide count, and the download endpoint takes a `bundleIndex`, so one string already carries several guides.

The check: keep one product, log in, keep a second, log in again, and see whether both are still in the addon's guide menu. If only the last survives, the checkbox becomes a radio group, the page keeps one product per install, and the copy changes with it. Report rather than guessing.

## States

| State | Trigger | Page |
| --- | --- | --- |
| Signed out | No RestedXP session | Account strip shows `Sign in`. Install cards stay, rows replaced by "Sign in to see your guides" |
| All current | Every kept product matches its published timestamp | Success pill `In game` per kept row |
| Downloading | A newer timestamp, fetch running | Accent-tinted row, pill `Downloading`, indeterminate `ProgressBar` under the row |
| Waiting | String downloaded, client running | Caution-tinted row, pill `Waiting for game to close`, running dot lit on the card header |
| Written | String written to `RXPString` this session | Success pill `Written · imports on next login` |
| Failure | Download or write failed | Critical-tinted row, pill `Failed`, "Download failed. Retrying in 10 s." and a `Retry` button |
| Session expired | Refresh token gone | Caution `InfoBar` above the cards with a `Sign in` action, rows dimmed to mute |
| No purchases | `/user-products` empty | "No guides on this account" with a `Browse guides` link |

The retry wording matches the account page's own guard, which reads "Try again in 10 seconds".

One state is missing from the frames and needs adding. The import string ends in a minimum addon version and the addon refuses a string below its own, so a Steward-driven RXPGuides update can invalidate a string written earlier. That reads as a caution row, "Needs a newer guide, fetching", not as a failure.

## Sign-in dialog

A `ContentDialog` titled `Sign in to RestedXP`, no other branding. Step two appears only when the account backend answers `mfaRequired`.

**Step one.** One line, "Your password is used once and never stored.", then `Username or email` and `Password`, primary `Sign in`, secondary `Cancel`.

**Step two.** The credentials collapse to one dim line showing the email. An `Authenticator code` box, centred in the dialog with its label, six digits in two blocks of three, mono. Primary `Verify`, secondary `Back`.

Failures are inline and critical, under the fields: "Wrong username or password" and "That code was not accepted". Busy disables the fields and puts a ring in the primary button, which keeps its label; the secondary button stays live so the dialog is never a trap.

## Control mapping

| Element | Control |
| --- | --- |
| Page slot | `NavigationView` menu item, third |
| Account strip | `Border` on Surface at 8px, `HyperlinkButton` for Sign out |
| Install card | `Expander`, expanded by default, matching the Addons page |
| Product rows | `ItemsControl` with a `CheckBox` per row |
| Row progress | `ProgressBar`, indeterminate |
| Running indicator | `Ellipse` at 8px in Caution, with the word `Running` beside it |
| Status pills | `Border` + `TextBlock` on Chip, semantic foreground |
| Session message | `InfoBar`, page level |
| Sign-in | `ContentDialog` |

## Deliberately absent

- A guide picker or preview. The page keeps guides current; choosing which to follow is the addon's job in game.
- A progress percentage on the download. The string is one file and the page shows an indeterminate bar rather than a fake figure.
- Any storage of the RestedXP password. The dialog exchanges it once and discards it.
- A manual import-string box. Route 2 in [restedxp-guides.md](restedxp-guides.md) is the only path this page uses.
