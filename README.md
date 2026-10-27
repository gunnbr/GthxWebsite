# gthx.net website

The website for **gthx**, an IRC bot that lived in `#reprap`. gthx remembers
"factoids" (`gthx: filament is meltable plastic...`), tracks when users were
last seen, relays messages to people who are offline, and counts Thingiverse
references. This site lets people search the factoid database and view usage
statistics.

This repository is a snapshot of the web root as deployed to gthx.net. It was
last updated in April 2018 and is the starting point for a modernization. See
[docs/MODERNIZATION.md](docs/MODERNIZATION.md) for the plan and the current
state of the live site.

## How it works

```
Browser ──► gthx.net (Apache, H-Sphere shared hosting)
             │  .htaccess: unknown paths → /index.html (SPA routing)
             │
             ├─ Angular app (index.html + *.bundle.js)
             │     ├─ GET  http://ec2-52-10-64-85.us-west-2.compute.amazonaws.com/cgi-bin/factoids.py?searchstring=…
             │     ├─ GET  http://ec2-52-10-64-85.us-west-2.compute.amazonaws.com/cgi-bin/stats.py
             │     └─ POST http://localhost:3000/auth/{login,register}
             │
             └─ Legacy pages: stats.html + stats.js (jQuery)
                   └─ cgi-bin/*-relay.py (Python 2 CGI) ──► same EC2 endpoints
```

The bot and its data live on a separate EC2 server, not in this repository.
This site only displays that data.

### Angular app

Only the **compiled** output is here. The TypeScript source isn't in this
repository.

- Angular 4.4.6, built with webpack 3.7.1 (Angular CLI), zone.js 0.8.18, core-js 2.5.1
- PrimeNG (`ui-datatable`) and Font Awesome 4.7
- Routes: `/welcome`, `/commands`, `/factoids`, `/stats`, `/admin`, `/register`
- Login stores a token in `localStorage` and sends it as an `Authorization: token …` header

`index.html` loads the bundles with the hashes `inline.eb57…`, `polyfills.1417…`,
`vendor.9be6…`, `main.7126…` and `styles.de18…`. The other `*.bundle.*` files
are leftovers from earlier deploys.

### Legacy pages and CGI

| File | Purpose |
|---|---|
| `stats.html`, `stats.js` | Standalone jQuery stats page (factoid and Thingiverse reference counts) |
| `index.js` | Script for an older jQuery search page that no longer exists |
| `layout.css`, `bootstrap.min.css`, `jquery*.js`, `loading-bar.gif` | Assets for the legacy pages |
| `cgi-bin/factoid-relay.py`, `cgi-bin/stats-relay.py` | Python 2 CGI scripts that proxy requests to the EC2 API |
| `cgi-bin/test.py` | "Hello" CGI test script |

### Hosting-panel files

These were installed by the H-Sphere control panel rather than written for
this project:

- `cp/`: stock Extropia/BSCounter script docs and images
- `images/`: control-panel theme images
- `cgi-bin/awstats.pl`, `cgi-bin/lib/`, `cgi-bin/plugins/`, `cgi-bin/lang/`, `awstats/icon/`: AWStats 6.8
- `webalizer/.htaccess`: password protection for Webalizer 2.20 reports
- `cgi-bin/printenv`, `cgi-bin/test-cgi`, `phpinfo.php`: server diagnostic scripts

## Deployment

The site was originally deployed by uploading the files over FTP to an H-Sphere
web root. It's now hosted on Namecheap shared hosting (LiteSpeed).
There's no build step for the files in this repository. Apache serves
them as-is, and `.htaccess` sends unknown paths to `index.html` so the Angular
router can handle them.

## Not in this repository

`.gitignore` leaves out the following files from the deployed snapshot:

- Generated traffic statistics (`awstats/data/`, `webalizer/` reports), which contain visitor IP addresses
- Hosting-panel temp files (`.ftpquota`, `cgi-bin/lang/d2utmp*`)
- `mychill.mp3`, a large audio file unrelated to the site

## Known issues

- The API is reached through a raw EC2 hostname over plain HTTP. The hostname
  is probably gone, and an HTTPS page can't call an HTTP one.
- Login, registration and admin point at `localhost:3000`, so they can't work in production.
- The Python 2 `cgi` module is no longer available in current Python versions.
- The relay scripts don't URL-encode the search string.
- `index.js` and `stats.js` insert unescaped data into the page, which allows XSS.
- `phpinfo.php`, `printenv` and `test-cgi` expose server details.
- AWStats 6.8 has known vulnerabilities.
- The IRC link points to Freenode. `#reprap` has since moved to Libera.Chat.
