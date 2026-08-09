# Smart Lists — Results View — Design Spec

*Date: 2026-08-09. Scope: Smart Lists screen (`SmartScreenViewModel`/`SmartScreen.axaml`) only.*

## 1. Problem

`SmartListQueryBuilder.Build(ctx, list)` already evaluates a smart list's conditions and returns
the real matched `List<Issue>` — `MatchCount(ctx, list)` is literally `Build(ctx, list).Count`. But
`SmartScreenViewModel` only ever surfaces the count (`MatchCountLabel`); it never keeps the matched
issues themselves. `SmartScreen.axaml` has nothing to render them with, so the screen shows the
rule builder and a live "Currently matches N issues" pill, then nothing — for every list, built-in
or custom.

Checked the original `2026-08-06-smart-lists-design.md`: a results view was never scoped in or
explicitly deferred. This is a gap, not an intentional Alpha-scope cut.

## 2. Fix

**`SmartScreenViewModel`:**
- Add `public ObservableCollection<IssueCardSample> Results { get; }`.
- Replace the current `RecomputeMatchCount()` (which calls `SmartListQueryBuilder.MatchCount`) with
  a method that calls `SmartListQueryBuilder.Build(context, transient)` once, populates `Results`
  from the returned issues, and sets `MatchCountLabel = Results.Count.ToString()` — one query
  instead of two.
- Each `Issue` maps to an `IssueCardSample` the same way `DetailTabsViewModel.LoadSeries` already
  does: `CoverBrush = SeriesCardSample.CoverBrushFor(issue.Series.Name)` (per-issue, since Smart
  List results span many series, unlike Detail's single-series Issues tab),
  `CoverImage = CoverImageCache.Get(issue.Id)`, `Title` from `issue.Number` (`"#{n}"` /
  `"#?"` fallback, matching `DetailTabsViewModel`'s existing formatting exactly).
  `SmartListQueryBuilder.Build` already `.Include(i => i.Series)`s, so `issue.Series.Name` is safe
  to read without an extra query.
- Called from the same two places `RecomputeMatchCount()` is today: after loading a list
  (`LoadSmartList`) and after every condition edit (`SmartListConditionViewModel`'s change
  callback).
- New constructor param: `Action<int> goToSeries` (mirrors `DetailTabsViewModel`'s
  `Action<int> goToProperties` pattern), wired in `MainViewModel` to `GoDetailForSeries` — the same
  method Library cards already use.
- New `[RelayCommand] SelectResult(IssueCardSample? issue)` calling `goToSeries(seriesId)`. Since
  `IssueCardSample` doesn't carry a `SeriesId` today, add one (`public int SeriesId { get; init; }`)
  alongside the existing `Id`/`Title`/etc. — used here and left unused (harmlessly) by
  `DetailTabsViewModel`'s existing construction site.

**`SmartScreen.axaml`:**
- Below the existing condition-list `Border`, add an `ItemsControl` over `Results` in a `WrapPanel`,
  using the *exact* `Border.issueTile`/`Classes.selected` template `DetailTabs.axaml` already
  defines (cover image, title overlay) — copy the tile `DataTemplate` markup, not a new style class,
  so the two screens can't visually drift apart. No selection state here (`Classes.selected` simply
  never triggers, since Smart List results aren't selectable/editable) — just click-to-navigate via
  a `Button`-wrapped tile (simpler than Detail's pointer/keyboard dual handling, since there's no
  Shift-range-select concept on a read-only results view).
- Empty state: when `Results.Count == 0` (and the list has at least one condition), show a small
  muted "No issues match this list yet." message instead of an empty grid — mirrors the empty-state
  pattern already used elsewhere (e.g. Detail's Related/Activity tabs).

## 3. Explicitly not doing

- **Virtualization.** Some lists are large (2000+ matches observed). Shipping the same
  non-virtualized `ItemsControl`+`WrapPanel` pattern Library already uses (fine at 371 series
  cards) for consistency. Revisit only if it's actually laggy in practice — not building it
  preemptively.
- **Selection/bulk actions from the results grid.** Read-only, click-to-navigate-to-Detail only.
  Bulk editing already exists via Detail's own Issues tab.
- **Duplicate Candidates' actual duplicate-group UI.** That list still renders as a flat grid like
  every other smart list under this fix — a dedicated grouped-duplicate view is Duplicate Finder's
  job (separate, already-scoped-for-later cleanup), not this screen's.

## 4. Testing

- `SmartScreenViewModelTests` (new or extended, matching the existing test file's DB-override
  pattern): `LoadSmartList_PopulatesResults_MatchingBuildQuery`,
  `EditingCondition_UpdatesResultsAndCount_Live`, `Results_MapSeriesNamePerIssue_NotSharedAcrossSeries`
  (a list matching issues from 2+ different series must show each with its own cover brush, not
  one shared brush).
- Manual: open a built-in list (e.g. "Recently Added"), confirm real cover tiles render below the
  rule area; click one, confirm it lands on that issue's series in Detail; add/remove a condition
  and confirm the grid updates live alongside the count pill.
