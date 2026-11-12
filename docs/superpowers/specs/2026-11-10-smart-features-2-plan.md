# Smart features 2 — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-10-smart-features-2-design.md*

Three independent slices (A recap card, B time budget, C natural-language search). They share no files except `AppSettings`
(A only) and can be built in any order; within a slice the steps are in dependency order. Paths are relative to `src/`.

**Rules for every step** (CLAUDE.md + memory): targeted tests only (`dotnet test <proj> --filter "FullyQualifiedName~<Class>"`), never two
`dotnet` processes at once, kill build servers afterwards, build into `bin` before handing over, no commits, no installer builds. Load the
`avalonia` router + the matching subskill before XAML, and the `paperbunkr-design-language` skill before any view.

## Survey corrections to the spec (record in the spec's §6 Implementation notes when building)

1. **Reader queue (spec §4.4 open question) — resolved: in-memory queue anchor, no hidden reading list.** `ReadingOrderResolver`
   already has two mutually exclusive anchors (reading list, story event) threaded through `ResolveNeighbour`, `ResolveContext`,
   `NextIssueStager.EnsureStaged` and `ReaderScreenViewModel.Load/LoadIssue`. A third anchor, `IReadOnlyList<int>? queue` (issue ids in
   order), fits the same shape: walked by index, cleared by any plain open, kept across boundary crossings like the others.
2. **Read state (spec §5.4) — no behaviour change needed.** `IssueMetadataExtensions.IsUnread` (`LastPageRead is not > 0`),
   `ReadPercentage` (0 when `LastPageRead` ≤ 0) and the Lens's `LibraryViewPipeline.IsUnread` (`LastPageRead is null or 0`) already agree.
   An issue opened but left on page 1 has `LastPageRead == 0` and is Unread everywhere. The spec's "moves from Unread to Reading" and the
   changelog note are dropped; the chips reuse `IsUnread` / `IsInProgress` / `HasBeenRead` and their smart-list equivalents.
3. **No extra snapshot load for C.** The Library's data load already includes `Issues.Tags` and `Issues.MetadataProposals`
   (`ViewModels/LibraryScreenViewModel.cs:1205-1206`), and parsed rules never touch the fields that make `LoadSnapshot` load more (custom
   values, continuity, duplicates, virtual tags, series stats). So the live filter wraps the already-loaded issues in a
   `SmartListQueryBuilder.LibrarySnapshot { Issues = … }`, with no database hit.
4. **Up Next on Home is a ranked vertical list** (`Views/Home/UpNextSection.axaml`, `attentionBanner` rows), not a cover rail as drawn in
   the mockup. The 15/30/60 switch goes on its header line, right-aligned.

---

## Slice A — "Previously on…" card

### A1. Setting column and migration
**Files:** `Paperbunkr.Data/Entities/AppSettings.cs` (edit), `Paperbunkr.Data/Migrations/<ts>_AddRecapGapDays.cs` + `.Designer.cs` (new,
via `dotnet ef migrations add`), `PaperbunkrDbContextModelSnapshot.cs` (generated), `Paperbunkr.Data.Tests/AddRecapGapDaysMigrationTests.cs` (new)
**What:** `public int RecapGapDays { get; set; } = 21;` (0 = off). Hand-edit the generated `Down()` to leave the column, with the same
comment as `20261006105543_AddSmartFeaturesSettings.cs` (rollback-chain bug). Migration test mirrors `AddSmartFeaturesSettingsMigrationTests`
(Up adds with default 21; Down leaves it; no reuse of a dropped column name).
**Depends on:** none
**Verify:** `dotnet test Paperbunkr.Data.Tests --filter "FullyQualifiedName~AddRecapGapDaysMigrationTests|FullyQualifiedName~AddSmartFeaturesSettingsMigrationTests"`.
The shared dev DB migrates the next time the app runs (worktrees share it).

### A2. `RecapCardResolver`
**Files:** `Paperbunkr.Data/Metadata/RecapCardResolver.cs` (new), `Paperbunkr.Data.Tests/RecapCardResolverTests.cs` (new)
**What:** `public static RecapCard? Build(PaperbunkrDbContext ctx, int issueId, int? readingListId, DateTime nowUtc, int gapDays)` returning
`RecapCard(string ContextName, int DaysSince, IReadOnlyList<RecapItem> Items, RecapResume? Resume, IReadOnlyList<string> Characters)`.
- Last activity: max of `ReadingEvents.Where(SeriesId == s).Max(TimestampUtc)` and the series' max `Issue.OpenedTime` (same inputs as
  `SeriesProgress.Of`). Null when `gapDays <= 0` or the gap is shorter.
- Earlier items: reading-list items before the opened one by `SortOrder` when `readingListId` is set, else series issues before it by
  `NumberSortKey` (use the same ordering helper `ReadingOrderResolver` uses). Keep finished ones (Finished event for the item, or
  `HasBeenRead()`), take the nearest 3, return oldest first. `RecapItem(int IssueId, string Label, string? Summary)`.
- Resume: when the opened issue `IsInProgress()` → `RecapResume(int PageIndex)`.
- Characters: `CharacterResolver.ParseNames` over the items' `Characters`, `MainCharacterOrTeam` first, then by count, max 5.
- Null when no item is finished, or no item has a summary and there is no resume. Never reads the opened issue's `Summary`.
**Depends on:** none
**Verify:** new tests: gap below/at/above; `gapDays` 0; series vs list order; finished by event vs by 95 %; null cases; opened issue's
summary excluded; character ranking and cap. `dotnet test Paperbunkr.Data.Tests --filter "FullyQualifiedName~RecapCardResolverTests"`.

### A3. Reader view model: compute, show, dismiss
**Files:** `Paperbunkr.App/ViewModels/ReaderScreenViewModel.Recap.cs` (new partial), `ReaderScreenViewModel.cs` (edit),
`Paperbunkr.App.Tests/ReaderRecapTests.cs` (new)
**What:**
- New partial with `[ObservableProperty] bool IsRecapVisible`, `RecapCard? Recap` plus display rows (cover via the existing thumbnail/cover
  cache the end card uses), `DismissRecapCommand`, `OpenRecapSettingsCommand` (navigates to Preferences › Reader, anchor `reader.recap`).
- In `Load` (`ReaderScreenViewModel.cs:946`): call `RefreshRecap(context, issue, readingListId)` immediately after `RefreshInfoPanel`
  (~:1017) and **before** `issue.OpenCount++` (:1024). Read `RecapGapDays` from settings the same way the comfort settings are read.
- Close on `CurrentPageIndexChanged` (raised at :1251 and :2535; ignore the initial raise from `Load` itself) and on a new `Load`.
**Depends on:** A1, A2
**Verify:** new tests: gap measured before `OpenedTime` is written (an issue last opened 30 days ago still shows the card); first page
turn closes; reload closes; setting 0 never shows. `dotnet test Paperbunkr.App.Tests --filter "FullyQualifiedName~ReaderRecapTests"`.

### A4. Esc through the input service
**Files:** `Paperbunkr.App/Views/ReaderScreen.axaml.cs` (edit), `Paperbunkr.App.Tests/ReaderRecapTests.cs` (extend)
**What:** In `OnReaderInputAction` (attached at :61), the Escape/back action closes the recap first and returns true when
`IsRecapVisible`; otherwise falls through unchanged. No `KeyDown` handler. The panel is never focusable (no `Focusable`, no tab stops other
than its ✕ and link, which stay out of the page-turn path).
**Depends on:** A3
**Verify:** headless test with `InputHost.Attach(window, service)`: Esc closes the recap and does not leave the reader; a second Esc leaves.
Same filter as A3.

### A5. Recap panel XAML
**Files:** `Paperbunkr.App/Views/ReaderScreen.axaml` (edit, near the jump-back `floatingPanel` ~:510)
**What:** Left-docked `Border.card`, width 320, inset 12, `controls:Reveal.IsOpen="{Binding IsRecapVisible}"` (Reveal role, 20 px from
the left). Overline "Previously in …" (`pbTextOverline`), title "Welcome back" (`pbTextTitle`), meta "Last read N days ago"
(`pbTextMeta`); optional "Where you stopped" row; item rows (cover 34×50 `PbRadiusSm`, label `pbTextBody`, summary `pbTextMeta`
wrapping in a Grid `*` column); characters as `Border.pbChip` (first one `.on`/accent state); footer `Button.ghost sm` "Turn off recaps";
`Button.iconButton sm` ✕ with `AutomationProperties.Name="Close recap"`. A `ScrollViewer` inside caps it at the reader height. Tokens only.
**Depends on:** A3
**Verify:** render in default/daylight/matrix with `FoundationStyleTests.WithFoundation` (PNG via `PB_RENDER_OUT`) at 1366×768;
`dotnet test Paperbunkr.App.Tests --filter "FullyQualifiedName~XamlConsistencyLintTests|FullyQualifiedName~ThemeHardcodedColorLintTests|FullyQualifiedName~SharedControlsStyleTests"`.

### A6. Preferences › Reader row
**Files:** `Paperbunkr.App/ViewModels/PreferencesScreenViewModel.Comfort.cs` (edit), `Views/Preferences/ReaderSection.axaml` (edit),
`Models/PreferenceIndex.cs` (edit)
**What:** `RecapGapDays` property loaded with the comfort settings (:76 pattern) and saved with `PersistComfort` (:86 pattern). A
`pref:SettingsRow` "Recap after a break" with a `SuggestBox` (Off / 7 / 14 / 21 / 30 / 60 days), in a `Border` whose `Tag` is
`reader.recap`. A `PreferenceIndexEntry` (keywords: recap, previously, welcome back, summary, break).
**Depends on:** A1
**Verify:** `dotnet test Paperbunkr.App.Tests --filter "FullyQualifiedName~PreferenceIndexTests"` (anchor resolves); plus the lint set.

### A7. Slice A close-out
**Files:** spec §6 (edit)
**What:** run `avalonia-pro-max/review-checklist` on A5/A6; fill the spec's Implementation notes (corrections 1-4 above where relevant);
build into `bin`; hand to the user for the on-screen check (open a series not read for 3+ weeks; resume a half-read issue; open from a
reading list).
**Depends on:** A1-A6
**Verify:** on screen by the user. Not verified until then.

---

## Slice B — "I've got 30 minutes"

### B1. Queue anchor in the reading-order resolver
**Files:** `Paperbunkr.App/Services/Reader/ReadingOrderResolver.cs` (edit), `Services/Reader/NextIssueStager.cs` (edit),
`ViewModels/ReaderScreenViewModel.cs` + `ReaderScreenViewModel.Info.cs` (edit), `Paperbunkr.App.Tests/ReadingOrderResolverTests.cs` (extend)
**What:** add `ReadingContextKind.Queue`. Add optional `IReadOnlyList<int>? queue = null` to `ResolveNeighbour` (:41), `ResolveContext`
(:126) and `EnsureStaged` (`NextIssueStager.cs:141`); when set it wins over list/event and walks the ids by index (skipping missing files,
stopping at the end with no series fallback), label "Up next queue". In the reader add `_activeQueue`, a `queue` parameter on
`LoadIssue`/`Load` (cleared by a plain open, passed back through at every existing `_activeReadingListId` pass-through: :2848, :2873,
:2966, :3341/:3350, :3398, Info :62).
**Depends on:** none
**Verify:** resolver tests: forward/back through a queue, end of queue, missing file skipped, queue beats list. Reader test: a plain open
clears the queue. `--filter "FullyQualifiedName~ReadingOrderResolverTests|FullyQualifiedName~ReaderScreenViewModelTests"` (the second is
large; run it alone).

### B2. `TimeBudgetPlanner`
**Files:** `Paperbunkr.Data/Metadata/TimeBudgetPlanner.cs` (new), `Paperbunkr.Data.Tests/TimeBudgetPlannerTests.cs` (new)
**What:** `Plan(PaperbunkrDbContext ctx, int minutes, DateTime nowUtc)` → `TimeBudgetPlan(IReadOnlyList<TimeBudgetPick> Picks, double
TotalMinutes, bool IsRoughEstimate, TimeBudgetPick? ShortestAlternative)`; `TimeBudgetPick(int IssueId, string Label, string Reason,
double Minutes, int? PagesLeft)`.
- Candidates: in-progress comic/manga issues (same source as `HomeFeedResolver.GetContinueReadingMixed` :308, issues only), then
  `UpNextResolver.Build(ctx, nowUtc, limit: 20)` (:42) expanded to the next unread issues of each series by `NumberSortKey`, max 3 in a row.
- Time: `ReadingPaceResolver` (`TimeLeftInIssue` / `TimeFor`); when it has no pace, constants `DefaultComicSecondsPerPage = 20`,
  `DefaultMangaSecondsPerPage = 12` and `IsRoughEstimate = true`.
- Fill with the 110 % rule, skipping items that overflow and continuing; empty → `ShortestAlternative`. Books never included.
**Depends on:** none
**Verify:** new tests for every rule in spec §4.5. `dotnet test Paperbunkr.Data.Tests --filter "FullyQualifiedName~TimeBudgetPlannerTests"`.

### B3. Duration parser
**Files:** `Paperbunkr.App/Services/TimeBudgetQuery.cs` (new), `Paperbunkr.App.Tests/TimeBudgetQueryTests.cs` (new)
**What:** `static bool TryParseMinutes(string text, out int minutes)` for `30`, `30 min`, `30m`, `45 minutes`, `1h`, `1h30`, `time 45`;
range 5-240; anything else false.
**Depends on:** none
**Verify:** table test. `--filter "FullyQualifiedName~TimeBudgetQueryTests"`.

### B4. Dialog view model and view
**Files:** `Paperbunkr.App/ViewModels/TimeBudgetDialogViewModel.cs` (new), `Views/TimeBudgetDialogView.axaml` + `.axaml.cs` (new, **both
in the same step** — CLAUDE.md AVLN2000 gotcha), registration wherever the existing overlay dialogs are mapped (follow
`BookMatchDialogView` as the precedent), `Paperbunkr.App.Tests/TimeBudgetDialogViewModelTests.cs` (new)
**What:** `controls:DialogCard` Size Medium, title "Your next N minutes", subtitle per spec §4.3 (rough-estimate wording when flagged),
pick rows (cover, label, reason, "~14 min"), total row, `FooterStart` "Save as reading list" (`Button.secondary`), `Footer` Cancel (`ghost`)
+ "Start reading" (`primary`); nothing-fits text with "Read it anyway". Start → `LoadIssue(firstId, queue: pickIds)`. Save → a normal
reading list "Next N minutes · <date>" with the picks in order (use the existing reading-list create path).
**Depends on:** B1, B2
**Verify:** VM tests (Save creates the list in order; Start passes the queue; nothing-fits state); render in three themes; lint set.

### B5. Home Up Next switch
**Files:** `Views/Home/UpNextSection.axaml` (edit header), `ViewModels/Home/HomeSections.cs` / `HomeScreenViewModel.cs` (edit: a
`OpenTimeBudgetCommand(int minutes)`), `Paperbunkr.App.Tests/HomeScreenViewModelTests.cs` (extend), `RealWindowKeyboardTests.cs` (extend)
**What:** turn the header `StackPanel` into a `*,Auto` Grid; right column `Border.segGroup` with a muted "I've got" caption and three
`Button.segItem` (15 min / 30 min / 60 min), each running the command; no persisted selection. The section is already omitted when empty.
**Depends on:** B4
**Verify:** VM test (command opens the dialog with the right minutes); keyboard test in the real window (arrow to the switch, Enter opens
the dialog, Esc closes it, focus returns); lint set.

### B6. Quick Open entry
**Files:** `ViewModels/QuickOpenViewModel.cs` (edit `Rerank` :131), `ViewModels/MainViewModel.cs` (edit `ActivateQuickOpenEntry`
:1851-1930), `Paperbunkr.App.Tests/QuickOpenViewModelTests.cs` (extend)
**What:** when `TimeBudgetQuery.TryParseMinutes(Query)` succeeds, insert `QuickOpenEntry(QuickOpenKind.Action, EntityId: minutes,
"I've got N minutes", "Pick what to read", "Timer", null, Key: "timeBudget")` at the top of `Results`. `ActivateQuickOpenEntry` gets
`case "timeBudget"` → open the dialog with `EntityId`.
**Depends on:** B3, B4
**Verify:** VM tests (`30 min` shows the entry first; `hello` does not; activation opens the dialog). `--filter "FullyQualifiedName~QuickOpenViewModelTests"`.

### B7. Slice B close-out
**What:** review checklist on B4/B5; spec §6 notes (correction 1); build into `bin`; user's on-screen check (Home switch, Ctrl+P `30 min`,
Start reading walks the picks, Save as reading list).
**Depends on:** B1-B6
**Verify:** on screen by the user.

---

## Slice C — Natural-language search

### C1. Word split and quoted phrases
**Files:** `Paperbunkr.App/Services/LibrarySearch/SearchTerms.cs` (new), `LibrarySearchIndex.cs` (edit :156-187),
`LibraryViewPipeline.cs` (edit `Compute` :85), `Paperbunkr.App.Tests/LibrarySearchIndexTests.cs` + `LibraryViewPipelineTests.cs` (extend)
**What:** `SearchTerms.Split(string)` → normalized words (split on whitespace and `,;`) and quoted phrases. In **All** mode the match
becomes "every term is contained in the item's index string" (terms may match different fields — the deliberate CE deviation). Other
modes keep the current single-substring match. The per-keystroke cost stays one normalize + N ordinal `Contains`.
**Depends on:** none
**Verify:** tests: `hickman avengers` matches across fields; `"x-men red"` exact phrase; Writer mode unchanged. Existing perf gate in the
search perf tests still passes. `--filter "FullyQualifiedName~LibrarySearchIndexTests|FullyQualifiedName~LibraryViewPipelineTests"`.

### C2. `NaturalQueryParser`
**Files:** `Paperbunkr.App/Services/LibrarySearch/NaturalQueryParser.cs` (new), `NaturalQueryVocabulary.cs` (new),
`Paperbunkr.App.Tests/NaturalQueryParserTests.cs` (new)
**What:** `Parse(string text, NaturalQueryVocabulary vocab)` → `ParsedQuery(IReadOnlyList<ParsedPhrase> Phrases, IReadOnlyList<string>
FreeWords)`; `ParsedPhrase(string Kind, string Label, string Value, SmartListTemplateRule Rule, int Start, int Length)`. Grammar per spec
§5.2 table: read state, content type, publisher/imprint, creator ("by"/"written by"/"drawn by"), genre/tag, character ("with"/
"featuring"), year forms, rating, added, format; longest match first; creator surname → full name when unique; ambiguity → free text;
connector words only inside a phrase. Read-state rules use the existing fields (Unread = `ReadPercentage Is 0`, Reading = `InRange`
just above 0 and below 95, Read = `ReadPercentage GreaterThan`/≥ 95 — check the exact operator semantics in `SmartListCatalog` and
match `IsUnread`/`IsInProgress`/`HasBeenRead`). `NaturalQueryVocabulary` is built from the Library's suggestion index
(`BuildSuggestionIndex` :1341-1389) — publishers, imprints, creators, genres, tags, characters, formats, series names — once per data load.
**Depends on:** none
**Verify:** table test covering every phrase kind, longest match, creator resolution (unique vs several), ambiguity fallbacks, connector
words, mixed queries; a perf test (p95 < 1 ms over a 10k-name vocabulary). `--filter "FullyQualifiedName~NaturalQueryParserTests"`.

### C3. CE `MATCH` / `NOT` queries
**Files:** `Paperbunkr.App/Services/LibrarySearch/CeQueryTranslator.cs` (new), `Paperbunkr.App.Tests/CeQueryTranslatorTests.cs` (new)
**What:** when the trimmed text starts with `MATCH` or `NOT` (case-insensitive, All mode), tokenize with `ComicSmartListItem.TokenizeQuery`
and build matchers with `ComicBookGroupMatcher.CreateMatcherFromQuery` (`Paperbunkr.Engine`), then map them to a `SmartListConditionGroup`
with `CeMatcherMap` (the `.cbl` importer's mapping; follow `CeSmartListReader` for how the importer drives it). Return the group or a
failure; failure → caller searches plain words and shows the note.
**Depends on:** none
**Verify:** tests on CE example queries (from `CeSmartListCorpusTests` fixtures where possible) and a malformed one.
`--filter "FullyQualifiedName~CeQueryTranslatorTests|FullyQualifiedName~CeSmartListReaderTests"`.

### C4. Evaluate in the Library pipeline
**Files:** `LibraryViewPipeline.cs` (edit inputs :11-22 and `Compute`), `LibraryScreenViewModel.cs` (edit: build the vocabulary and a
`SmartListQueryBuilder.LibrarySnapshot` from the loaded issues once per data load; parse on the UI thread, pass the result in the inputs),
`Paperbunkr.App.Tests/LibraryViewPipelineTests.cs` (extend)
**What:** add `ParsedSearch? Parsed` to `LibraryViewInputs` (rules as a transient `SmartList` root group + free words, or a CE group). In
`Compute`, when set: issue ids matching `SmartListQueryBuilder.Evaluate(snapshot, list)` (computed once per input set, not per item) AND
free terms (C1) AND the lens/chips as today; series cards match when any issue matches. The snapshot wraps the already-loaded issues (survey
correction 3). Cancellation and the generation guard stay as they are.
**Depends on:** C1, C2, C3
**Verify:** pipeline tests: parsed rules + free words + lens combine; series card matching; empty parse = today's behaviour. Then the
existing search perf tests.

### C5. "Understood as" row
**Files:** `Views/LibraryToolbar.axaml` (edit, a new row between the search line ~:170 and the chip row :312), `LibraryScreenViewModel.cs`
(edit: `ParsedChips`, `HasParsedChips`, `ParseNote`, `RevertChipCommand`, `SearchAsPlainText` toggle), `Paperbunkr.App.Tests/LibraryNaturalSearchTests.cs` (new)
**What:** a `WrapPanel` row shown only with chips or a note: caption "Understood as" (`pbTextCaption`, muted); each chip a
`Button.filterChip` with `.on`, a muted kind label + value; leftover words as muted text; right side `Button.ghost sm` "Save as Smart List"
and "Search as plain text" (toggle). Clicking a chip marks its span literal (kept while the word is in the box, dropped when the box is
cleared) and re-runs the search. Row content uses `controls:Replace.On` on result change; no per-keystroke entrance.
**Depends on:** C4
**Verify:** VM tests (chips appear for a mixed query; revert keeps the word as text; plain-text toggle hides chips and searches literally;
Writer mode shows no row). Render in three themes at 1366×768 wrapped and unwrapped; lint set; keyboard test that Tab reaches the chips and
Enter reverts one.

### C6. Save as Smart List
**Files:** `LibraryScreenViewModel.cs` (edit: `SaveSearchAsSmartListCommand`), `Paperbunkr.App.Tests/LibraryNaturalSearchTests.cs` (extend)
**What:** build a `SmartList` (Issue target) from the phrases' rules plus one AllProperties "contains all of" rule for the free words (or
the CE group), name = query text; save following `SmartScreenViewModel.CreateFromGallery` (:999-1013: `SortOrder`, `Add`, `SaveChanges`);
wrap with `CollectionService.SetIssueSmartList` (:265) so it shows in the Library sidebar; select it.
**Depends on:** C4, C5
**Verify:** test: the saved list's `SmartListQueryBuilder.MatchCount` equals the live result count for the same query; it appears in the
sidebar collection list.

### C7. Slice C close-out
**What:** review checklist on C5; spec §6 notes (corrections 2 and 3); build into `bin`; user's on-screen check (`unread marvel from 2023 by
hickman avengers`, a quoted phrase, a `MATCH` query, revert a chip, save and reopen the list).
**Depends on:** C1-C6
**Verify:** on screen by the user.
