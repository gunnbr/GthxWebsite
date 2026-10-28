# gthx.net modernization

Working notes for replacing the 2017/2018 gthx.net website. Started 2026-10-27.

## Related repositories

| Repo | What it is | Status (2026-10-27) |
|---|---|---|
| [gthx-frontend](https://github.com/gunnbr/gthx-frontend) | Angular 4 source for the deployed site (Angular CLI 1.4.9, PrimeNG 4) | Last commit 2017-11-22. The deployed bundle was built from about `83fd1a3`–`9ae81c5` (2017-11-06/07). Later commits switched to `gthx-backend` but were never deployed. |
| [gthx-backend](https://github.com/gunnbr/gthx-backend) | Node/Express + MySQL API: `/factoids/:search`, `/stats/factoids`, `/auth/login`, `/auth/register` (JWT), in Docker | 2017. Never used by the live site. |
| [GthxNet](https://github.com/gunnbr/GthxNet) | Start of an ASP.NET Core 3.1 Razor Pages rewrite of the site | 14 commits, Feb–Apr 2020. In-memory test data only. |
| [GthxNetData](https://github.com/gunnbr/GthxNetData) | Shared EF Core data layer (`IGthxData`, SQL Server migrations) | Active, .NET 10 |
| [GthxNetBot](https://github.com/gunnbr/GthxNetBot) | The bot, rewritten in C#: .NET 10, Azure Container Apps, Azure SQL, on Libera | Active. In production since 2026-10-27. |

The old data source (`ec2-52-10-64-85.us-west-2.compute.amazonaws.com`) is
gone. The live data is now in the bot's **Azure SQL** database.

## Live site findings (checked 2026-10-27)

- Hosted on Namecheap shared hosting (LiteSpeed, CloudLinux, PHP 7.2.34). This
  hosting is free for us, which is a strong reason to keep using it.
- **`/phpinfo.php` is publicly served.** It exposes the PHP version, the kernel,
  the server hostname and the account's home path.
- **HTTPS doesn't work:** the certificate doesn't match `gthx.net`
  (`SEC_E_WRONG_PRINCIPAL`). Only HTTP works.
- Every CGI script returns 500: Python 2 isn't available, and the execute permission is
  missing (`lscgid: execve(): Permission denied`). Factoid search and stats are
  completely broken.
- AWStats and Webalizer return 401. Their `.htaccess` files point at an
  H-Sphere password file that no longer exists, so nobody can reach them.
- Leftovers still served: `/cp/` (Extropia/BSCounter docs), `mychill.mp3`,
  `stats.html`, and three generations of Angular bundles.

## Requirements and constraints

- **Cost:** as close to $0 as possible. Azure is fine only if the cost is near
  zero. The bot already uses Azure Container Apps and Azure SQL.
- **Hosting:** Namecheap shared hosting is free and available for static files.
- **Security:** the browser must never connect to the database directly.
- **Auth:** avoid authentication complexity for now. A future admin panel (for example,
  editing or locking factoids) will need some kind of login, so the design
  shouldn't rule that out.
- **Low maintenance:** it should be possible to leave the site alone for years and still
  rebuild and redeploy it (the reason GthxNet was started).

## Architecture options

Prices below are from memory and **need checking** before we decide.

### A. Static frontend on Namecheap + small read-only API on Azure (BFF)

The static site stays on Namecheap. A small API on Azure reads Azure SQL with a
read-only database login and returns JSON. The browser calls the API across
origins (CORS).

- The API could be Azure Functions (Consumption/Flex: a monthly free grant of
  executions) or a scale-to-zero Container App. The Container Apps free grant is
  per subscription, and the always-on bot probably uses it up already.
- Pros: free static hosting, no database credentials in the browser, and the API can
  share GthxNetData.
- Cons: two deployments. Cold starts after idle. CORS. A future admin
  panel would need auth added to the API.

### B. Azure Static Web Apps (Free plan)

Frontend and a managed Functions API in one Azure resource. The Free plan
includes custom domains, free TLS and built-in auth (GitHub/Entra) with
role-based route protection.

- Pros: likely $0. Fixes HTTPS. The API is same-origin (no CORS). Built-in auth
  makes a future admin panel much simpler.
- Cons: the site leaves Namecheap. There are Free-plan limits to check (managed Functions
  runtime and .NET version support, bandwidth, number of custom domains).

### C. Server-rendered ASP.NET Core on Azure Container Apps

Revive GthxNet as a .NET 10 Razor Pages app in the bot's Container Apps
environment, scaled to zero.

- Pros: one codebase, same stack and deploy pipeline as the bot, uses GthxNetData directly.
- Cons: probably not $0 once the free grant is used up. Cold starts.

### D. Static data export (no live backend)

A scheduled job, for example a GitHub Action or a timer in the bot, exports the
factoids and stats from Azure SQL to JSON. The JSON is published with a static
site to Namecheap, and search runs in the browser.

- Pros: $0. Nothing on the web server can touch the database. The simplest to
  keep running.
- Cons: the data is only as fresh as the export schedule. Needs an automated upload to
  Namecheap (FTP/SFTP). An admin panel would still need a backend later.

### Cost and capability findings (checked 2026-10-27)

- **The bot today** (from GthxNetBot's `infra/` and ROADMAP): about $25/month
  in `rg-gthx-prod` (westus). Azure SQL `Gthx` on server `gthxdb` is
  **Basic tier** (5 DTU, about $5/month flat), so website reads add load but
  no cost. The bot's identity signs in to SQL with Entra (managed identity), not a password.
- **Container Apps free grant is used up.** It's 180,000 vCPU-s and 360,000 GiB-s
  per subscription per month. The bot's always-on replica (0.25 vCPU / 0.5 GiB)
  uses about 657,000 vCPU-s and 1,310,000 GiB-s. Any website container (option C)
  pays from the first second. The cost would be small if it scaled to zero, but
  cold starts are slow.
- **Static Web Apps Free rules out B as written.** Managed Functions support
  .NET only up to 8.0, which goes out of support on 2026-11-10. They also have
  **no managed identity and no Key Vault references**, so the API would need a SQL
  password in an app setting. Linking a normal Functions app ("bring your own")
  needs the Standard plan (about $9/month). SWA's built-in auth would protect only
  the SWA routes, not a separate API.
- **Azure Functions Flex Consumption** has a free grant of 100,000 GB-s and
  250,000 executions per subscription per month. It supports .NET 10 (isolated
  worker, GA February 2026) and managed identity. A 512 MB instance gets about
  200,000 seconds of execution a month for free, far more than this site needs.
  It also needs a storage account (cents per month). The free grant applies to
  pay-as-you-go subscriptions; check whether this subscription is one.
- **HTTPS on Namecheap is fixed** (Let's Encrypt, see phase 0), and SSH now
  works, so deploys to Namecheap can be scripted (scp/rsync, or from a GitHub
  Action) instead of uploaded by hand. This removes D's main drawback and
  one of B's advantages.

### Decision

**Decided 2026-10-27: A, with the API on Azure Functions Flex Consumption (.NET 10, isolated).**

- Frontend: static files on Namecheap (free, HTTPS now works), deployed over SSH.
- API: a few read-only HTTP functions (`/api/factoids?search=`, `/api/stats`)
  using GthxNetData. They sign in to Azure SQL with a managed identity that has
  only `db_datareader`, so there's no password anywhere and the API can't write.
  CORS allows only `https://gthx.net` and `https://www.gthx.net`.
- Protect the bot's 5 DTUs: cache responses (stats for minutes, searches
  briefly), cap result counts, and set a low maximum instance count.
- Deploy with Bicep + GitHub Actions (OIDC), like the bot, either in
  `rg-gthx-prod` or in its own resource group with its own budget.
- Expected cost: about $0 inside the free grant. Cold starts after idle take
  a few seconds.
- Later admin panel: add Entra sign-in (App Service Authentication) to the
  Functions app, and give the admin API a separate identity or role with write access.

D is the fallback if Flex's free grant doesn't apply to this subscription:
a scheduled export to JSON, uploaded to Namecheap over SSH.

## Open questions

- ~~Free grant left?~~ Container Apps: no (see findings). Functions Flex: yes, separate grant.
- ~~Azure SQL read cost?~~ None: Basic tier is a flat rate. The limit is 5 DTU shared with the bot.
- ~~Subscription type?~~ Visual Studio credit: $50/month for everything, and the bot uses about $25.
  The Flex free grant may not apply to credit subscriptions, but without it the API costs well under
  $1/month at this site's traffic (roughly $0.03 per 1,000 half-second requests at 512 MB, plus a
  few cents of storage). Put it in `rg-gthx-prod` so the existing $50 budget alert covers it.
- ~~Data size?~~ 5,714 factoids and 1,923 refs (2026-10-27). A full-table `LIKE '%x%'` scan is trivial
  even on 5 DTU. The whole set would also fit in a few hundred KB of gzipped JSON, so option D's
  client-side search is feasible too.
- Which frontend stack? Plain static HTML/JS, a small framework, or Blazor WebAssembly to
  share C# models with GthxNetData?
- `IGthxData` only has the queries the bot needs. Website queries (substring
  search, top references, counts) need adding to GthxNetData, or a separate
  read-only interface.

## Phases

0. **Stop exposing the live server.**
   - **Urgent (2026-10-27):** the host runs PHP 7.2.34, which is affected by the
     "php2shell" remote code execution bugs (CVE-2026-109618/109619, in CISA's
     KEV catalog since 2026-10-26). The fixed releases are 8.5.12, 8.4.27, 8.3.36 and 8.2.35.
     CISA's advice for 8.1 and earlier is to take PHP offline. The site doesn't use
     PHP, so: delete `phpinfo.php`, deny all PHP requests in `.htaccess`,
     and switch the account's PHP to a patched version in cPanel if one is offered.
     PHP 7.2 was publicly reachable, so check the server for files that aren't
     in this repo (possible web shells).
     **Done 2026-10-27:** `.htaccess` PHP block uploaded, `phpinfo.php` deleted,
     and the account's PHP switched to 8.5.12 (confirmed from outside). An
     Imunify360 scan found nothing.
   - Remove the diagnostic and dead CGI scripts and the leftover hosting-panel
     files from the live site.
   - Fix HTTPS on Namecheap if it's free.
     **Done 2026-10-27:** the account has no AutoSSL, so acme.sh (in
     `~/.acme.sh` on the host, reached over SSH)
     issued a Let's Encrypt certificate for gthx.net and www.gthx.net through
     the `~/gthx.net` webroot and installed it with the `cpanel_uapi` deploy hook.
     acme.sh's cron job renews and redeploys it. `.htaccess` redirects HTTP to HTTPS.
1. ~~Decide the architecture.~~ Done 2026-10-27: option A (see Decision).
2. ~~Add website read queries to GthxNetData.~~ Done 2026-10-27: `IGthxWebData` /
   `GthxWebData` (search, most-referenced factoids, factoid count), GthxNetData #9
   (`f036b96`). Tests are in GthxNetBot `Gthx.Test/GthxWebDataTest.cs`, GthxNetBot #69.
3. ~~Build the API and the new frontend.~~ Done 2026-10-27: `api/` (Azure Functions
   Flex Consumption, deployed by `.github/workflows/deploy.yml`) and `site/`
   (design B, Terminal), merged in #1.
4. Deploy, switch gthx.net over, and remove the 2018 files.
   **Done 2026-10-27:** the API is live, and `site/` is in the Namecheap document root,
   uploaded with `scripts/upload-site.sh`. `site/.htaccess` redirects the 2018 Angular
   routes (`/commands`, `/factoids`, `/welcome`, `/stats`, `/admin`, `/register`) to the
   new pages. The 2018 files, including `cgi-bin/`, `cp/`, `awstats/` and
   `webalizer/`, were moved to `~/gthx.net-2018` on the host, outside the web root.
   Delete that folder once nothing turns out to be missing.
   The earlier attempts, gthx-frontend, gthx-backend and GthxNet, are archived on
   GitHub, each with a README note pointing at this repo.
   The 2018 snapshot (the first commit of this repo) was removed from the repo root.
5. Later: admin panel with login.
