# Guides page design

The front end for the RestedXP guides feature: the page, its states, and the sign-in dialog. The API, the token handling and the generated addon write are in [restedxp-guides.md](restedxp-guides.md), which is the source of truth for everything this page talks to.

Rendered mockups of every state: https://claude.ai/artifact/2o3JamD7X4xRvjWRiDRnTL

Fourteen frames at 1100x720 plus a notes frame, in the order below. Foundations, palette, type and surfaces are unchanged from [home-and-settings.md](home-and-settings.md), and the frames carry no colour outside that palette.

## Shell

A third `NavigationView` menu item, `Guides`, after `Addons` and `Sync`, with `Settings` staying on the footer. The item is present only while the RXPGuides addon is installed in at least one WoW install and a RestedXP session exists; otherwise it is collapsed, and leaving either condition while on the page returns the user to Addons. The title bar account chip is the gigagrug identity and does not change; the RestedXP identity is a separate account and lives in the page's account strip, so the two are never confused.

The sign-in entry point is on the Addons page, not here: the RestedXP Guides addon row shows a `Sign in to RestedXP` button in its action area while the addon is installed and there is no session. It opens the sign-in dialog, and a successful sign-in reveals the Guides item and navigates to it once. With that gate, the page's own signed-out state (frame 01) is unreachable and is dropped; the account strip only carries the signed-in treatment.

## Layout

Top to bottom:

1. Page header. `RestedXP Guides` at 26/600, with a `Refresh` button right-aligned on the same row.
2. Account strip. A Surface card holding "Signed in as {email} · {BattleTag}" with the BattleTag in mono, and `Sign out` as a hyperlink button. Signed out, it reads "Not signed in" with an accent `Sign in` button. No avatar.
3. One card per WoW install. Header carries the install title and the path in mono at 11.5px.
4. A `Keep in game` column header above the rows, once per card.
5. One row per owned product: checkbox, product name at 13.5px, "Updated {relative time}" in dim beneath, and a status pill at the right on each kept row.

`productName` from `/user-products` is the row's name, and the relative time comes from that product's entry in `/addon/get-all-timestamps`.

## Several products per install

The frames' checkbox per row stands. The `RXPString` route would have held one product per account, but it is dead on this client (see [restedxp-guides.md](restedxp-guides.md), "Getting the string into the addon"), and the generated-addon route that replaced it hands each string to the importer's paste path, which is additive. Verified in game on 20 Sep 2026 with Forever and Mists together.

So the row control is a `CheckBox`, any number of owned products can be kept per install, and every kept row carries its own pill. Products whose client flavour is not the install's (the "Client gating" section of the same doc) stay listed, in Text mute, with the checkbox disabled and a dim "Not for this client" line in place of the updated time, and a tooltip on the row reading "{product} is for another client. This install is {install}."

Because the addon file is only read at login or `/reload`, there is no client-closed rule any more: the "Waiting for game to close" state is gone, and the written state reads "Written · imports on next login or /reload".

## States

| State | Trigger | Page |
| --- | --- | --- |
| All current | The addon's saved variable marks the string | Success pill `Up to date` per kept row |
| Downloading | A newer timestamp, fetch running | Accent-tinted row, pill `Downloading`, indeterminate `ProgressBar` under the row |
| Written | `Guides.lua` regenerated this session | Success pill `Written · imports on next login or /reload`, clears on the next `/reload` or logout after the import |
| Failure | Download or write failed | Critical-tinted row, pill `Failed`, "Download failed. Retrying in 10 s." and a `Retry` button |
| Rejected | The saved variable carries the current generation and a status text for the string | Critical-tinted row, pill `Failed`, the addon's own status text as the message, `Retry` re-writes the folder |
| Not finished | The saved variable carries the current generation, with neither a mark nor a status for the string | Caution-tinted row, pill `Not finished`, dim "Import did not finish; it runs again on the next login", no `Retry` |
| Session expired | Refresh token gone while the page is open | Caution `InfoBar` above the cards with a `Sign in` action, rows dimmed to mute; dismissing it or cancelling the dialog returns to Addons, since the nav item collapses without a session |
| No purchases | `/user-products` empty | "No guides on this account" with a `Browse guides` link |

The retry wording matches the account page's own guard, which reads "Try again in 10 seconds".

One state is missing from the frames. The import string ends in a minimum addon version and the addon refuses a string below its own, so a Steward-driven RXPGuides update can invalidate a string written earlier. That reads as a caution row, "Needs a newer guide, fetching", not as a failure: Caution tint on the selected row, pill `Needs a newer guide`, dim line "Fetching a build for RXPGuides {installed version}", indeterminate bar under the row as in frame 03. Frame-equivalent: frame 04 with the running dot off and that copy.

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
| Product rows | `ItemsControl` with a `CheckBox` per row, disabled for a product of another client |
| Row progress | `ProgressBar`, indeterminate |
| Status pills | `Border` + `TextBlock` on Chip, semantic foreground |
| Session message | `InfoBar`, page level |
| Sign-in | `ContentDialog` |

## Deliberately absent

- A guide picker or preview. The page keeps guides current; choosing which to follow is the addon's job in game.
- A progress percentage on the download. The string is one file and the page shows an indeterminate bar rather than a fake figure.
- Any storage of the RestedXP password. The dialog exchanges it once and discards it.
- A manual import-string box. The generated addon (route 3 in [restedxp-guides.md](restedxp-guides.md)) is the only path this page uses.
