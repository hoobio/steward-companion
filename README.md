# Steward

A small Windows desktop app that keeps Hoobi's WoW addons up to date. It signs in against gigagrug, finds your WoW installs, and shows the installed and available version of each managed addon so you can update it in one click.

It's built for Hoobi's own addons and guild tooling, not a general-purpose addon manager.

## Install

Download the latest MSI from the [Releases](https://github.com/hoobio/steward-companion/releases) page and run it. It installs per-user (no admin prompt) to `%LocalAppData%\Steward`, with a Start Menu shortcut. Requires the [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/), which ships with Windows 11 and most Windows 10 installs already.
