# Installation

PaperBunkr is **Windows-only** for now. Mac/Linux are a later goal.

## Install a packaged build (recommended)

1. Go to the [latest release](https://github.com/heisehis/PaperBunkr/releases/latest).
2. Download the installer (`.exe`).
3. Run it and follow the prompts.

The build is fully self-contained — it bundles its own .NET runtime, so there is
nothing else to install. When it finishes, launch **PaperBunkr** from the Start menu.

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the repo's
`global.json` pins the toolchain; newer SDKs are used for local dev but 8 is the shipping target).

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

Download and run the newer installer over the top. Your database in `%AppData%\Paperbunkr\`
is left in place and migrated automatically on first launch. See
[Troubleshooting](Troubleshooting) if an update won't start.

## Uninstalling

Use **Add or remove programs** in Windows. That leaves `%AppData%\Paperbunkr\` behind —
delete that folder by hand if you also want to remove your library database and caches.
