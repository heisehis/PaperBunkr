# Landing site redesign (heisehis.github.io/PaperBunkr)

**Date:** 2026-09-27
**Status:** approved in a grilling session (3 rounds + a design summary), implementation requested the same day.

## Problem

1. **The live site is stale because every Pages build fails.** Pages uses the legacy
   "deploy from `master` `/docs`" source, so Jekyll renders every Markdown file under `docs/`.
   `docs/superpowers/specs/2026-09-09-installer-redesign-plan.md` contains a literal `{{`, which is a
   Liquid syntax error. Every build since at least 2026-09-14 has errored, and the site still serves an
   old build.
2. **Even the repo copy is stale.** `docs/index.html` hard-codes `0.3.0-beta` and a "New in 0.3.0"
   list. The current release is `0.7.3-beta`.
3. **The site doesn't look like the product.** It uses a generic blue dev-tool style and the old teal
   hooded-reader logo. The app's brand is a near-black background, amber `#C9803F`, Bebas Neue
   headings, Source Serif 4 body text and the amber crate "P" mark.

## Decisions

| # | Decision |
|---|---|
| Scope | One landing page. Link out to the wiki, releases and CHANGELOG instead of copying them. |
| Visual | Direction A ("Bunker"): the app's own brand, dark only. |
| Framework | **Astro**, in a new `site/` folder at the repo root. No UI framework integration; plain Astro `<script>` for the lightbox and mobile menu. |
| Logo | The app's existing `Assets/paperbunkr-logo-source.png`, optimised by `astro:assets`, plus a simplified favicon. The teal logo is retired from the site. |
| Features | Four themes (Read / Organise / Discover / Automate) as alternating screenshot + text rows. |
| Release data | Fetched from the GitHub API **at build time** (tag, date, installer asset URL and size, release body). A failed fetch fails the build in CI; local dev falls back to `/releases/latest`. |
| Download | Links directly to the `PaperbunkrSetup-*.exe` asset, labelled with version and size. Falls back to the releases page. |
| Deploy | `.github/workflows/site.yml`: builds on PRs touching `site/**`; builds and deploys (`actions/deploy-pages`) on pushes to `master` touching `site/**` and on `workflow_dispatch`. `release.yml` gets a final `refresh-site` job that runs `gh workflow run site.yml --ref master`. A release created with `GITHUB_TOKEN` cannot trigger a `release: published` workflow, and calling the site workflow as a reusable workflow would run it on the release tag, which the `github-pages` environment's default branch rule rejects. Dispatching on master avoids both. |
| Pages source | Switched from "master /docs" to "GitHub Actions" (`gh api -X PUT repos/heisehis/PaperBunkr/pages -f build_type=workflow`), **only after the user confirms at that moment**. Side effect: the `docs/*.md` internal notes stop being served on the site. |
| Fonts | Self-hosted via `@fontsource/bebas-neue` and `@fontsource-variable/source-serif-4`. No Google Fonts requests, in keeping with the "no cloud" message. |
| SEO | `@astrojs/sitemap`, canonical URL, a 1200×630 Open Graph card, and `SoftwareApplication` JSON-LD whose `softwareVersion` comes from the build-time release data. The Google verification file moves to `site/public/`. |
| Tooling | npm with the lockfile committed, Node 22 in CI, and `astro check` before `astro build`. |
| Cleanup | Delete `docs/index.html`, `docs/sitemap.xml` and `docs/google9d51fa1649c7e040.html` (moved or replaced). Keep `docs/assets/`, which the README uses. |

## Architecture

```
site/
  astro.config.mjs        site: https://heisehis.github.io, base: /PaperBunkr, sitemap integration
  package.json            astro, @astrojs/check, @astrojs/sitemap, typescript, @fontsource*
  public/                 favicon.png, google9d51fa1649c7e040.html, og-card.png
  src/
    assets/               logo.png, screenshots (copied from docs/assets or new shots)
    lib/release.ts        getLatestRelease(): Promise<Release>, the only network code
    lib/content.ts        the four themes, CE bullets, facts: plain data, no markup
    styles/global.css     tokens (:root), resets, type scale, .wrap, buttons, focus ring
    components/           Nav, Hero, FactStrip, ThemeRow, ComicRackBand,
                          GetStarted (includes the
                          release card), Features, Footer, Lightbox, Reveal (IntersectionObserver)
    layouts/Base.astro    <head>: meta, OG/Twitter, JSON-LD, fonts, favicon
    pages/index.astro     composes the sections in order
```

**`release.ts` contract.** It returns
`{ version, tag, publishedAt, htmlUrl, installer: { url, sizeBytes } | null, highlights: string[] }`.
`highlights` holds the first 4 bolded lead-ins from the release body's `### Added` list (for example
"Comic reader: page intelligence."). The request uses `GITHUB_TOKEN` when it is present. If
`process.env.CI` is set, any failure throws; otherwise it logs a warning and returns a fallback whose
`installer` is `null` and whose `version` is `"latest"`.

**Components** take plain props. Only `Lightbox` (open/close, Esc, focus trap, focus restore), the
`Nav` mobile toggle and `Reveal` ship client JS.

## Page content

1. **Nav:** the logo and a "PAPERBUNKR" wordmark in Bebas Neue; links to Features, ComicRack users,
   Wiki and GitHub; an amber Download button. Below 760px the links collapse into a disclosure menu.
2. **Hero:** kicker `Free · Windows · v{version}`; H1 "Your comics. Your machine."; one-paragraph intro
   (a local-first comic, manga and book library and reader, a ground-up successor to ComicRack, no
   cloud and no account); primary "Download for Windows" with version and size, and secondary "View
   on GitHub"; meta line "Windows 10+ · self-contained installer · updates itself". The Home screenshot
   sits in perspective on the right over the red and amber glow.
3. **Fact strip:** Free & open source (AGPL-3.0) · Local-first, no account · Reads CBZ, CBR, PDF, EPUB,
   FB2, MOBI/AZW3 · Imports ComicRack CE.
4. **Themes** (`#features`), with contents taken from CHANGELOG, not invented:
   - **01 Read:** guided view, smooth 25–400% zoom, auto levels, sharpening and auto-crop, continuous
     and webtoon scroll, RTL manga and spreads, reader profiles, warm tint and eye-rest, Xbox
     controller, the next issue opening in the background; the Books section (EPUB, PDF, FB2,
     MOBI/AZW3, highlights and notes).
   - **02 Organise:** series and issue views, Collections, Smart Lists (nested AND/OR, regex),
     Reading Lists (CBL/CSV), Virtual Tags, single and bulk editors, ComicInfo.xml write-back,
     Library Organizer, Library Health (missing files, duplicates, review queues).
   - **03 Discover:** Home recommendations, Insights (goals, trends, year-in-review), story events
     and continuity maps, ComicVine and Metron scraping, AniList, MangaBaka, MangaUpdates, MangaDex
     and Kitsu tracking, Wanted list and pull list.
   - **04 Automate:** scheduled tasks (rescans, backups, cover checks, ad-page detection), Activity
     Center, plugins (package manager, Python commands).
   - **A closer look** (after the four rows): a 2×2 gallery of the extra screenshots (series
     detail, the issues grid, Reading Lists, Insights Trends), using the same lightbox.
5. **ComicRack band** (`#comicrack`): a non-destructive, re-runnable import of series, issues, read
   state and metadata; near-duplicate series flagged for merge or keep; guessed content types
   reviewed in Library Health; CE's publisher icon pack and reader textures included. Links to
   `wiki/Importing-from-ComicRack-CE`.
6. **Get started** (`#download`), with the **latest release card** as a sticky side panel: version,
   date, up to 4 highlights (one per "Prefix:" group, so four "Comic reader: …" entries don't crowd
   out the rest), a Download button, "Full release notes" and "All releases".
   1 install (bundles its own .NET runtime), 2 add a comics folder, 3 or import from
   CE. Then links to the wiki and README.
7. **Footer:** GitHub · Wiki · Releases · Issues · Privacy · ComicVine & Metron notice; the AGPL-3.0
   line; "Built on .NET 10 and Avalonia UI".

## Visual and behaviour details

- Tokens: `--bg #0b0b0d`, `--panel #141210`, `--line #2a2723`, `--text #ece6dd`, `--muted #b0a89c`,
  `--faint #7d766c`, `--accent #c9803f`, `--accent-text #e0995a`, `--glow-red rgba(140,40,40,.45)`.
  Squircle radii (8–12px), never 999px pills (house rule).
- Type: Bebas Neue for H1, H2 and theme titles; Source Serif 4 for everything else.
- Images: `<Image>`/`<Picture>` with WebP and width variants. The hero image is eager; everything
  else lazy-loads.
- Motion: sections fade and rise 12px on first view; disabled under `prefers-reduced-motion`. The hero
  perspective is flattened below 900px.
- Accessibility: visible `:focus-visible` ring in `--accent-text`; skip link; real alt text; lightbox
  is `role="dialog"` with `aria-modal` and returns focus. No horizontal scroll at 360px, 16px
  gutters.

## Screenshots

Supplied by the user on 2026-09-27 (1366×720). Each had a screen-recorder overlay pill at the top
centre (about x 622–742, y 0–36), painted out by blending the pixels either side of it.
Mapping: Home → hero; Books → Read; Library with the preview panel → Organise; Insights Today →
Discover; the older Preferences → Automation shot → Automate; detail, issues grid, Reading Lists and
Insights Trends → the gallery.

Deliberately not used:
- **Event Map**: not in any released CHANGELOG entry yet. Add it once it ships.
- **Wanted → Releases**: its Request buttons belong to the Prowlarr/qBittorrent acquisition feature,
  which the user asked to keep off the public site. The text mentions only the Metron pull list and
  Wanted list.

A comic-reader screenshot (ideally guided view) would suit the Read row better than Books.

## Verification

- `npm run check` (astro check) and `npm run build` both pass.
- Preview in the browser pane at desktop width and at 375px: no horizontal scroll, the menu works,
  the lightbox opens and closes with Esc, and focus returns to the trigger.
- Built HTML contains the real version, an `.exe` download URL, canonical, OG tags and JSON-LD.
- After deploy (with the user's go-ahead): the live URL serves the new page, and
  `pages/builds` / Actions show success.

## Out of scope

- The app's own `welcome-source.png` still uses the teal logo.
- `wiki/Importing-from-ComicRack-CE.md` still places the Needs Review queue in the migration overlay
  (moved to Library Health in 0.7.3).
- Redrawing the mark as a vector.
