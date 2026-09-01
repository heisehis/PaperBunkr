# Installation

PaperBunkr is **Windows-only** for now. Mac/Linux are a later goal.

## Install a packaged build (recommended)

1. Go to the [latest release](https://github.com/heisehis/PaperBunkr/releases/latest).
2. Download the installer (`.exe`).
3. Run it and follow the prompts. Two options along the way are off by default and worth
   knowing about: **Launch PaperBunkr when Windows starts**, and **Associate comic/manga files**
   with PaperBunkr (`.cbz`, `.cbr`, `.cb7`, and others) — you can also turn file associations on
   or off later, per format, from Preferences → Advanced.

The build is fully self-contained — it bundles its own .NET runtime, so there is
nothing else to install. When it finishes, launch **PaperBunkr** from the Start menu.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the repo's
`global.json` pins the exact toolchain version).

```bash
git clone https://github.com/heisehis/PaperBunkr.git
cd PaperBunkr
dotnet build Paperbunkr.sln
dotnet run --project src/Paperbunkr.App/Paperbunkr.App.csproj
```

## Where PaperBunkr keeps its data

Everything lives under `%AppData%\Paperbunkr\`:

| Path | Contents |
|---|---|
| `paperbunkr.db` | the SQLite library database |
| `backups\` | automatic and manual database backups |
| `thumbnails\`, `book-thumbnails\`, `arc-covers\` | cached cover images |
| `logs\` | `startup.log` and crash reports |
| `skins\` | installed skins |

Your comic/manga files themselves are **never moved or modified** — PaperBunkr only
reads them from wherever they already are.

## Updating

PaperBunkr checks for new releases on startup and lets you know when one's available; you can
also check manually from **Preferences → About → Check for Updates**. Approve the update and it
downloads and installs itself. You can turn off the startup check in the same About section if
you'd rather update manually — grab the newer installer from the
[latest release](https://github.com/heisehis/PaperBunkr/releases/latest) and run it over the top.
Either way, your database in `%AppData%\Paperbunkr\` is left in place and migrated automatically
on first launch. See [Troubleshooting](Troubleshooting) if an update won't start.

## Uninstalling

Use **Add or remove programs** in Windows. The uninstaller asks whether to also delete your
library data (database, settings, and cached thumbnails) from `%AppData%\Paperbunkr\` — say yes
if you want a clean removal, or no to keep your library around for a future reinstall. Either
way, your comic/book files themselves are never touched.
