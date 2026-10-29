#!/bin/sh
# Uploads site/ to the Namecheap host over SSH, pointed at the given API.
#
#   scripts/upload-site.sh <API base URL> <target folder on the host> [<tenant ID> <admin client ID>]
#
# The last two turn on the admin panel's sign-in; infra/setup-admin-signin.sh prints them.
#
# For example, to try it out next to the old site before switching over:
#
#   scripts/upload-site.sh https://func-gthx-web-xxxx.azurewebsites.net/api gthx.net/next
#
# Needs the "namecheap" host in ~/.ssh/config. Files already on the host that aren't in site/ are left alone.
set -eu
API=${1:?API base URL, e.g. https://<function app>.azurewebsites.net/api}
TARGET=${2:?target folder on the host, e.g. gthx.net/next}
TENANT_ID=${3:-}
ADMIN_CLIENT_ID=${4:-}
HOST=${UPLOAD_HOST:-namecheap}

cd "$(dirname "$0")/.."
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cp -R site/. "$work/"
cat > "$work/assets/config.js" <<EOF
// Where the API lives, and the admin panel's sign-in. Written by scripts/upload-site.sh.
window.GTHX_API = "$API";
window.GTHX_ADMIN = { tenantId: "$TENANT_ID", clientId: "$ADMIN_CLIENT_ID" };
EOF
# scp gives the target folder the same mode as $work, which mktemp makes 700. The web server couldn't read
# the site then, so make everything readable: folders 755, files 644.
chmod -R u=rwX,go=rX "$work"

ssh "$HOST" "mkdir -p '$TARGET'"
scp -rq "$work/." "$HOST:$TARGET/"
echo "Uploaded site/ to $HOST:$TARGET, using the API at $API"
