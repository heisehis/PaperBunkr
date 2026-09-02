# Books Reader Screen-Reader Accessibility — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-01-books-reader-screen-reader-accessibility-design.md*

## Status check before starting

Component 2 (`ParagraphViewAutomationPeer`) is **already done** — it exists at
`src/Paperbunkr.App/Views/ParagraphViewAutomationPeer.cs`, is wired via
`ParagraphView.OnCreateAutomationPeer()` (`ParagraphView.cs:521`), and cites this spec directly in
its own doc comment. It was built alongside `ParagraphView` in the ergonomics-and-annotations
spec's implementation, per that cross-spec dependency note. `BookReaderScreen.axaml` also already
has 2 of its ~20 controls labeled (Highlights, Export annotations buttons) from that same pass.
This plan covers what's left: the rest of Component 1 (chrome labeling + live region) and
Component 3 (heading trail).

## Step 1: `BookReaderScreenViewModel` — reading-position property + heading-trail command

**Files:** `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit)
**What:**
- New `[ObservableProperty] private string _readingPositionAnnouncement = string.Empty;` next to the
  other chrome-adjacent observable properties (~line 233, by `ProgressPercent`).
- In `RecomputeCurrentPage()` (currently ends ~line 1040), set
  `ReadingPositionAnnouncement = chapter.Title;` alongside the existing `ChapterTitle = chapter.Title;`
  assignment (~line 1021). This method is the single call site for both in-chapter page turns and
  chapter changes, so this covers the design's "page-turn/chapter-change" live-region trigger
  without inventing a page-count concept the reflow pagination doesn't otherwise track. Same-chapter
  page turns re-assign the same string, which is a no-op for a live region (AT only announces on an
  actual text change) — not spammy.
- New `[RelayCommand] private void AnnounceReadingPosition()` (grouped with the other chrome commands
  ~line 407 on): no-ops if `_source is null`; otherwise builds
  `$"Chapter {_position.ChapterIndex + 1} of {_source.Chapters.Count}: {_source.Chapters[_position.ChapterIndex].Title}"`
  and assigns it to `ReadingPositionAnnouncement` — same property Component 1's live region already
  renders, per the design's explicit decision to reuse it rather than add a second live region.
**Depends on:** none
**Verify:** `dotnet test src/Paperbunkr.App.Tests` (new tests added in Step 3 exercise this directly).

## Step 2: `BookReaderScreen.axaml` — chrome labeling, live region, keybinding

**Files:** `src/Paperbunkr.App/Views/BookReaderScreen.axaml` (edit)
**What:**
- `AutomationProperties.Name` (+ `AutomationProperties.AutomationId` for the FlaUI proxy check) on
  every currently-unlabeled icon-only chrome control: TOC (☰), Search (🔍), Bookmarks (🔖), Font/theme
  (Aa), Close (✕) in the top bar; Previous (‹) / Next (›) in the bottom bar; the bookmark/highlight
  "Bookmark this page" toggle; delete-row (✕) buttons in the Bookmarks and Highlights lists ("Remove
  bookmark" / "Remove highlight" — mirrors their existing `ToolTip.Tip` text); the 4 highlight-color
  swatch buttons in the popup ("Yellow highlight" / "Green highlight" / "Blue highlight" / "Pink
  highlight" — icon-only, no text `Content`); the 6 theme swatch buttons in the font sheet ("Light
  theme" / "Dark theme" / "Sepia theme" / "Match app skin theme" / "OLED black theme" / "High
  contrast theme" — same reasoning). Buttons whose `Content` is already plain readable text (Serif/
  Sans/Mono/OpenDyslexic, Compact/Normal/Relaxed, Cancel, Remove highlight, search results, TOC/
  bookmark/highlight rows) are skipped — their accessible name already comes from `Content` per the
  accessibility subskill's own guidance, adding `Name` there would be redundant.
- `SearchBox` gets `AutomationProperties.Name="Search this book"` (a `Watermark` alone isn't reliably
  exposed as the accessible name).
- `AutomationProperties.LabeledBy`: give `x:Name` to the "Chapters"/"Bookmarks"/"Highlights" drawer
  header `TextBlock`s and set `AutomationProperties.LabeledBy="{Binding #<name>}"` on each
  corresponding `ItemsControl` (TOC's, Bookmarks', Highlights').
- New always-in-tree, visually-hidden live-region `TextBlock`: `Text="{Binding
  ReadingPositionAnnouncement}"`, `AutomationProperties.LiveSetting="Polite"`,
  `AutomationProperties.Name="Reading position"`, `AutomationProperties.AutomationId=
  "ReadingPositionLiveRegion"`. Hidden via `Opacity="0"` + `IsHitTestVisible="False"` (not
  `IsVisible="False"` — an invisible-via-`IsVisible` element risks being dropped from the automation
  tree entirely in Avalonia, which would silently defeat the whole point; this needs the manual
  Narrator pass in Step 4 to actually confirm either way per the design's own risk callout).
- `<UserControl.KeyBindings><KeyBinding Gesture="Ctrl+Shift+W"
  Command="{Binding AnnounceReadingPositionCommand}" /></UserControl.KeyBindings>` — same
  `UserControl.KeyBindings` mechanism `LibraryScreen.axaml` already uses for its own gesture-to-
  command bindings (`Ctrl+I`/`Ctrl+A`/`Delete`), not a new pattern.
**Depends on:** Step 1 (`AnnounceReadingPositionCommand`, `ReadingPositionAnnouncement`)
**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` (AVLN2000 gotcha doesn't apply —
no new `.axaml`/`x:Class`, just edits to an existing compiled view). Visual smoke check via `dotnet
run` that the reader still opens and renders normally (this file has zero prior AutomationId/
LiveSetting usage in this codebase to copy a known-good precedent from — first use of both in this
project).

## Step 3: Unit tests for the new VM behavior

**Files:** `src/Paperbunkr.App.Tests/BookReaderScreenViewModelTests.cs` (edit)
**What:** New `[Fact]`s, mirroring this file's existing `EpubFixture`-backed pattern (2-chapter book,
"The Beginning" / "The End"):
- `RecomputeCurrentPage_OnLoad_SetsReadingPositionAnnouncementToChapterTitle` — load the book, assert
  `vm.ReadingPositionAnnouncement == "The Beginning"`.
- `GoToChapter_UpdatesReadingPositionAnnouncement` — call `vm.GoToChapterCommand.Execute(...)` for
  chapter index 1, assert it becomes `"The End"`.
- `AnnounceReadingPositionCommand_BuildsChapterOfTotalString` — after load, execute the command,
  assert `vm.ReadingPositionAnnouncement == "Chapter 1 of 2: The Beginning"`; navigate to chapter 2
  and re-execute, assert `"Chapter 2 of 2: The End"`.
**Depends on:** Step 1
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter BookReaderScreenViewModelTests`

## Step 4: Manual Narrator pass (required, not optional)

**Files:** none — this is the verification the design doc calls out as having no automated
substitute, and as genuinely new territory for this codebase (no prior Narrator-testing precedent
to build on).
**What:** With the app running (`dotnet run --project src/Paperbunkr.App`), turn on Windows Narrator
(`Ctrl+Win+Enter`) and, from inside an open book in the reflow reader:
1. Confirm the chrome buttons above are announced by name, not silently skipped.
2. Confirm the TOC/Bookmarks/Highlights drawers announce their header as the list's label when
   focus moves in.
3. Confirm a page turn or chapter change produces a spoken position announcement (validates the
   live region actually reaches the accessibility tree — the one thing Step 2 flagged as unverified
   by construction alone).
4. Press `Ctrl+Shift+W` and confirm the "Chapter N of M: Title" trail is spoken.
5. Tab through the reader using only the keyboard and confirm every chrome control is reachable and
   operable without a mouse.
6. Focus a paragraph in the reading pane and confirm Narrator reads its actual text (validates
   `ParagraphViewAutomationPeer`'s tree discoverability — the specific risk the design doc flags as
   "can silently half-work").
**Depends on:** Steps 1-2 built and running
**Verify:** This step *is* the verification — record pass/fail per item above; report any control
that stays silent or unreachable back as a bug against this spec, not a pre-existing gap to route
elsewhere.

## Known, explicitly out-of-scope gap: no FlaUI on-screen test for the reader itself

`Paperbunkr.App.UiTests` drives the real compiled exe via FlaUI/UIA3 — it can't shortcut through
code, only through real clicks. Reaching `BookReaderScreen` that way requires a book card click in
`BooksScreen.axaml` → `BookDetailScreen` → its `DetailHero`-rendered "Continue" action, and **none of
those three have any `AutomationProperties.AutomationId` today** (confirmed: zero in
`BookDetailScreen.axaml`; `DetailHero.axaml`'s action buttons are a shared, data-driven
`ItemsControl` used by all three detail screens, comic/manga/book alike). Wiring that up is a
materially separate undertaking, not a natural extension of this spec's stated component list
(`BookReaderScreen.axaml` chrome + `ParagraphViewAutomationPeer` + the heading-trail command) — and
it's the exact same standing gap `HomeScreenTests.cs`'s own doc comment already calls out for the
comic reader ("no UI-automation fixture in this codebase has [a way to open a real archive and page
through]"). This plan does not attempt to close it; Step 4's manual pass is the real verification
for this round, matching the design doc's own "Testing posture" decision.
