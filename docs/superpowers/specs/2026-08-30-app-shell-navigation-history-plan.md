# App Shell Navigation History — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-30-app-shell-navigation-history-design.md*

**Correction from the design doc, found during planning:** the design's Input section said
Backspace would be a new `KeyboardCommandRegistry` entry. Surveying that file shows it's entirely
Reader-scoped (`ConflictContext` is defined purely in terms of `PageCanvas`'s paged/continuous
states, and every consumer is `PageCanvas.OnKeyDown`) — not a general shell shortcut system. The
existing shell-level precedent is `Escape`: a plain `<KeyBinding Gesture="Escape"
Command="{Binding EscapeCommand}" />` directly in `MainWindow.axaml` (line 25). Step 6 below uses
that same mechanism for Backspace. Same user-facing behavior as the design doc describes, corrected
plumbing.

Survey notes (exact current shapes, confirmed by reading the files):
- `DetailScreenViewModel`/`MangaDetailScreenViewModel`/`BookDetailScreenViewModel`/
  `ReaderScreenViewModel`/`BookReaderScreenViewModel`/`PdfPageReaderScreenViewModel` all take a
  `goBack`/`goBooks` `Action` as their first constructor parameter, currently wired in
  `MainViewModel`'s constructor to `GoLibrary`/`GoBooks`/`GoBackFromReader`/`GoBackFromBookReader`.
  All six get rewired to `NavigateBack` — this is the natural completion of the design's own
  Background section ("the other four drill-down screens... have no back concept at all beyond
  their own hardcoded `GoLibrary`/`GoBooks` callbacks"), not a new decision.
- The six drill-down `ContentControl`s (BookReader/PdfReader/Detail/BookDetail/MangaDetail/Reader)
  are siblings inside one `<Grid>` in `MainWindow.axaml` (~line 736-810), each `IsVisible`-toggled,
  layered with the lateral `TransitioningContentControl`. The breadcrumb bar is a new sibling
  element in that same `Grid`, added last (topmost z-order), `VerticalAlignment="Top"`.
- Latest migration: `20260830174643_AddAutoBackupSettings`. New one goes after it.
- `AppSettings` nullable string/int fields (`LibrarySearchQuery`, `LibraryActiveCollectionId`) need
  no special EF config (`HasDefaultValue`/`HasSentinel` is only for non-nullable enum columns) — the
  two new fields follow that same no-config pattern.

## Step 1: `NavigationHistoryService` + entities
**Files:**
- `src/Paperbunkr.App/Models/NavigationEntryKind.cs` (new) — `enum { Series, MangaSeries, Issue,
  Book, BookSeries }`.
- `src/Paperbunkr.App/Models/NavigationEntry.cs` (new) — `record(string ScreenKey,
  NavigationEntryKind Kind, int EntityId, string Label)`.
- `src/Paperbunkr.App/Services/NavigationHistoryService.cs` (new) — `List<NavigationEntry>` + cursor,
  exactly as specced: `ResetRoot`, `Push`, `Back`, `Forward`, `JumpTo`, `CanGoBack`, `CanGoForward`,
  `RootScreenKey`, `BreadcrumbTrail`.

**Depends on:** none
**Verify:** `src/Paperbunkr.App.Tests/NavigationHistoryServiceTests.cs` (new) — push/back/forward,
cursor-truncation-on-push-after-back, `JumpTo` truncation, `ResetRoot` clearing + setting root,
`BreadcrumbTrail` slicing, `CanGoBack`/`CanGoForward` at empty/single/mid/end-of-stack boundaries.

## Step 2: `AppSettings` + migration
**Files:**
- `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit) — add `public string? LastScreenKey { get;
  set; }` and `public int? LastScreenEntityId { get; set; }`, doc-commented same style as
  `LibraryActiveCollectionId`.
- New migration `AddLastScreenState` via `dotnet ef migrations add AddLastScreenState --project
  src/Paperbunkr.Data --startup-project src/Paperbunkr.Data`.

**Depends on:** none
**Verify:** `dotnet build` on `Paperbunkr.Data`; migration applies cleanly to a scratch DB.

## Step 3: CLI arg parsing
**Files:**
- `src/Paperbunkr.App/Services/NavigationCliArgs.cs` (new) — `record NavigationCliTarget(string
  Kind, int Id)`; `static bool TryParseOpenArg(string[] args, out NavigationCliTarget? target)`
  parsing `--open <kind>:<id>` for `kind` in `{series, issue, book, collection}`. Malformed/missing/
  unrecognized → `false`, `target = null`. Pure C#, no Avalonia dependency.

**Depends on:** none
**Verify:** `src/Paperbunkr.App.Tests/NavigationCliArgsTests.cs` (new) — valid parse per kind, no
`--open`, malformed id, unrecognized kind, extra/missing colon.

## Step 4: `MainViewModel` wiring
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:**
- New `private readonly NavigationHistoryService _history = new();` field.
- Split each drill-down entry point into a `...Core` method (sets `CurrentScreen`/loads data, no
  history side effect) + the existing method becomes a thin wrapper that calls `...Core` then
  `_history.Push(...)`:
  - `GoDetailForSeries(int)` → `NavigateToDetailCore(int)` (returns nothing; reuses existing
    `LoadDetailSeries` which already sets `CurrentScreen`... actually keep `CurrentScreen` assignment
    in the Core method per the design doc's exact snippet).
  - `GoReaderForIssue(int)` / `GoReaderForIssueInReadingList(int, int)` → `NavigateToReaderCore(int,
    int? readingListId)`.
  - `GoBookDetailForBook(int)` / `GoBookSeriesDetailForSeries(int)` → `NavigateToBookDetailCore`
    (two variants: by book id, by series id — mirror existing `LoadBook`/`LoadSeries` split).
  - `GoBookReaderForBook(int, BookFormat, BookPosition?)` → `NavigateToBookReaderCore` (branches
    pdfReader/bookReader exactly as today).
  - `GoNewIssuePropertiesForPlaceholder` keeps calling `GoDetailForSeries` (wrapper, not Core) — it's
    a real navigation, should push like any other route to Detail.
- Every lateral `GoX()` (`GoHome`, `GoLibrary`, `GoLibraryWithSearch`, `GoLibraryWithCollection`,
  `GoBooks`, `GoSmart`, `GoReading`, `GoReadingWithList`, `GoEvents`, `GoPreferences`,
  `GoLibraryFoldersPreferences`) gets one added line: `_history.ResetRoot("<screenKey>");` before
  setting `CurrentScreen`.
- New commands/properties:
  ```csharp
  [RelayCommand(CanExecute = nameof(CanNavigateBack))]
  private void NavigateBack() => TryLeaveCurrentEditor(() => {
      var entry = _history.Back();
      if (entry is null) { /* go to _history.RootScreenKey via a small switch, mirroring old GoBackFromReader's cases */ }
      else ReplayEntry(entry);
      RaiseHistoryChanged();
  });

  [RelayCommand(CanExecute = nameof(CanNavigateForward))]
  private void NavigateForward() => TryLeaveCurrentEditor(() => {
      var entry = _history.Forward();
      if (entry is not null) ReplayEntry(entry);
      RaiseHistoryChanged();
  });

  public bool CanNavigateBack => _history.CanGoBack;
  public bool CanNavigateForward => _history.CanGoForward;
  public bool ShowBreadcrumb => IsDetail || IsMangaDetail || IsBookDetail || IsReader || IsBookReader || IsPdfReader;
  public IReadOnlyList<NavigationEntry> BreadcrumbTrail => _history.BreadcrumbTrail;
  public string RootScreenLabel => RailScreenLabel(_history.RootScreenKey); // small static Dictionary<string,string> lookup, mirrors MainWindow.axaml's railLabel text

  [RelayCommand]
  private void NavigateToBreadcrumbIndex(int index) => TryLeaveCurrentEditor(() => {
      var entry = _history.JumpTo(index);
      if (entry is not null) ReplayEntry(entry);
      RaiseHistoryChanged();
  });
  ```
  `ReplayEntry` switches on `entry.ScreenKey` calling the matching `...Core` method.
  `RaiseHistoryChanged()` fires `OnPropertyChanged` for `CanNavigateBack`/`CanNavigateForward`/
  `BreadcrumbTrail`/`RootScreenLabel` — called from `RaiseHistoryChanged` itself and also added to
  the end of `OnCurrentScreenChanged` (covers `ShowBreadcrumb` recompute + persistence, see below).
- **Delete:** `_screenBeforeReader`, `RememberScreenBeforeReader()`, `GoBackFromReader()`,
  `_screenBeforeBookReader`, `GoBackFromBookReader()`. Their call sites (constructor wiring for
  `Detail`/`MangaDetail`/`BookDetail`/`Reader`/`BookReader`/`PdfReader`) all become `NavigateBack`.
- **Persistence:** in `OnCurrentScreenChanged`, after the existing `OnPropertyChanged` calls, save
  `AppSettings.LastScreenKey = value` and `LastScreenEntityId = <current entity id, if any>` (track
  via a small private field set alongside each `...Core` call, similar to the existing
  `_currentDetailSeriesId` pattern — extend that field's role or add one field per drill-down kind as
  needed) via `PaperbunkrDb.CreateContext()` + `SaveChanges()`, same as `OnNavRailPinnedChanged`.
- **Startup entry points for Step 5:** two new public methods —
  `public void OpenDeepLink(NavigationCliTarget target)` (maps `Kind` string to the right `...Core`
  call, calls `_history.ResetRoot(...)` first with the design doc's root-default rules) and
  `public void RestoreLastScreen()` (reads `AppSettings.LastScreenKey`/`LastScreenEntityId`, same
  dispatch, falls back to `GoHome()` + `DiagnosticsService.LogMilestone` if the entity is missing —
  wrap the entity lookup in a try/catch or existence check per screen kind).

**Depends on:** Step 1 (uses `NavigationHistoryService`), Step 2 (uses `AppSettings` fields)
**Verify:** extend `MainViewModelTests` — `NavigateBackCommand`/`NavigateForwardCommand`
`CanExecute` through a push/back/forward sequence; lateral `GoX()` resets the stack; unsaved-editor
guard applies to Back/Forward; `RestoreLastScreen()`'s deleted-entity fallback to Home against a
scratch DB; `OpenDeepLink` for each kind.

## Step 5: `App.axaml.cs` startup wiring
**Files:** `src/Paperbunkr.App/App.axaml.cs` (edit)
**What:** right after `var mainViewModel = new MainViewModel();` (~line 87), before the
`offerFirstRunMigration` check:
```csharp
if (NavigationCliArgs.TryParseOpenArg(desktop.Args ?? [], out var target) && target is not null)
{
    mainViewModel.OpenDeepLink(target);
}
else
{
    mainViewModel.RestoreLastScreen();
}
```
`desktop` is the existing `IClassicDesktopStyleApplicationLifetime` already in scope in this method.

**Depends on:** Step 3, Step 4
**Verify:** manual — launch with `--open series:<id>`, launch with no args after having navigated
somewhere and closed, confirm restore. No existing App.axaml.cs test harness in this codebase to
extend automatically.

## Step 6: Breadcrumb UI + Backspace + trackpad swipe
**Files:**
- `src/Paperbunkr.App/Views/Breadcrumb.axaml` + `.axaml.cs` (new) — `ItemsControl` bound to
  `RootScreenLabel` + `BreadcrumbTrail`, each segment a clickable button/text calling
  `NavigateToBreadcrumbIndexCommand` with its index (root = index `-1`, handled as "go to
  RootScreenKey" the same way `NavigateBack`'s null case is).
- `src/Paperbunkr.App/Views/MainWindow.axaml` (edit):
  - Add `<views:Breadcrumb IsVisible="{Binding ShowBreadcrumb}" .../>` as the last child of the
    drill-down `<Grid>` (~after line 810), `VerticalAlignment="Top"`.
  - Add `<KeyBinding Gesture="Backspace" Command="{Binding NavigateBackCommand}" />` next to the
    existing `Gesture="Escape"` binding (~line 25).
  - Trackpad swipe: a `PointerWheelChanged` handler added to the window/root content (code-behind,
    `MainWindow.axaml.cs`) — threshold check on a large single horizontal `Delta.X` distinct from
    small accumulated scroll deltas, calling `NavigateBackCommand`/`NavigateForwardCommand` by sign.
    Implemented as best-effort per the design doc's explicit caveat.
- Backspace guard: in `NavigateBack()`'s command body (Step 4), skip if
  `TopLevel.GetTopLevel(...)?.FocusManager?.GetFocusedElement() is TextBox` — defensive backstop
  regardless of whether Avalonia's own routing already stops it at a focused TextBox.

**Depends on:** Step 4
**Verify:** manual/on-screen only, per the design doc's own stated limitation (no unattended GUI
automation in this environment) — breadcrumb click-to-jump, Backspace outside vs. inside a text
field, trackpad swipe on real hardware.

## Step 7: Full-suite verification
**Verify:** `dotnet build` on the full solution; `Paperbunkr.Data.Tests`, `Paperbunkr.App.Tests`
green; app smoke-launched via `PowerShell Start-Process` (per this project's own documented gotcha —
a backgrounded bash job gets killed by shell teardown, not a real crash) to confirm no startup
crash and that navigating around + Backspace + a breadcrumb click behave as expected.

**Depends on:** all prior steps

## Roadmap
Once landed: update `docs/alpha-roadmap.md` Beta backlog and
`2026-08-24-navigation-shell-motion-system-design.md`'s scope note, per the design doc's own Roadmap
section.
