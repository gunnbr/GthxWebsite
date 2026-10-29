# gthx.net website

The website for **gthx**, an IRC bot that has lived in `#reprap` (now on Libera.Chat) since 2014. gthx
remembers "factoids" (`gthx: filament is meltable plastic...`), tracks when users were last seen, and relays
messages to people who are offline. This site lets people search the factoid database, see the most
referenced factoids, and look up the bot's commands. An admin panel (`admin.html`) lets the bot's admin edit,
delete and lock factoids, and clean up ones that were set by mistake. The bot itself is
[GthxNetBot](https://github.com/gunnbr/GthxNetBot).

## How it works

```
Browser ──► gthx.net (static files on Namecheap shared hosting)
             │  site/: index.html, search.html, commands.html, admin.html, assets/
             │
             └─ fetch() ──► Azure Functions (Flex Consumption, .NET 10)    api/Gthx.Web.Api
                              │  GET /api/factoids?q=   /api/factoids/top   /api/stats
                              │  /api/manage/...  (admin; Microsoft Entra token checked by App Service Authentication)
                              │
                              ├─ id-gthx-web, read-only ─────────┐
                              └─ id-gthx-web-admin, admin changes ┴──► Azure SQL (the bot's database)
                                                                       via lib/GthxNetData (Gthx.Data)
```

The browser never talks to the database. The public endpoints can only read it. The admin endpoints make
their changes as a second managed identity, which can only change factoids (and record that in their history),
and only for the Microsoft Entra accounts listed in `ADMIN_OBJECT_IDS`.

| Folder | What's in it |
|---|---|
| `site/` | The static site. Plain HTML, CSS and JavaScript with no build step. `assets/config.js` says where the API is. |
| `api/` | The API (`Gthx.Web.Api`) and its tests. `FactoidFunctions` is the public, read-only part; `AdminFunctions` is the admin panel's. |
| `lib/GthxNetData` | Submodule with the bot's data library, shared with GthxNetBot. |
| `infra/` | Bicep for the API's Azure resources, and the one-time setup. |
| `scripts/` | `upload-site.sh`, which copies `site/` to the host. |

## Building and testing

```
git clone --recurse-submodules https://github.com/gunnbr/GthxWebsite.git
dotnet test GthxWebsite.slnx
```

To run the API locally, use the Azure Functions Core Tools (`func start` in `api/Gthx.Web.Api`) with
`ConnectionStrings:GthxDb` and `ConnectionStrings:GthxAdminDb` settings in `local.settings.json` (they can be
the same local database). `site/assets/config.js` points at `http://localhost:7071/api` by default, so any
static file server on `site/` will use it.

There's no App Service Authentication locally, so to try the admin panel, set `Admin:AllowedObjectIds` to
any ID and `Admin:TrustPrincipalHeaders` to `true` in `local.settings.json`, and the same ID as
`localObjectId` in `site/assets/config.js`. The page then sends that ID itself instead of signing in. Never
set `Admin:TrustPrincipalHeaders` in Azure.

The data library's queries are tested against SQL Server in GthxNetBot (`GthxWebDataTest.cs` and
`GthxAdminDataTest.cs`).

## Deploying

- **API:** `.github/workflows/deploy.yml` deploys `infra/main.bicep` and publishes the API on every push to
  `main` that changes `api/`, `lib/` or `infra/`, after approval for the `production` environment. See
  [infra/README.md](infra/README.md) for the one-time setup.
- **Site:** upload it by hand over SSH, pointed at the API:

      scripts/upload-site.sh https://<function app>.azurewebsites.net/api gthx.net <tenant ID> <admin client ID>

  It needs a `namecheap` host in `~/.ssh/config`. The last two arguments turn on the admin panel's sign-in
  (see [infra/README.md](infra/README.md)). `site/.htaccess` blocks PHP, redirects HTTP to HTTPS, and
  redirects the old 2018 site's addresses to the new pages (`/admin` now goes to the admin panel).
- **HTTPS:** gthx.net and www.gthx.net use a free Let's Encrypt certificate from acme.sh on the host
  (`~/.acme.sh`). Its cron job renews the certificate and installs it through cPanel.

## History

The site that ran from 2018 to 2026 was a compiled Angular 4 app that called the bot's old API on EC2. Its
deployed files were the first commit of this repository, and were removed once the new site went live.
Earlier attempts at a replacement are archived: gthx-frontend, gthx-backend and GthxNet.
