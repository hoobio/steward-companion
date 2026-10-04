# Upcoming features

## Bug report to Discord

A Report bug action that copies a prefilled report (app version, build, selected install, a pointer to the day's log) to the clipboard and opens the owner's Discord profile, where the user clicks Message and pastes.

- `discord://-/users/<id>` opens that user's profile in the desktop client with a Message button, verified 29 Sep 2026 against the owner's own id and another user's.
- `https://discord.com/users/<id>` is the fallback when the desktop client is not installed; it opens the same profile in the browser.
- Discord has no URL parameter that prefills a message, so the clipboard carries the text.
- A DM link (`discord://-/channels/@me/<channelId>`) needs the DM channel id between two specific people and only opens for them, so it cannot be baked into the app. Whether a user id in that slot opens a DM is untested.
- Alternative: a steward-server route (`POST /api/bug-report`) that has the bot DM the owner with the report and log attached, using the session the app already holds, so the user pastes nothing.
