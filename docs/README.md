# Docs

`AGENTS.md` at the repo root is the index, build, delivery and rules. The files here hold the detail.

## Layout

- `design/`: contracts and designs of built features, in more detail than `AGENTS.md` gives.
- `upcoming.md`: plans for work that is not built. When part of a plan ships, that part is removed from the plan, and the facts that stay true move to `design/`.
- `forever-release.md`: the Forever release checklist.
- `screenshots/`: the Store listing screenshots, also shown in the root `README.md`.

When you add, move or empty a doc, update its line below in the same change.

## Index

| Doc | Content |
|---|---|
| [design/avalonia-shell.md](design/avalonia-shell.md) | Avalonia app, MSIX packaging, the custom shell, resources, window on Linux and macOS, tray, flyouts |
| [design/avalonia-text-and-input.md](design/avalonia-text-and-input.md) | Bundled fonts, glyphs, scrolling, text centring, headless tests, branding |
| [design/managed-addons.md](design/managed-addons.md) | Managed addon list, features, parents, auto-install, requirements, ignored updates |
| [design/addon-sources.md](design/addon-sources.md) | Manifests, CurseForge and TukUI sources, the Local scan, Get addons, release channels |
| [design/desktop-auth.md](design/desktop-auth.md) | Default-browser sign-in over a loopback callback, `/api/me`, features, access loss |
| [design/wow-install-discovery.md](design/wow-install-discovery.md) | WoW install and running client discovery |
| [design/self-update.md](design/self-update.md) | Store, MSI and package update paths |
| [design/microsoft-store.md](design/microsoft-store.md) | Store package, build and submit jobs, single instance, WinRT calls |
| [design/dev-install.md](design/dev-install.md) | Side-by-side development package built on push |
| [design/releases.md](design/releases.md) | Build, Desktop Preview and release workflows, the Linux package feed |
| [design/diagnostics.md](design/diagnostics.md) | Memory trimming and diagnostic logging |
| [design/ui-verification.md](design/ui-verification.md) | Driving and capturing the app, `--screenshot` |
| [design/ui-design.md](design/ui-design.md) | Design system rules, navigation, pointers to the page designs |
| [design/roster-and-character-sync.md](design/roster-and-character-sync.md) | Roster pull and character push as the app implements them |
| [design/saved-variables.md](design/saved-variables.md) | SavedVariables reader, `StewardSyncFile` and freshness |
| [design/restedxp-client.md](design/restedxp-client.md) | RestedXP sign-in, guide import and the `StewardGuides` addon in the app |
| [design/banners-and-products.md](design/banners-and-products.md) | Server-driven banners and product overrides |
| [design/addon-settings-client.md](design/addon-settings-client.md) | Addon settings page in the app |
| [design/tray-and-update-checks.md](design/tray-and-update-checks.md) | Tray icon, close and minimise to tray, the 1-minute check |
| [design/addon-manager.md](design/addon-manager.md) | Addon manager (Addons page) design |
| [design/addon-settings.md](design/addon-settings.md) | Addon settings contract |
| [design/character-sync.md](design/character-sync.md) | Character sync contract |
| [design/curseforge.md](design/curseforge.md) | CurseForge integration design |
| [design/guides-page.md](design/guides-page.md) | Guides page design |
| [design/home-and-settings.md](design/home-and-settings.md) | Home and settings design |
| [design/restedxp-guides.md](design/restedxp-guides.md) | RestedXP paid guide API contract |
| [design/roster-sync.md](design/roster-sync.md) | Roster sync contract between steward-server, the app and the addon |
| [design/sync.md](design/sync.md) | Sync page design |
