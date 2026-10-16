# Steward

The desktop half of Steward, a guild toolkit for World of Warcraft. It signs in with Discord, finds your WoW installs, and keeps the Steward addon current. In time it carries guild roster, loot history and attendance between the game and the guild admin panel, and combines what several officers record into one view.

It manages a set of addons rather than one, so the other addons published alongside it ride along. It is not a general-purpose addon manager, and it manages World of Warcraft: Forever installs only.

Steward checks each managed addon's release channel when it starts and every minute while it runs. By default Steward applies the updates it finds while the game client is not running; the Auto-update addons setting changes that to always or never. An update applied while the client is running takes effect after `/reload` in game, and the row says so until the client stops.

## Install

On Windows, install Steward from the [Microsoft Store](https://apps.microsoft.com/detail/9PBKMZFKZHKX), or from the MSI attached to each [GitHub release](https://github.com/hoobio/steward-companion/releases/latest). An MSI install updates itself.

## macOS (preview)

Download the dmg from the newest `desktop-preview-*` pre-release on the [releases page](https://github.com/hoobio/steward-companion/releases). The build is unsigned and not notarised, so clear the quarantine flag after copying Steward to Applications: `xattr -dr com.apple.quarantine /Applications/Steward.app`.

## Linux (preview)

Preview builds of Steward for Linux come from a signed package feed at `https://hoobio.github.io/steward-companion/`, for x64 and arm64. The feed holds the 3 newest previews. They are for testing and are not for general use.

The packages and the feed metadata are signed with the Steward Linux packages key, fingerprint `498B 931B 1F0F CDED 1174 620E E6ED 1058 10B4 1C42`.

WoW itself runs through Wine, Lutris or Proton. Steward is a native Linux app and does not need Wine.

Debian and Ubuntu:

1. Download the key: `sudo install -d -m 0755 /etc/apt/keyrings && sudo curl -fsSLo /etc/apt/keyrings/steward.gpg https://hoobio.github.io/steward-companion/steward.gpg`
2. Check the key against the fingerprint above. The command prints "Steward key OK", or removes the key on a mismatch: `gpg --show-keys --with-colons /etc/apt/keyrings/steward.gpg | awk -F: '/^fpr:/ {print $10; exit}' | grep -qx '498B931B1F0FCDED1174620EE6ED105810B41C42' && echo "Steward key OK" || { echo "Steward key does NOT match: removed it, stop here"; sudo rm -f /etc/apt/keyrings/steward.gpg; }`
3. Add the feed: `echo "deb [signed-by=/etc/apt/keyrings/steward.gpg] https://hoobio.github.io/steward-companion/apt preview main" | sudo tee /etc/apt/sources.list.d/steward.list`
4. Update the package lists: `sudo apt update`
5. Install Steward: `sudo apt install steward`

Fedora and RHEL:

1. Download the key and check it against the fingerprint above. The command prints "Steward key OK", or removes the key on a mismatch: `curl -fsSLo /tmp/steward.asc https://hoobio.github.io/steward-companion/steward.asc && gpg --show-keys --with-colons /tmp/steward.asc | awk -F: '/^fpr:/ {print $10; exit}' | grep -qx '498B931B1F0FCDED1174620EE6ED105810B41C42' && echo "Steward key OK" || { echo "Steward key does NOT match: removed it, stop here"; rm -f /tmp/steward.asc; }`
2. Import the key: `sudo rpm --import /tmp/steward.asc`
3. Add the feed: `sudo dnf config-manager addrepo --from-repofile=https://hoobio.github.io/steward-companion/steward.repo`. On dnf 4 (RHEL 9 and earlier Fedora), use `sudo dnf config-manager --add-repo https://hoobio.github.io/steward-companion/steward.repo`.
4. Install Steward: `sudo dnf install steward`. On dnf 5, the first install asks to import the Steward key again to check the feed itself; accept it when the fingerprint matches the one above.

New previews arrive with your system updates, through `sudo apt upgrade` or `sudo dnf upgrade`. Start Steward from your applications menu, or run `steward`.

## Sign-in

Steward opens your default browser for the Discord sign-in and stores the session locally, so you stay signed in between runs.
