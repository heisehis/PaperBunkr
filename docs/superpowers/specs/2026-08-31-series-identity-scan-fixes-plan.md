# Series identity scan fixes — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-31-series-identity-scan-fixes-design.md*

## Step 1: Extract `MoveIssueToSeries` from `SeriesReassignmentResolver`
**Files:** `src/Paperbunkr.Data/Metadata/SeriesReassignmentResolver.cs` (edit)
**What:** Pull the body of `Apply(context, proposal)` (target find-or-create by name, both-sides
EF navigation fixup, two-phase `SaveChanges`, prune-if-empty) into a new
`internal static void MoveIssueToSeries(PaperbunkrDbContext context, Issue issue, string
targetSeriesName)`. `Apply` becomes a thin wrapper: resolves `issue` from
`proposal.Issue ?? context.Issues.Find(proposal.IssueId)`, returns early on a null issue or
blank `proposal.ProposedValue` (unchanged early-out), then calls
`MoveIssueToSeries(context, issue, proposal.ProposedValue)`. Pure refactor — no behavior change.
**Depends on:** none
**Verify:** `SeriesReassignmentResolverTests` (existing 5 tests) pass unmodified — a failure here
means the extraction wasn't behavior-preserving.

## Step 2: `SeriesSplitDetector`
**Files:** `src/Paperbunkr.Data/Metadata/SeriesSplitDetector.cs` (new)
**What:** New static class alongside `SeriesReassignmentResolver`:
- `public sealed record SeriesSplitResult(string OriginalSeriesName, IReadOnlyList<(string
  NewSeriesName, int IssueCount)> SplitGroups);`
- `public static SeriesSplitResult? DetectAndSplit(PaperbunkrDbContext context, int seriesId)`:
  - Load the series (`context.Series.Include(s => s.Issues).FirstOrDefault(s => s.Id ==
    seriesId)`); return `null` if not found or it has fewer than 2 issues (can't split).
  - Group `series.Issues` by `Title` trimmed, `StringComparer.OrdinalIgnoreCase`, excluding
    null/whitespace titles and any title equal to `series.Name` (same comparer).
  - Keep only groups with `Count >= 2`. Return `null` if none qualify.
  - For each qualifying group, call `SeriesReassignmentResolver.MoveIssueToSeries(context, issue,
    groupTitle)` for every issue in the group. Read `series.ContentType`/`series.ReadingMode`
    *before* the loop (the source series may be deleted by the last move once it empties out) and,
    right after each group's first successful move creates the new series, assign
    `newSeries.ContentType`/`newSeries.ReadingMode` from those captured values — resolve the new
    series via `context.Series.First(s => s.Name == groupTitle)` immediately after the first
    move in that group (it's guaranteed to exist and be persisted by then).
  - Build and return the `SeriesSplitResult` from the original name + each group's
    `(groupTitle, issue count)`.
- `public static IReadOnlyList<SeriesSplitResult> DetectAndSplitAll(PaperbunkrDbContext context)`:
  snapshot `context.Series.Select(s => s.Id).ToList()` first (avoid mutating the set being
  enumerated, since splitting adds/removes rows), then call `DetectAndSplit` per id, collecting
  non-null results.
**Depends on:** Step 1 (`MoveIssueToSeries` must exist and be `internal`/visible within
`Paperbunkr.Data`).
**Verify:** new `SeriesSplitDetectorTests` (Step 3).

## Step 3: `SeriesSplitDetector` tests
**Files:** `src/Paperbunkr.Data.Tests/SeriesSplitDetectorTests.cs` (new)
**What:** Mirror `SeriesReassignmentResolverTests`' real-SQLite-file setup (temp `.db` path,
`DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite(...)`, `Dispose` clears pools + deletes
the file). Seed series/issues directly via `context.Series.Add`/`series.Issues.Add` +
`SaveChanges`, matching that file's `SeedIssueWithProposal`-style helper. Cases:
- `DetectAndSplit_TwoTitleGroups_SplitsBothIntoNewSeries` — series "Warhammer 40" with issues
  titled `["Damnation Crusade", "Damnation Crusade", "Dawn of War III", "Dawn of War III"]` →
  result has 2 split groups, 2 new `Series` rows exist with those names + 2 issues each, and the
  original "Warhammer 40" row is gone (pruned empty, same as
  `SeriesReassignmentResolver`'s existing prune behavior).
- `DetectAndSplit_AllIssuesShareOneTitle_NoSplit` — every issue titled identically (a normal
  ongoing series using the series name as-is, or all blank titles) → returns `null`, series/issue
  count unchanged.
- `DetectAndSplit_OneRepeatedTitlePairAmongSingles_SplitsOnlyThatPair` — 3 issues with distinct
  singleton titles + 2 issues sharing one title → only the shared-title pair moves out; the
  original series survives with its 3 remaining issues.
- `DetectAndSplit_TitleEqualsSeriesName_NotTreatedAsSplitGroup` — issues whose `Title` equals the
  series `Name` are excluded from grouping even if there are 2+ of them.
- `DetectAndSplit_CopiesContentTypeAndReadingMode_FromOriginalSeries` — original series has
  `ContentType.Manga`/`ReadingMode.RightToLeft`; a resulting new series has the same values, not
  the entity defaults.
- `DetectAndSplitAll_MultipleSeries_SplitsEachIndependently` — two unrelated series in the same
  context, each with its own qualifying group → both split correctly in one call.
**Depends on:** Step 2
**Verify:** `dotnet test src/Paperbunkr.Data.Tests` (or the solution-wide run in Step 8).

## Step 4: TPB folding in `LibraryFolderScanner`
**Files:** `src/Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit)
**What:**
- Add `private static bool IsTradePaperback(string? format)` — trims/lowercases `format`,
  compares against `{"tpb", "trade paperback", "trade"}` (mirrors `format-aliases.tsv`'s "Trade
  Paper Back" row; comment notes the mirror so the two don't drift silently).
- Add `private static string StripCollectionWording(string seriesName)`: truncate at the first
  `:`; then strip a trailing `Vol.`/`Volume` + number (regex, case-insensitive); then strip a
  trailing `(N)` or `#N`; trim whitespace/punctuation left over. Return the result (may equal the
  input unchanged if nothing matched).
- In `ImportFiles`, right after the existing `bool isNewSeries = !seriesByName.TryGetValue(seriesName,
  out var series);` miss (i.e. inside `if (isNewSeries)`, before creating a new `Series`), add: if
  `IsTradePaperback(embeddedInfo?.Format)`, compute `StripCollectionWording(seriesName)`; if it
  differs from `seriesName` and `seriesByName.TryGetValue(stripped, out var folded)` succeeds, use
  `folded` as `series` and set `isNewSeries = false` instead of creating a new row. Only create the
  new `Series` (using the original, un-stripped `seriesName`) when this fold attempt also misses —
  matching the design's "never name a new series from the stripped form" rule.
**Depends on:** none (independent of Steps 1-3)
**Verify:** `LibraryFolderScannerTests` (Step 5).

## Step 5: TPB folding tests
**Files:** `src/Paperbunkr.App.Tests/LibraryFolderScannerTests.cs` (edit — add tests near the
existing embedded-metadata tests, ~line 185-245)
**What:** Using `CbzFixture.Create` + `cYo.Projects.ComicRack.Engine.ComicInfo { Series = ...,
Format = ... }`, following this file's existing two-file-scan pattern:
- `ScanAllAsync_TpbWithExistingBaseSeries_FoldsIntoIt` — pre-seed (via a first scanned file, or
  directly via `context.Series.Add`) a "Batman" series, then scan a TPB file with embedded
  `Series = "Batman: Court of Owls"`, `Format = "TPB"` → only one `Series` row ("Batman") exists
  afterward, and the TPB's issue's `SeriesId` points at it.
- `ScanAllAsync_TpbWithNoMatchingBaseSeries_CreatesNewSeriesUnderRawName` — same TPB, no
  pre-existing "Batman" series → a new series named exactly `"Batman: Court of Owls"` (raw, not
  stripped) is created — unchanged fallback behavior.
- `ScanAllAsync_TpbVolumeWording_FoldsIntoExistingSeries` — embedded `Series = "Batman Vol. 1"`,
  `Format = "Trade Paperback"` (alias variant), existing "Batman" series present → folds.
- `ScanAllAsync_NonTpbFormat_DoesNotFold` — a regular issue with `Series = "Batman: Court of
  Owls"`, `Format = null` (or e.g. `"Annual"`), existing "Batman" series present → creates a
  *separate* series (folding is TPB-gated only) — locks in that this isn't a general fuzzy-match
  feature.
- `ScanAllAsync_TpbExactRawNameMatch_UsesItDirectly_NoStrippingAttempted` — embedded `Series =
  "Batman"` (already exact), `Format = "TPB"`, existing "Batman" series present → attaches
  directly, still exactly one series (confirms the exact-match path still short-circuits first).
**Depends on:** Step 4
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter LibraryFolderScannerTests`

## Step 6: Wire split-on-scan (trigger a)
**Files:** `src/Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit)
**What:** Change `seriesTouched` from `HashSet<string>` to `HashSet<Series>` (reference equality
is fine — same `Series` instances are reused via `seriesByName` within one scan). Update the one
existing `seriesTouched.Add(seriesName)` call (~line 262) to `seriesTouched.Add(series!)`. After
`context.SaveChanges()` (~line 272) and after the existing `autoAcceptedSeriesProposals` foreach
(~line 279-282) — split detection must run last, since it needs the post-reassignment state, not
mid-scan state — add:
```csharp
foreach (int seriesId in seriesTouched.Select(s => s.Id).Distinct())
{
    SeriesSplitDetector.DetectAndSplit(context, seriesId);
}
```
Update `return new LibraryFolderScanResult(issuesAdded, seriesTouched.Count);` — `seriesTouched.Count`
still means "distinct series touched," now counting entities instead of names; behaviorally
equivalent since names were already unique per `Series` at that point in the method. Add
`using Paperbunkr.Data.Metadata;` if not already present (it is — `SeriesReassignmentResolver` is
already used from that namespace in this file).
**Depends on:** Step 2
**Verify:** new test in Step 7 (integration-level, same scan touching + splitting in one run).

## Step 7: On-demand "Detect & Split Mismatched Series" command
**Files:**
- `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit)
- `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit)
- `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)

**What:** In `PreferencesScreenViewModel`, next to the existing `IsScanning`/`ScanNow` block
(~line 1129-1167), add:
```csharp
[ObservableProperty]
private bool _isSplittingSeries;

[RelayCommand]
private async Task SplitMismatchedSeries()
{
    if (IsSplittingSeries) return;

    IsSplittingSeries = true;
    try
    {
        var results = await Task.Run(() =>
        {
            using var context = _contextFactory();
            return SeriesSplitDetector.DetectAndSplitAll(context);
        });

        string summary = results.Count == 0
            ? "No mismatched series found."
            : $"Split {results.Count} series: " + string.Join(", ",
                results.Select(r => $"{r.OriginalSeriesName} → {r.SplitGroups.Count} series"));
        _showToast("Series split complete", summary);
    }
    finally
    {
        IsSplittingSeries = false;
    }
}
```
(Mirrors `ScanNow`'s simple busy-flag + single-final-toast shape, not `GenerateCovers`'/
`SyncMetadata`'s `ToastProgressViewModel` — this operation has no natural per-item progress to
report.) Add `using Paperbunkr.Data.Metadata;` if needed.

In `LibrarySection.axaml`, add a fourth button to the existing `StackPanel` at lines 57-86
(alongside Scan Now / Generate Covers / Sync Metadata):
```xml
<Button Classes="headerAction ghost" Command="{Binding SplitMismatchedSeriesCommand}"
        IsEnabled="{Binding !IsSplittingSeries}"
        ToolTip.Tip="Finds series whose issues have divergent titles (e.g. an anthology/imprint label covering several unrelated stories) and splits them into separate series automatically">
    <StackPanel Orientation="Horizontal" Spacing="6">
        <fi:SymbolIcon Symbol="ArrowSplit" />
        <TextBlock Text="Split Mismatched Series" />
    </StackPanel>
</Button>
```
(Check `FluentIcons.Avalonia`'s `Symbol` enum has `ArrowSplit`; fall back to another
already-used-in-this-file symbol, e.g. `ArrowClockwise`, if not — confirm via the
`avalonia-docs` MCP or a quick grep of other `fi:SymbolIcon Symbol=` usages in this codebase
before picking a name that doesn't exist.)

Add a `PreferencesScreenViewModelTests` test mirroring the existing `ScanNow`/`ScanBooksNow`
coverage: seed a splittable series directly in the test's isolated DB, invoke
`SplitMismatchedSeriesCommand`, assert `IsSplittingSeries` toggled and back, and that the toast
callback captured the expected summary text (same assertion style this file already uses for
`_showToast` calls elsewhere in the class, e.g. around lines 1116/1161).
**Depends on:** Step 2
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter PreferencesScreenViewModelTests`; then
the manual on-screen check in Step 9.

## Step 8: Full automated suite
**Files:** none (verification step)
**What:** `dotnet build` the solution, then run the full `Paperbunkr.App.Tests` and
`Paperbunkr.Data.Tests` suites (and `Paperbunkr.Plugins.Tests` if the solution-wide test command
includes it) to catch any cross-file regression the individual step runs above didn't already
cover (e.g. the `LibraryFolderScannerTests` change to `seriesTouched`'s type affecting an
unrelated existing assertion on `SeriesTouched` counts).
**Depends on:** Steps 1-7
**Verify:** all green; investigate and fix, don't skip, any failure.

## Step 9: Manual on-screen verification
**Files:** none
**What:** Per CLAUDE.md's UI-change convention — run the app (`dotnet run` against
`Paperbunkr.App`), and:
1. Preferences → Libraries: confirm the new "Split Mismatched Series" button renders correctly
   next to the existing three, with the tooltip, and is disabled while running.
2. Scan a small real (or fixture-built) library containing a TPB whose embedded series name
   includes volume/subtitle wording, alongside an existing base series with single issues —
   confirm it folds into the existing series in Library, no duplicate series card appears.
3. If a real anthology-style series is available (or seed one via the dev DB), click "Split
   Mismatched Series" and confirm the toast summary and the resulting series split are correct
   in Library/Series Detail.
Note any environment limitation encountered (e.g. this environment's known FlaUI/UiTests
launch gap) rather than silently skipping.
**Depends on:** Step 8

**Outcome:** Not completed — `request_access` for the Paperbunkr app was denied in this session
(same recurring limitation noted in [[project_paperbunkr_manga_detail_and_mangabaka]]), so no
on-screen check was possible. Steps 1-8 are otherwise done and green; this step is the one
open item if/when computer-use access is granted.
