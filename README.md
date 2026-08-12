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

- **Library**: series/issue browsing, Smart Lists, Reading Lists, migration from an existing
  ComicRack CE library.
- **Formats**: real page rendering for CBZ/CBR/PDF comics, plus a separate Books section for
  EPUB/PDF novels.
- **Reader**: fit modes, zoom, rotation, continuous/webtoon scroll, fullscreen with auto-hiding
  overlays, live brightness/contrast/saturation/gamma adjustment, background/margin customization.
- **Preferences**: skins, remappable keyboard shortcuts, database backups, file associations.

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
