# Preferences

Open **Preferences** from the bottom of the navigation rail (or press `Ctrl+,` from anywhere).
Seven tabs.

## Appearance

**Themes**

- Pick the active theme from the list. Each theme is either **Light** or **Dark** and sets the
  colors, corner radius, spacing and icons. Built in: the default look, `windows_11`, **Daylight**,
  **Overcast**, **Matrix**, **Maximum Contrast** and a **colour-blind-safe** theme.
- **Install…** adds a `.crpck` theme package (a ZIP of `theme.json` + icons). It's extracted on
  install. Themes change **colors, fonts, corner radius, spacing, and icons** — not window layout
  or control shapes.

**Theme options**

- **True black** — forces pure black backgrounds on dark themes; saves battery on OLED screens.
  It is suspended automatically while you're reading.
- **Matrix rain** — the falling-glyph background of the Matrix theme. Turn it off for a static
  Matrix look; this also saves GPU and battery.
- **Auto switch** — follow the system Light/Dark preference, or switch on a schedule with
  **Switch to dark at** and **Switch to light at** (local hour, 0–23).
- **Auto-enable true black at** — a local hour (0–23) at which true black turns on by itself;
  leave blank to toggle it manually.
- **Accent color override** — a hex color such as `#FF8800`; blank uses the theme's own accent.

**Font**

- **Font family** — override the app's UI font, with a live preview line.

**Interface**

- **Reduce motion** — shortens UI transitions to effectively instant.
- **Expand nav rail on hover** — when off, the rail only expands while pinned open.

## General

**Startup**

- **Reopen the screen I was on last time** — off means every launch opens Home.
- **Scan library folders for new files at startup** — picks up files added or removed while
  Paperbunkr wasn't running; runs in the background. Off by default.

**Reading**

- **Resume issues where you left off**
- **Reading past the last page opens the next issue**
- **Ask me to rate a comic when I finish it** — opens the Quick Rate prompt at the end of a book.
  Off by default.

**Library**

- **Allow importing files by dragging them into the window** — on by default; turn off to disable
  drag-and-drop import on the Library and Reading List screens.

**Window**

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
- **Auto-hide toolbar when idle** — fades the floating toolbar after a few seconds without pointer
  movement. Off keeps it always visible.
- **Chrome reveal style** — *PerCluster*: each corner control pops in only when you hover it.
  *Ambient*: any pointer movement reveals the whole toolbar at once.
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
  `.epub` / `.fb2` / `.mobi` (tick each type).
- **Backup Manager** — **Backup Location**, **Backups to Keep**, **Backup Now**. Backups
  are copies of `paperbunkr.db`; see [Troubleshooting](Troubleshooting) for restoring.
- **Reading List Sources** — configure the online arc-lookup sources for
  [Reading Lists](Reading-Lists).
- **Trackers** — connect accounts for **AniList, MyAnimeList, Shikimori, Bangumi,
  MangaBaka, MangaUpdates, Kitsu, MangaDex**. Most need you to register your own API app
  and paste a Client ID (links are in the UI). Credentials go to your OS credential store.
- **Tracking behavior** — automatic progress updates, the mark-as-read prompt, and more.
  Step-by-step setup for every tracker is on the [Trackers](Trackers) page.

## Plugins

Manage installed plugins — see [Plugins](Plugins).

## About

- **Updates** — current version, **Check for Updates**, and a toggle for checking on startup.
- **Changelog** — every release's notes, right in the app.
