<p align="center">
  <img src="src/Paperbunkr.App/Assets/paperbunkr-logo-source.png" alt="Paperbunkr" width="160" />
</p>

# PaperBunkr - Lightweight Desktop Comic & Manga Reader

<p align="center">
  <a href="https://github.com/heisehis/PaperBunkr/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/heisehis/PaperBunkr?label=release" /></a>
</p>

---

Paperbunkr is a free, ground-up rewrite of ComicRack: a local-first desktop library and reader for
CBZ/CBR comics, PDF, and EPUB — built on .NET and Avalonia UI, currently packaged for Windows.
Your library and settings never leave your machine; there's no cloud dependency or account
required.

> **Status: beta.** This is a solo project — expect rough edges here and there. Keep backups of
> anything you point it at.

## Key Features

- **Comic & manga reading** — real page rendering for `.cbz` / `.cbr` / `.pdf`, fit modes, zoom,
  rotation, page transitions, double-page spread, continuous/webtoon scroll, auto-scroll, and RTL
  (manga) reading direction.
- **Books** — a separate section for EPUB/PDF novels alongside your comics.
- **Library management** — series/issue browsing, Collections (manual or rule-based), pluggable
  sort/group, full keyboard navigation, browse history (back/forward), Smart Lists, Reading Lists,
  saved list layouts, and migration from an existing ComicRack CE library.
- **Home dashboard** — continue reading, "because you read" recommendations, and spotlight
  modules, driven by a relationship-aware recommendation engine.
- **Rich metadata** — series relations, continuity groupings, story events, and a live AniList
  adapter for external metadata lookups.
- **Custom reader experience** — fullscreen with auto-hiding overlays, live
  brightness/contrast/saturation/gamma adjustment, background/margin customization, and
  remappable keyboard shortcuts.
- **Plugins** — an installable plugin ecosystem with its own package manager.
- **Preferences** — skins, database backups, file associations, and in-app auto-update.

## Tech Stack

- **.NET 10** / **C#**
- **Avalonia UI** (Fluent theme) for the desktop UI
- **CommunityToolkit.Mvvm** for MVVM
- **Entity Framework Core + SQLite** for the local library database
- **Inno Setup** for the Windows installer

## Installation & Build

### Install a build

Grab the installer from the [latest release](https://github.com/heisehis/PaperBunkr/releases/latest)
and run it. It's fully self-contained — bundles its own .NET runtime, nothing else to install.
Windows only for now. Paperbunkr checks for new releases on startup and can update itself in-app.

### Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build Paperbunkr.sln
dotnet run --project src/Paperbunkr.App/Paperbunkr.App.csproj
```

To build a Windows installer yourself (requires [Inno Setup 6](https://jrsoftware.org/isdl.php)):

```powershell
pwsh installer/BuildInstaller.ps1
```

## Known limitations (beta)

- Windows only for now.
- No cloud sync — this is by design, not a gap, but worth knowing up front.
- Metadata providers beyond AniList (MAL, MangaDex, GCD, etc.) aren't wired up yet.

## Contributing & License

This is a solo project and not yet set up for external contributions (no `CONTRIBUTING.md` or
issue templates yet) — but bug reports and feedback are genuinely welcome via
[GitHub Issues](https://github.com/heisehis/PaperBunkr/issues).

No license has been chosen yet, so standard copyright applies (all rights reserved) until one is
added — don't assume MIT/Apache-style permissions in the meantime.

## Feedback

This is a beta. If something breaks, [open an issue](https://github.com/heisehis/PaperBunkr/issues) —
reports are genuinely welcome.
