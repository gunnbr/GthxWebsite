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

### Current thinking

Not decided. B is the best fit if its Free plan really is $0 and supports what
we need, because it also solves HTTPS and future auth. D is the cheapest and
simplest if a delay in data updates is acceptable. A is the fallback if the site
has to stay on Namecheap with live data.

## Open questions

- Does the existing Azure subscription have a free grant left, given the bot's always-on container?
- What does Azure SQL charge for extra read traffic from the website? Which tier
  does the bot use (serverless with auto-pause or provisioned)?
- How large is the factoid table? This matters for option D's client-side search.
- Which frontend stack? Plain static HTML/JS, a small framework, or Blazor WebAssembly to
  share C# models with GthxNetData?
- `IGthxData` only has the queries the bot needs. Website queries (substring
  search, top references, counts) need adding to GthxNetData, or a separate
  read-only interface.
- Can Namecheap's free AutoSSL / PositiveSSL fix HTTPS for gthx.net in the meantime?

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
1. Decide the architecture (answer the open questions above).
2. Add website read queries to GthxNetData.
3. Build the API (if any) and the new frontend.
4. Deploy, switch gthx.net over, and remove the 2018 files.
5. Later: admin panel with login.
