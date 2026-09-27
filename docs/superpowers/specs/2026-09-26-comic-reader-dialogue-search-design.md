# Comic reader — Dialogue search (slice H1) design

Date: 2026-09-26. Status: **draft for review** (nothing built). Design settled in one grilling round ("recommended" on all nine questions); the defaults chosen without asking are listed under "Decisions taken without asking" for the review gate.
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, slice H: **#8 on-device OCR search and manga translate overlay**, whose search half is also roadmap item #14 ("full-text dialogue search"). This spec is the **search half only**. The translated overlay is a separate later spec (see "Out of scope").
Built after slices A–G and E1/E2; the working-tree caveats of those slices apply (uncommitted, shared tree).

## Facts this design rests on (verified 2026-09-26)

- **No OCR exists in the app**, and CE has none, so there is no parity rule; this is Paperbunkr-original. Two older specs (books-reader accessibility, Insights) explicitly left OCR out.
- **Windows OCR is available and fast enough.** A spike (`Windows.Media.Ocr.OcrEngine`, Windows TFM console) recognised a 1366×720 UI screenshot in 130–260 ms with clean text. `OcrEngine.MaxImageDimension` is 10000. Only **`en-US` and `en-GB`** recognisers are installed on this machine; other languages need a Windows language pack, which needs admin rights to install and which the app cannot install itself.
- **Calling WinRT from the App needs a Windows TFM.** The App is plain `net10.0`. Measured in the spike: a `net10.0-windows10.0.19041.0` project builds and runs `OcrEngine` with no extra SDK, but adds `Microsoft.Windows.SDK.NET.dll` (about 25 MB uncompressed) to the output. The alternative, a filtered CsWinRT projection on plain `net10.0`, failed here ("Could not find the Windows SDK in the registry") because the generator needs an installed Windows SDK on every build machine (including CI); rejected.
- **Projects referencing the App must follow its TFM:** `Paperbunkr.App.Tests`, `Paperbunkr.Benchmarks` and `Paperbunkr.ScrollHarness` reference `Paperbunkr.App.csproj` and are `net10.0`; `Paperbunkr.App.UiTests` is already `-windows`. The Daemon does not reference the App. The App is already a Windows-only build (WinExe, WebView2, DPAPI, XInput, `BatteryStatusInterop`).
- **The database is 7 MB and is copied whole by the 4-hourly backup** (`BackupService` copies the single SQLite file). OCR text for a large library is orders of magnitude bigger and is rebuildable, so it must not live in that file.
- **Library search runs on an in-memory snapshot** (`LibraryScreenViewModel.MatchesSearch`, per keystroke, no SQL) and `SearchMode` is guarded by `SearchFieldBundleCatalogParityTests` against CE's field bundles. Dialogue search is a DB query, so it cannot be a bundle; it needs its own async path. `AppSettings.LibrarySearchMode` is stored as a string, so appending an enum member is safe.
- **File metadata write-back rewrites the archive** (memory: `File.Replace`), changing `Issue.FileSize` and `FileModifiedTime` without changing a single page. A size+mtime staleness check would therefore throw the index away on every metadata edit; the design uses page content instead (see Invalidation).
- **Scheduling, progress and alerts exist**: `ScheduledTaskCatalog` (`DiskCpu` class, off-by-default tasks, Run-now, latest is `DetectAdPages`), Activity Center jobs and alerts with dedupe keys, toasts.
- **Reader building blocks exist**: `PageDecodeCore.DecodeSinglePage(path, page)`, `PageHasher` (dHash), the left-dock panels (Info, pin), `KeyCommandBinding`/`KeyboardCommandRegistry`, `PageType` (`Story, Cover, Advertisement, Deleted`), guided-view `ShownPanels`, page rotation and slice C's crop.
- **`Ctrl+F` is unbound** in the app (`F` alone is fullscreen).
- **Real-page quality was measured on English comics (2026-09-26)**, six 1988×3057 pages from two DC digital issues (Titans 007, Aquamen 006, in the Mylar cache) and a webtoon (`A Modern Man Who Got Transmigrated Into the Murim World` #028), `en-US` engine:
  - **Most balloon text is recognised** and grouped into sensible lines (15–58 lines per page). Sound effects and stylised display text are partly or wholly missed, as expected.
  - **The letter confusions are systematic, not random:** D read as P (`ANP`, `PO`, `MAPE A PEAL`, `THE PECISION`), G as `€`, `C` or `6` (`EA€ER`, `A€AINST`, `COINC`, `TELL/N6`), I as `1` or `/` (`1 AM`, `W/TH`, `ML/TARY`), S as `5` (`YE5`, `5TR/KE`), U as V (`VNPER`), and `€`/`/`/`Ø` land inside words, which a normal tokenizer would split. A plain `and` search would miss most of the word "and" on these pages. Rescaling the page (0.5×–1.3×) did not change this.
  - **Speed is 1.1–2.4 s per 1988×3057 page** (about 0.3–0.5 s for a 760×4000 webtoon strip), not the 0.1–0.3 s first estimated: a 24-page issue is 30–60 s, a 250 000-page library is on the order of 100 hours. On-demand indexing is the realistic path; the whole-library task is for small libraries or patient overnight runs.
  - **Webtoon pages fail outright above 10000 px** (`Image dimensions are too large`; these pages are 760×15000), so tall pages must be cut into overlapping strips.
  - Vertical Japanese manga was **not tested** (no pack installed, no raw manga in the library).

## Scope

In: an OCR engine seam with a Windows implementation, a separate derived text index (`pagetext.db`) with FTS5 and per-line boxes, an indexer with three triggers (on demand, in-reader, scheduled), a "Find dialogue" reader panel with match highlighting, a library "Dialogue" search mode, a Preferences status/clear panel, and cheap revalidation so metadata write-back does not invalidate the index.
Out of scope (decided): cloud OCR; OCR of scanned PDFs (books already have real text; EPUB/PDF untouched); remote issues (no local file); the plugin API; copying or selecting text on the page; **translation** (own spec: needs an offline MT model or a cloud API, decided after H1 is checked on real pages); fuzzy matching; Tesseract or any second engine (the seam allows it later).

## Decisions settled in the grilling round

1. **Split H:** H1 search now, translation deferred.
2. **Engine and languages:** Windows OCR behind `IPageTextRecognizer`; English works today; the app lists which OCR languages are installed, names the missing pack for issues it must skip, and never installs anything itself. Manga is not in scope for the first pass; if the quality gate shows vertical Japanese is needed, the decision is reopened with Tesseract (`jpn_vert`) as the candidate.
3. **Triggers, all opt-in:** "Index page text" on an issue, series or selection; an "Index this issue (about N seconds)" button in the reader panel; a scheduled task off by default. Everything reports through the Activity Center, can be cancelled, and resumes.
4. **Pages:** every page except `PageType.Advertisement` and `PageType.Deleted` (and any page the user marked skipped by slice B); local files only.
5. **Stored:** recognised lines with normalised boxes plus an FTS5 index; per-issue state row; `Clear indexed text` in Preferences.
6. **Reader UX:** a "Find dialogue" panel in the left dock with results, next/previous hit, a highlight over the match, guided-view step to the containing panel, a remappable key (`Ctrl+F`).
7. **Library:** a new `SearchMode.Dialogue`, not part of "All"; results are issues with a hit count; opening one starts the reader at the first hit with the panel pre-filled.
8. **Matching:** FTS5 `unicode61`, all words, last word as prefix, quotes for phrases, case- and diacritic-insensitive, no fuzzy. **Amended after the real-page test (D13):** plus a deterministic "lettering fold" of the letter pairs the engine confuses on comic capitals, so `and` finds `ANP`. This is a change to what was approved in round 1 and needs your explicit OK.
9. **Non-goals** as listed above.

## Decisions taken without asking (review these)

- **D1. Separate store.** `%AppData%\Paperbunkr\pagetext.db`, opened through `Microsoft.Data.Sqlite` directly (raw SQL and FTS5; not EF, not in the main migrations chain), path from `AppDataPaths` with a test override so tests never touch the real one. It is derived data: **not backed up**, not restored, safe to delete, recreated on demand. Corruption is handled by deleting and recreating it (with an alert), never by a recovery dialog.
- **D2. App TFM becomes `net10.0-windows10.0.19041.0`**, with the three referencing projects. Cost: about 25 MB more in the output (trimming is not attempted). Alternative if the ripple proves worse than expected: a small `Paperbunkr.Ocr` helper exe launched per batch with JSON lines over stdout (also isolates OCR crashes and lets it run at below-normal process priority), at the price of an IPC protocol and installer changes.
- **D3. FTS row per page, boxes per line.** OCR returns lines, not balloons, so a phrase can span lines. FTS indexes the page's lines joined by spaces; boxes are kept per line. After FTS names the pages, the app matches the query terms against that page's lines to choose which boxes to outline (a line is outlined if it contains any query term). Simple, and it degrades gracefully when OCR splits a balloon oddly.
- **D4. Coordinates** are stored as integers 0–10000 of the source page width/height (about 0.2 px precision on a 2400 px page), independent of the display tier, crop and rotation; the canvas maps them through slice C's crop override and the page rotation at draw time.
- **D5. Recognition image:** the page is decoded with `PageDecodeCore.DecodeSinglePage` and converted to `SoftwareBitmap` (Bgra8) at native size; the test showed rescaling neither helps accuracy nor saves much time. A page taller than 4000 px (webtoon strips are up to 15000) is cut into strips of at most 4000 px overlapping by 200 px, each recognised separately; line boxes are shifted back into whole-page coordinates, and a line that appears in two strips' overlap is kept once (same text within a small box distance). Wide spreads over 4000 px wide are cut the same way horizontally.
- **D6. Language selection:** `Issue.LanguageISO` mapped to a BCP-47 tag; null falls back to the user's profile languages, then `en-US`. A page set in an unavailable language is skipped with a reason, and the job's summary and one deduped Activity alert say "Japanese OCR pack is not installed in Windows (12 issues skipped)". Not-yet-indexable issues are remembered so they are not retried until the language list changes.
- **D7. Scheduled task** `index-page-text`: `DiskCpu`, priority 17, weekly, off by default, one issue at a time on one background thread at below-normal priority, waits between issues while any reader screen is open. Each issue is atomic (all its pages or none); cancelling discards the partial issue. Orphan rows (issues no longer in the library) are swept at the end of each run and on `LibraryDeletionHelper` removal.
- **D13. Lettering fold (amends Q8).** A pure function `LetteringFold`, applied symmetrically to indexed text and to the query, in two steps. (1) *Repair:* inside a word, the lookalike symbols the engine emits are turned into letters before tokenizing (`€`→`G`, `Ø`→`O`, `/` and `|`→`I`, `1`→`I` and `6`→`G` only when adjacent to letters or standing alone as the pronoun), so `EA€ER` is one token. (2) *Fold:* confusable letters map to one class letter (`D`/`P`, `G`/`C`, `I`/`1`, `S`/`5`, `O`/`0`, `U`/`V`), so `ANP` and `AND` both become the same string. The FTS table has two columns, `raw` (repaired only) and `folded`; a query matches either, `bm25` weights `raw` higher so exact hits rank first, and the highlight step (D3) uses the same functions. The cost is precision (a search for `dad` also matches `pap`), accepted because a search that misses `AND` is worse than one that returns a few extra pages. Fixtures for its tests are the real recognised strings from the 2026-09-26 test. Not done: dictionary correction, edit-distance matching, any per-language tables (the fold is Latin capitals only; other languages are unfolded).
- **D8. Invalidation.** Per issue the state row stores `PageCount`, `FileSize`, `FileModifiedTicks`, three sample dHashes (first, middle, last page), engine id and version, language and indexed-at. Query time trusts a row only if its `PageCount` equals the issue's current `PageCount` (in memory, free). When the file stamp differs (opening the issue in the reader, or the scheduled run), it revalidates: decode the three sample pages, and if all three hashes are within distance 6 of the stored ones, refresh the stamp and keep the text; otherwise drop the rows and queue a re-index. An engine-version bump re-queues everything gradually through the scheduled task.
- **D9. Highlight scope:** outlined in paged and double-page modes; **continuous scroll** jumps to the page without an outline in this pass. The outline pulses for about 1.2 s and then stays as a thin frame until the search is cleared or the page changes.
- **D10. Where things live:** Preferences › Library gets a "Page text" group (issues and pages indexed, size on disk, installed OCR languages, "Index the whole library now" = the task's Run-now, "Clear indexed text"). The reader panel button sits next to Info in Reader Tools › Page, plus a palette and menu entry, per the pattern of slices C–E.
- **D11. Library mode plumbing:** choosing Dialogue debounces the text (300 ms), runs `PageTextIndex.SearchIssues` off the UI thread, stores the matching issue-id set with hit counts (a request id drops stale results) and re-runs the filter, where `MatchesSearch` for Dialogue is `s.Issues.Any(i => ids.Contains(i.Id))`. The mode gets a `dialogue:` field prefix, no value suggestions (like `File`), and an empty-state line when nothing is indexed ("No page text indexed yet"). `SearchFieldBundleCatalogParityTests` explicitly excludes it as Paperbunkr-original. The hit count uses the existing chip style (`PbRadiusChip`) where the card model has a free slot; if a card has none, the count appears in the tooltip only (checked while building).
- **D12. Reader hand-off:** `ReaderScreenViewModel` gains an optional initial dialogue query, set by the library when opening a result; it opens the panel, runs the search and jumps to the first hit.

## Design

### Units

- **`IPageTextRecognizer`** (`Services/PageText/`): `AvailableLanguages`, `Task<PageTextResult> RecognizeAsync(SKBitmap/pixel buffer, string languageTag, CancellationToken)`. `PageTextResult` = lines (`Text`, normalised box). `WindowsPageTextRecognizer` is the only implementation; a fake serves tests.
- **`PageTextIndex`**: owns `pagetext.db`. Tables `IssueText` (state), `PageText` (issue, page, joined text; FTS5 external-content table `PageTextFts` with `tokenize='unicode61 remove_diacritics 2'`, `prefix='2 3'`), `PageLine` (issue, page, line, text, x, y, w, h). API: `Replace(issue, state, pages)`, `Delete(issue)`, `SearchIssues(query, limit)` → id/hit-count, `SearchPages(issueId, query)` → pages with snippet and line boxes, `GetState(issue)`, `Stats()`, `Clear()`, `SweepOrphans(validIds)`. A `FtsQuery` builder turns user text into a safe MATCH expression (each word quoted, last word `*`, `"..."` phrases kept, operators and stray quotes neutralised).
- **`PageTextIndexer`**: takes issue ids, decodes pages, calls the recognizer, writes results; the scheduled task, the context-menu action and the reader button all call it. Progress goes to an `IActivityJobHandle`.
- **`PageTextRevalidator`**: the D8 sample-hash check.
- **Reader:** `ReaderFindDialogueViewModel` (query, state line, results, current hit, commands), the panel in `ReaderScreen.axaml` with code-behind, a `SearchHighlightOverlay` drawn by `PageCanvas` (boxes in normalised page space), and the guided-view hook that steps to the panel containing a box.
- **Library:** the `SearchMode.Dialogue` value and D11 plumbing; context-menu entry "Index page text" (issue, series, selection) and the same on the Detail issue menu.
- **Preferences:** the "Page text" group (D10).

### Reader panel behaviour

Toggling the panel (button, palette, menu or `Ctrl+F`) opens the left dock and focuses the search box. States: **Not indexed** ("Index this issue (about N s)" button, estimated from page count × the measured mean time, refined after the first pages), **Indexing** (progress bar, cancellable; results appear for pages already done only after the issue completes, since indexing is atomic), **Ready** ("114 pages · English"), **Unavailable** (language pack missing, names it; nothing else offered). Results list rows `p. 12 — …matched words in bold…`; `Enter`/`Down` and `Shift+Enter`/`Up` (and the on-panel next/previous buttons) move through hits; choosing a hit jumps to the page, draws the outline, and with guided view on steps to the containing panel. `Escape` closes the panel (deferred one dispatcher tick per the CLAUDE.md routed-event gotcha). While the query is empty nothing is drawn.

### Notifications

Activity Center job "Indexing page text" (kind `Other`) for every run, cancellable; a toast for on-demand runs only ("Indexed 3 issues"); one deduped alert per missing OCR language; a toast if the reader button finishes an issue while the panel is closed.

### Keys and settings

New remappable key `Reader.FindDialogue`, default `Ctrl+F`, `ConflictContext.Always`, added to `KeyboardCommandRegistry` and the keyboard-shortcuts wiki page. No new `AppSettings` columns: the scheduled task's enabled/interval use the existing task-state table; the index location is fixed.

## Build order

0. **Gate:** the English quality gate is **passed** (see Facts: usable text, systematic confusions handled by D13, speed measured). What remains is the Windows TFM change (D2) building with the existing suites still compiling. Manga stays untested until a raw manga file and its language pack exist; if manga becomes a goal, that is a new gate with Tesseract as the candidate.
1. `IPageTextRecognizer`, `WindowsPageTextRecognizer`, `PageTextIndex`, `FtsQuery` and their tests.
2. `PageTextIndexer`, `PageTextRevalidator`, the scheduled task, the context-menu action, Activity Center reporting.
3. Reader panel, view model, highlight overlay, guided hook, key, palette and menu entries.
4. Library `Dialogue` mode, hand-off to the reader.
5. Preferences group, deletion hook, docs (wiki: Reading, Keyboard-Shortcuts, Preferences), roadmap/todo/memory, review checklist.

## Testing

- **`LetteringFold`:** table-driven tests on real recognised strings (`ANP`→matches `AND`, `EA€ER`→`EAGER`, `TELL/N6`→`TELLING`, `YE5`, `VNPER`, `1 AM`), symmetric fold of index and query, exact hits outrank folded hits, no change to lowercase-only accented text, non-Latin text untouched.
- **Strips:** a 760×15000 page is cut into strips ≤4000 px with 200 px overlap, coordinates map back to whole-page, an overlap line is kept once, nothing crashes at the engine's 10000 px limit.
- **Pure/store** (temp DB path, never the real one): `FtsQuery` escaping (quotes, `AND`/`OR`/`NEAR`, `*`, unicode, empty); `SearchIssues` all-words/prefix/phrase/diacritics/case; page and line results; `Replace` atomicity; `Clear`; `SweepOrphans`; corruption recreates the file.
- **Indexer** (fake recognizer): skips Advertisement/Deleted/skipped pages and remote issues; missing language marks the issue skipped without retry; cancel discards the partial issue; resume skips finished issues; atomic write on failure mid-issue.
- **Revalidator:** metadata write-back (same pages, new size/mtime) keeps the text; a replaced file with different pages drops it; `PageCount` mismatch hides rows at query time.
- **Windows recognizer:** renders known text with Skia and expects the words back; skipped (not failed) when the engine or `en-US` is absent.
- **Reader** (headless, `ReaderScreenTestHost`): panel states, hit navigation moves the page, highlight rectangles map correctly through rotation and slice C's crop, the guided step, key binding, escape closing.
- **Library:** Dialogue mode filters through the async path, stale result dropped, empty-state line, not included in All, `dialogue:` prefix, the parity test's exclusion, setting persistence as a string.
- **Regression:** the Compare/Info/Pin tests and the remote-row isolation allowlist (`RemoteRowIsolationTests`: the new files are local-only) stay green.

## Not verified by design

OCR quality on manga and on stylised sound effects, how long your real library takes to index, whether the fold's extra matches feel noisy, whether outline placement looks right on rotated and auto-cropped pages, and how the panel and library results feel on screen. The quality gate covers the first only for the pages it is given; the rest are the user's on-screen checks.
