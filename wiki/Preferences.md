# Preferences

Open **Preferences** from the bottom of the navigation rail (or press `Ctrl+,` from anywhere).
Seven tabs.

## Appearance

- **Skins** — pick the active skin. `windows_11` is the reference skin; `default` is the
  built-in look.
- **Install Skin** — add a `.crpck` skin package (a ZIP of `theme.json` + icons). It's
  extracted on install. Skins change **colors, fonts, corner radius, spacing, and icons** —
  not window layout or control shapes.
- **Font** — override the app's UI font family.
- **Motion** — animation intensity for navigation transitions.
- **Developer / Future** — diagnostic toggles and placeholders.

## Behavior

- **Resume issues where you left off**
- **Reading past the last page opens the next issue**
- **Minimize to tray** (closing the window minimizes to tray while on)

## Libraries

- **Book Folders** — add/remove watched comic folders, toggle **Watch for changes** per
  folder, **Scan Now**, **Generate Covers**, **Sync Metadata**. See
  [Getting Started](Getting-Started).
- **Migrate from ComicRack CE** — **Migrate…** opens the importer and the Needs Review
  queue. See [Importing from ComicRack CE](Importing-from-ComicRack-CE).
- **Virtual Tags** — define named computed tags for [Smart Lists](Smart-Lists).

## Reader

- **Right to Left** — *Reverse left/right page-turn direction for right-to-left books*.
- **Display** — default fit mode, double-page spread default, **auto-rotate landscape
  pages**, **high quality page display** (smoother scaling, more CPU), **page transition**
  style, whether jumping animates.
- **Zoom & Navigation** — **reset zoom when turning the page**, mouse-wheel zoom speed.
- **Image Adjustment** — default brightness / contrast / saturation / gamma for every
  book (the reader toolbar adjusts further per book).
- **Background & Margin** — canvas background (*Auto* app background, or a fixed **color**),
  optional **margin around the page**.
- **Keyboard Shortcuts** — remap every reader command; **Import Layout… / Export
  Layout…**. See [Keyboard Shortcuts](Keyboard-Shortcuts).

## Advanced

- **App Behavior** — misc app-level toggles.
- **Rendering** — **Graphics backend**: *Auto* (GPU with software fallback — recommended),
  *Gpu* (forces GPU, no fallback), *Software* (CPU renderer, for broken GPUs / RDP / VMs).
  Also **Prefer native OpenGL over ANGLE** (only if the GPU renderer misbehaves — ANGLE /
  Direct3D is the better Windows default). *Changes take effect after restart.*
- **File Association** — register PaperBunkr as the handler for `.cbz` / `.cbr` / `.pdf` /
  `.epub` (tick each type).
- **Backup Manager** — **Backup Location**, **Backups to Keep**, **Backup Now**. Backups
  are copies of `paperbunkr.db`; see [Troubleshooting](Troubleshooting) for restoring.
- **Reading List Sources** — configure the online arc-lookup sources for
  [Reading Lists](Reading-Lists).
- **Trackers** — connect accounts for **AniList, MyAnimeList, Shikimori, Bangumi,
  MangaBaka**. Most need you to register your own API app and paste a Client ID (links are
  in the UI). Credentials go to your OS credential store.

## Plugins

Manage installed plugins — see [Plugins](Plugins).

## About

- **Updates** — current version, **Check for Updates**, and a toggle for checking on startup.
- **Changelog** — every release's notes, right in the app.
