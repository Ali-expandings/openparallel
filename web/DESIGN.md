# OpenParallel website: design system (as built, 2026-10-14)

Documented from `site/` after the build. Direction: "The desk" (see `PLAN.md` §5).

## World
The visitor's own desktop doing the job. The hero is an amber wallpaper (the icon's amber)
carrying a working desktop: a Dock, Windows taskbar or GNOME dash chosen from the visitor's
system, two windows of one app signed in to different accounts. Below the hero the page is a
calm light (or graphite) ground; windows, list views and native-style controls carry the world
into every section without performing it again.

## Tokens (`site.css` `:root`, dark overrides under `prefers-color-scheme`)
- Wall: `--wall` amber (night: graphite), `--wall-deep`, `--wall-mark`, `--wall-ink`, `--wall-ink-2`.
- Ground and ink: `--ground`, `--ground-2`, `--ink`, `--ink-2`, `--ink-3`, `--line`, `--line-strong`.
- Action: `--button` graphite on day (amber on night), `--on-button`, `--quiet` (translucent on the wall), `--accent`, `--accent-ink` (links, icons; under 5% of any viewport).
- Window chrome: `--win-bg`, `--win-bar`, `--win-side`, `--win-line`, `--win-ink`, `--win-ink-2`, `--win-chip`, `--dock`, `--taskbar`, `--dash`, lights `--light-*`, selection `--select`.
- Status (same words as the apps): `--st-tested`, `--st-supported`, `--st-limited`, `--st-wanted`, `--st-no`.
- App tiles in the illustration: `--tile-1`…`--tile-6` (letters, never vendor logos).
- Shadows: `--shadow-win`, `--shadow-soft`, `--shadow-chip`, `--shadow-dock`, `--shadow-tile`; focus `--focus`, `--focus-halo`.
- Space: `--space-3xs` 2 px … `--space-5xl` 128 px (4 px steps). Radii 6/10/14. Ease `--ease-out` (expo out).
No colour or font appears outside these tokens.

## Type
- Display: Bricolage Grotesque Bold, self-hosted (`fonts/`, OFL). h1 clamp(2.4rem → 4.6rem), line-height 1, tracking −0.032em; h2 clamp(1.875rem → 2.875rem).
- Everything else, including the desk: the visitor's system UI font (SF, Segoe UI Variable, Cantarell, Ubuntu, Noto). Mono only for paths, file names and the report text.

## Components
Native push buttons (`.btn-primary`, `.btn-quiet`, `.btn-plain`), segmented controls (`.seg`,
radio semantics, arrow keys), desktop windows (`.window`: mac lights, Windows caption buttons,
GNOME close), the Dock/taskbar/dash (`.dock`), the New Instance sheet replica (`.sheet`),
list view (`.listview`: search, sticky header, status dots, tabular numerals), ledger (separate
vs shared), plain statements (`.promises`), download rows (`.dl`), settings-pane form
(`.field`, `.choice`, `.input`, `.textarea`, `.check`), report preview (`.preview`).

## Motion
One authored moment: the copy's window gathers into its icon, the icon bounces (macOS) or
highlights, and the window opens again from it (`desk.js`). Restaging on app/system change is a
short fade. Everything else is 160 ms colour transitions. `prefers-reduced-motion` turns all of
it into instant state changes.

## Rules
- No eyebrows, no section numbers except the three real steps, no gradient text, no card grids.
- Claims come from data: tested counts are computed from `data/register.json` (native WhatsApp
  is listed as not supported, as in the app). Download and source links come from `/project.json` and
  stay disabled with a plain note until `repositoryUrl` is set.
- The drawn desktop is labelled "Illustration"; people and workspaces in it are invented.
- Pages: home (Persuade), `/apps/` (Read), `/report/` (Operate), `/privacy/` (Read).
