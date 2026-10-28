# Azure deployment of the API

`main.bicep` describes the read-only API's resources, in their own resource group (`rg-gthx-web`), apart
from the bot's (`rg-gthx-prod`). The website's deploy identity can't touch the bot.

| Resource | Name | Purpose |
|---|---|---|
| Managed identity | `id-gthx-web` | What the function app runs as. It signs in to the storage account and to the bot's database, where it can only read. |
| Storage account | `stgthxweb<suffix>` | The Functions host's storage and the deployment package. Shared keys are off. |
| Flex Consumption plan and function app | `asp-gthx-web`, `func-gthx-web-<suffix>` | Runs the API (.NET 10 isolated, 512 MB instances, at most 2, scales to zero). CORS allows only `https://gthx.net` and `https://www.gthx.net`. |
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

## The static site

`site/` is uploaded to Namecheap by hand (it has no build step). `scripts/upload-site.sh` copies it over SSH
and writes `assets/config.js` with the API's URL from step 3:

    scripts/upload-site.sh https://<function app>.azurewebsites.net/api gthx.net
