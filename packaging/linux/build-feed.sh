#!/bin/sh
set -eu

rm -rf "$1"
mkdir -p "$1"
site="$(cd "$1" && pwd)"
current="$2"
key=498B931B1F0FCDED1174620EE6ED105810B41C42
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
tab="$(printf '\t')"

sign() {
  gpg --batch --yes --pinentry-mode loopback --passphrase-file "$PASSPHRASE_FILE" --local-user "$key" "$@"
}

gh api "repos/$GH_REPO/releases?per_page=100" --jq '
  [.[] | select((.draft | not) and any(.assets[]; .name | endswith(".deb") or endswith(".rpm")))] as $releases
  | ([$releases[] | select(.prerelease | not)][:3][] | ["stable", .tag_name]),
    ([$releases[] | select(.prerelease and (.tag_name | startswith("desktop-preview-")))][:3][] | ["preview", .tag_name])
  | @tsv' > "$work/releases.tsv"
cat "$work/releases.tsv"
mkdir -p "$work/stable" "$work/preview"
while IFS="$tab" read -r channel tag; do
  gh release download "$tag" -p '*.deb' -p '*.rpm' -D "$work/$channel"
done < "$work/releases.tsv"

cp "$here/steward.asc" "$here/steward.repo" "$here/steward-preview.repo" "$site/"
gpg --dearmor < "$here/steward.asc" > "$site/steward.gpg"

mkdir -p "$site/apt/pool/main" "$site/apt/pool/stable"
for channel in stable preview; do
  pool="$site/apt/pool/main"
  [ "$channel" = stable ] && pool="$site/apt/pool/stable"
  for deb in "$work/$channel"/*.deb; do
    [ -e "$deb" ] || continue
    cp "$deb" "$pool/$(dpkg-deb -f "$deb" Package)_$(dpkg-deb -f "$deb" Version)_$(dpkg-deb -f "$deb" Architecture).deb"
  done
done

cd "$site/apt"
# preview offers the stable releases too, so a preview install moves on to the next release.
for suite in stable preview; do
  pool=pool
  [ "$suite" = stable ] && pool=pool/stable
  for arch in amd64 arm64; do
    dir="dists/$suite/main/binary-$arch"
    mkdir -p "$dir"
    apt-ftparchive --arch "$arch" packages "$pool" > "$dir/Packages"
    if [ "$suite" = preview ] && ! grep -q '^Package: steward$' "$dir/Packages"; then
      echo "No $arch packages in the apt preview suite"
      exit 1
    fi
    gzip -9kn "$dir/Packages"
  done
  apt-ftparchive \
    -o APT::FTPArchive::Release::Origin=Steward \
    -o APT::FTPArchive::Release::Label=Steward \
    -o "APT::FTPArchive::Release::Suite=$suite" \
    -o "APT::FTPArchive::Release::Codename=$suite" \
    -o APT::FTPArchive::Release::Components=main \
    -o "APT::FTPArchive::Release::Architectures=amd64 arm64" \
    release "dists/$suite" > "$work/Release"
  mv "$work/Release" "dists/$suite/Release"
  sign --clearsign -o "dists/$suite/InRelease" "dists/$suite/Release"
  sign --armor --detach-sign -o "dists/$suite/Release.gpg" "dists/$suite/Release"
done
cd "$here"

rpmdb="$(mktemp -d)"
rpmkeys --dbpath "$rpmdb" --import "$here/steward.asc"
for arch in x86_64 aarch64; do
  mkdir -p "$site/rpm/stable/$arch" "$site/rpm/preview/$arch"
done
for channel in stable preview; do
  for rpm in "$work/$channel"/*.rpm; do
    [ -e "$rpm" ] || continue
    if ! rpmkeys --dbpath "$rpmdb" -K "$rpm" | grep -q 'signatures OK'; then
      case "$(basename "$rpm")" in
        "Steward-$current-linux-"*) echo "$rpm is not signed with the Steward key"; exit 1 ;;
      esac
      echo "::warning::Skipped unsigned $(basename "$rpm")"
      continue
    fi
    arch="$(rpm -qp --nosignature --qf '%{ARCH}' "$rpm")"
    cp "$rpm" "$site/rpm/preview/$arch/"
    if [ "$channel" = stable ]; then
      cp "$rpm" "$site/rpm/stable/$arch/"
    fi
  done
done
rm -rf "$rpmdb" "$work"

for repo in stable preview; do
  for arch in x86_64 aarch64; do
    dir="$site/rpm/$repo/$arch"
    if [ "$repo" = preview ] && ! ls "$dir"/*.rpm >/dev/null 2>&1; then
      echo "No $arch packages in the rpm preview repo"
      exit 1
    fi
    createrepo_c --quiet "$dir"
    sign --armor --detach-sign -o "$dir/repodata/repomd.xml.asc" "$dir/repodata/repomd.xml"
  done
done
