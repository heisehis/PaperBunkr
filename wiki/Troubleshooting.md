# Troubleshooting

> PaperBunkr is early alpha. Keep external backups of files and metadata that matter.

## Where things live

Everything is under `%AppData%\Paperbunkr\` (paste that into Explorer's address bar):

| Item | Path |
|---|---|
| Library database | `paperbunkr.db` |
| Database backups | `backups\pb_*.db` |
| Logs & crash reports | `logs\startup.log` |
| Cover caches | `thumbnails\`, `book-thumbnails\`, `arc-covers\` |
| Skins | `skins\` |
| Plugins | `plugins\` |
| Graphics backend cache | `graphics.json` |

## The app won't start

1. Open `%AppData%\Paperbunkr\logs\startup.log` — the last lines usually name the failure.
2. **Graphics / GPU driver:** if the log mentions rendering, GL, ANGLE, or a driver, force
   the software renderer. With the app closed, edit the `AppSettings` / graphics backend
   value, or delete `graphics.json` and relaunch; once it starts, set
   **Preferences → Advanced → Rendering → Graphics backend → Software**.
3. **Corrupt database:** if the log points at EF Core / SQLite / migration, restore a
   backup (below).
4. Still stuck? Open an issue with `startup.log` attached at
   <https://github.com/heisehis/PaperBunkr/issues>.

## Restoring a database backup

1. Close PaperBunkr.
2. In `%AppData%\Paperbunkr\`, rename the current `paperbunkr.db` to `paperbunkr.db.bad`.
3. Copy the newest good `backups\pb_YYYYMMDD_HHMMSS.db` into `%AppData%\Paperbunkr\` and
   rename it to `paperbunkr.db`.
4. Start PaperBunkr.

Set backup frequency/retention in **Preferences → Advanced → Backup Manager**, and use
**Backup Now** before anything risky (a big migration, a bulk edit).

## Scanning problems

- **Files not showing up:** confirm the folder is listed in **Preferences → Libraries →
  Book Folders**, then **Scan Now**. EPUB/novel PDFs go in the **Books** section's own
  folder list instead.
- **Changes on disk not picked up:** turn on **Watch for changes** for that folder, or
  re-run **Scan Now**.
- **Wrong or missing covers:** **Generate Covers**. For a single item, use **Set Cover…**
  on its detail page.
- **Stale metadata after editing `ComicInfo.xml` externally:** **Sync Metadata**.

## Reader issues

- **Arrow keys do nothing right after opening an issue:** click once on the page area to
  focus it.
- **Wrong page-turn direction for manga:** set the series to **Right to Left** (right-click
  in Library → *Set Reading Direction*), or flip **Preferences → Reader → Right to Left**.
- **Slow scrolling / high memory in webtoon mode:** turn off **high quality page display**
  in **Preferences → Reader → Display**; very large libraries of huge scans are demanding
  on the current alpha.
- **Missing file when opening:** the file moved or was deleted — **Relink…** it from the
  migration **Needs Review** queue, or the *Missing Files* Smart List.

## Migration didn't find my CE library

Point **Browse…** at your `ComicDb.xml` directly. The default location is
`%AppData%\cYo\ComicRack Community Edition\ComicDb.xml`. Migration is always safe to
re-run — it never modifies the CE install.

## Reporting a bug

<https://github.com/heisehis/PaperBunkr/issues> — include your PaperBunkr version, what
you did, and `%AppData%\Paperbunkr\logs\startup.log`.
