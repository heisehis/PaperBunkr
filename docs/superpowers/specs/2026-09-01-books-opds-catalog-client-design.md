# Books OPDS Catalog Client — Design

**Fourth and last of four follow-ups** to the Thorium/eBoox research-doc application to Paperbunkr's
Books section. First (reader ergonomics + annotations/export), second (FB2/MOBI ingestion), and third
(screen-reader accessibility) are written and committed, all on hold pending implementation plans.

## Background

The research doc's catalog-federation domain bundled OPDS client support with cross-device cloud
sync of reading state. Cloud sync was dropped after clarifying the actual need: the user runs a
self-hosted **Kavita** and/or **Komga** server and wants Paperbunkr to browse and download from it,
not to synchronize reading position across installs. Paperbunkr remains local-first; this spec adds
one new capability — pulling books *into* the local library from a server the user controls — with no
account/cloud infrastructure of any kind.

Both Kavita and Komga implement **OPDS 1.2** (Atom/XML) reliably; Komga additionally offers OPDS 2.0
(JSON) but 1.2 is targeted as the common baseline both definitely support. Their auth models differ:
Kavita embeds an API key in the catalog URL path; Komga uses HTTP Basic auth.

This codebase already has a precedent for storing connection secrets: `ProviderCredential`/
`CredentialStore` (`docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md` §2), used by
the AniList/MyAnimeList/MangaBaka/Shikimori/Bangumi tracker adapters, with an explicit, documented
trust model — **plaintext storage, single-user local SQLite DB, no vault exists anywhere else in this
codebase**. This spec follows that same convention rather than introducing new encryption, since doing
otherwise would be inconsistent with how every other stored credential in this app is already handled.
`ProviderCredential` itself isn't reused directly (it's one credential set per provider *name*; OPDS
needs to support multiple named server connections, potentially more than one of the same kind), but
the plaintext/local-trust posture carries over to a new dedicated entity.

Downloaded books land in an existing watched Book Folder and go through the existing
`BookFolderScanService` pipeline — no separate import path.

**CE note:** no CE equivalent — nothing to verify against.

## Decisions

| Area | Decision |
|---|---|
| **Scope** | OPDS 1.2 client for Kavita and Komga specifically (not a generic any-OPDS-server client, though the underlying Atom parsing is server-agnostic). No cloud sync, no accounts. |
| **Data model** | New `OpdsServerConnection` entity: `Id`, `Name`, `BaseUrl`, `ServerKind` (enum: `Kavita`/`Komga`), `AuthValue1`/`AuthValue2` (meaning depends on `ServerKind` — Kavita's API key vs. Komga's username/password), `CreatedAt`, `LastUsedAt`. Plaintext, matching the existing `ProviderCredential` trust-model precedent. One EF migration. |
| **Auth handling** | `IOpdsAuthStrategy` per `ServerKind`: Kavita splices `AuthValue1` (the API key) into every request URL's path; Komga attaches an HTTP Basic auth header built from `AuthValue1`/`AuthValue2`. |
| **Feed parsing** | One shared `OpdsFeedParser` (Atom XML) regardless of server, since both target OPDS 1.2. Navigation feeds (links to sub-catalogs) and acquisition feeds (books with download links) both parsed by it. |
| **Search** | Uses each feed's own OpenSearch `<link rel="search">` description when present (both servers support it) — no bespoke query mechanism. |
| **Download destination** | User picks among their existing watched Book Folders (no new import path) — download streams there, then `BookFolderScanService.ScanAllAsync()` is invoked immediately (not waiting for its next scheduled pass) so the book appears in the library right away. |
| **Connection management UI** | Preferences → Libraries, alongside the existing "Book Folders"/"Comic Library Folders" sections — a new "OPDS Servers" section: add/edit/remove connections, "Test Connection" button (fetches the root feed to confirm it resolves before saving). |
| **Catalog browsing UI** | A new screen, reached via a toolbar action on `BooksScreen` ("Browse Server ▾" listing saved connections) — not a permanent nav-rail item, matching how this app treats occasional/secondary workflows versus its six permanent rail screens (Home/Library/Books/Smart Lists/Reading Lists/Events/Preferences). Navigation-feed breadcrumb drilling into libraries/collections, bottoming out in an acquisition-feed grid reusing `BookCardSample`-style tiles from `BooksScreen` (cover thumbnail from the feed's own link, title/author, Download button per book). |

## Components

### 1. Data model

- `OpdsServerConnection` (`Paperbunkr.Data.Entities`) per the Decisions table. One EF migration
  (enum-as-string + sentinel pattern for `ServerKind`, matching every other enum column in this
  codebase).

### 2. `Paperbunkr.Engine/IO/Provider/Opds/` — client library

- `IOpdsAuthStrategy` + `KavitaAuthStrategy`/`KomgaAuthStrategy` implementations (URL-key-injection
  vs. Basic-auth header, per Decisions).
- `OpdsFeedParser`: parses an Atom XML response into either a navigation-feed model (list of
  sub-catalog links) or an acquisition-feed model (list of book entries — title, author, cover-thumbnail
  link, one-or-more acquisition links tagged by format, an optional OpenSearch description link).
- `OpdsCatalogClient`: takes an `OpdsServerConnection`, resolves its `IOpdsAuthStrategy`, and exposes
  `FetchNavigationFeed(url)` / `FetchAcquisitionFeed(url)` / `DownloadAsync(acquisitionLink, destinationPath)`.
  All requests go through the resolved auth strategy uniformly — callers never branch on `ServerKind`
  themselves.

### 3. `Paperbunkr.App/Services/OpdsConnectionService.cs`

- CRUD over `OpdsServerConnection` rows (mirrors other simple entity-management services in this
  codebase), plus a `TestConnectionAsync` method used by both the Preferences "Test Connection" button
  and internally before the first browse of a newly-added connection.
- `DownloadAndImportAsync(connection, acquisitionLink, targetBookFolder)`: streams the download via
  `OpdsCatalogClient`, then calls the existing `BookFolderScanService.ScanAllAsync()`.

### 4. UI

- **Preferences → Libraries**: new "OPDS Servers" section (list of connections + add/edit/remove +
  Test Connection), same visual/interaction pattern as the existing Book Folders section on the same
  tab.
- **`BooksScreen.axaml`**: new "Browse Server ▾" toolbar button (only shown when at least one
  connection exists) opening a small picker, then navigating to the new browse screen.
- **New `OpdsBrowseScreen`/`OpdsBrowseScreenViewModel`**: breadcrumb navigation state (stack of
  visited navigation-feed levels), current acquisition-feed grid (reusing `BookCardSample` tile
  visuals), a search box bound to the current feed's OpenSearch link when present, per-book Download
  button wired to `OpdsConnectionService.DownloadAndImportAsync` with a target-folder picker (or the
  connection's last-used folder, remembered via a per-connection preference to avoid re-asking every
  download).

## Risks / Open Questions

- **Neither Kavita's nor Komga's OPDS implementation has been exercised against this codebase yet** —
  both are documented as OPDS 1.2-compliant, but real-world quirks (pagination behavior, exact
  acquisition-link `type` values, thumbnail link conventions) should be validated against real running
  instances early in implementation, not assumed from the spec alone.
- **Large libraries and pagination**: OPDS feeds paginate via `<link rel="next">`; the browse screen
  needs to handle this (infinite-scroll or a Next button) rather than assuming a feed fits on one page
  — worth confirming actual page sizes Kavita/Komga return by default during implementation.
- **Format selection**: an acquisition entry can offer multiple format links (e.g. both EPUB and PDF
  for the same book) — the Download button should let the user pick when more than one is offered,
  defaulting to EPUB when available.

## Testing

- `OpdsFeedParserTests`: real sample feed fixtures (captured or hand-authored Atom XML matching
  Kavita's and Komga's actual response shapes) for navigation feeds, acquisition feeds, and OpenSearch
  descriptions.
- `KavitaAuthStrategyTests`/`KomgaAuthStrategyTests`: URL construction and Basic-auth header
  construction respectively.
- `OpdsConnectionServiceTests`: CRUD, `DownloadAndImportAsync` end-to-end against a fixture server
  response (mocked HTTP) verifying the file lands in the target folder and `ScanAllAsync` is invoked.
- `OpdsServerConnection` migration test, mirroring `AddBooksBrowseStateMigrationTests`.
- UI automation (FlaUI, `Paperbunkr.UiTests`): add-connection → test-connection → browse → download
  flow, since this is exactly the kind of multi-step interactive flow this codebase's UI-automation
  suite already exists to cover.
