# Insights pitch completion — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md*

One deliberate deviation from the spec's wiring: the new results are **not** added to the `StatsSnapshot` record. `StatsResolver.Build` already loads
every issue and event for each range switch, and the new analyses are partly range-independent. Each resolver is a separate static class;
`StatsScreenViewModel` holds its results as separate cached properties (Rhythm per range; DropOff/Affinity/Storage lazily, once per invalidation).
Same Data → ViewModel → XAML shape, no change to the snapshot record or its tests.

Test commands (run in Release because a running Paperbunkr.App locks `bin/Debug`; never kill the app):
`dotnet test src/Paperbunkr.Data.Tests -c Release --filter "FullyQualifiedName~<Class>"`, and for App: `dotnet test src/Paperbunkr.App.Tests -c Release --filter "FullyQualifiedName~<Class>"`.
Never run two dotnet build/test commands at once.

## Step 1: RhythmResolver
**Files:** `src/Paperbunkr.Data/Metadata/RhythmResolver.cs` (new), `src/Paperbunkr.Data.Tests/RhythmResolverTests.cs` (new)
**What:** `RhythmResolver.Build(context, range, nowUtc, TimeZoneInfo? zone)` → `RhythmData` per spec §1. Uses `StatsResolver.RangeStartUtc` (make it `internal`, already is).
Backfill rows (Opened+Finished same timestamp for the same item) are excluded.
**Depends on:** none. **Verify:** `RhythmResolverTests`.

## Step 2: MilestoneResolver
**Files:** `src/Paperbunkr.Data/Metadata/MilestoneResolver.cs` (new), `src/Paperbunkr.Data.Tests/MilestoneResolverTests.cs` (new)
**What:** spec §5: finished-N ladder, streak ladder, first complete series, first manga volume, first book, first goal. Excludes backfill.
**Depends on:** none. **Verify:** `MilestoneResolverTests`.

## Step 3: DropOffBreakdownResolver
**Files:** `src/Paperbunkr.Data/Metadata/DropOffBreakdownResolver.cs` (new), `src/Paperbunkr.Data.Tests/DropOffBreakdownResolverTests.cs` (new)
**What:** spec §2. **Depends on:** none. **Verify:** `DropOffBreakdownResolverTests`.

## Step 4: AffinityResolver
**Files:** `src/Paperbunkr.Data/Metadata/AffinityResolver.cs` (new), `src/Paperbunkr.Data.Tests/AffinityResolverTests.cs` (new)
**What:** spec §3, via `CreatorCredit`. **Depends on:** none. **Verify:** `AffinityResolverTests`.

## Step 5: StorageResolver
**Files:** `src/Paperbunkr.Data/Metadata/StorageResolver.cs` (new), `src/Paperbunkr.Data.Tests/StorageResolverTests.cs` (new)
**What:** spec §4. Uses `SmartListQueryBuilder.BuildDuplicateGroups` and `DuplicateKeeperRanker`. **Depends on:** none. **Verify:** `StorageResolverTests`.

## Step 6: Trends view-model + cards
**Files:** `ViewModels/StatsScreenViewModel.cs` (edit), `Views/Stats/RhythmHeatmap.cs` (new), `Views/InsightsScreen.axaml` (edit: Activity, Composition, Top Lists cards),
`ViewModels/InsightsScreenViewModel.cs` + `ViewModels/MainViewModel.cs` (edit: optional `openPreferences` callback for the Review-duplicates button).
**What:** properties `Rhythm`, `DropOff`, `Affinity`, `Storage` (+ `HasX`, chip selections, Open commands), loaded in `Refresh`/lazily on group select, off the UI thread via the existing pattern; each wrapped in try/catch.
**Depends on:** Steps 1, 3, 4, 5. **Verify:** `StatsScreenViewModelTests` additions; headless XAML load; headless render.

## Step 7: Milestones strip on History
**Files:** `ViewModels/HistoryTabViewModel.cs` (edit), `Views/Insights/InsightsHistoryView.axaml` (edit), `Models/MilestoneBadge.cs` (new)
**What:** strip above the toolbar; load with the existing background refresh; Hide toggle.
**Depends on:** Step 2. **Verify:** `HistoryTabViewModelTests` additions; headless render.

## Step 8: HideMilestones setting + migration
**Files:** `Paperbunkr.Data/Entities/AppSettings.cs` (edit), new migration `AddInsightsMilestonesToggle` (bool, default false, `Down` no-op), model snapshot.
**Depends on:** Step 7. **Verify:** migration test pattern used by neighbouring migrations; if the EF tool cannot run, the toggle stays session-only and this is reported.

## Step 9: Docs and verification
**Files:** `docs/paperbunkr-todo.md`, `docs/Paperbunkr-Roadmap.md` (status + corrections), memory entry.
**Verify:** scoped `Insights|Stats|Rhythm|Milestone|DropOff|Affinity|Storage|History` filters in both test projects; headless renders of Trends and History in dark/light. On-screen check stays with the user.
