# Books improvement arc — Slice 4c: OPDS catalogs (design)

Date: 2026-10-27. Status: **built, uncommitted** (confirmed 2026-10-28 via source and tests; on-screen check by the user outstanding). Decisions: Slice 4 grilling rounds 1 and 2, all accepted. **Assumed** = not asked.

## 1. Goal

Browse and download from OPDS catalogs (Calibre content server, Calibre-Web, Komga, Kavita, COPS) into the Books library. **Client only**; serving your own library over OPDS is out of scope.

## 2. Protocol (OPDS 1.2, Atom)

- Parse Atom feeds with `System.Xml.Linq` into `OpdsFeed` (title, entries, links) and `OpdsEntry` (title, authors, summary, series from `calibre:series` / `calibre:series_index` when present, categories, published date, language, links). Link relations used: `self`, `start`, `up`, `next`, `search` (OpenSearch description or a `{searchTerms}` template), `subsection` / navigation entries, `http://opds-spec.org/acquisition[/open-access|/buy]`, `http://opds-spec.org/image` and `/image/thumbnail`.
- A feed is a **navigation feed** (entries link to other feeds) or an **acquisition feed** (entries carry acquisition links) — decided per entry by its links.
- `next` pagination is followed on demand as the user scrolls ("Load more"). OPDS 2.0 (JSON) is out of scope.

## 3. Catalogs and credentials

New table `OpdsCatalog(Id, Name, Url, Username, ProtectedPassword, AddedTime, SortOrder)` — one migration `AddOpdsCatalogs`. The password is stored through the existing per-user encrypted credential store (`Paperbunkr.Data/Credentials`, **verified to exist**), never in the clear; HTTP basic auth is sent only to the catalog's own host. A plain `http://` URL shows a one-line warning ("sent without encryption") but is allowed. Redirects to another host drop the credentials.

## 4. UI

A **Catalogs** button on the Books toolbar opens a full-screen browser (a `ScreenInput` screen like the others): a left list of catalogs with Add / Edit / Remove, and a right pane with a breadcrumb path, a search box (when the catalog has a search link), and a grid of entries using the existing card families: navigation entries as text tiles, acquisition entries as book cards with thumbnail, title, author, series and a Download button (a format chooser flyout when several are offered; otherwise the first of EPUB, FB2, MOBI, PDF). Already-downloaded books (matched by title + author or the catalog entry id kept in a small `OpdsDownload` log) show "In library". Add/Edit uses a `DialogCard`. Tab bumpers, F5 (reload feed), Ctrl+F (search) and back/up follow the input rules.

## 5. Downloads

- Into a user-chosen **Downloads folder** (setting `AppSettings.OpdsDownloadFolder`, picked the first time one is needed). If it is not already a Books library folder, the first download offers to add it as one; the normal folder scan then imports the file.
- Files stream to `{name}.part` and are renamed on completion (the existing `.crswap` / sweeper pattern is *not* reused); a cancelled or failed download deletes the part file. Names are sanitised and de-duplicated.
- After the scan imports the file, the catalog's metadata (series, summary, categories as tags) is applied through `BookMetadataApplier` with source `Opds` (a new `BookFieldSourceKind`, stored by name) in FillEmpty mode. **Assumed**: the link between the download and the imported book is the file path, resolved when the scan adds the book.
- Progress and failures use the Activity Center, not toasts.

## 6. Tests and verification

- Parser tests on captured feed samples written by hand for Calibre content server, Calibre-Web and Komga shapes (navigation, acquisition, pagination, search templates, relative links, missing links).
- A fake `HttpMessageHandler` for browsing, auth headers (sent to the same host only), redirect handling and a download with cancel; credential protection round trip; catalog CRUD; "In library" matching.
- **On the real library:** Calibre's content server can serve OPDS from the user's new Calibre; start it, add `http://localhost:8080/opds`, browse and download one book into a scratch folder (**asked first**, since it starts a server on the user's machine).
- On screen: the catalog browser at 1366×768.

## 7. Out of scope

OPDS 2.0, serving OPDS, DRM / paid acquisition flows, OAuth catalogs, automatic periodic checks for new entries, uploading.
