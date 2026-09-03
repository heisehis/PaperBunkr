# Books Reflow Reader — WebView Rendering Engine Redesign — Design

## Background

The current reflow reader (EPUB/FB2/MOBI, `BookReaderScreenViewModel`/`BookReaderScreen.axaml`)
renders via a custom Avalonia `ParagraphView` control built on `TextLayout`, fed by
`IBookTextSource` → flattened `BookParagraph` (plain text + bold/italic spans only, everything else
dropped) via `HtmlProseExtractor`. This pipeline, along with drag-selection highlighting,
`ParagraphViewAutomationPeer` screen-reader support, word-spacing/typography controls, and
`BookPaginator`'s custom viewport-height pagination, shipped earlier this cycle
(`2026-09-01-books-reader-ergonomics-and-annotations-design.md`). FB2/MOBI ingestion
(`Fb2BookSource`/`MobiBookSource`, `2026-09-01-books-format-ingestion-fb2-mobi-design.md`) and
reader chrome accessibility (`2026-09-01-books-reader-screen-reader-accessibility-design.md`) also
shipped this cycle, both against that same plain-paragraph content model.

Prompted by hands-on use of a real book (a Dune EPUB) and a side-by-side look at Thorium Reader,
two concrete gaps surfaced: **the reader renders zero in-body images** (`HtmlProseExtractor`
silently drops every `<img>` tag — a deliberate scope decision from the original
`2026-08-09-novels-epub-pdf-support-design.md`, now being reversed), and more fundamentally the
reader doesn't approach the fidelity of mainstream EPUB readers. Research into how EPUB is
"normally" rendered (the user's own research note, kept locally at
`C:\Users\DeeDee\Downloads\thorium-vs-eboox-epub-research.md` — not checked into this repo, referenced
here for context but not a repo path) confirmed that virtually every serious EPUB reader (Apple
Books/WebKit, Google Play Books/Calibre/Thorium-Readium all on Chromium or QtWebEngine, even
Kindle's KF8 content being HTML+CSS internally) renders the format's real XHTML+CSS through an
embedded browser engine, using CSS multi-column layout for pagination. Paperbunkr's "flatten
everything to plain paragraphs, hand-roll text layout" approach is the minority pattern — workable,
and deliberately chosen for tight control over highlighting/accessibility/typography, but capped
well below what a browser engine gets for free.

**Decision: rebuild the reflow engine around an embedded WebView**, normalizing all three
reflowable formats (EPUB/FB2/MOBI) to real HTML/CSS instead of flattened plain text, and rebuilding
pagination, typography, highlighting, position-tracking, and accessibility against DOM/WebView
primitives. This is a genuine architectural rewrite, not an extension — it retires `ParagraphView`,
`ParagraphViewAutomationPeer`, and `BookPaginator` outright. `Paperbunkr.App.csproj` builds
`OutputType=WinExe` and the Engine project already carries Windows-only dependencies (System.Drawing/
GDI+, PDFium Win32, HEIF win-x64) — this is a Windows desktop app in practice, so the WebView choice
is **WebView2** (Chromium/Edge, built into Windows 11, installable on 10), not a more exotic
cross-platform Avalonia WebView story.

**Existing highlights, bookmarks, and in-progress reading positions are reset, not migrated**, on
this upgrade — explicit user decision, given negligible current userbase and the fact that the old
"global character offset into flattened plain text" locator has no clean mechanical mapping onto the
new DOM-block-anchored model. A user-facing warning dialog about this reset is explicitly deferred
to a later pass, not part of this spec.

**Out of scope:** PDF reading (`PdfPageReaderScreenViewModel`, rasterized page images) — untouched,
same standing exclusion as every prior Books spec this cycle.

**CE note:** no CE equivalent — nothing to verify against.

## Decisions

| Area | Decision |
|---|---|
| **Rendering engine** | Embedded WebView2 (Chromium/Edge), replacing `ParagraphView`/`TextLayout` custom rendering entirely. |
| **Format normalization** | All three reflowable formats normalized to real HTML+CSS *before* rendering. `IBookTextSource`'s shared model changes shape for reflowable sources: `BookChapter` carries an HTML string (its real markup) instead of `IReadOnlyList<BookParagraph>`. `EpubBookSource` reworked to extract each chapter's real XHTML content (still via `VersOne.Epub`, just no longer piped through `HtmlProseExtractor`) instead of flattened paragraphs. `Fb2BookSource` emits real HTML (`<p>`, `<em>`, `<strong>`, `<img>` from its `<binary>` blocks) instead of `BookParagraph`/`BookTextSpan`. `MobiBookSource`'s already-reconstructed HTML-ish stream feeds the WebView directly. `HtmlProseExtractor` itself is not deleted — `PdfBookSource` is explicitly out of scope for this redesign and keeps returning the old paragraph-based `BookChapter` shape (PDF has no real markup to normalize to), so `IBookTextSource`'s exact interface split between "HTML-carrying" and "paragraph-carrying" chapters is a concrete open question for the implementation plan to resolve against the real current interface — not decided here beyond "PDF's existing behavior is untouched." |
| **Pagination** | CSS multi-column layout: `column-width` = full reading-pane width, `column-gap: 0`, fixed `height` = pane viewport height. Page-turn = `scrollLeft += viewportWidth` via injected JS. Font-size/window-resize reflows are handled by the browser engine recomputing column breaks — no custom re-pagination algorithm. Replaces `BookPaginator` (`FillPage`/`ComputeParagraphOffsets`/`CurrentPageRange`) entirely. Chosen over the faster-but-less-mature `overflow: paged-x` alternative for the same stability reason EDRLab's own web reader stayed on CSS columns. |
| **Typography/theming** | Three-layer CSS injection (`before` reset/normalize → the book's own real stylesheet → `after`). The `after` layer declares `--pb-*` custom properties (font-family/size, letter/word/line spacing, bg/fg colors) **and** forces them onto broad selectors with `!important` — bare custom-property declarations alone don't override a book's hardcoded `color`/`font-family` on `body`/`p`. Existing sliders (font size, character/word/paragraph spacing, page margin) and the six theme presets (Light/Dark/Sepia/MatchAppSkin/OledBlack/HighContrast) map 1:1 onto these variables; `BookReaderSettings`/per-book override columns on `Book` keep their current shape, just drive CSS variables instead of `TextLayout` construction. |
| **Highlighting — selection** | Native `window.getSelection()`/DOM Range via the WebView's JS↔host messaging bridge, replacing `ParagraphView`'s custom drag-selection. |
| **Highlighting — rendering** | A `<span class="pb-highlight pb-color-{color}">` wrapped around the selected range (simple, works everywhere); the CSS Custom Highlight API (`::highlight()`, DOM-non-mutating) is the preferred implementation *if* the shipped WebView2/Chromium version supports it — verify during implementation, span-wrapping is the safe fallback. |
| **Anchoring (highlights + position — shared model)** | A stable `id="pb-p<n>"` injected on every paragraph-level block during the same normalization pass that produces the HTML for each format. Highlights become `(ChapterIndex, BlockId, OffsetWithinBlock, Length, Color)`. Reading position/bookmarks become `(ChapterIndex, BlockId, OffsetWithinBlock, ProgressionFraction)` — `ProgressionFraction` (`scrollLeft/scrollWidth`, 0–1) is a coarse fallback if a block ID can't be found on reload. Deliberately simpler than EPUB CFI: Paperbunkr's own stored positions only ever need to be read back by Paperbunkr itself, so CFI's cross-reading-system portability isn't a problem here, and normalization is fully deterministic and under our control for all three formats. |
| **Highlight popup anchoring** | Same UX as today, fed by `getBoundingClientRect()` from JS instead of `ParagraphView`-local bounds, translated into `RootGrid` space the same way `BookReaderScreen.axaml.cs`'s `TranslateToRootGrid` does now. |
| **Data migration** | `Book.LastChapterIndex`/`LastCharacterOffset` and the `BookBookmark`/`BookHighlight` anchor columns are **cleared**, not converted, in the migration — no attempted best-effort mapping (rejected as fragile given the fundamentally different position encodings). A follow-up item (tracked, not part of this spec) is a user-facing warning dialog before this ships broadly. |
| **Accessibility — chrome** | Unaffected. Button `AutomationProperties.Name`/`HelpText`, drawer `LabeledBy`, and the `Ctrl+Shift+W` heading-trail live region all live on the surrounding Avalonia chrome, not the reading content — carry over unchanged. |
| **Accessibility — reading content** | `ParagraphViewAutomationPeer` retires with `ParagraphView`. Real semantic HTML in Chromium usually gets solid AT-tree exposure for free, but **whether WebView2's internal accessibility tree bridges out through Avalonia's own automation-peer system (as opposed to WPF/WinForms, which WebView2's docs are written against) is unverified** — must be checked with a real Narrator session before relying on it, same "verify, don't assume from docs" discipline the prior accessibility spec required. Fallback if it doesn't bridge cleanly: a minimal peer on the hosting WebView control reporting the current page's `innerText`. |
| **Annotation image capture** | Expected to survive largely unchanged (`CaptureOverlay`/`BookAnnotationCaptureService` operate on rendered pixels, not the paragraph model) — verify during implementation rather than assume. |

## Components

1. **Format normalization layer** — `IBookTextSource`'s shared model reworked to carry HTML for
   reflowable formats; `EpubBookSource` reworked to extract real chapter XHTML instead of flattening
   it; `Fb2BookSource` reworked to emit HTML; `MobiBookSource`'s reconstructed stream routed to the
   new pipeline instead of through `HtmlProseExtractor`; `PdfBookSource` untouched. A shared block-ID
   injection pass applied after per-format HTML generation, before handing content to the WebView.
2. **WebView host control + JS bridge** (new, e.g. `BookReaderWebView`) — hosts WebView2, injects the
   before/after CSS layers and normalized HTML, exposes page-turn/selection/highlight/position
   messaging between JS and the host ViewModel.
3. **`BookReaderScreenViewModel` rework** — locator/anchor model change (position, bookmarks,
   highlights), settings→CSS-variable mapping, replacing direct calls into `ParagraphView`/
   `BookPaginator`.
4. **Data model changes** — new locator columns/shape on `Book`/`BookBookmark`/`BookHighlight`
   replacing the flattened-offset columns; a migration that clears the old data per the Decisions
   table.
5. **Accessibility verification pass** — a real Narrator session against the new WebView2-in-Avalonia
   reading surface, called out as its own explicit step, not assumed satisfied by code review.

## Risks / Open Questions

- **WebView2-in-Avalonia accessibility bridging is unverified** — the single biggest risk in this
  spec. If it doesn't bridge automatically, the fallback (page-level `innerText` peer) is materially
  worse than today's per-paragraph `ParagraphViewAutomationPeer` granularity; this needs an early
  spike, not something to discover after everything else is built on top of it.
- **CSS Custom Highlight API support** in whatever Chromium version ships with WebView2 needs
  checking directly, not assumed from general "modern browsers support it" knowledge — span-wrapping
  is the fallback either way.
- **Fb2BookSource/MobiBookSource test rework**: the tests shipped alongside those sources this same
  cycle (`Fb2BookSourceTests`, `MobiBookSourceTests`) assert against the *old* `BookParagraph`/
  `BookTextSpan` model. Switching those sources to emit HTML means these tests need real rework
  (asserting HTML structure/content), not incremental tweaks — a genuinely large side effect of this
  redesign worth sizing honestly, not treated as a footnote.
- **WebView2 Runtime dependency**: bundled by default on Windows 11, but a Windows 10 install may
  need the Evergreen runtime installed separately — a packaging/distribution concern to resolve, not
  assume away.
- **Reset-not-migrate is a real, deliberate user-facing data loss** for anyone currently using the
  app. Accepted per explicit user call ("we don't have much users anyway"), but should be stated
  plainly in the eventual commit/release notes, and the deferred warning-dialog follow-up should get
  its own tracked item so it isn't silently forgotten.
- **Size**: this is a full rewrite of the reflow reader's rendering substrate, touching ingestion,
  pagination, typography, highlighting, position tracking, and accessibility simultaneously. The
  implementation plan should phase this explicitly (foundation/pagination first, typography next,
  highlights+position together since they share the block-ID anchor, accessibility verification
  last) rather than attempt it as one pass.

## Testing

- **Unit-level**: reworked `Fb2BookSource`/`MobiBookSource` tests asserting HTML output (structure,
  not the old paragraph/span model); block-ID injection determinism tests (same input → same IDs);
  locator/anchor round-trip tests for the new position/highlight shape.
- **Manual/on-screen** (not automatable the way plain-text assertions were): a real Narrator pass
  verifying WebView2's accessibility tree actually reaches Narrator/NVDA/JAWS through Avalonia;
  visual verification of CSS multi-column pagination across real EPUB/FB2/MOBI files (including one
  with in-body images, to confirm the core gap that started this redesign is actually closed);
  highlight creation across a page/column boundary; typography/theme slider live-updates flowing
  through the CSS variable layer correctly.
