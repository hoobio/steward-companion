# Privacy Policy

**Steward**

*Last updated: 26 September 2026*

## Data Collection

Steward does not collect, store, or transmit any personal data or telemetry. There is no analytics endpoint, crash reporter, or "phone home" of any kind.

## How It Works

Steward runs as a local desktop application that manages a set of World of Warcraft addons and syncs guild data between the game and gigagrug, the guild's own Discord bot and admin API:

- It signs you in through Discord via gigagrug's own desktop OAuth flow, opening your default browser.
- It reads and writes files inside your World of Warcraft installation folders (addon files, SavedVariables) to install, update, and sync addons.
- It downloads addon releases from `addon.hoobi.io` (Steward, HoobiScripts), `api.hoobi.io` (gigagrug's mirror of RestedXP's release manifest), `github.com` (the RestedXP zip itself), and (for RestedXP guides, if you sign in to that service) `rxpgcr.restedxp.com`.
- It reads addon owner avatars from `github.com`.
- It checks the Microsoft Store's own update service for its own updates.

## Local Files

| What | Where |
|------|-------|
| App state, session tokens (DPAPI-protected) | `%LocalAppData%\Steward\state.json` |
| RestedXP guide cache | `%LocalAppData%\Steward\guides\` |
| Switch-to-Store log (for an installed MSI still finding its way to the Store) | `%LocalAppData%\Steward\update.log` |

The gigagrug session token and the RestedXP session are both encrypted with Windows DPAPI, so they can only be decrypted on the machine and under the Windows account that created them.

## Network Access

Steward talks to:

- `api.hoobi.io` (gigagrug): Discord sign-in, reading your guild's roster, Discord member list, and admin role, and RestedXP's release manifest and icon.
- `addon.hoobi.io`: addon release manifests and zips for Steward and HoobiScripts.
- `github.com`: the RestedXP zip and addon owner avatar images shown in the app.
- `rxpgcr.restedxp.com` and RestedXP's Keycloak realm: only if you sign in to RestedXP for levelling guides, to download guide content you have purchased.
- The Microsoft Store's own update service, to check for and install app updates.

None of these requests carry telemetry beyond what the Microsoft Store itself collects for a Store-installed app under Microsoft's own privacy terms; they are the addon-update and guild-sync features working as designed. Steward itself makes no other outbound requests and has no analytics, crash reporting, or telemetry of its own.

## Third-Party Services

Discord (via gigagrug's OAuth flow), GitHub, RestedXP, and Microsoft, each only for the purpose described above.

## Contact

If you have questions about this privacy policy, please open an issue at [github.com/hoobio/steward-companion](https://github.com/hoobio/steward-companion/issues).
