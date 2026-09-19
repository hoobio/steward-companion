# Steward

The desktop half of Steward, a guild toolkit for World of Warcraft. It signs in with Discord, finds your WoW installs, and keeps the Steward addon current. In time it carries guild roster, loot history and attendance between the game and the guild admin panel, and combines what several officers record into one view.

It manages a set of addons rather than one, so the other addons published alongside it ride along. It is not a general-purpose addon manager, and it manages World of Warcraft: Forever installs only.

Steward checks each managed addon's release channel when it starts and every 15 minutes while the window is open. Steward applies the updates it finds, whether or not the game client is running. An update applied while the client is running takes effect after `/reload` in game, and the row says so until the client stops.

## Install

Download the latest MSI from the [Releases](https://github.com/hoobio/steward-companion/releases) page and run it. It installs per-user (no admin prompt) to `%LocalAppData%\Steward`, with a Start Menu shortcut.

## Sign-in

Steward opens your default browser for the Discord sign-in and stores the session locally, so you stay signed in between runs.
