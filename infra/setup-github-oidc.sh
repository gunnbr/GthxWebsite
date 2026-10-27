#!/bin/sh
# Lets this repo's GitHub Actions deploy the API to Azure without a stored password (OpenID Connect).
#
# Creates an app registration and service principal that trust only tokens for this repo's "production"
# environment (the deploy job), and gives it, on the website's resource group only:
#   - Contributor
#   - Role Based Access Control Administrator, limited by a condition to assigning only the role
#     main.bicep creates (Storage Blob Data Owner)
# It gets nothing on the bot's resource group or the database.
#
# Run it once the resource group exists (infra/README.md, step 1), signed in as an Owner of the subscription.
#
#   infra/setup-github-oidc.sh [resource group] [owner/repo]
set -eu
RESOURCE_GROUP=${1:-rg-gthx-web}
REPO=${2:-gunnbr/GthxWebsite}
APP_NAME=gthx-web-github-deploy

STORAGE_BLOB_DATA_OWNER=b7e6dc6d-f1e8-4753-8033-0f276bb0955b
ROLES="$STORAGE_BLOB_DATA_OWNER"

SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
RG_ID=$(az group show -n "$RESOURCE_GROUP" --query id -o tsv)

APP_ID=$(az ad app list --display-name "$APP_NAME" --query '[0].appId' -o tsv)
if [ -z "$APP_ID" ]; then
    APP_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
fi
SP_ID=$(az ad sp list --filter "appId eq '$APP_ID'" --query '[0].id' -o tsv)
if [ -z "$SP_ID" ]; then
    SP_ID=$(az ad sp create --id "$APP_ID" --query id -o tsv)
fi

add_credential() {
    name=$1
    subject=$2
    if [ -z "$(az ad app federated-credential list --id "$APP_ID" --query "[?name=='$name'].name" -o tsv)" ]; then
        az ad app federated-credential create --id "$APP_ID" --parameters "{
            \"name\": \"$name\",
            \"issuer\": \"https://token.actions.githubusercontent.com\",
            \"subject\": \"$subject\",
            \"audiences\": [\"api://AzureADTokenExchange\"]
        }" >/dev/null
    fi
}
add_credential github-production "repo:$REPO:environment:production"

assign() {
    az role assignment create --assignee-object-id "$SP_ID" --assignee-principal-type ServicePrincipal "$@" >/dev/null
}
assign --role Contributor --scope "$RG_ID"
assign --role "Role Based Access Control Administrator" --scope "$RG_ID" --condition-version 2.0 --condition "(
 (
  !(ActionMatches{'Microsoft.Authorization/roleAssignments/write'})
 )
 OR
 (
  @Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ROLES}
 )
)
AND
(
 (
  !(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'})
 )
 OR
 (
  @Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {$ROLES}
 )
)"

cat <<EOF

Done. Set these in GitHub (Settings > Secrets and variables > Actions):

  Variables:
    AZURE_CLIENT_ID=$APP_ID
    AZURE_TENANT_ID=$TENANT_ID
    AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID
    AZURE_RESOURCE_GROUP=$RESOURCE_GROUP
  Secret:
    BUDGET_CONTACT_EMAIL=<where budget alerts go>

Then create the "production" environment with yourself as a required reviewer.
EOF
