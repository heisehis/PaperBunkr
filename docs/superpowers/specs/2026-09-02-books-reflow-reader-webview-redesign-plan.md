# Books Reflow Reader WebView Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-02-books-reflow-reader-webview-redesign-design.md*

## Survey notes (grounding for the steps below)

- **WebView package**: `Avalonia.Controls.WebView` 12.0.0 (first-party AvaloniaUI OÜ package, targets
  Avalonia 12.x/.NET 8+/10 — matches Paperbunkr's `Avalonia 12.1.1`/`net10.0` exactly). Windows uses
  WebView2 automatically, matching the design's chosen engine. Confirmed via NuGet + Avalonia's own
  docs (`docs.avaloniaui.net/controls/web/nativewebview`), not assumed.
  - Embeddable control: `NativeWebView` (namespace `Avalonia.Controls`), placed in a normal layout
    container (`<NativeWebView x:Name="ReaderWebView" />` inside a `Grid`/`DockPanel`, same as any
    other control).
  - `NavigateToString(string html)` — loads raw HTML directly (no server needed; this is how each
    chapter's normalized HTML gets shown).
  - `InvokeScript(string script)` — runs JS in the loaded page, `async Task<...>`.
  - `WebMessageReceived` event — fires when page JS calls `invokeCSharpAction(body)`; this is the
    JS→host bridge for page-turn/selection/highlight/position messages.
  - `NavigationCompleted` event — fires once a chapter's HTML has finished loading (needed to know
    when it's safe to run the block-ID/pagination setup script).
- **`IBookTextSource` blast radius**: `BookChapter.Paragraphs`/`BookParagraph`/`BookTextSpan`
  (`Paperbunkr.Engine/IO/Provider/Books/IBookTextSource.cs`) are currently consumed well beyond just
  the reading pane — `BookDetailScreenViewModel.LoadChaptersAndBookmarks` (chapter titles for the TOC
  section), `AnnotationExportService.ResolveChapterTitles`, `HomeBookCard`/`BookReaderScreenViewModel`
  (chapter counts/progress). **Deliberate phasing decision** (not explicitly in the design doc, which
  says HTML "instead of" paragraphs — this is a sequencing refinement, not a contradiction of intent):
  `BookChapter` gains a new `Html` property **alongside** the existing `Paragraphs`, so every other
  consumer keeps working unmodified while the reading pane migrates over. `Paragraphs` and
  `HtmlProseExtractor` get removed only in the final cleanup step, once nothing reads them anymore.
- **Existing reading-pane wiring to replace**: `BookReaderScreen.axaml`'s
  `<ScrollViewer><ItemsControl ItemsSource="{Binding CurrentPageParagraphs}">` +
  `<views:ParagraphView>` DataTemplate (lines ~108-123); `BookReaderScreenViewModel.RecomputeCurrentPage`/
  `CurrentPageRange`/`MeasureParagraphHeight` (the whole `BookPaginator`-driven pagination path);
  `BookReaderScreen.axaml.cs`'s `OnParagraphSelectionCompleted`/`OnParagraphHighlightTapped` handlers.
- **Entities touched later** (position/highlight anchor change, Step 8): `Book.LastChapterIndex`/
  `LastCharacterOffset`, `BookBookmark.ChapterIndex`/`CharacterOffset`, `BookHighlight.ChapterIndex`/
  `StartOffset`/`EndOffset` — all currently plain `int` columns via `PaperbunkrDbContext`, no special
  conversion, so the migration is a real column shape change (drop old, add new), not just a value
  reinterpretation.
- **Tests needing rework**: `ParagraphViewTests.cs`, `BookPaginatorTests.cs` (removed once Step 10
  retires those classes); `Fb2BookSourceTests.cs`/`MobiBookSourceTests.cs` (reworked in Step 2 to
  assert HTML output rather than the old paragraph/span model, since those sources change shape).

## Step 1: Add the WebView package, prove basic embedding ✅ done

**Files:** `src/Paperbunkr.App/Paperbunkr.App.csproj` (edit)
**What:** Add `<PackageReference Include="Avalonia.Controls.WebView" Version="12.0.0" />`. Confirm
the solution still builds clean (this alone doesn't wire anything into the reader yet — later steps
do).
**Depends on:** none
**Verify:** `dotnet build` whole solution.

## Step 2: `IBookTextSource` gains an `Html` chapter property; all three reflowable sources populate it ✅ done

**Files:** `src/Paperbunkr.Engine/IO/Provider/Books/IBookTextSource.cs` (edit — add
`BookChapter.Html`), `src/Paperbunkr.Engine/IO/Provider/Books/EpubBookSource.cs` (edit),
`src/Paperbunkr.Engine/IO/Provider/Books/Fb2BookSource.cs` (edit),
`src/Paperbunkr.Engine/IO/Provider/Books/MobiBookSource.cs` (edit),
`src/Paperbunkr.App.Tests/Fb2BookSourceTests.cs` (edit), `src/Paperbunkr.App.Tests/MobiBookSourceTests.cs` (edit)
**What:**
- `BookChapter.Html` (nullable `string?`) added alongside `Paragraphs` (kept, per the phasing note
  above).
- `EpubBookSource`: capture each `content.Content` (its real XHTML string, already available from
  `VersOne.Epub`) into `Html` directly, in addition to the existing `HtmlProseExtractor.ExtractParagraphs`
  call for `Paragraphs`.
- `Fb2BookSource`: alongside the existing paragraph-collection walk, build a real HTML string per
  chapter (`<p>`/`<em>`/`<strong>` from the same XML structure already being walked; `<img>` for any
  `<coverpage>`/inline image references resolvable against the `<binary>` blocks already parsed).
- `MobiBookSource`: its already-reconstructed HTML-ish stream (before `HtmlProseExtractor` flattens
  it) becomes `Html` directly, per-chapter-chunk (same chunks `SplitIntoChapters` already computes).
- Update `Fb2BookSourceTests`/`MobiBookSourceTests` with new assertions on `Html` content
  (string-contains checks on the real markup) alongside the existing paragraph-based assertions,
  which stay green unmodified since `Paragraphs` isn't removed yet.
**Depends on:** Step 1 (not technically, but sequenced first since it's foundational to everything else)
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter Fb2BookSourceTests|MobiBookSourceTests`;
existing `BookReaderScreenViewModelTests`/`BookDetailScreenViewModelTests`/`BookFolderScanServiceTests`
stay green untouched (they only ever read `Paragraphs`/`Title`, both still populated).

## Step 3: Block-ID injection helper (shared across formats) ✅ done

**Files:** `src/Paperbunkr.Engine/IO/Provider/Books/BlockIdInjector.cs` (new),
`src/Paperbunkr.App.Tests/BlockIdInjectorTests.cs` (new)
**What:** A small, format-agnostic post-processing pass: given a chapter's `Html` string, inject a
deterministic `id="pb-p<n>"` onto every top-level paragraph-level element (`<p>`, `<h1>`-`<h6>`,
`<li>`, `<blockquote>` — whatever's already a block-level tag in the generated markup) via a single
regex/tokenizing pass (same "regex-tokenizing, not a real DOM parser" posture `HtmlProseExtractor`
already uses successfully for this exact class of problem — a real `HtmlAgilityPack`-style DOM
parser is overkill given the markup is entirely our own generated output, not arbitrary third-party
HTML). Determinism (same input → same IDs) is the one hard requirement, since highlights/positions
anchor against these IDs.
**Depends on:** Step 2 (needs real `Html` to inject into)
**Verify:** New unit tests: same input twice → identical IDs; a block-level element without an
existing `id` attribute gets one; an element that already has one is left alone (needed for the
`<binary>`/image cover-reference ids Fb2BookSource already assigns).

## Step 4: `NativeWebView` reading surface — EPUB only, static (no pagination/highlights/typography yet) ✅ done (needs on-screen confirmation)

**Files:** `src/Paperbunkr.App/Views/BookReaderScreen.axaml` (edit — replace the
`ScrollViewer > ItemsControl > ParagraphView` block with a `NativeWebView`),
`src/Paperbunkr.App/Views/BookReaderScreen.axaml.cs` (edit — wire `NavigationCompleted`),
`src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit — new method building the
per-chapter HTML document to load, replacing `RecomputeCurrentPage`'s paragraph-fitting role for now
with "load the whole current chapter")
**What:** First visible milestone: opening an EPUB book loads its current chapter's real HTML
(images included) into the `NativeWebView` via `NavigateToString`, wrapped in a minimal HTML
document shell (`<html><head><style>...</style></head><body>{chapter.Html}</body></html>`) with a
bare-bones reset stylesheet (no theming/pagination CSS yet — that's Steps 5-6). This step
deliberately does NOT yet handle FB2/MOBI, pagination, highlights, or typography — it exists to
prove the core "real markup renders, including images" gap is closed, which is the concrete thing
that started this whole redesign, before layering the rest on top.
**Depends on:** Steps 2, 3
**Verify:** Manual/on-screen only (this is inherently a rendering-visible change) — open the same
Dune EPUB used to diagnose the original gap, confirm in-body images now render. No automated test
meaningfully covers "does a WebView visually render HTML correctly."

## Step 5: Pagination + page-turn JS bridge ✅ done (needs on-screen confirmation; NextPage/PreviousPage's granularity changed from paragraph-fitted to chapter-level at the ViewModel layer, page-turn buttons now use Click handlers not Command bindings)

**Deviation from the design doc's decision, found necessary after real on-screen testing:** the
design called for CSS multi-column layout (`column-width`), matching Thorium/Readium. Two different,
individually well-reasoned fixes for it were tried against the real running app and **both produced
the identical symptom** - the next column's text visibly bleeding in at the right edge - first using
`vw`-based sizing, then having JS measure the real rendered box and set `column-width` as an exact
pixel value. Two different fixes failing identically pointed at the actual defect being somewhere in
how this specific WebView hosting mode (`Avalonia.Controls.WebView` 12.0.0) handles multi-column
layout + horizontal overflow clipping generally, not in either fix's own math - and that's not
diagnosable further without live devtools access, which this session doesn't have. **Switched to
plain vertical scroll** (`#pb-content { overflow-y: auto }`, page-turn = one-`clientHeight` `scrollTop`
jump) instead - no columns, no horizontal-overflow-clipping question to get wrong, and it's about as
basic and reliably-implemented a browser behavior as exists. Builds clean, tests pass; **not yet
on-screen confirmed** - relaunched for the user to check.
This is a real, disclosed departure from the design doc's stated decision (Section: pagination) -
worth revisiting later if paged (rather than continuous-scroll) reading is a hard requirement, but not
re-attempted blind in this session given the track record above.

**Real gap found and fixed after this step shipped:** the reading pane's tap-to-toggle-chrome
(`OnContentPointerPressed`, previously an Avalonia `PointerPressed` handler on a `Border` wrapping the
reading pane) went dead the moment that `Border` was replaced by a full-bleed `NativeWebView` in Step
4 - a native embedded control doesn't reliably bubble pointer input through Avalonia's own routed-event
system, so tapping the content stopped revealing the chrome bars at all. Since chrome starts hidden
and nothing else was setting it visible, this meant **no way to reach close/TOC/search/settings once
hidden** - found via the user actually looking at a running build, not caught by any test (nothing in
this suite exercises native WebView input routing). Fixed by moving tap detection into
`HighlightScript`'s JS (a `click` listener on `#pb-content` that ignores highlight-taps and active
selections, then messages the host) instead of relying on Avalonia bubbling - same fix pattern Step 7's
selection/highlight-tap messaging already established. The `Ctrl+Shift+W` accessibility shortcut
(`UserControl.KeyBindings`) had the identical exposure - fixed the same way, via a JS `keydown`
listener forwarding to the host. **Still unverified/lower-confidence:** `NotifyPointerActivity`
(auto-reveal near the top edge) and `NotifyKeyActivity` (auto-reveal on any key press) still depend on
Avalonia-level `PointerMoved`/`KeyDown` bubbling from the WebView and were not fixed the same way -
they may or may not still work; not yet confirmed either way.

**Files:** `src/Paperbunkr.App/Views/BookReaderScreen.axaml.cs` (edit),
`src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit — `PreviousPageCommand`/
`NextPageCommand` now call `InvokeScript` instead of touching `BookPaginator`),
`src/Paperbunkr.App/Resources/BookReaderPagination.js` (new, embedded resource or inline string
constant — small enough either way, prefer embedded resource for readability)
**What:** Inject the `column-width`/`column-gap:0`/fixed-`height` CSS from the design's pagination
decision; page-turn becomes `scrollLeft += viewportWidth` via `InvokeScript`. `ProgressPercent`
becomes `scrollLeft/scrollWidth` read back via `InvokeScript`'s return value. `CanGoPrevious`/page
boundary detection (is this the first/last column) also read back this way.
**Depends on:** Step 4
**Verify:** Manual/on-screen — page-turn buttons/keyboard actually move through a multi-page chapter;
progress percentage updates sensibly.

## Step 6: Typography/theme CSS variable injection ✅ done (needs on-screen confirmation)

**Files:** `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit — settings changes now
re-inject the `after` CSS layer instead of touching `ParagraphView`/`TextLayout` construction),
`src/Paperbunkr.App/Views/BookReaderPagination.js` or a new `BookReaderTheme.css`-equivalent string
builder (new)
**What:** The three-layer CSS injection from the design (`before` reset → book's real CSS → `after`
forcing `--pb-*` variables with `!important`). Every existing `BookReaderSettings` slider (font
size/family, character/word/paragraph spacing, page margin, line spacing) and the six theme presets
map onto this layer, replacing their current `ParagraphView`/`TextLayout`-construction wiring.
**Depends on:** Steps 4, 5
**Verify:** Manual/on-screen — each slider/theme swap visibly updates the rendered page without a
full reload feeling jarring.

## Step 7: Highlighting — selection, rendering, and the shared block-ID anchor ✅ done (needs on-screen confirmation; real JS DOM manipulation I can't click-test myself — highest-risk step so far)

**Files:** `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit — highlight
creation/deletion/popup-anchoring now driven by JS-reported selection + block IDs instead of
`ParagraphView`'s `SelectionCompleted` event), `src/Paperbunkr.App/Views/BookReaderScreen.axaml.cs`
(edit — remove `OnParagraphSelectionCompleted`/`OnParagraphHighlightTapped`, replace with
`WebMessageReceived` handling), JS bridge script additions for `window.getSelection()` capture +
`<span class="pb-highlight pb-color-X">` wrapping (or the CSS Custom Highlight API if the shipped
Chromium version supports it — check via `InvokeScript` feature-detection at implementation time,
this is explicitly unresolved per the design's Risks section)
**Depends on:** Steps 3 (block IDs), 4, 5
**Verify:** Manual/on-screen — select text, create a highlight, confirm it persists across a page
reload and renders in the right spot; delete a highlight.

## Step 8: Position/locator model — entity + migration change (reset, not convert) — scoped down (see below)

**What actually shipped:** `BookHighlight`'s anchor (the piece Step 7 needed to function at all) is
done - `BlockId`/`StartOffset`/`Length`, migration resets old rows, real test coverage, all above.

**What's deferred, honestly:** `Book.LastChapterIndex`/`LastCharacterOffset` and
`BookBookmark.ChapterIndex`/`CharacterOffset` are **unchanged** - not migrated to the BlockId scheme.
Reason: unlike highlight creation (a drag-selection, which the WebView already reports precisely via
`HighlightScript`), capturing "the currently topmost visible block" for a bookmark or resume-position
needs a *new* JS query (walk visible blocks, find the first one at/after the current scroll offset)
that nothing built so far provides - a real, separate piece of work comparable in size to Step 7's
selection-capture script, not a small addition. Building it without being able to click-test it
myself (same standing limitation as the rest of this plan's WebView-facing pieces) felt like the
wrong tradeoff to force in this pass.

**Concrete, disclosed behavior change this leaves in place:** `BookPosition.CharacterOffset` no
longer tracks a real within-chapter position (Step 5 made page-turn chapter-granular already, before
Step 8 was reached) - `ToggleBookmark`/`IsCurrentPositionBookmarked` were updated to match by
`ChapterIndex` alone, honestly reflecting this rather than silently comparing two always-0 offsets. So
today: one bookmark per chapter (not per-paragraph like before), and resuming a book lands at the
start of the last-read chapter (not the precise last-read position within it). Both are real
regressions from the pre-redesign behavior, not invisible ones - worth a deliberate decision on
priority for a follow-up pass, not something to silently ship as "done."

<details>
<summary>Original step description</summary>

**Files:** `src/Paperbunkr.Data/Entities/Book.cs` (edit — replace `LastChapterIndex`/
`LastCharacterOffset` with the new locator shape), `src/Paperbunkr.Data/Entities/BookBookmark.cs`
(edit), `src/Paperbunkr.Data/Entities/BookHighlight.cs` (edit), `src/Paperbunkr.Data/PaperbunkrDbContext.cs`
(edit — column config for the new properties), new EF migration under
`src/Paperbunkr.Data/Migrations/` (drops the old offset columns, adds the new locator columns — no
data-preserving conversion, per the design's explicit reset decision),
`src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit — position tracking/bookmark
creation against the new shape), a new migration test mirroring the project's existing pattern but
asserting the reset behavior specifically (old rows' position data is gone post-migration, not
garbage-mapped).
**Depends on:** Step 7 (reuses the same block-ID anchor shape highlights just established)
**Verify:** New migration test; `BookReaderScreenViewModelTests`/`BookDetailScreenViewModelTests`
reworked for the new position shape (real rework, not incremental — same caveat as Step 2's source
tests).

</details>

## Step 9: Accessibility verification spike — automated probe done 2026-09-02, positive result; a real Narrator spot-check is still the final word

**What actually shipped:** no computer-use access this session means an actual Narrator audio
session isn't something this environment can drive directly. Built the next best thing instead: a
real FlaUI/UIA3 UI test (`src/Paperbunkr.App.UiTests/BookReaderAccessibilityTests.cs`, new) that
launches the real compiled app, seeds a throwaway EPUB with distinctive prose ("It was a dark and
stormy night"), navigates Books → a book card → "Start reading" into the WebView-based reader, and
queries the live Windows UI Automation tree for that exact text.

**Real finding along the way, not just the headline result:** FlaUI's own `FindFirstDescendant`/
`FindAllDescendants` (scoped to UIA's "control view" by default) could not find even a definitely-
present plain `TextBlock` (`ReadingPositionLiveRegion`) that a raw, unfiltered `FindAllChildren()`
walk confirmed *is* there - the control-view filter itself hides real elements in this app, not
something specific to the WebView. Switched the probe to the raw walk throughout once this surfaced,
since a false negative from the wrong search API would have been worse than no test at all.

**Positive result:** with the raw walk, the chapter's real rendered prose text ("dark") *is*
discoverable as a descendant `AutomationElement.Name` within ~25s of navigating into the reader -
Chromium's own accessibility tree does bridge through this `Avalonia.Controls.WebView` 12.1.0
hosting into Windows UIA, at least at the raw-tree level. This is real evidence against needing the
design's fallback (`BookReaderWebViewAutomationPeer`), not a guess.

**Honest caveat, not swept under the rug:** a raw UIA tree match is a strong proxy, not proof
Narrator announces it the same way a sighted developer would expect, especially given the control-
view gap just found - if Narrator's own navigation modes (e.g. structural heading/paragraph
browsing, not just linear reading) rely on the control view rather than the raw tree, there could
still be a real gap this probe wouldn't catch. **A real Narrator spot-check remains worth 10 minutes
of the user's own time to fully close this out** - open a book, turn Narrator on (Win+Ctrl+Enter),
confirm it reads the chapter text aloud and that Ctrl+Shift+W's "where am I" shortcut is audible.
No code changes are blocked on that check; this is a confidence-raising step, not a hard gate.

**Small, real, in-scope prerequisite fixes made getting here:** `BooksScreen.axaml`'s book card and
`DetailHero.axaml`'s primary action button had no `AutomationProperties.AutomationId` at all before
this - added (`BookCard` + `AutomationProperties.Name="{Binding Title}"`, and
`AutomationProperties.AutomationId="{Binding Label}"` respectively), matching this codebase's
existing convention everywhere else and needed to make the reader screen reachable via UI automation
at all - no prior UI test in this project ever opened the reader (comic or book) via clicks, so this
was genuinely new ground, not filling in a known gap. `BookReaderScreen.axaml`'s `NativeWebView` also
gained `AutomationProperties.AutomationId="BookReaderWebView"` for the same reason.

**Files:** `src/Paperbunkr.App.UiTests/BookReaderAccessibilityTests.cs` (new),
`src/Paperbunkr.App/Views/BooksScreen.axaml` (edit — book card AutomationId/Name),
`src/Paperbunkr.App/Views/DetailHero.axaml` (edit — primary action AutomationId),
`src/Paperbunkr.App/Views/BookReaderScreen.axaml` (edit — WebView AutomationId). No
`BookReaderWebViewAutomationPeer` fallback class needed per the result above.
**Depends on:** Steps 4, 5, 6
**Verify:** `dotnet test src/Paperbunkr.App.UiTests --filter BookReaderAccessibilityTests` (passing);
a real Narrator session is still the recommended final confirmation, not a blocking one.

## Step 10: Retire the dead paragraph-rendering path — done 2026-09-02, with a real scope correction

**Real scope correction found while doing this, not assumed from the plan's original wording:**
`BookChapter.Paragraphs`/`BookParagraph`/`BookTextSpan` could **not** be removed from
`IBookTextSource.cs`, and `BookPaginator`/`HtmlProseExtractor` could **not** be deleted wholesale -
both were live, load-bearing dependencies the plan hadn't accounted for:
- **`PdfBookSource`** (a completely separate reader path - `PdfPageReaderScreenViewModel`/PDFium page
  rasterization, never touched by this WebView redesign, per the design's own explicit note that PDF
  has no real markup to normalize `Html` to) builds `BookParagraph` objects directly and structurally
  depends on `IBookTextSource.Paragraphs` existing at all.
- **`BookReaderScreenViewModel.ToggleBookmark`** (bookmark excerpt text) and **`.RunSearch`** (the
  reading pane's in-book search feature) are real, currently-shipping features that call
  `BookPaginator.FindParagraphIndex`/`ComputeParagraphOffsets` - not part of the old renderer at all,
  just sharing its utility class.
- `EpubBookSource`/`MobiBookSource` still call `HtmlProseExtractor.ExtractParagraphs` to populate
  `Paragraphs` for exactly those two features (Fb2BookSource builds paragraphs from its own XML walk
  instead, never via `HtmlProseExtractor`).

**What actually shipped**, once this was untangled:
- Deleted (fully dead, confirmed via a real cross-codebase grep before touching anything, not
  assumed): `src/Paperbunkr.App/Views/ParagraphView.cs`, `ParagraphViewAutomationPeer.cs`,
  `src/Paperbunkr.App.Tests/ParagraphViewTests.cs`, `src/Paperbunkr.App/Models/BookParagraphDisplay.cs`
  (only ever used by the paragraph-fitted `CurrentPageParagraphs` collection below).
- `BookPaginator.cs`: removed only `FillPage` (the actual page-layout-fitting algorithm, the truly
  dead half) - kept `ComputeParagraphOffsets`/`FindParagraphIndex`/`ParagraphSeparator`, per the real
  dependencies above. Class doc comment rewritten to explain the split.
- `BookPaginatorTests.cs`: removed the 5 `FillPage_*` tests, kept the 5
  `ComputeParagraphOffsets_*`/`FindParagraphIndex_*` tests.
- `BookReaderScreenViewModel.cs`: removed `CurrentPageParagraphs`, `CurrentPageRange`,
  `MeasureParagraphHeight`, and the paragraph-fitting body of `RecomputeCurrentPage` (its blank-
  chapter-skip logic, `CurrentChapterHtml` assignment, TOC/bookmark/progress updates all stayed -
  `ProgressPercent` simplified to a chapter-start fraction only, since within-chapter progress is the
  WebView's own concern per Step 5). Removed now-unused `Avalonia.Media`/`Avalonia.Media.TextFormatting`
  usings. Updated several stale doc comments (class-level, constructor, a `ParagraphView`-typed cref)
  that described the old renderer.
- `BookReaderScreenViewModelTests.cs`: 4 tests that asserted on `CurrentPageParagraphs` reworked to
  assert on `CurrentChapterHtml` instead (the real signal the WebView renders from) - same regressions
  covered, not weakened: normal-chapter load, blank-cover-chapter skip, resolved-image-cover NOT
  skipped, and the real cross-book-switch stale-`_source` bug.
- Fixed 3 other stale `<c>ParagraphView</c>`/`<see cref="ParagraphView">` doc-comment references
  (`CaptureOverlay.cs`, `HighlightColorConverter.cs`, `BookReaderSettings.cs`) that would have left
  dangling crefs once the type was gone.
- **Kept unchanged, deliberately:** `IBookTextSource.cs` (`Paragraphs`/`BookParagraph`/`BookTextSpan`),
  `HtmlProseExtractor.cs`, `EpubBookSource.cs`/`Fb2BookSource.cs`/`MobiBookSource.cs`/`PdfBookSource.cs`
  - all still genuinely load-bearing per the dependencies above.

**Files:** `src/Paperbunkr.App/Views/ParagraphView.cs` (deleted),
`src/Paperbunkr.App/Views/ParagraphViewAutomationPeer.cs` (deleted),
`src/Paperbunkr.App/Models/BookParagraphDisplay.cs` (deleted),
`src/Paperbunkr.App.Tests/ParagraphViewTests.cs` (deleted),
`src/Paperbunkr.App/Views/BookPaginator.cs` (edit - `FillPage` removed only),
`src/Paperbunkr.App.Tests/BookPaginatorTests.cs` (edit - `FillPage_*` tests removed only),
`src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/BookReaderScreenViewModelTests.cs` (edit - 4 tests reworked),
`src/Paperbunkr.App/Views/CaptureOverlay.cs`, `HighlightColorConverter.cs`,
`src/Paperbunkr.App/Models/BookReaderSettings.cs` (doc-comment fixes only).
**Depends on:** Steps 2-9 (landed first, per the plan's own gating).
**Verify:** Full solution build (`dotnet build Paperbunkr.sln`) - 0 errors; full non-UI test suite
(`dotnet test Paperbunkr.sln --filter "FullyQualifiedName!~UiTests"`) - 1488/1488 App.Tests green,
710/710 Data.Tests green, 20/20 Plugins.Tests green, all passing 2026-09-02.

## Testing strategy summary

- Unit-testable pieces (Steps 2, 3, 8): real xUnit tests, following this project's existing fixture
  conventions (`Fb2Fixture`/`MobiFixture`/`EpubFixture`, migration-test pattern from
  `AddBooksBrowseStateMigrationTests`).
- Rendering/interaction pieces (Steps 4-7, 9): explicitly manual/on-screen — no automated test in
  this codebase can assert "the WebView visually renders correctly" or "Narrator reads this aloud,"
  same standing limitation the project's own UI-automation work has already accepted for reader
  screens generally.
- Step 10 is a deletion step verified by the full suite staying green with nothing left referencing
  the deleted classes.
