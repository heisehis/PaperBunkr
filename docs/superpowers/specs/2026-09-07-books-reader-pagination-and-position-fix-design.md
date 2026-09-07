# Books Reflow Reader — Pagination Retry + Position/Bookmark Precision Fix — Design

## Outcome (2026-09-07, after implementation and real on-screen verification)

**Position/bookmark precision: shipped and working as designed.** BlockId-anchored resume/bookmark
tracking, one-bookmark-per-block, and the bookmark/highlight same-chapter-jump fix are all in place
per this doc's Decisions table below.

**Pagination: the transform-based retry (Decisions table, "Pagination mechanism" row) also failed
on-screen** — the user's own screenshot of a real Dune EPUB in paged mode showed the *identical*
next-column-bleeding-in-at-the-right-edge symptom as both of the original 2026-09-02 attempts, even
though this attempt used `transform: translateX()` specifically to avoid a native scroll ever
happening. That rules out the leading theory (a native-scroll-triggered WebView2 repaint bug) as the
sole cause - three independently-reasoned attempts (vw sizing, exact-pixel sizing, transform-instead-
of-scroll) hitting the same visual defect points at something more fundamental in how
`Avalonia.Controls.WebView` composites CSS multi-column layout + `overflow: hidden` clipping, not
diagnosable further without live devtools access no session in this project's history has had. Per
this doc's own disclosed fallback chain, `BookReaderScreen.axaml.cs`'s `UseColumnPaging` const is
flipped to `false` - vertical scroll (dressed with CSS scroll-snap) is the shipped, final behavior.
**True CSS-column pagination for the Books reader is now closed out as permanently declined**, same
status as the magnifier - not a gap to revisit blind a fourth time.

## Background

The 2026-09-02 WebView rewrite (`docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-
redesign-design.md`, plan `-plan.md`, merged `51b51c3`/PR #38) replaced the reflow reader's custom
`ParagraphView`/`TextLayout` rendering with an embedded `NativeWebView` (`Avalonia.Controls.WebView`
12.0.0, WebView2 on Windows). That rewrite shipped with two disclosed, deliberate regressions instead
of the design's original intent:

1. **Pagination (Step 5).** The design called for CSS multi-column layout (`column-width`,
   `column-gap: 0`), matching Thorium/Readium, with page-turns driven by `scrollLeft +=
   viewportWidth`. Two different fixes for it were tried against the real running app — first
   `vw`-based column sizing, then JS-measured exact-pixel sizing — and **both produced the identical
   symptom**: the next column's text visibly bleeding in at the right edge. Two independently
   reasoned fixes failing identically pointed at the defect being in how this specific WebView
   hosting mode handles multi-column layout + horizontal scroll, not in either fix's own math — not
   diagnosable further without live devtools access, which that session didn't have (same standing
   limitation this session has too). Switched to plain vertical scroll
   (`BookReaderScreen.axaml.cs`'s `PushCurrentChapterHtml`/`NextPageScript`/`PreviousPageScript`) as
   a working fallback.
2. **Position/bookmark granularity (Step 8, scoped down).** `BookHighlight` got a real anchor rework
   — `(ChapterIndex, BlockId, StartOffset, Length)`, a `BlockIdInjector`-assigned `id="pb-p<n>"` on
   every block-level element (`src/Paperbunkr.Engine/IO/Provider/Books/BlockIdInjector.cs`) — because
   Step 7's highlight-creation flow needed it to function at all. `Book.LastChapterIndex`/
   `LastCharacterOffset` and `BookBookmark.ChapterIndex`/`CharacterOffset` were left unmigrated: the
   old "offset into flattened plain text" scheme is meaningless against real chapter HTML, but
   nothing built a replacement. `ToggleBookmark`/`IsCurrentPositionBookmarked` were narrowed to match
   by `ChapterIndex` alone (`BookReaderScreenViewModel.cs`) — an honest reflection of the gap, not a
   silent one. Concretely: one bookmark per chapter instead of per-paragraph, and resuming a book
   lands at the start of the last-read chapter instead of the precise last-read spot.

This spec fixes both, building on the block-ID anchor infrastructure `BookHighlight` already
established.

**A third, related bug found while reading the code for this spec** (not one of the two regressions
above, but the same class): `GoToBookmark`/`GoToSearchResult` set a new `_position` and call
`RecomputeCurrentPage()`, which only pushes new HTML into the `NativeWebView` when
`CurrentChapterHtml` (a `[ObservableProperty]`, equality-checked) actually changes value. Jumping to
a bookmark or search result **within the chapter already open does nothing visually** — no reload,
no scroll, the view just sits where it was. The bookmark-jump half of this is fixed for free by the
work below (once a "scroll to `BlockId`" JS action exists, `GoToBookmark` uses it same as resume-on-
load does). The search-jump half is **explicitly deferred**: `BookSearchResult`/`RunSearch`
(`BookReaderScreenViewModel.cs`) still anchor to the old `CharacterOffset`-into-flattened-`Paragraphs`
scheme, and paragraph index isn't guaranteed to line up 1:1 with `BlockIdInjector`'s block index (two
independent passes over the HTML). Converting search to `BlockId` anchoring is real, separate work,
tracked as a follow-up rather than silently left broken without a note.

**CE note:** no CE equivalent — nothing to verify against (same as the parent WebView-redesign spec).

## Decisions

| Area | Decision |
|---|---|
| **Pagination mechanism** | Keep CSS `column-width`/`column-gap: 0` for layout, but drive page-turns via `transform: translateX(-N * viewportWidth)` on the `#pb-content` wrapper instead of `scrollLeft`. Rationale: both prior failed attempts varied the column-width sizing math while keeping `scrollLeft`-driven native scroll of the columned content; the failure mode (identical symptom regardless of sizing method) points at the scroll mechanism itself, not the math — a known class of bug where an embedded native control's internal scroll doesn't reliably trigger a correct repaint of the embedded surface. Driving the "page" via `transform` on the content avoids a native scroll happening at all, so that repaint path can't be hit. This is a genuinely different mechanism from both prior attempts, not a third variation on the same one. |
| **Pagination verification / fallback chain** | Manual on-screen verification only (same standing limitation as every WebView step in the parent spec — no live devtools, no automated way to assert "renders correctly"). **If it verifies:** vertical scroll retires entirely; paged columns become the only reading mode (matches original design intent, no new mode-toggle setting). **If it doesn't verify:** fall back to the current vertical scroll, dressed with `scroll-snap-type: y mandatory` / `scroll-snap-align: start` per block for a crisper page-turn feel — still not true side-by-side pages, but a real improvement over an undressed scroll with zero risk of hitting the same bug class again. If even that feels unsatisfying, plain vertical scroll (today's behavior) stands as-is and this item is marked permanently declined, same status as the magnifier. |
| **Position/bookmark anchor shape** | `(ChapterIndex, BlockId)` — **no character offset within the block.** Unlike `BookHighlight`, position/bookmarks don't need to re-wrap a text range, just land somewhere reasonable; paragraph-top granularity is already a large precision jump from today's chapter-top granularity, and avoids the real extra complexity character-offset restore would need (there's no native "scroll to character offset" API — it would require wrapping a temporary marker span and scrolling that into view). A coarse `ProgressionFraction`-style fallback (0–1, the scroll/page fraction at capture time) rides alongside `BlockId` for the rare case a block can't be found on reload (e.g. a re-parse producing different HTML) — lands roughly right instead of snapping to chapter start. This matches the parent spec's own already-decided-but-unbuilt "Anchoring" row for the shared locator model. |
| **Capture mechanism** | A JS query (`getBoundingClientRect()` of each block vs. the viewport rect) finds the topmost block currently intersecting the viewport, reported back via the existing `invokeCSharpAction` bridge (`HighlightScript`'s pattern in `BookReaderScreen.axaml.cs`). This check is viewport-relative, not scroll-mechanism-specific — it works identically whether the viewport moved via vertical `scrollTop` (the fallback path) or the paging `transform` (the primary path), so it doesn't fork on the pagination outcome. |
| **Capture cadence** | Both: (a) immediately on every explicit navigation (page-turn, chapter change, bookmark toggle — same points `PersistPosition()` already fires from today), and (b) a debounced (~500ms after the viewport stops moving) listener for continuous coverage, so a crash or force-quit mid-chapter doesn't lose the spot the way today's "only chapter-level, only on explicit nav" tracking does. |
| **Bookmarks: one-per-block, not one-per-chapter** | `ToggleBookmark`/`DeleteBookmark`/`IsCurrentPositionBookmarked` key off `(ChapterIndex, BlockId)` instead of `ChapterIndex` alone — restores the ability to have multiple bookmarks in one chapter, matching pre-redesign behavior. Excerpt text comes directly from the JS capture (the identified block's own `textContent`, truncated) rather than round-tripping through `BookPaginator.FindParagraphIndex`/`Paragraphs` — simpler, and doesn't depend on paragraph-index-to-block-index alignment holding (the same alignment gap that makes the search-jump fix separate scope). |
| **Restore ("jump to a saved position")** | A JS action scrolls/pages to a given `BlockId`: `document.getElementById(blockId).scrollIntoView()` in the vertical-scroll fallback path; "find which page/column currently contains this block, then set the `transform` to that page" in the paged-column path. Used by: resume-on-load (`LoadBook`), `GoToBookmark`, and (per above) `GoToBookmark`'s same-chapter case now actually moves the view. `GoToSearchResult`/search-jump is explicitly **not** wired to this — deferred, see Background. |
| **Migration** | Full reset, matching `BookHighlight`'s own `ReworkBookHighlightAnchor` migration precedent exactly: `DELETE FROM BookBookmarks` (existing rows have no clean mapping onto the new scheme, same reasoning as the highlight migration), `Book.LastChapterIndex`/`LastCharacterOffset` zeroed. New shape: `Book` gains a nullable `LastBlockId` (string, maxLength 64, same convention as `BookHighlight.BlockId`) + `LastProgressionFraction` (nullable double), replacing `LastCharacterOffset`; `LastChapterIndex` (int) stays as a column, just reset to 0 for existing rows — chapter identity is still a valid, needed concept going forward, only the stored value resets. `BookBookmark` gains `BlockId` (string, maxLength 64, required) + `ProgressionFraction` (nullable double), replacing `CharacterOffset`; `ChapterIndex`/`Excerpt`/`CreatedTime` columns are untouched by the migration itself (the rows just get deleted, per the full-reset decision — those columns' shape doesn't change). |

## Components

1. **`BookReaderPaginationScript` (JS, new/reworked in `BookReaderScreen.axaml.cs`)** — replaces
   `NextPageScript`/`PreviousPageScript`'s `scrollTop` logic with `transform: translateX()` paging
   over `column-width` layout; computes total page count from `scrollWidth`/viewport width for
   boundary detection (today's `CanGoPrevious`-at-chapter-start gap, noted in `ApplyScrollResult`'s
   own doc comment, gets a real fix here since page count is now knowable up front instead of
   discovered by scrolling into a wall).
2. **`BookReaderPositionScript` (JS, new)** — the topmost-visible-block query (capture) and the
   scroll/page-to-`BlockId` action (restore), added to the same script surface `HighlightScript`
   already lives on. Capture messages reuse the `invokeCSharpAction` bridge with a new message
   `type: "position"` alongside the existing `selection`/`highlightTap`/`contentTap`/`announcePosition`/
   `pageTurn` types `OnReaderWebMessageReceived` already switches on.
3. **Data model changes** — `Book`/`BookBookmark` entity + `PaperbunkrDbContext` column config changes,
   one new EF migration (full reset, per the Decisions table) mirroring
   `20260902162546_ReworkBookHighlightAnchor`'s shape and doc-comment style.
4. **`BookReaderScreenViewModel` rework** — `BookPosition` (`src/Paperbunkr.App/Models/BookPosition.cs`)
   changes shape from `(ChapterIndex, CharacterOffset)` to `(ChapterIndex, BlockId, ProgressionFraction)`;
   `PersistPosition`, `ToggleBookmark`, `DeleteBookmark`, `GoToBookmark`, `LoadBook`'s resume path, and
   `IsCurrentPositionBookmarked`'s matching logic all move onto the new shape. A new debounced-capture
   handler wired the same way `OnHighlightsCollectionChanged`/`OnSettingsPropertyChanged` already are
   in `BookReaderScreen.axaml.cs`.
5. **Fallback-mode CSS** (`scroll-snap-type`/`scroll-snap-align`) — added to the vertical-scroll path
   only, gated behind whichever pagination outcome actually ships (see Decisions).

## Risks / Open Questions

- **Transform-based paging may fail on-screen for a different reason than `scrollLeft` did** — the
  theory behind it (native-scroll repaint bug) is well-reasoned from research (WebView2 "airspace"-
  adjacent embedded-control repaint issues turned up during this spec's research pass) but not
  confirmed against this specific `Avalonia.Controls.WebView` version directly — same "verify, don't
  assume" discipline as everything else in the parent spec. The three-way fallback chain (Decisions
  table) means this can't leave the reader in a worse state than today even if it fails.
- **Debounced capture chattiness** — a ~500ms debounce on a scroll/page-move listener is cheap, but
  worth confirming during implementation that it doesn't fire (and write to DB) on cosmetic events
  like a font-size slider drag re-triggering layout; `PushTypographyCss`'s existing re-injection path
  doesn't move the viewport, so this should be a non-issue, but worth a real check.
- **Search-jump stays broken for the same-chapter case** — explicitly deferred (Background), not
  silently left off this doc's radar. Worth its own small follow-up spec once `BookSearchResult`'s
  anchor scheme is looked at.
- **`BlockIdInjector` determinism is the one hard dependency this entire fix leans on** — already a
  hard requirement and already tested (`BlockIdInjectorTests`) from the parent spec; this spec doesn't
  change that contract, just consumes it more heavily (resume/bookmark restore now depends on the same
  ID still resolving on reload, not just highlights).

## Testing

- **Unit-level**: `BookPosition` shape-change tests; `PersistPosition`/`ToggleBookmark`/`GoToBookmark`
  logic against the new `(ChapterIndex, BlockId)` matching (mockable/testable without a live WebView,
  same as today's `BookReaderScreenViewModelTests` pattern); new migration test mirroring
  `ReworkBookHighlightAnchor`'s own test pattern, asserting the reset behavior specifically.
- **Manual/on-screen** (not automatable — same standing limitation as every WebView-facing piece of
  the parent spec): transform-based page-turn actually renders and advances through a real multi-page
  chapter without the bleed artifact; resume lands at the last-read paragraph after closing and
  reopening a book; a bookmark set mid-chapter, then jumped to from the Bookmarks drawer, lands at the
  right paragraph including when already in that chapter; multiple bookmarks in one chapter all
  persist and list correctly; the scroll-snap fallback (if pagination doesn't verify) feels reasonable.
