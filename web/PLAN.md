# OpenParallel website and Report a problem: plan

Status: approved direction "The desk" (Ali, 2026-10-14). Source in `web/site/`, built into
`web/dist/` by `web/build.sh`. Product truth: `/PRODUCT.md`. Shared public facts (report
email, repository, downloads): `/project.json`. Nothing here is published until Ali says so.

Design guidance: impeccable (updated 2026-10-14) as the main playbook and quality floor,
Hallmark's slop test as an extra gate. Built code-led (no image generation available); the
direction contract below is what the finish review audits.

## 1. Who arrives and what they must do

A person who already uses an app in two accounts and is tired of signing out and in. They
arrive from a link, a search, or GitHub. In one viewport they must understand: *a second copy
of the same app, signed in to another account, original untouched*, and see a download for
their system. Then they check whether *their* app works (the register) and whether it is safe
(private, open source). Success: they download, or they find their app's honest status.

## 2. Site map

| Page | Mode | Job |
|---|---|---|
| `/` | Persuade | The desk demo, download for your system, how it works, what a copy gets, compatibility preview, privacy, open source, download for every system |
| `/apps/` | Read/Operate | The full register: 199 entries, search, filter by status, what each status means |
| `/report/` | Operate | Report a problem: build the report, send it by email (or a GitHub issue once public), or copy it |
| `/privacy/` | Read | What OpenParallel and this site do and don't collect (nothing; no analytics, fonts are the system's) |

Shared header: mark, How it works, Apps, Download, Report a problem, GitHub (hidden until
`repositoryUrl` is set). Footer: MIT license, privacy, report, source.

## 3. Report a problem (apps and site)

One report format everywhere, so reports arrive the same way whatever sent them:

```
Subject: [OpenParallel] Something doesn't work: Discord
What happened: Something doesn't work | An app doesn't keep its data separate | Idea | Other
App: Discord 0.0.309 (Electron, Supported)            ← optional, prefilled from an instance
OpenParallel: 1.0.0 (macOS build)
System: macOS 15.6 (arm64)
---
<what happened, in the person's words>
Steps to see it again:
<optional>
```

- **Email (default):** opens the person's own mail app with the report filled in (`mailto:`).
  Nothing is sent by OpenParallel or the site; the person reviews and sends it. Address from
  `project.json` `reportEmail`; while empty, the Email button is disabled with one line saying
  the address isn't set in this build, and Copy report still works.
- **GitHub issue:** a pre-filled `…/issues/new` link, shown only when `repositoryUrl` is set.
- **Copy report:** always available (long reports, webmail users).
- System details are shown in full before sending and can be turned off.
- Entry points: Mac Help menu and Settings, Windows/Linux Settings › Help and the instance
  page ("Report a problem with this instance" prefills the app, version, family and status),
  and `/report/` on the site.

## 4. Technology

Static HTML, CSS and a little JavaScript; no framework, no CDN, no web fonts (the page uses the
visitor's own system font: it *is* their desktop), no analytics, no cookies. `build.sh`
assembles `site/` + `project.json` + `data/register.json` into `dist/`. Any static host works
Hosted on Cloudflare Pages (Ali, 2026-10-14): https://openparallel.pages.dev, deployed
with `web/deploy.sh` (`preview` for a preview URL).

## 5. Direction contract

THESIS: The first viewport is the visitor's own desktop doing the job: two identical app
icons, two windows, two accounts. It refuses the category default (a slogan over a floating
screenshot, then a row of feature cards) and the rejected terminal look.

OWN-WORLD: A saturated amber wallpaper field (the icon's amber) with real desktop chrome on it:
system-font type, light native windows with title bars, a Dock / taskbar / GNOME dash chosen by
the visitor's system, letter-tile app icons, graphite text, one amber accent. Dark mode is the
graphite night version of the same desk. Lists look like Finder/Explorer list views; controls
look like native push buttons and segmented controls.

STORY: "I can have Discord twice, each signed in to a different account, and Discord itself is
never touched." Then: my app's status is honest; it's private and open source; download.

FIRST VIEWPORT: Left third: headline, one sentence, Download for <your system> (working) and
"Other systems". Right two-thirds and full bleed: the desk at true UI scale: a Dock/taskbar
with the app and its copy side by side, two windows open, each showing a different account; an
app switcher (Claude, Discord, Telegram, VS Code, Obsidian) restages the desk.

FORM: "The desk" (Ali's pick, 2026-10-14; first of the presented hand). seed: none (unavailable)

SIGNATURE INTERACTION: click the copy's icon: it bounces (Mac) or highlights (Windows/Linux),
and its window opens from the icon signed in to the second account. Reduced motion: instant.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the
verdict, DESIGN.md, and every shipping raster carrying its provenance.

## 6. Phases

1. Plan, product truth, shared config (this file, `/PRODUCT.md`, `/project.json`).
2. Site: home with the desk, apps register, report page, privacy; responsive; light and dark.
3. Report a problem in the Mac app (SwiftUI) and the Windows/Linux app (Avalonia).
4. Finish review: one batched inspection (desktop + mobile, light + dark), one fix round,
   Hallmark slop test, then `web/DESIGN.md` from the built site.
5. Ali: create the report address and set `reportEmail`; set `repositoryUrl` when the repo is
   public; say when to deploy.

## 7. Progress log

### 2026-10-14: built

- Design skills compared (Aug 2026 roundups, repos): kept **impeccable** as the main playbook
  and updated it to upstream (2026-10-14: modes, craft floor, platform guides); added
  **Hallmark** (Together AI, MIT, slop-test gates). Both in the skills hub, pinned/vendored.
- Site in `site/`: home (desk demo with app and system switchers, how it works with the New
  Instance sheet, what stays apart, compatibility preview, privacy, downloads), `/apps/`
  (register with search and status filter), `/report/` (report builder: email, GitHub issue,
  copy), `/privacy/`. `build.sh` → `dist/` (400 KB). Preview: `.claude/launch.json` "site".
- One batched inspection (1280×800 and 1440×900, light and dark, 390 mobile) and one fix round:
  headline moved full width (fit the 13" fold), Dock labels, mobile segmented control overflow,
  wallpaper shapes crossing text, header mark, `[hidden]` vs `.btn`, dark wallpaper square,
  every colour lifted into tokens. Confirm pass clean: no horizontal scroll at 390 px; Windows
  taskbar and GNOME dash variants render; words follow the chosen system.
- Report a problem in the apps: Mac `ReportProblemView` (Help menu, Instance menu, instance page,
  Settings; `ProblemReport` model, 2 new tests, 48/48); Windows/Linux `ReportPage` (sidebar
  footer, F1, instance page, Settings; `Core/ProblemReport.cs`, 3 new tests, 51/51; Wine 39/39;
  seen working in the bottle). All read `/project.json`.
- Not done: deploy (waits for Ali), real report address (Ali creates it, then sets
  `reportEmail`), repository URL (when public). Old drafts in `web/index.html` and
  `web/deploy/` are untouched history.

### 2026-10-14: live on Cloudflare Pages

- https://openparallel.pages.dev (Pages project `openparallel`, classic Pages, direct upload; no Git
  integration because the repo isn't public). Deploy: `web/deploy.sh` (production, branch `main`)
  or `web/deploy.sh preview` (https://preview.openparallel.pages.dev, `noindex`). Needs
  `npx wrangler login` once; Ali's Mac is logged in.
- Added for the host: `site/_headers` (CSP `'self'` only, nosniff, DENY framing, referrer and
  permissions policies; fonts cached a year, icons a week, everything else revalidates),
  `site/404.html` (real 404s, not the home page), `web/wrangler.toml`, `web/package.json`.
- Fixed: Privacy page marked "How it works" and "Download" as the current page. Privacy copy now
  names Cloudflare as the host and says its visitor analytics are off (they are: no beacon).
- Verified on the preview and production: every page and file 200, `/apps` → 308 `/apps/`, unknown
  paths 404 with the site's page, headers present, register loads 198 rows, report prefill works,
  no console or CSP errors. Download buttons and email stay disabled until `project.json` gets
  `repositoryUrl` and `reportEmail`; then rebuild and run `web/deploy.sh`.

### 2026-10-14: Windows test build downloadable from the site

- https://openparallel.pages.dev/download/OpenParallel-1.0.0-windows-x64.zip (48,693,963 bytes,
  sha256 e07b5480…6040095f, the 08:41 build with the Report page). For Ali's friend's test; not
  linked from any page.
- Pages caps files at 25 MiB and R2 needs a card, so `web/add-download.sh <file>` splits a file into
  20 MiB parts in `web/downloads/` (git-ignored) with a manifest; `build.sh` copies them to
  `dist/files/`; `functions/download/[file].js` joins them into one response (FixedLengthStream,
  exact Content-Length, attachment). Verified: download hash equals the original, `unzip -t` clean,
  unknown names 404. To replace a build: `web/add-download.sh <zip>` then `web/deploy.sh`.
