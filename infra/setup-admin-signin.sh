#!/bin/sh
# Sets up Microsoft Entra sign-in for the admin panel (site/admin.html).
#
# Creates an app registration that:
#   - lets admin.html sign in with the authorization code flow and PKCE (a "single-page application", so
#     there's no client secret), returning only to the given sites' admin.html
#   - exposes one scope, Factoids.Manage, which is what the admin panel asks for a token to
#   - issues v2 access tokens, which App Service Authentication on the function app checks (infra/main.bicep)
# and makes its service principal require assignment, then assigns only you. Nobody else in the tenant can get
# a token for it, and the API also checks the account against ADMIN_OBJECT_IDS.
#
# Run it signed in to the Azure CLI as someone who can create app registrations, in the tenant the admin
# accounts are in.
#
#   infra/setup-admin-signin.sh [site origin ...]
#
# The default origins are https://gthx.net, https://www.gthx.net and http://localhost:8000 (for testing with a
# local static server). Running it again updates the registration to match.
set -eu
APP_NAME=gthx-web-admin
SCOPE=Factoids.Manage
DEFAULT_ACCESS_ROLE=00000000-0000-0000-0000-000000000000
[ $# -gt 0 ] || set -- https://gthx.net https://www.gthx.net http://localhost:8000

redirects=""
for origin in "$@"; do
    redirects="$redirects${redirects:+,}\"${origin%/}/admin.html\""
done

new_uuid() {
    uuidgen 2>/dev/null || cat /proc/sys/kernel/random/uuid
}

TENANT_ID=$(az account show --query tenantId -o tsv)
ME=$(az ad signed-in-user show --query id -o tsv)

APP_ID=$(az ad app list --display-name "$APP_NAME" --query '[0].appId' -o tsv)
if [ -z "$APP_ID" ]; then
    APP_ID=$(az ad app create --display-name "$APP_NAME" --sign-in-audience AzureADMyOrg --query appId -o tsv)
fi
APP_OBJECT_ID=$(az ad app show --id "$APP_ID" --query id -o tsv)

# Keep the scope's ID on later runs: Entra won't replace an enabled scope.
SCOPE_ID=$(az ad app show --id "$APP_ID" --query "api.oauth2PermissionScopes[?value=='$SCOPE'].id | [0]" -o tsv)
[ -n "$SCOPE_ID" ] || SCOPE_ID=$(new_uuid)

az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$APP_OBJECT_ID" \
    --headers Content-Type=application/json --body "{
        \"identifierUris\": [\"api://$APP_ID\"],
        \"spa\": { \"redirectUris\": [$redirects] },
        \"api\": {
            \"requestedAccessTokenVersion\": 2,
            \"oauth2PermissionScopes\": [{
                \"id\": \"$SCOPE_ID\",
                \"value\": \"$SCOPE\",
                \"type\": \"User\",
                \"isEnabled\": true,
                \"adminConsentDisplayName\": \"Manage gthx factoids\",
                \"adminConsentDescription\": \"Edit, delete and lock gthx factoids from the admin panel.\",
                \"userConsentDisplayName\": \"Manage gthx factoids\",
                \"userConsentDescription\": \"Edit, delete and lock gthx factoids from the admin panel.\"
            }]
        }
    }"
echo "App registration $APP_NAME ($APP_ID) returns to: $redirects"

SP_ID=$(az ad sp list --filter "appId eq '$APP_ID'" --query '[0].id' -o tsv)
if [ -z "$SP_ID" ]; then
    SP_ID=$(az ad sp create --id "$APP_ID" --query id -o tsv)
fi
az ad sp update --id "$SP_ID" --set appRoleAssignmentRequired=true

assigned=$(az rest --method GET --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$SP_ID/appRoleAssignedTo" \
    --query "value[?principalId=='$ME'] | length(@)" -o tsv)
if [ "$assigned" = "0" ]; then
    az rest --method POST --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$SP_ID/appRoleAssignedTo" \
        --headers Content-Type=application/json \
        --body "{\"principalId\": \"$ME\", \"resourceId\": \"$SP_ID\", \"appRoleId\": \"$DEFAULT_ACCESS_ROLE\"}" >/dev/null
fi
echo "Only assigned accounts can sign in; you ($ME) are assigned."

cat <<EOF

Done. Set these repository variables in GitHub (Settings > Secrets and variables > Actions), then run the
deploy workflow:

    ADMIN_CLIENT_ID=$APP_ID
    ADMIN_OBJECT_IDS=$ME

Then upload the site with sign-in turned on:

    scripts/upload-site.sh https://<function app>.azurewebsites.net/api gthx.net $TENANT_ID $APP_ID
EOF
