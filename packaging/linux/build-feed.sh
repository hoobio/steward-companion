#!/bin/sh
set -eu

packages="$(cd "$1" && pwd)"
current="$3"
rm -rf "$2"
mkdir -p "$2"
site="$(cd "$2" && pwd)"
key=498B931B1F0FCDED1174620EE6ED105810B41C42
here="$(cd "$(dirname "$0")" && pwd)"

sign() {
  gpg --batch --yes --pinentry-mode loopback --passphrase-file "$PASSPHRASE_FILE" --local-user "$key" "$@"
}

mkdir -p "$site/apt/pool/main" "$site/rpm/preview"
cp "$here/steward.asc" "$here/steward.repo" "$site/"
gpg --dearmor < "$here/steward.asc" > "$site/steward.gpg"

for deb in "$packages"/*.deb; do
  cp "$deb" "$site/apt/pool/main/$(dpkg-deb -f "$deb" Package)_$(dpkg-deb -f "$deb" Version)_$(dpkg-deb -f "$deb" Architecture).deb"
done

cd "$site/apt"
for arch in amd64 arm64; do
  dir="dists/preview/main/binary-$arch"
  mkdir -p "$dir"
  apt-ftparchive --arch "$arch" packages pool > "$dir/Packages"
  grep -q '^Package: steward$' "$dir/Packages" || { echo "No $arch packages in the apt feed"; exit 1; }
  gzip -9kn "$dir/Packages"
done
apt-ftparchive \
  -o APT::FTPArchive::Release::Origin=Steward \
  -o APT::FTPArchive::Release::Label=Steward \
  -o APT::FTPArchive::Release::Suite=preview \
  -o APT::FTPArchive::Release::Codename=preview \
  -o APT::FTPArchive::Release::Components=main \
  -o "APT::FTPArchive::Release::Architectures=amd64 arm64" \
  release dists/preview > "$site/Release"
mv "$site/Release" dists/preview/Release
sign --clearsign -o dists/preview/InRelease dists/preview/Release
sign --armor --detach-sign -o dists/preview/Release.gpg dists/preview/Release
cd "$here"

rpmdb="$(mktemp -d)"
rpmkeys --dbpath "$rpmdb" --import "$here/steward.asc"
for rpm in "$packages"/*.rpm; do
  if ! rpmkeys --dbpath "$rpmdb" -K "$rpm" | grep -q 'signatures OK'; then
    case "$(basename "$rpm")" in
      *"$current"*) echo "$rpm is not signed with the Steward key"; exit 1 ;;
    esac
    echo "::warning::Skipped unsigned $(basename "$rpm")"
    continue
  fi
  arch="$(rpm -qp --nosignature --qf '%{ARCH}' "$rpm")"
  mkdir -p "$site/rpm/preview/$arch"
  cp "$rpm" "$site/rpm/preview/$arch/"
done
rm -rf "$rpmdb"

for arch in x86_64 aarch64; do
  dir="$site/rpm/preview/$arch"
  [ -d "$dir" ] || { echo "No $arch packages in the rpm feed"; exit 1; }
  createrepo_c --quiet "$dir"
  sign --armor --detach-sign -o "$dir/repodata/repomd.xml.asc" "$dir/repodata/repomd.xml"
done
