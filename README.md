# gthx.net website

The website for **gthx**, an IRC bot that has lived in `#reprap` (now on Libera.Chat) since 2014. gthx
remembers "factoids" (`gthx: filament is meltable plastic...`), tracks when users were last seen, and relays
messages to people who are offline. This site lets people search the factoid database, see the most
referenced factoids, and look up the bot's commands. The bot itself is
[GthxNetBot](https://github.com/gunnbr/GthxNetBot).

## How it works

```
Browser ──► gthx.net (static files on Namecheap shared hosting)
             │  site/: index.html, search.html, commands.html, assets/
             │
             └─ fetch() ──► Azure Functions (Flex Consumption, .NET 10)    api/Gthx.Web.Api
                              │  GET /api/factoids?q=   /api/factoids/top   /api/stats
                              │
                              └─ managed identity, read-only ──► Azure SQL (the bot's database)
                                                                  via lib/GthxNetData (Gthx.Data)
```

The browser never talks to the database. The API can only read it.

| Folder | What's in it |
|---|---|
| `site/` | The static site. Plain HTML, CSS and JavaScript with no build step. `assets/config.js` says where the API is. |
| `api/` | The read-only API (`Gthx.Web.Api`) and its tests. |
| `lib/GthxNetData` | Submodule with the bot's data library, shared with GthxNetBot. |
| `infra/` | Bicep for the API's Azure resources, and the one-time setup. |
| `scripts/` | `upload-site.sh`, which copies `site/` to the host. |

## Building and testing

```
git clone --recurse-submodules https://github.com/gunnbr/GthxWebsite.git
dotnet test GthxWebsite.slnx
```

To run the API locally, use the Azure Functions Core Tools (`func start` in `api/Gthx.Web.Api`) with a
`ConnectionStrings:GthxDb` setting in `local.settings.json`. `site/assets/config.js` points at
`http://localhost:7071/api` by default, so any static file server on `site/` will use it.

## Deploying

- **API:** `.github/workflows/deploy.yml` deploys `infra/main.bicep` and publishes the API on every push to
  `main` that changes `api/`, `lib/` or `infra/`, after approval for the `production` environment. See
  [infra/README.md](infra/README.md) for the one-time setup.
- **Site:** upload it by hand over SSH, pointed at the API:

      scripts/upload-site.sh https://<function app>.azurewebsites.net/api gthx.net

  It needs a `namecheap` host in `~/.ssh/config`. `site/.htaccess` blocks PHP, redirects HTTP to HTTPS, and
  redirects the old 2018 site's addresses to the new pages.
- **HTTPS:** gthx.net and www.gthx.net use a free Let's Encrypt certificate from acme.sh on the host
  (`~/.acme.sh`). Its cron job renews the certificate and installs it through cPanel.

## History

The site that ran from 2018 to 2026 was a compiled Angular 4 app that called the bot's old API on EC2. Its
deployed files were the first commit of this repository, and were removed once the new site went live.
Earlier attempts at a replacement are archived: gthx-frontend, gthx-backend and GthxNet.
