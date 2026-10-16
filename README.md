# Steward

The desktop half of Steward, a guild toolkit for World of Warcraft. It signs in with Discord, finds your WoW installs, and keeps the Steward addon current. In time it carries guild roster, loot history and attendance between the game and the guild admin panel, and combines what several officers record into one view.

It manages a set of addons rather than one, so the other addons published alongside it ride along. It is not a general-purpose addon manager, and it manages World of Warcraft: Forever installs only.

Steward checks each managed addon's release channel when it starts and every minute while it runs. By default Steward applies the updates it finds while the game client is not running; the Auto-update addons setting changes that to always or never. An update applied while the client is running takes effect after `/reload` in game, and the row says so until the client stops.

## Install

On Windows, install Steward from the [Microsoft Store](https://apps.microsoft.com/detail/9PBKMZFKZHKX), or from the MSI attached to each [GitHub release](https://github.com/hoobio/steward-companion/releases/latest). An MSI install updates itself. Each release also attaches a zip that holds a portable `Steward.exe`. The portable exe does not update itself: when a newer release exists, it opens the release page.

## macOS

Download the dmg for your Mac (`arm64` for Apple silicon, `x64` for Intel) from the [latest release](https://github.com/hoobio/steward-companion/releases/latest). The build is unsigned and not notarised, so clear the quarantine flag after copying Steward to Applications: `xattr -dr com.apple.quarantine /Applications/Steward.app`. When a newer release exists, Steward opens the release page.

## Linux

Steward for Linux comes from a signed package feed at `https://hoobio.github.io/steward-companion/`, for x64 and arm64. The feed has two channels:

- `stable`, the 3 newest releases. The steps below use this channel.
- `preview`, the 3 newest preview builds and the stable releases. Preview builds are for testing and are not for general use. See [Linux preview channel](#linux-preview-channel).

Releases before 0.24.1 have no Linux packages, so the `stable` channel is empty until 0.24.1 is released.

The packages and the feed metadata are signed with the Steward Linux packages key, fingerprint `498B 931B 1F0F CDED 1174 620E E6ED 1058 10B4 1C42`.

WoW itself runs through Wine, Lutris or Proton. Steward is a native Linux app and does not need Wine.

Debian and Ubuntu:

1. Download the key: `sudo install -d -m 0755 /etc/apt/keyrings && sudo curl -fsSLo /etc/apt/keyrings/steward.gpg https://hoobio.github.io/steward-companion/steward.gpg`
2. Check the key against the fingerprint above. The command prints "Steward key OK", or removes the key on a mismatch: `gpg --show-keys --with-colons /etc/apt/keyrings/steward.gpg | awk -F: '/^fpr:/ {print $10; exit}' | grep -qx '498B931B1F0FCDED1174620EE6ED105810B41C42' && echo "Steward key OK" || { echo "Steward key does NOT match: removed it, stop here"; sudo rm -f /etc/apt/keyrings/steward.gpg; }`
3. Add the feed: `echo "deb [signed-by=/etc/apt/keyrings/steward.gpg] https://hoobio.github.io/steward-companion/apt stable main" | sudo tee /etc/apt/sources.list.d/steward.list`
4. Update the package lists: `sudo apt update`
5. Install Steward: `sudo apt install steward`

Fedora and RHEL:

1. Download the key and check it against the fingerprint above. The command prints "Steward key OK", or removes the key on a mismatch: `curl -fsSLo /tmp/steward.asc https://hoobio.github.io/steward-companion/steward.asc && gpg --show-keys --with-colons /tmp/steward.asc | awk -F: '/^fpr:/ {print $10; exit}' | grep -qx '498B931B1F0FCDED1174620EE6ED105810B41C42' && echo "Steward key OK" || { echo "Steward key does NOT match: removed it, stop here"; rm -f /tmp/steward.asc; }`
2. Import the key: `sudo rpm --import /tmp/steward.asc`
3. Add the feed: `sudo dnf config-manager addrepo --from-repofile=https://hoobio.github.io/steward-companion/steward.repo`. On dnf 4 (RHEL 9 and earlier Fedora), use `sudo dnf config-manager --add-repo https://hoobio.github.io/steward-companion/steward.repo`.
4. Install Steward: `sudo dnf install steward`. On dnf 5, the first install asks to import the Steward key again to check the feed itself; accept it when the fingerprint matches the one above.

New releases arrive with your system updates, through `sudo apt upgrade` or `sudo dnf upgrade`, and Steward can also install them when it finds one. Start Steward from your applications menu, or run `steward`.

### Linux preview channel

The `preview` channel gets each preview build and each release. Steward installed from it offers the next preview or release, whichever is newer.

- Debian and Ubuntu: in step 3, use `preview` in place of `stable`: `echo "deb [signed-by=/etc/apt/keyrings/steward.gpg] https://hoobio.github.io/steward-companion/apt preview main" | sudo tee /etc/apt/sources.list.d/steward.list`
- Fedora and RHEL: in step 3, add `steward-preview.repo` in place of `steward.repo`: `sudo dnf config-manager addrepo --from-repofile=https://hoobio.github.io/steward-companion/steward-preview.repo`. On dnf 4, use `sudo dnf config-manager --add-repo https://hoobio.github.io/steward-companion/steward-preview.repo`.

To go back to `stable`, put `stable` back in `steward.list`, or remove the file in `/etc/yum.repos.d` that holds `[steward-preview]` and add `steward.repo`. A preview build stays installed until a release with a higher version arrives.

## Sign-in

Steward opens your default browser for the Discord sign-in and stores the session locally, so you stay signed in between runs.
