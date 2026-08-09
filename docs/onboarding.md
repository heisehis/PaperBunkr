# Paperbunkr — Project Onboarding & Phase 0 Findings

*A ground-up rewrite of ComicRack, built on Avalonia/.NET, that preserves ComicRack's core principles while adding Tachiyomi/Mihon-inspired manga/manhua/manhwa library and reading features. This document consolidates everything decided and discovered during Phase 0, replacing ten separate working docs with one coherent source of truth.*

## Table of contents
1. [Identity & scope](#1-identity--scope)
2. [Provenance & licensing](#2-provenance--licensing)
3. [Engine portability (Phase 0 discovery)](#3-engine-portability-phase-0-discovery)
4. [Native dependencies & the retarget spike](#4-native-dependencies--the-retarget-spike)
5. [Database](#5-database)
6. [Data model schema](#6-data-model-schema)
7. [Content-type classification](#7-content-type-classification)
8. [Reader canvas architecture](#8-reader-canvas-architecture)
9. [Manga/manhua/manhwa metadata scraping](#9-mangamanhuamanhwa-metadata-scraping)
10. [Plugin API v2](#10-plugin-api-v2)
11. [Existing plugin portfolio → core vs. plugin](#11-existing-plugin-portfolio--core-vs-plugin)
12. [Wireframes & Avalonia translation](#12-wireframes--avalonia-translation)
13. [Skin/theme system](#13-skinstheme-system)
14. [Migration UX](#14-migration-ux)
15. [Open items](#15-open-items)
16. [Release staging: Alpha & Beta](#16-release-staging-alpha--beta)

---

## 1. Identity & scope

- **Name:** Paperbunkr. Chosen deliberately distinct from "ComicRack" given the trademark angle discussed in §2 — "paper" nods to the physical medium, "bunker" reflects the local-first, no-cloud-dependency architecture that's core to what this is.
- **Scope:** Full rewrite, not a plugin layer on ComicRack CE. Desktop-first, local-first (no client-server model like Komga/Kavita). Cross-platform (Mac/Linux) is a later-phase goal, not a v1 requirement — Windows-first for now.
- **Stack:** Avalonia on modern .NET (8/9/10), no webview anywhere (this rules out Tauri and Electron, both of which render through a webview under the hood). Chosen over Qt/Rust/Flutter alternatives specifically to preserve C#/.NET continuity with ComicRack CE's actual backend and its plugin-authoring community — see §3.
- **Deferred by explicit choice:** a Tachiyomi-style source/extension system for browsing and downloading chapter content from external sites. Everything in this document (metadata scraping, tracking) is scoped to enriching files the user already owns locally, not fetching new content. Revisit this as a separate decision later if desired.

## 2. Provenance & licensing

ComicRack CE is licensed GPLv2, but — per the maintainer's own README — it's a **decompilation of proprietary commercial software** (the original ComicRack by Markus Eisenstöck/cYo), republished without confirmed authorization from the original copyright holder. The maintainer applying GPL to it doesn't necessarily make that license valid, since he doesn't appear to hold the copyright needed to grant it.

**Decision:** proceed anyway (explicit call), accepting the risk. Mitigation practice agreed: keep architectural separation between "adapted from CE" and "written new for Paperbunkr" as the project is built — file-level or module-level provenance tracking, not a heavy process, just enough that a targeted response (strip and clean-room-reimplement specific files) is actually possible if it's ever needed, rather than an all-or-nothing exposure. Being open source (which GPLv2 requires regardless) does not cure a takedown — it doesn't grant rights to someone else's proprietary work, it only governs code Paperbunkr is actually entitled to license.

**Practical implication already applied:** the native P/Invoke shims (§4) are being written fresh against their underlying open-source libraries rather than ported verbatim, specifically to reduce derived-work surface area where it costs nothing to do so.

## 3. Engine portability (Phase 0 discovery)

Source: `maforget/ComicRackCE`, evaluated directly (cloned, read, not assumed from memory).

**Architecture is already layered**, better than expected:
```
cYo.Common                      → general-purpose utilities
cYo.Common.Windows/.Presentation → WinForms-specific utility/presentation layers
ComicRack.Engine                 → core domain: database, IO, metadata, providers
ComicRack.Engine.Display.Forms   → WinForms-specific display layer (already split from Engine)
ComicRack.Plugins                → plugin host + IronPython bridge
ComicRack                        → the WinForms application shell
```

**Target framework:** everything is .NET Framework 4.8 (`net48`). A retarget to modern SDK-style `net8.0`+ projects is required across the board, even for pure-logic code.

**WinForms coupling is much shallower than the file layout suggests.** Only 15/81 files in `ComicRack.Engine` (excl. `Controls/`) and 27/326 in `cYo.Common` reference `System.Windows.Forms` at all. Checked the most concerning case directly: `ComicBook.cs` (3,076-line core domain model) has exactly one WinForms call — `Clipboard.SetDataObject()` — trivial to strip. Same pattern in `BackupArchiveCreator.cs`/`PdfStorageProvider.cs` (`Application.CompanyName`/`ProductName` used as string values). **The core domain model is portable with minimal, mechanical changes, not a rewrite.** Real coupling is concentrated in `ComicRack.Engine/Controls/*` (custom UI controls) — expected to be rebuilt natively regardless.

**Format/provider architecture is extensive and worth preserving almost entirely** — this is the single biggest scope-saver in the codebase:
- Archive formats: CBZ, CBR, CB7, CBT, PDF, DjVu, each with dedicated provider classes
- Archive engines: SharpCompress, 7-Zip, ZipSharp, TarSharp, pluggable via `ProviderFactory`
- Image codecs: WebP, HEIF/AVIF, JPEG 2000, JPEG XL, PDF rendering via Pdfium and Ghostscript
- Metadata: `ComicInfoProvider`, `MetronInfoProvider`, `XmlInfoProvider` — directly relevant to the manga-scraper and Comic Vine Scraper work (§9, §11)

**Database/library layer is compact:** `ComicDatabase.cs` (444 lines) + `ComicLibrary.cs` (514 lines), under 1,000 lines total.

**Plugin system:** `IPluginEnvironment` interface (extends `IPluginConfig`, `ICloneable`) exposing `MainWindow`, `App`, `OpenBooks`, `Browser`, `ComicDisplay`, `CommandPath`, paired with pluggable `*Initializer` classes (`XmlPluginInitializer`, `PythonPluginInitializer`). The shape is sound and reusable; `PythonPluginInitializer` is only 66 lines — IronPython is a thin adapter, not deeply embedded. See §10.

**Bottom line:** this is a "port the engine, rebuild the UI" project, not a from-scratch rewrite of comic-format handling.

## 4. Native dependencies & the retarget spike

Every image codec/archive provider wraps a native (non-.NET) binary, and all are wired up in Windows-only ways:

| Provider | Native dep | Resolution mechanism | Risk |
|---|---|---|---|
| WebP | `libwebp.dll` (x86/x64) | Hardcoded relative path **baked into the `DllImport` attribute string** | Highest |
| HEIF/AVIF | `libheif.dll` | Bare name, `Cdecl` — portable pattern, only x64 bundled today | Lower |
| JPEG XL | `jxl.dll` | `.dll` extension hardcoded into a `const string` | High |
| 7-Zip | `7z.dll` (x86/x64) | Path via `Assembly.GetExecutingAssembly().Location` + hardcoded path | Highest |
| PDF (Pdfium) | `pdfium.dll` (x86/x64) | Bundled via post-build `xcopy` in the `.csproj` | Highest |

For the **net8 retarget (near-term, Windows-first)**: low risk — modern .NET still honors this `DllImport` style. Worth modernizing to `NativeLibrary.SetDllImportResolver` while this code is being touched anyway. For **true cross-platform (later)**: distinct scope — sourcing/building `.so`/`.dylib` builds and wiring per-RID resolution for five different native libraries.

**Licensing upside:** these are thin shims around separately-licensed, already-open-source native libraries (libwebp/BSD, libheif/LGPL, PDFium/BSD-Apache, jxl/BSD-3, 7z/LGPL). Per §2's provenance practice, these are being **rewritten fresh** rather than ported verbatim — costs little, reduces derived-work surface area.

**Retarget spike status:** not yet run — this container has no .NET SDK and no network access to install one. This is a Claude Code task. Handoff prompt:

> Clone `https://github.com/maforget/ComicRackCE` (reference only, read-only). Create a new solution with two SDK-style `net8.0` class library projects: `Paperbunkr.Common` (from `cYo.Common`, excluding `.Windows`/`.Presentation`) and `Paperbunkr.Engine` (from `ComicRack.Engine`, excluding `Controls/` and `.Display.Forms`). Strip the known trivial WinForms references (`Clipboard.SetDataObject` in `ComicBook.cs`; `Application.CompanyName/ProductName` in `BackupArchiveCreator.cs`/`PdfStorageProvider.cs`) and sweep the rest, confirming each rather than assuming. Attempt a clean build; report every error, categorized by root cause — expect `System.Drawing`/GDI+ availability under modern .NET to be the main friction point (flag whether to keep `System.Drawing.Common` Windows-only for now or migrate to SkiaSharp/ImageSharp during this pass). Rewrite the five P/Invoke shims fresh (not ported) using `NativeLibrary.SetDllImportResolver`. Bring back the categorized error list and specifically whether `System.Drawing` usage is shallow or pervasive.

## 5. Database

CE's persistence is a single whole-library `ComicDb.xml`, fully loaded and fully rewritten on every save (`XmlUtility.Load`/`Store`), with 20 `SmartListSeriesXMatcher` classes doing in-memory predicate filtering — no indexing, no partial writes.

**Decision: SQLite via EF Core.** Precedent: YACReader (CE's closest sibling) already moved to SQLite; Kavita is built on EF Core + SQLite. EF Core specifically because the smart-list matchers map naturally to LINQ-to-SQL, and it brings migration tooling for the schema changes in §6.

**Migration path:** `ComicDatabase.LoadXml()` is itself portable engine code — the migration tool is: load with the ported parser, walk the object graph, write into the new schema. No from-scratch legacy parser needed.

**Open spike:** confirm EF Core's Sqlite provider handles the harder matchers (`SmartListSeriesGapsMatcher` and similar gap-detection logic) without silently falling back to loading everything into memory — prototype 2-3 of these before assuming all 20 port cleanly.

## 6. Data model schema

CE's `ComicInfo` base class already carries most of the ComicInfo.xml standard (`Series`, `Number`, `Volume`, `StoryArc`, credits, `Genre`, `Tags`, etc.), plus two fields directly relevant here: `Manga` (`MangaYesNo`: `Unknown|No|Yes|YesAndRightToLeft`) and `SeriesComplete` (a `YesNo` flag stored **per issue**, redundantly).

Two structural findings: (1) `Manga` conflates content origin with reading direction — it can't represent a Korean webtoon distinctly from Japanese manga. (2) Series isn't a stored entity in CE at all — `IBookGrouper`/`IGrouper<ComicBook>` confirms it's a runtime grouping over a flat string field. `SeriesComplete`'s per-issue duplication is a direct symptom of having nowhere else to put a series-level fact.

**Decision: elevate `Series` to a first-class entity** (matching how Mihon separates `Manga` from `Chapter`), not just adding fields to the flat model.

### Schema

**`Series`** (new): `Id`, `Name`, `SortName`, `ContentType` (enum: `Comic|Manga|Manhua|Manhwa|Unknown`, new), `ReadingMode` (enum: `LeftToRight|RightToLeft|VerticalContinuous|HorizontalContinuous`, new — see reconciliation note below), `IsComplete` (replaces duplicated per-issue flag), `Publisher`/`Genre`/`Summary` (promoted to series-level), `CoverIssueId`, `Categories` (M:M → `Category`), `TrackingLinks` (1:M → `TrackingLink`).

**`Issue`** (ported from `ComicBook`/`ComicInfo`, retargeted with `SeriesId` FK): keeps existing fields (`Number`, `Volume`, `StoryArc`, credits, dates, read-state fields), adds `StoryArcNumber` (confirmed absent from CE entirely — this is what the Comic Vine Scraper fork's `comicinfo_patch.py` is designed to write) and `ReadingModeOverride` (nullable, escape hatch for one-shots that differ from series default).

**`Category`** (new): `Id`, `Name`, `SortOrder` — M:M with `Series`, Mihon-style custom collapsible categories.

**`TrackingLink`** (new): `Id`, `SeriesId`, `Service` (`AniList|MangaUpdates|MyAnimeList|Kitsu|Metron|ComicVine`), `ExternalId`, `LastSyncedIssueNumber`, `LastSyncedAt`.

### Reading-mode reconciliation (resolves the open item flagged in the original reader canvas doc)
The reader canvas's layout model (§8) uses these same four `ReadingMode` values directly. **Double-page spread is *not* a fifth enum value** — it's a display toggle orthogonal to reading mode (applies as a rendering option under `LeftToRight`/`RightToLeft`), not a distinct mode. This was ambiguous across the original docs; resolved here as the single source of truth.

### Migration mapping (lossy — see §14 for how this surfaces to the user)
| CE `Manga` value | Inferred `ContentType` | Inferred `ReadingMode` |
|---|---|---|
| `YesAndRightToLeft` | `Manga` | `RightToLeft` |
| `Yes` | `Manga` | `LeftToRight` |
| `No` | `Comic` | `LeftToRight` |
| `Unknown` | `Unknown` | `LeftToRight` (default, not a real inference) |

CE's schema cannot distinguish manhua/manhwa from manga at all — every `Manga=Yes/YesAndRightToLeft` series lands as `ContentType.Manga` by default, correctly or not.

## 7. Content-type classification

Rather than a bespoke heuristic, this reuses tracker APIs already in the stack:
- **MangaUpdates** `get_series` returns a `type` field directly (`Manga`, `Manhwa`, presumably `Manhua` — confirm full enum during integration).
- **AniList** `Media.countryOfOrigin` (`CountryCode`) maps `JP→Manga`, `KR→Manhwa`, `CN/TW→Manhua`.

**Pipeline:** (1) existing `TrackingLink` wins if present, MangaUpdates' direct `type` field taking priority over AniList's inferred one if both exist and disagree; (2) no link yet → automatic title lookup, presented for **confirmation**, not auto-applied; (3) no network match → local heuristic using `LanguageISO` (already a populated CE field, free — `ja→Manga`, `ko→Manhwa`, `zh→Manhua`); (4) `Unknown` rather than guessing further. `ContentType` stays user-editable regardless of source at every layer.

**Architectural note:** this pipeline *is* the minimum viable version of tracker integration — classification is just its first output, not a separate feature to build and reconcile later.

## 8. Reader canvas architecture

Highest-risk, most novel component — nothing in CE does continuous/webtoon rendering.

**Rendering:** two Avalonia APIs, chosen per mode. `CompositionCustomVisualHandler` (render-thread-independent) for the continuous/webtoon canvas, where smooth scroll during background decode matters. `ICustomDrawOperation` (simpler, UI-thread-synced) for discrete paged modes (`LeftToRight`/`RightToLeft`), triggered per page-turn rather than continuous drag.

**Known failure mode to design around from day one:** a live Avalonia GitHub issue (#18498) documents unbounded memory growth (3.1GB+) with large bitmaps, because Skia's native bitmap memory isn't GC-tracked — `Bitmap.Dispose()` frees managed memory but not native allocation promptly. This is exactly the pattern a webtoon reader hits constantly. Mitigation is architectural, not incidental: explicit disposal the moment a page leaves the virtualization window, periodic `SKGraphics.PurgeResourceCache()`, and treating "how many decoded bitmaps exist at once" as a hard-bounded resource the app manages directly.

**GPU resource cache:** Avalonia's default (~28MB) is trivial for comic pages — raise via `SkiaOptions.MaxGpuResourceSizeBytes` at startup (256–512MB reasonable desktop default, consider making it user-configurable given how much page sizes vary).

**Two-tier bitmap strategy:** display tier (downsampled to viewport width, used 95% of the time, including webtoon's very tall images — never held at native resolution during scroll) and detail tier (cropped, on-demand, high-resolution decode only when zoomed in past what the display tier supports, discarded once zoom settles).

**Virtualization window:** small buffer around current position (start ±2, tune later), background thread pool for decode, priority-ordered and cancellable queue.

**Layout model vs. virtualization** kept as separate layers: layout model (given `ReadingMode` + scroll position, computes visible/near-visible pages — this is where the enum from §6 is consumed) vs. render layer (decode/dispose/draw, agnostic to what produced the page list). Adding a reading mode later is a layout-model change, not a rendering-engine change.

**Zoom/pan:** transform applied at composition/present time, decoupled from decode — render a bitmap somewhat larger than viewport, pan/zoom cheaply via transform, only trigger fresh high-fidelity decode once interaction settles.

## 9. Manga/manhua/manhwa metadata scraping

**Scope boundary:** metadata enrichment for files already owned locally — not the deferred source/download extension system (§1). MangaDex's API supports both; this only touches the metadata side.

**Existing architecture hook:** `MetronInfoProvider.ToXml()` (in `ComicRack.Engine/IO/Provider/XmlInfo/`) is a working precedent for exactly this task — mapping an external schema onto `ComicInfo` field-by-field. The `XmlInfoProvider` base class itself is wired for a different transport (deserializing a bundled archive file, not a live API call), so this isn't a literal subclass, but the mapping-function *shape* isn't novel for this codebase.

**Sources:** MangaDex (primary) — no auth for search/metadata reads, `/manga/{id}/feed` gives explicit `volume`/`chapter` numbers per chapter directly (easier than ComicVine's `store_date`-sorting inference for story arcs). AniList/MangaUpdates (supplementary), same APIs as §7.

**Matching flow:** identical search-and-confirm UI to §7 and `TrackingLink` creation — one shared component serves classification, tracking, and metadata-scrape confirmation, not three separate integrations.

**Chapter/volume alignment:** match MangaDex's chapter numbers against the existing `Number`/`Volume` fields on `Issue`; flag mismatches for manual review rather than silently skipping.

**Field mapping:** title→`Series.Name`, description→`Series.Summary`, tags/genres→`Series.Genre`/`Issue.Tags`, author/artist→`Issue.Writer`/`Issue.Penciller` (needs an explicit credit-collapsing convention, open item), status→`Series.IsComplete`, content rating→`Issue.AgeRating`, cover art→stored thumbnail, per-chapter title→`Issue.Title`.

## 10. Plugin API v2

**Keep the shape** of `IPluginEnvironment` — interface exposing `App`/`OpenBooks`/`Browser`/`ComicDisplay`/`CommandPath`, paired with pluggable `*Initializer` implementations. Confirmed via `ComicRack.Plugins/Automation` that this is a genuinely capable surface (library CRUD, navigation, thumbnails), not just cosmetic hooks — worth preserving that level of power.

**What changes:** `MainWindow: IWin32Window` → thin Avalonia-appropriate abstraction (not the raw Avalonia `Window` type, same insulation instinct as the original). `IComicDisplay` → points at the new reader canvas (§8), designed alongside it rather than ahead of it.

**IronPython replacement:** sequenced, not a single up-front choice. Ship a C# scripting initializer first (lower-risk, no new interop layer to validate before anything else works). Defer `pythonnet` (modern CPython interop, preserves the existing plugin-author experience) to a follow-on spike once there's a working host to test it against.

## 11. Existing plugin portfolio → core vs. plugin

Reframe: every existing plugin exists because CE was hard to extend cleanly. That constraint is gone in a native rewrite, so the question per-plugin is "was this a genuine extension, or a workaround for something the app should do natively."

- **ThemeFramework + WebView2 DevKit** → retired as a plugin (Avalonia themes natively), but its design output (tokens, wireframes) feeds §12 directly.
- **CRReaderOverhaul** → retired as a plugin; its feature list is now the reader canvas spec (§8), built natively.
- **CBL Manager + Comic Vine Scraper fork + manga-scraper → converge into one core system**, not three plugins. All three need the same search-and-confirm matching UI, the same field-mapping pattern, and overlapping ordering logic. Source backends (ComicVine, Metron, MangaDex, AniList, MangaUpdates) are the pluggable seam; the scraping/reading-list infrastructure itself is core, matching what Kavita/Komga/YACReader all treat as baseline.

**What's left for the plugin API to be for:** genuinely novel, user-specific automation — not backfilling missing core features. A healthier model (VS Code/Obsidian-style) than CE's plugin system had to be.

## 12. Wireframes & Avalonia translation

New wireframe pass covers the full app (Library, Reader, Series Detail, Preferences, Config Menu/skin system), built as HTML mockups again — but **the wireframe's role has changed.** Previously the HTML *was* the shipped UI (WebView2-rendered, tokens synced live). Now nothing renders it directly — every screen becomes a manual translation to Avalonia XAML.

**Translates cleanly:** layout structure, color/spacing as a token system, typography scale, component boundaries (map to `ItemsControl` templates, `UserControl`s if drawn as clear self-contained units).

**Doesn't translate directly — design the intent, not the mechanism:** CSS transitions/animations (Avalonia has its own system), hover/pseudo-states (`:pointerover`/`:pressed`, different mechanism), backdrop-blur/acrylic (Avalonia's Fluent brushes are native but won't match a specific CSS recipe automatically), JS-driven logic (becomes ViewModel/code-behind), web fonts (confirm embedding licensing).

**New required artifact:** an explicit token sheet (colors, spacing, radii, type scale) as its own document — last time tokens lived in CSS custom properties and *were* the source of truth by construction; that mechanism doesn't exist anymore.

**Mihon/Komikku-inspired patterns to design for** (Mihon is Apache 2.0 — clean lineage, safe to reference closely unlike CE): collapsible categories, filter by read/download/tracking status, "continue reading" quick action, unread badges, per-series reading-mode override, smart background adapting to page color, tracking-service UI, snackbar-style undo on destructive actions.

## 13. Skin/theme system

Translates the `.crpck` design from `typed-floating-crayon.md` (originally scoped for the WinForms/ThemeFramework plugin) against Avalonia's actual capabilities. Headline finding: **the original design never scoped structural GUI reshaping** — a `.crpck` is colors/tokens + icons, not custom layouts or control templates. That's the one part of Avalonia's runtime-theming story with real friction (loading external, uncompiled XAML); this design doesn't need it at all.

**Ports over conceptually, unchanged:** `.crpck` as a self-contained ZIP (`theme.json` + `icons/`), extracted once on install rather than read live; settings as JSON with legacy-fallback parsing; icon caching cleared on skin switch; staged/independently-verifiable implementation order; `windows_11` as the reference skin; the multi-section Preferences layout (Skins / Install Skin / Font / Future) already named in §12.

**Needs real redesign:**
- **Theme application** — not "load an external `.axaml` resource dictionary at runtime" (the exact mechanism flagged as second-class/inconsistent across Avalonia versions). Instead: keep `theme.json` as JSON, parse in code, apply by setting entries directly on `Application.Current.Resources` (`Resources["AccentBrush"] = new SolidColorBrush(...)`), consumed via `{DynamicResource}`. This is a genuine improvement over the original design, not just a port — it sidesteps the runtime-XAML-loading friction entirely since nothing ever asks Avalonia to parse loose XAML.
- **Context-menu style retires as a separate concept.** The original design's real, documented bug (`apply_theme()` and the picker dialog sometimes disagreeing on whether the custom menu renderer got applied, silently reverting skins to native menu styling) exists because WinForms treats context-menu rendering as a distinct subsystem. In Avalonia, `ContextMenu`/`MenuItem` theme exactly like any other control via the same `ControlTheme`/`Styles` resources — there's no separate renderer step to forget, so this bug class disappears by construction. The Preferences "Context Menu" section shrinks or disappears rather than porting as designed.
- **Font enumeration**: GDI+ `InstalledFontCollection` (Windows-only) → SkiaSharp's `SKFontManager.Default.FontFamilies`, a genuine upgrade since Avalonia already renders through Skia and this isn't inherently Windows-only — lines up with the cross-platform-later goal rather than adding a new Windows-only dependency.
- **`theme.json` schema needs an actual design pass**, not inheritance from the WinForms version — Avalonia's themeable surface is different (real `ControlTheme` overrides, not just color substitution). Do this alongside the wireframe token sheet (§12), since they name the same tokens.

**Net effect:** yes, third parties will be able to create their own GUI take as an installable `.crpck`, no recompiling Paperbunkr required — at the scope the original design actually intended (colors, icons, a bounded set of additional tokens pending the schema pass). Structural layout/control-template changes by third parties remain out of scope, as they always were — not a gap introduced by the rewrite.

## 14. Migration UX

**Design principle: import fast, review at leisure** — not a blocking wizard gating the library behind resolving every guess up front.

1. **Detection** — check default CE library path (`%AppData%\cYo\ComicRack Community Edition\ComicDb.xml`) or manual selection; available as a menu action too, not just first-run.
2. **Dry-run scan** — parse via the ported `LoadXml()`, preview series/issue counts and *how many series will land with a guessed `ContentType`* before writing anything.
3. **Series-identity conflict check** — since Series is now a real entity (§6), surface likely near-duplicate names (fuzzy match) for a merge-or-keep-separate decision CE's implicit grouping never had to make explicitly.
4. **Commit** — write via EF Core with progress feedback; **non-destructive**, original CE install untouched, always safe to re-run.
5. **Post-migration: a persistent "Needs Review" queue**, not a dismissible report — content-type guesses, series-name conflicts, missing files (CE's `FileIsMissing` check carried forward). Can be implemented as a smart-list/category using existing infrastructure. Resolving a content-type item reuses the exact §7/§9 search-and-confirm flow — not new UI.

## 15. Open items

All items below were resolved in a dedicated pass (`paperbunkr_open_items_resolved.md`) — summarized here, full reasoning in that doc:

- **Author/artist credit convention (§9)** — resolved: MangaDex/AniList both distinguish author/artist natively, so map directly to `Writer`/`Penciller`; only collapse (populate both with the same name) when a source provides one undifferentiated credit.
- **MangaUpdates type enum + MangaDex rate limits (§9)** — resolved: `ContentType` stays at its current 5 values (MangaUpdates' broader taxonomy like "webtoon" routes to `Genre`/`Tags` or is already covered by `ReadingMode`); MangaDex confirmed at ~5 req/s globally, target ~2-3 req/s for bulk scraping with real margin.
- **Decode-queue threading primitive (§8)** — resolved: `System.Threading.Channels`, multiple bounded channels (one per priority tier) rather than a general priority queue.
- **Double-page spread heuristics (§8)** — designed: aspect-ratio + reading-mode + not-a-cover-page auto-pairing, stored as a user-overridable display preference, not schema.
- **CE `Config.xml` migration scoping (§14)** — resolved via direct source inspection of `EngineConfiguration.cs`: roughly a quarter of its 65 settings port as literal values, a handful port as concepts needing new defaults, the WiFi-sync settings are out of scope entirely (feature doesn't exist in Paperbunkr), and the rest are artifacts of the old rendering pipeline with nothing to carry over.
- **Category nesting (§6)** — closed, stays flat.
- **`theme.json` schema (§13)** — designed: color tokens + font family + corner radius + spacing unit + icon manifest, all applied via code-behind resource assignment, deliberately staying short of `ControlTemplate`/structural territory.

**Still genuinely open:**
- **Retarget spike (§4) has not been run yet** — this container has no .NET SDK and no network access to install one; this remains a Claude Code task, and the one everything else's scope estimate depends on being confirmed for real.

## 16. Release staging: Alpha & Beta

**Alpha — core read+library loop runs locally.** Everything else deferred. Concretely:
- Retarget spike (§4) complete, engine compiling clean on net8
- SQLite/EF Core (§5) operational with basic CRUD — schema in place (§6), but `ContentType`/`ReadingMode` can sit at their `Unknown`/`LeftToRight` defaults; no classification pipeline required
- Reader canvas (§8): **one** reading mode working end-to-end — paged `LeftToRight`, the simplest case — proving the decode/dispose/virtualization pipeline actually works. Continuous/webtoon rendering explicitly deferred to Beta.
- Migration (§14): detection, dry-run scan, and commit (steps 1–4) — enough to get a real library in for testing. The "Needs Review" queue (step 5) can be stubbed; it's a completeness feature, not a functionality one.
- Library browsing UI: functional, not necessarily wireframe-polished yet
- **Explicitly not in Alpha:** content-type classification (§7), manga metadata scraping (§9), plugin API (§10) beyond what's needed for the host itself to run, CBL/reading-list convergence (§11), skin system (§13) beyond one hardcoded default theme — no `.crpck` loading yet

**Beta — feature-complete for outside testers.** Everything deferred from Alpha, plus:
- Full reader canvas including continuous/webtoon modes and double-page spread heuristics
- Content-type classification pipeline (§7) and manga metadata scraping (§9) live
- Plugin API v2 (§10) functional against at least one real test plugin
- CBL Manager / scraper convergence (§11) operating as core reading-list functionality
- Skin system (§13) with the `windows_11` reference skin installable and loadable
- Wireframe-driven UI (§12) across all five screens, not just Library
- Full migration UX including the "Needs Review" queue

**One thing worth naming rather than letting slide:** this split defers the single highest-risk, most novel piece of the whole project — continuous/webtoon rendering and its documented memory-management failure mode (§8) — all the way to Beta. That's a reasonable sequencing call (get *something* reading before tackling the hardest rendering problem), but it means Alpha doesn't actually validate the part most likely to go wrong. Worth considering a narrow, throwaway memory-safety spike of the `CompositionCustomVisualHandler` canvas *during* Alpha — not full webtoon feature completeness, just proving the dispose/purge strategy from §8 actually holds under a tall image — rather than discovering that risk for the first time when Beta is already underway and harder to unwind.
