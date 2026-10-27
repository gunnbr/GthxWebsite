#!/bin/sh
# Uploads site/ to the Namecheap host over SSH, pointed at the given API.
#
#   scripts/upload-site.sh <API base URL> <target folder on the host>
#
# For example, to try it out next to the old site before switching over:
#
#   scripts/upload-site.sh https://func-gthx-web-xxxx.azurewebsites.net/api gthx.net/next
#
# Needs the "namecheap" host in ~/.ssh/config. Files already on the host that aren't in site/ are left alone.
set -eu
API=${1:?API base URL, e.g. https://<function app>.azurewebsites.net/api}
TARGET=${2:?target folder on the host, e.g. gthx.net/next}
HOST=${UPLOAD_HOST:-namecheap}

cd "$(dirname "$0")/.."
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cp -R site/. "$work/"
cat > "$work/assets/config.js" <<EOF
// Where the read-only API lives. Written by scripts/upload-site.sh.
window.GTHX_API = "$API";
EOF

ssh "$HOST" "mkdir -p '$TARGET'"
scp -rq "$work/." "$HOST:$TARGET/"
echo "Uploaded site/ to $HOST:$TARGET, using the API at $API"
