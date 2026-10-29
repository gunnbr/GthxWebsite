# Azure deployment of the API

`main.bicep` describes the API's resources, in their own resource group (`rg-gthx-web`), apart
from the bot's (`rg-gthx-prod`). The website's deploy identity can't touch the bot.

| Resource | Name | Purpose |
|---|---|---|
| Managed identity | `id-gthx-web` | What the function app runs as. It signs in to the storage account and to the bot's database, where it can only read. |
| Managed identity | `id-gthx-web-admin` | Used only for the admin panel's changes. Its database user can change factoids and nothing else. |
| Storage account | `stgthxweb<suffix>` | The Functions host's storage and the deployment package. Shared keys are off. |
| Flex Consumption plan and function app | `asp-gthx-web`, `func-gthx-web-<suffix>` | Runs the API (.NET 10 isolated, 512 MB instances, at most 2, scales to zero). CORS allows only `https://gthx.net` and `https://www.gthx.net`. |
| App Service Authentication | (on the function app) | Checks the admin panel's Microsoft Entra tokens and tells the API who signed in. Requests without a token still reach the public endpoints. Free; on only once `ADMIN_CLIENT_ID` is set. |
| Budget | `budget-gthx-web` | Email alerts above $5/month. |

The deploy workflow (`.github/workflows/deploy.yml`) runs the tests, deploys the template, and publishes the
API, on every push to `main` that changes the API, its data library or this folder, after approval for the
`production` environment.

There's no Application Insights, to keep the cost at zero. Add it if the API ever needs debugging in Azure.

## One-time setup

1. Pick a region that has Flex Consumption (`az functionapp list-flexconsumption-locations -o table`); use
   `westus` if it's listed, to sit next to the bot. Then create the resource group:

       az provider register -n Microsoft.Web --wait
       az group create -n rg-gthx-web -l westus --tags app=gthx component=website

2. Let GitHub Actions deploy, with OpenID Connect: `infra/setup-github-oidc.sh` creates an app registration
   that trusts only this repo's `production` environment, and gives it Contributor on the resource group and
   the right to assign only the role the template uses. Set the repository variables and secret it prints,
   then create the `production` environment with yourself as a required reviewer.

3. Run the **Deploy the API to Azure** workflow (or push to `main`). The run's summary shows the API's URL.

4. Let the API's identity read the database. Signed in to the `Gthx` database as the server's Microsoft Entra
   admin (for example in the portal's query editor):

       CREATE USER [id-gthx-web] FROM EXTERNAL PROVIDER;
       ALTER ROLE db_datareader ADD MEMBER [id-gthx-web];

   The server's firewall must allow Azure services (the bot needs that too). Then check:

       curl https://<function app>.azurewebsites.net/api/stats

5. Let the admin identity change factoids, and nothing else. The deploy (step 3) creates `id-gthx-web-admin`,
   so on a setup from before the admin panel, deploy first; until then this fails with "Principal
   'id-gthx-web-admin' could not be found". Steps 1 to 4 don't need doing again. In the same database:

       CREATE USER [id-gthx-web-admin] FROM EXTERNAL PROVIDER;
       GRANT SELECT, UPDATE, DELETE ON dbo.Factoid TO [id-gthx-web-admin];
       GRANT SELECT, INSERT ON dbo.FactoidHistory TO [id-gthx-web-admin];

   The admin panel's searches use `id-gthx-web`, like the public pages.

6. Set up the admin panel's sign-in. `infra/setup-admin-signin.sh` creates an app registration for
   `admin.html` (no client secret; it signs in with PKCE), makes it require assignment, and assigns only you.
   Set the repository variables it prints, `ADMIN_CLIENT_ID` and `ADMIN_OBJECT_IDS` (comma-separated, for
   more than one admin), run the deploy workflow, and upload the site with the tenant and client ID (below).
   Then sign in at `https://gthx.net/admin`.

   Until `ADMIN_CLIENT_ID` is set, App Service Authentication stays off and the admin endpoints answer 503,
   because without it anyone could claim to be signed in.

## The static site

`site/` is uploaded to Namecheap by hand (it has no build step). `scripts/upload-site.sh` copies it over SSH
and writes `assets/config.js` with the API's URL from step 3 and the sign-in settings from step 6:

    scripts/upload-site.sh https://<function app>.azurewebsites.net/api gthx.net <tenant ID> <admin client ID>

The public pages are cached for up to 10 minutes, so an admin change can take that long to show on them.
