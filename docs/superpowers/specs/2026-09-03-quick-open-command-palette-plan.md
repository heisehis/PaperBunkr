# Quick Open — Command Palette — Implementation Plan

*Implements: docs/superpowers/specs/2026-09-03-quick-open-command-palette-design.md*

Surveyed the real surface area. Facts the steps rely on:

- **`MainWindow.axaml.cs` `OnMainWindowKeyDown`** is a `Tunnel` `KeyDown` handler. Order today:
  `Escape` → `Key.BrowserBack` → **`if (e.Source is TextBox) return;`** → `Ctrl+,` / `Ctrl+Tab` /
  `Ctrl+Shift+Tab`. `Ctrl+P` must go **before** the TextBox early-return (like `Escape`) so it
  fires with the Library/Books search box focused.
- **`MainViewModel`** hosts every overlay as `[ObservableProperty] bool _isXOverlayOpen` + open/
  close `[RelayCommand]`s + a `<Border IsVisible="{Binding IsXOverlayOpen}" Background="#B0000000">`
  block in `MainWindow.axaml` (workspace-name overlay at line 906 is the newest example).
  `Escape()` is a long `if/else if` cascade over those flags. There is **no** shared
  "is any overlay open" property — this plan adds a small `IsEditorOverlayOpen`.
- **`MainViewModel.OpenDeepLink(NavigationCliTarget)`** (line ~1562) is the existing precedent for
  a kind→navigation dispatch: `switch (target.Kind) { "series" → GoDetailForSeries(id); "issue" →
  GoReaderForIssue(id); "book" → GoBookDetailForBook(id); "collection" → GoLibraryWithCollection(id); }`.
  `ActivateQuickOpenEntry` mirrors it and extends it (lists, smart lists, events, continuities,
  screens, actions).
- Navigation entry points, all `MainViewModel` private methods usable as `Action<int>`:
  `GoDetailForSeries`, `GoReaderForIssue`, `GoBookDetailForBook` (id only — **not**
  `GoBookReaderForBook`, which needs `BookFormat`), `GoLibraryWithCollection`, `GoReadingWithList`,
  plus the `Go{Home,Library,Books,Smart,Reading,Events,Preferences}Command`s.
  Smart-list select: `Smart.LoadSmartList(int)`. Event select: `Events.LoadEvent(int)`. Continuity
  select: `Events.LoadContinuity(int)` (in `EventsScreenViewModel.Continuities.cs`).
- **Scan / Backup / Check-for-updates live on `PreferencesScreenViewModel`**, not `MainViewModel`
  (the design assumed otherwise). v1 actions ship only the ones that map to a `MainViewModel`
  command; the rest are deferred (noted in Step 5).
- Entity fields for the projections (verified): `Series.{Id,Name}`; `SeriesTitle.{SeriesId,Value}`;
  `Issue.{Id,SeriesId,Number,Title,OpenedTime}` (`EffectiveNumber()`/`EffectiveTitle()` are C#
  methods — **not** EF-translatable, combine in memory after the projection); `Book.{Id,Title,
  Author,LastOpenedTime}`; `ReadingList.{Id,Name}`; `SmartList.{Id,Name}`; `Collection.{Id,Name}`;
  `StoryEvent.{Id,Name}`; `Continuity.{Id,Name}`. DbSets: `Series`, `Issues`, `Books`,
  `ReadingLists`, `SmartLists`, `Collections`, `StoryEvents`, `Continuities`, `SeriesTitles`.
- **Search-box arrow-key pattern** to mirror: `LibraryToolbar.axaml.cs` `OnSearchBoxKeyDown` relays
  `Key.Down`/`Up`/`Enter`/`Escape` straight into VM methods, `e.Handled = true`.
- `Border.floatingPanel` is app-global (`Styles/Primitives.axaml`); `Border.dropdown` is **local**
  to the two toolbars — the overlay will use `floatingPanel` + its own local row styles.
- New-view gotcha (CLAUDE.md): any new `.axaml` ships with its `.axaml.cs` in the **same step**.
- Deviation from the design's record shape: `QuickOpenEntry` gets a nullable `string? Key`
  (screen key like `"library"`, action key like `"addFolder"`) so Screen/Action rows route without
  string-matching the display label. Content rows keep using `EntityId`.

---

## Step 1: `QuickOpenEntry`, `QuickOpenKind`, `QuickOpenService`

**Files:**
- `Paperbunkr.App/Models/QuickOpenEntry.cs` (new) — the `record` (design §"The index", plus the
  `string? Key` field above) and `enum QuickOpenKind { Series, Book, Issue, ReadingList, SmartList,
  Collection, StoryEvent, Continuity, Screen, Action }`.
- `Paperbunkr.App/Services/QuickOpenService.cs` (new) — default ctor → `PaperbunkrDb.CreateContext`,
  `internal` test-seam ctor taking `Func<PaperbunkrDbContext>` (same shape as `WorkspaceService`).
  `IReadOnlyList<QuickOpenEntry> BuildIndex()`:
  - one `AsNoTracking` projected query per entity type into an anonymous DTO, `ToList()`, then
    map to `QuickOpenEntry` in memory (so `Issue` "Series #12 – Title" strings and the
    `Number ?? Title` fallback are plain C#);
  - Series also left-joins `SeriesTitles` (group by `SeriesId`) so an alt title becomes a second
    match string appended to `Primary` — or, simpler for v1, a `Secondary` "aka <altname>"; pick
    whichever keeps the matcher simple (matcher only scores `Primary`, so append alt titles into
    `Primary` as `"Name  altname"` — one string, still one row);
  - Screen rows: the 7 static entries with `Kind.Screen`, `Key` = the `CurrentScreen` string;
  - Action rows: a static `QuickOpenActions` list (`Label`, `Icon`, `Key`) — v1: `Add folder…`
    (`addFolder`), `Add issue to library…` (`addIssue`), `New reading list…` (`newReadingList`),
    `Import from ComicRack…` (`importCe`). (`Scan` / `Backup` / `Check updates` deferred — they're
    `PreferencesScreenViewModel` commands, not reachable from here without lifting them.)
  - icons: `fi:SymbolIcon` names — `BookOpen` (series), `Book` (book), `Document` (issue),
    `AppsList` (reading list), `Flash` (smart list), `Folder` (collection), `CalendarStar`
    (event), `Timeline` (continuity), `Navigation` (screen), `Wrench` (action). Confirm names
    against FluentIcons at impl time.

**Depends on:** none
**Verify:** `Paperbunkr.App.Tests/QuickOpenServiceTests.cs` (new, `DatabasePathOverride` temp DB) —
one entry per series/book/list/collection/event/continuity; N issue entries with `Secondary` =
series name and `RecencyUtc` = `OpenedTime`; book `RecencyUtc` = `LastOpenedTime`; the 7 Screen +
4 Action rows always present; a series with a `SeriesTitle` row is matchable by that alt name.

---

## Step 2: `QuickOpenMatcher`

**Files:**
- `Paperbunkr.App/Services/QuickOpenMatcher.cs` (new) — pure static.
  - `int? Score(string query, string target)` — case-insensitive subsequence; `null` = no match;
    higher = better. Rewards contiguous runs, word-boundary starts (after space / `#` / `-` / `:`),
    index-0 start, shorter target.
  - `IReadOnlyList<QuickOpenEntry> Rank(string query, IReadOnlyList<QuickOpenEntry> index)` —
    empty query → top 8 by `RecencyUtc` desc (Issues + Books only) then the 7 Screen rows;
    non-empty → `Score` each `Primary`, drop misses, order by `(score, recencyBoost, kindPriority)`
    where `recencyBoost` = opened in last 7 days, `kindPriority` = Series≈Book > Issue >
    List/Collection/Event/Continuity > Screen > Action. `Take(50)`.

**Depends on:** Step 1
**Verify:** `Paperbunkr.App.Tests/QuickOpenMatcherTests.cs` (new, no DB) — subsequence hit/miss;
prefix and word-boundary beat mid-word; shorter target wins on equal subsequence; recency boost
breaks a score tie; kind-priority breaks a score+recency tie; empty query → recency list + screens;
`Take(50)` cap.

---

## Step 3: `QuickOpenViewModel`

**Files:**
- `Paperbunkr.App/ViewModels/QuickOpenViewModel.cs` (new)
  - ctor `(Action<QuickOpenEntry> activate, Action close, QuickOpenService? service = null)`.
  - `[ObservableProperty] string _query`; `ObservableCollection<QuickOpenEntry> Results`;
    `[ObservableProperty] int _selectedIndex`; `SelectedEntry` computed.
  - `Open()` — `_index = _service.BuildIndex()`; `Query = ""`; re-rank (→ recency list);
    `SelectedIndex = 0`.
  - `OnQueryChanged` — re-rank via `QuickOpenMatcher.Rank`, reset `SelectedIndex` to 0. Debounce
    one `DispatcherTimer` tick (design §"Matching and ranking"); the re-rank itself is synchronous
    in-memory.
  - `MoveSelection(int delta)` — clamp `SelectedIndex` in `[0, Results.Count-1]`.
  - `ActivateSelected()` — if `SelectedEntry is { } e` then `_activate(e); _close();`.
  - `HasNoMatches => Query.Length > 0 && Results.Count == 0` (drives the "No matches" row).

**Depends on:** Steps 1, 2
**Verify:** `Paperbunkr.App.Tests/QuickOpenViewModelTests.cs` (new) — `Open()` populates the
recency list and clears a prior query; typing filters `Results`; `MoveSelection` clamps;
`ActivateSelected` invokes `_activate` with the selected entry then `_close`; `HasNoMatches` only
when a non-empty query matches nothing.

---

## Step 4: `QuickOpenOverlay.axaml` (+ `.axaml.cs`)

**Files:**
- `Paperbunkr.App/Views/QuickOpenOverlay.axaml` **+ `QuickOpenOverlay.axaml.cs`** (new, same step)
  - `Border Classes="floatingPanel" Width="560" MaxHeight="520"`, `Grid RowDefinitions="Auto,*,Auto"`:
    search `TextBox` (`x:Name="SearchBox"`, `AutomationProperties.AutomationId="QuickOpenSearchBox"`,
    `Text="{Binding Query}"`, leading `fi:SymbolIcon Symbol="Search"`); a `ListBox`
    `ItemsSource="{Binding Results}" SelectedIndex="{Binding SelectedIndex}"` with a row
    `DataTemplate` (icon + bold `Primary`, dim `Secondary` under it, right-aligned dim `Kind`);
    a dim footer `↑↓ navigate   ↵ open   esc close`; a single "No matches" row bound to
    `HasNoMatches`.
  - code-behind: `OnSearchBoxKeyDown` relays `Key.Down`→`MoveSelection(1)`, `Up`→`MoveSelection(-1)`,
    `Enter`→`ActivateSelected()`, `Escape`→close command; `e.Handled = true` for each. Autofocus:
    override `OnPropertyChanged`, when `IsEffectivelyVisibleProperty` flips true
    `Dispatcher.UIThread.Post(() => SearchBox.Focus())`. Double-click / row-tap on the ListBox also
    activates.
  - local styles for the row (mirroring `modeOption` sizing); no new global style.

**Depends on:** Step 3
**Verify:** `dotnet build` clean (heed the XAML-weave gotcha — if `CompileAvaloniaXaml` fails after
`CoreCompile`, delete `obj/Debug/net10.0/Paperbunkr.App.dll` + `.pdb` and rebuild). Visual pass in
Step 7.

---

## Step 5: `MainViewModel` wiring

**Files:**
- `Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
  - ctor: `QuickOpen = new QuickOpenViewModel(ActivateQuickOpenEntry, CloseQuickOpenOverlay);`
  - `public QuickOpenViewModel QuickOpen { get; }`
  - `[ObservableProperty] bool _isQuickOpenOverlayOpen;`
  - `bool IsEditorOverlayOpen =>` OR of the editor/dialog flags (`IsIssuePropertiesOverlayOpen`,
    `IsBulk*`, `IsBookProperties*`, `IsReadingListPropertiesOverlayOpen`,
    `IsCollectionPropertiesOverlayOpen`, `IsWorkspaceNameOverlayOpen`, `IsNewReadingListDialogOpen`,
    `IsNewEventDialogOpen`, `IsMigrationOverlayOpen`, `IsQuickRateOverlayOpen`,
    `IsWelcomeOverlayOpen`, `IsWelcomeTourOverlayOpen`).
  - `[RelayCommand] private void OpenQuickOpen() { if (IsEditorOverlayOpen || IsQuickOpenOverlayOpen) return; QuickOpen.Open(); IsQuickOpenOverlayOpen = true; }`
  - `[RelayCommand] private void CloseQuickOpenOverlay() => IsQuickOpenOverlayOpen = false;`
  - `private void ActivateQuickOpenEntry(QuickOpenEntry e)` — `switch (e.Kind)`:
    `Series → GoDetailForSeries(e.EntityId!.Value)`; `Issue → GoReaderForIssue(...)`;
    `Book → GoBookDetailForBook(...)`; `Collection → GoLibraryWithCollection(...)`;
    `ReadingList → GoReadingWithList(...)`; `SmartList → { GoSmartCommand.Execute(null);
    Smart.LoadSmartList(id); }`; `StoryEvent → { GoEventsCommand.Execute(null); Events.LoadEvent(id); }`;
    `Continuity → { GoEventsCommand.Execute(null); Events.LoadContinuity(id); }`;
    `Screen → Go<Key>Command.Execute(null)` (small key→command map);
    `Action → switch (e.Key) { "addFolder" → GoLibraryFoldersPreferences(); "addIssue" →
    { GoLibraryCommand.Execute(null); Library.OpenAddIssueCommand.Execute(null); }; "newReadingList"
    → OpenNewReadingListDialog(); "importCe" → OpenMigrationOverlay(); }`.
  - `Escape()` cascade: add `if (IsQuickOpenOverlayOpen) { CloseQuickOpenOverlay(); }` near the top.

**Depends on:** Steps 3, 4
**Verify:** `Paperbunkr.App.Tests` — extend `QuickOpenViewModelTests` isn't enough for the
dispatch; a `MainViewModelQuickOpenTests` (new, or fold into an existing MainViewModel test file
if one exists) asserting `ActivateQuickOpenEntry` for each `Kind` sets the expected `CurrentScreen`
/ calls the expected child-VM method (using the real `MainViewModel` against a temp DB, same as
other MainViewModel-level tests). `OpenQuickOpen` no-ops while `IsEditorOverlayOpen`.

---

## Step 6: `MainWindow` — overlay slot + `Ctrl+P`

**Files:**
- `Paperbunkr.App/Views/MainWindow.axaml` (edit) — one more
  `<Border IsVisible="{Binding IsQuickOpenOverlayOpen}" Background="#B0000000">` hosting
  `<views:QuickOpenOverlay DataContext="{Binding QuickOpen}" />` + the standard close button,
  alongside the existing overlay blocks (near line 906).
- `Paperbunkr.App/Views/MainWindow.axaml.cs` (edit) — in `OnMainWindowKeyDown`, **before** the
  `if (e.Source is TextBox) return;` line:
  ```csharp
  if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.Control)
  {
      if (viewModel.CurrentScreen is not ("reader" or "bookReader" or "pdfReader"))
      {
          viewModel.OpenQuickOpenCommand.Execute(null);
          e.Handled = true;
      }
      return;
  }
  ```

**Depends on:** Step 5
**Verify:** app launches (run the exe — "0 Errors" isn't proof the weave ran for the new view).
`Paperbunkr.App.Tests` full run green.

---

## Step 7: Tests — UiTests driver + on-screen

**Files:**
- `Paperbunkr.App.UiTests/QuickOpenTests.cs` (new) + minimal driver helpers (inline or a small
  `QuickOpenDriver.cs`).

Cases: `Ctrl+P` on the Library screen opens an overlay with a focused `QuickOpenSearchBox`; typing
a seeded series name + `Enter` lands on the detail screen; `Ctrl+P` inside the reader does nothing.

**Depends on:** Steps 4, 6
**Verify:** `dotnet test Paperbunkr.App.UiTests` — FlaUI can't launch here (UIA timeout, same as
every other UI test in this repo); treat as written-not-run, the matcher/service/VM tests are the
gate.

---

## Step 8: Docs

**Files:**
- `docs/Paperbunkr-Roadmap.md` (edit) — flip the "Recent/MRU + Quick Open" open item to
  **shipped 2026-09-03** with commit ref; note the deviations already in the design (Ctrl+P
  palette, not a menu/cover-wall; Scan/Backup/Check-updates actions deferred).
- `docs/ce-feature-inventory.md` §C (edit) — `Recent/MRU file list, Quick Open` row: "design done"
  → "✅ shipped — `QuickOpenService` / `QuickOpenViewModel`, `Ctrl+P` palette".

**Depends on:** Steps 1–7 landed
**Verify:** n/a.

---

## Test strategy summary

- **Unit / ViewModel** (`Paperbunkr.App.Tests`, xUnit): Steps 1, 2, 3, 5 — the real gate.
  `QuickOpenMatcher` is pure; service + VM use the `DatabasePathOverride` / `Func<DbContext>`
  seams every neighbouring test already uses.
- **On-screen** (`Paperbunkr.App.UiTests`, FlaUI): Step 7 — written, unrunnable in this
  environment (documented condition, not a blocker).
- No new framework, fixture style, or live-network dependency.
- Full `dotnet test` green before done.
