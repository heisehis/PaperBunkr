<p align="center">
  <img src="src/Paperbunkr.App/Assets/paperbunkr-logo-source.png" alt="Paperbunkr" width="160" />
</p>

<h1 align="center">Paperbunkr</h1>

<p align="center">A ComicRack-inspired comic &amp; manga library and reader for Windows, built fresh from scratch.</p>

<p align="center">
  <a href="https://github.com/heisehis/PaperBunkr/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/heisehis/PaperBunkr?include_prereleases&label=alpha%20release" /></a>
</p>

---

> **Status: early alpha.** Expect rough edges. Keep backups of anything you point it at.

Paperbunkr is a ground-up rewrite of ComicRack — not a fork, not a plugin layer — built on
Avalonia/.NET 8. It keeps ComicRack's core local-first, no-cloud-dependency philosophy (your
library and settings never leave your machine) while adding continuous/webtoon scroll, live image
adjustment, and other reading conveniences ComicRack never had.

## Install

Grab the installer from the [latest release](https://github.com/heisehis/PaperBunkr/releases/latest)
and run it. It's fully self-contained — bundles its own .NET 8 runtime, nothing else to install.
Windows only for now.

## What's in

- **Home**: continue reading, "because you read" recommendations, and spotlight modules, driven by
  a relationship-aware recommendation engine (not just whole-library similarity).
- **Library**: series/issue browsing, pluggable sort/group, browse history (back/forward), Smart
  Lists, Reading Lists, saved list layouts, migration from an existing ComicRack CE library.
- **Metadata**: series relations, continuity groupings, story events, and a live AniList adapter
  for external metadata lookups.
- **Formats**: real page rendering for CBZ/CBR/PDF comics, plus a separate Books section for
  EPUB/PDF novels.
- **Reader**: fit modes, zoom, rotation, page transitions, double-page spread, continuous/webtoon
  scroll, auto-scroll, fullscreen with auto-hiding overlays, live brightness/contrast/saturation/
  gamma adjustment, background/margin customization, remappable keyboard shortcuts.
- **Preferences**: skins, database backups, file associations.

## Known limitations (alpha)

- Windows only, and only tested on x64 so far.
- No cloud sync — this is by design, not a gap, but worth knowing up front.
- Some reader features (double-page spread, remapped shortcuts, auto-scroll) are automated-test
  verified but still pending broader manual on-screen testing across setups.
- Metadata providers beyond AniList (MAL, MangaDex, GCD, etc.) aren't wired up yet.

## Building from source

Requires the .NET 8 SDK.

```bash
dotnet build Paperbunkr.sln
dotnet run --project src/Paperbunkr.App/Paperbunkr.App.csproj
```

To build a Windows installer yourself (requires [Inno Setup 6](https://jrsoftware.org/isdl.php)):

```powershell
pwsh installer/BuildInstaller.ps1
```

## Feedback

This is an alpha. If something breaks, [open an issue](https://github.com/heisehis/PaperBunkr/issues) —
reports are genuinely welcome.
