# Getting Started

This walks you from a fresh install to a browsable library.

## 1. Point PaperBunkr at your comics

1. Open **Preferences** (bottom of the left navigation rail).
2. Go to the **Libraries** tab.
3. Under **Book Folders**, click **Add Folder…** and pick the folder that holds your
   `.cbz` / `.cbr` / `.pdf` files. Sub-folders are included.
4. Add as many folders as you like.

Supported comic formats: **CBZ, CBR, PDF** (page rendering). EPUB and novel-style PDFs
are handled in the separate [Books](Books-EPUB-and-PDF) section.

## 2. Scan

After adding a folder, click **Scan Now**. PaperBunkr walks the folders, reads embedded
`ComicInfo.xml` metadata where present, and creates **series** and **issue** records in
its database. Progress shows next to the buttons.

- **Generate Covers** — (re)builds the cached cover thumbnails for your library.
- **Sync Metadata** — re-reads metadata from the files on disk into the database
  (use this after you've edited `ComicInfo.xml` outside PaperBunkr).

## 3. Keep it up to date

Each watched folder has a **Watch for changes** checkbox. With it on, PaperBunkr picks up
files added to or removed from that folder while it's running. With it off, you re-run
**Scan Now** manually. **Open** reveals the folder in Explorer; **Remove** stops tracking
it (your files are untouched).

## 4. Browse

Click **Library** in the navigation rail. You'll see your series/issues as a cover grid.
From here you can:

- **Click a cover** to open it (issue → reader, series → detail page).
- **Right-click** any item for actions: mark read/unread, set content type and reading
  direction, edit properties, rate, show in Explorer, delete, and more.
- Use the **sidebar** to filter by collection or content type (Comic / Manga / Manhua / Manhwa).

See **[The Library](The-Library)** for searching, sorting, grouping, and saved layouts.

## 5. Read

Open any issue and see **[Reading](Reading)** for the full set of viewing controls, plus
**[Keyboard Shortcuts](Keyboard-Shortcuts)**.

---

**Coming from ComicRack?** Don't add folders manually — use
**[Importing from ComicRack CE](Importing-from-ComicRack-CE)** instead, which brings your
series, issues, read state, and metadata across in one step.
