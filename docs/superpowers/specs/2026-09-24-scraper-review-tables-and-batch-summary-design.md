# ComicVine Scraper Review Tables + Manual-Search Fallback + Batch Summary — Design

*Follow-up to `2026-09-24-comicvine-scraper-fidelity-{design,plan}.md`. That work covered
correctness bugs, the cover-hash safety gate, settings, and the batch-result breakdown
(`ScrapeBatchResult`). This pass covers three more gaps the user found live, on screen, after that
work shipped: the review dialogs don't look or sort like CE's real dialogs, an auto-choose miss
silently gives up instead of asking the user, and there is no end-of-batch summary at all.*

## Context

The user showed a live screenshot of `ComicVineMatchReviewDialogView` ("Which ComicVine series is
this?" for *Absolute Batman #22*) and asked for three things:

1. Sort criteria for the candidate list, redesigned as a real table "like the comicvine scraper
   plugin did."
2. The auto-choose-fails-silently gap: when CE can't find a confident automatic match, it falls
   back to letting the user search and pick manually. Paperbunkr currently just skips the book.
3. An end-of-batch summary, which CE had and Paperbunkr doesn't.

All three were verified directly against the extracted CE plugin source
(`seriesform.py`/`issueform.py`/`scrapeengine.py`/`finishform.py`/`utils.py`), per this project's
standing rule.

### Verified CE facts

- **`seriesform.py.__build_table`**: a `DataGridView` (`ButtonDataGridView`), `AllowUserToOrderColumns
  = True`, 4 visible columns — Series (fill), Year, Issues, Publisher — plus hidden ID/Match/Model-ID
  columns. Rows are populated from all found series, then `table.Sort(table.Columns[5] /* Match */,
  Descending)` sets the default order. Clicking any visible column header re-sorts by that column
  (WinForms `DataGridView`'s own default `SortMode`). Selecting a row updates the cover-preview panel
  next to the table (already ported in Paperbunkr as `ResolvedCoverImageUrl`/the cover `Border`).
- **`issueform.py.__build_table`**: the same shape, 2 visible columns — Issue# (`AllCells` width),
  Title (fill) — plus hidden ID/Model-ID columns. `table.SortCompare += self.__sort_compare_fired`
  wires a custom comparer that calls `utils.natural_compare` specifically for column 0 (Issue#, so
  "10" doesn't sort before "2"); the Title column uses plain string sort. Default sort:
  `table.Sort(table.Columns[0], Ascending)`. This is the same natural-sort algorithm already ported
  into `ComicVineIssueReviewDialogViewModel.NaturalKey`/`NaturalKeyComparer` this session (Step 15) —
  that logic is reused here, not re-derived.
- **`scrapeengine.py:229-266` (`__scrape_book`'s caller loop)**: `bookstatus.equals("UNSCRAPED")` —
  "this return code means 'no series could be found using the current (automatic or manual) search
  terms'. when that happens, force the user to choose the search terms" — sets `manual_search_b =
  True` and loops back into `__scrape_book`, which (per its own doc comment) "fall[s] back to the
  user-interactive method of identifying the comic" whenever the automatic key lookup fails, **with
  no dependency on `autochoose_series_b`** beyond it being what triggered the automatic attempt in
  the first place. This only happens on an interactive run — CE's plugin had no headless mode.
- **`finishform.py.FinishForm`**: the last modal shown after a scrape batch, blocking, with two
  numbers (`scraped_n`, `skipped_n`) and an OK button. Paperbunkr has no equivalent today — only the
  Activity Center job-result string built in the prior session's Step 14 work.

## Decisions (settled via grilling, both rounds)

1. **Table control**: Avalonia's official `Avalonia.Controls.DataGrid` package (`CanUserSortColumns=
   "True"` gives header-click sorting for free, closely matching CE's own default `DataGridView`
   behavior). New dependency, not currently referenced anywhere in `Paperbunkr.App`. Accepted caveat:
   `DataGrid` column bindings are always reflection bindings, even in this otherwise-compiled-bindings
   app — a known, isolated Avalonia limitation, confirmed via `avalonia-controls/data-display`.
2. **Match-score column**: visible and sortable (Paperbunkr's own existing deviation from CE, which
   hides it) — not hidden.
3. **Both review dialogs** get the redesign (series-choice and issue-choice), not just the one
   screenshotted, matching CE's own consistency between `seriesform.py`/`issueform.py`.
4. **Batch summary scope**: counts *and* a per-book list, richer than CE's plain two-number
   `FinishForm`. Each row shows the book's label *and* a reason (why it landed in that bucket — e.g.
   the real exception message for Failed, "Skipped permanently" vs "Skipped" for SkippedByUser).
   Informational only for this pass — no retry-from-the-summary action (CE never had one either).

## §1 — Sortable DataGrid tables

### §1.1 `ComicVineMatchReviewDialogView`/`ComicVineMatchCandidateViewModel`

Replace the candidate `ItemsControl` (currently a `StackPanel` of `Button`-wrapped rows, split into
"Best match"/"Other results" sections) with a `DataGrid` bound to `Candidates`
(`ObservableCollection<ComicVineMatchCandidateViewModel>`, unchanged type), `SelectedItem="{Binding
SelectedCandidate}"`, `SelectionMode="Single"`. Columns, `AutoGenerateColumns="False"`:

| Header | Binding | Notes |
|---|---|---|
| Series | `Volume.Name` | `Width="*"` |
| Year | `Volume.StartYear` | |
| Issues | `IssueCountLabel` | already computed on the candidate VM |
| Publisher | `Volume.Publisher` | |
| Score | `Score` | `StringFormat` to match the current `"score {0:0}"` look |

The "Best match"/"Other results" section split and `IsTopMatch`/`IsFirstOtherResult` flags on
`ComicVineMatchCandidateViewModel` are dropped — CE's table has no such split, it's one flat,
sortable list defaulting to Score descending. `DataGrid.Sort` (or setting the initial sort
description via `CollectionView`) needs to run once after `SetCandidates` repopulates the
collection, since `DataGrid`'s own default sort state doesn't automatically reapply after an
`ObservableCollection.Clear()`/re-add — reset it explicitly in code-behind or via an attached
behavior after each `SetCandidates` call.

Cover-preview pane (column 1 of the existing `Grid`) is unchanged — still bound to
`SelectedCandidate`/`ResolvedCoverImageUrl`, still respects `ShowCovers`/`ForceSeriesArt`.

### §1.2 `ComicVineIssueReviewDialogView`/`ComicVineIssueCandidateViewModel`

Same treatment. Columns: Issue# (`Issue.IssueNumber`), Title (`Issue.Name`, `Width="*"`). Default
sort: Issue# ascending, using the *existing* `NaturalKey`/`NaturalKeyComparer` — `DataGrid` doesn't
support a custom per-column comparer via a simple XAML property (confirmed: only
`DataGridTemplateColumn.SortMemberPath`, which redirects to a *different bound property*, not a
custom comparer function), so the fix is to expose a precomputed, naturally-sortable key as its own
property on `ComicVineIssueCandidateViewModel` (e.g. a numeric `NaturalSortKey` plus a text
tiebreaker, or a single zero-padded string) and point the Issue# column's `SortMemberPath` at it
while still *displaying* `Issue.IssueNumber` via a `DataGridTemplateColumn`. The natural-sort
ordering that already happens once at construction time (Step 15) becomes this column's default
sort order, not a one-time `OrderBy`.

## §2 — Auto-choose miss falls back to interactive search

In `ScrapeOrchestrator.ScrapeAsync`'s `AutoChooseTopMatch` branch, `ranked.Count == 0` currently
always does `if (searchFailed) failed++; else noMatchFound++; break;` regardless of `isInteractive`.
Change: when `isInteractive && interactiveReview is not null` **and** the search didn't outright
throw (`!searchFailed` — a real exception still isn't something to paper over by silently opening a
search box, though showing the dialog either way is also defensible; decide during planning which
reads better), fall through to the *exact same* `interactiveReview(...)` call already used for the
cover-hash-gate-decline case (Step 11), passing the empty `ranked` list — `ComicVineMatchReview
DialogViewModel`'s constructor already handles an empty `initialCandidates` list correctly
(`SearchStatus = "No matches found - try a different search."`, search box pre-filled with the
query, ready for the user to retype and hit Search). No new dialog behavior needed, this is purely
an orchestrator control-flow change reusing existing, already-tested UI.

Resulting bucket: whatever the interactive resolution produces — `Applied` if the user finds and
confirms a match, `SkippedByUser` if they skip, matching the existing shared `if (chosen is null)`
path. No new `ScrapeBatchResult` bucket required for this specific change.

Non-interactive (or no reviewer) stays exactly as today: `noMatchFound++`/`failed++` and skip — CE
never had a headless mode, so there's nothing to match there; this preserves Paperbunkr's own
existing headless-automation gate.

## §3 — End-of-batch summary with per-book detail

### §3.1 Data model — `ScrapeBatchResult` gains per-book detail

Current shape (this session's Step 14): `ScrapeBatchResult(int Applied, int SkippedByUser, int
NoMatchFound, int Failed)`. Replace with per-outcome records so a UI can list *which* books landed
where and *why*, while keeping the counts as computed properties (existing call sites that only
read the counts, e.g. `ScrapeCoordinator`'s job-result string, keep working unchanged):

```csharp
public enum ScrapeOutcomeKind { Applied, SkippedByUser, NoMatchFound, Failed }

public sealed record ScrapeBookOutcome(int IssueId, string BookLabel, ScrapeOutcomeKind Kind, string? Reason);

public sealed record ScrapeBatchResult(IReadOnlyList<ScrapeBookOutcome> Outcomes)
{
    public int Applied => Outcomes.Count(o => o.Kind == ScrapeOutcomeKind.Applied);
    public int SkippedByUser => Outcomes.Count(o => o.Kind == ScrapeOutcomeKind.SkippedByUser);
    public int NoMatchFound => Outcomes.Count(o => o.Kind == ScrapeOutcomeKind.NoMatchFound);
    public int Failed => Outcomes.Count(o => o.Kind == ScrapeOutcomeKind.Failed);
    public int Total => Outcomes.Count;
}
```

Every existing counter-increment site in `ScrapeOrchestrator.ScrapeAsync` (there are ~8, all mapped
during Step 14: no-series-name, permanently-skipped, empty-search, gate-declined-no-reviewer,
auto-choose-off-unattended, dialog-cancelled ×2, chosen-is-null, plus the 4 `applied++` sites)
becomes `outcomes.Add(new ScrapeBookOutcome(issue.Id, BookLabel(issue), Kind, reason))` instead of a
bare `bucket++`. `BookLabel(Issue)` needs a `ScrapeOrchestrator`-local equivalent of
`ScrapeCoordinator.BookLabel` (`$"{issue.Series?.Name} #{issue.EffectiveNumber()}"`) since the
Data-layer orchestrator can't reference the App-layer coordinator's private helper.

Reason text per kind (exact wording decided during planning, examples):
- `Failed`: the caught `ComicVineException.Message`.
- `SkippedByUser`: "Skipped" vs "Skipped permanently" (Ctrl-held Skip, Step 16) — needs a flag
  threaded from the dialog result, since `ComicVineMatchReviewDialogViewModel`'s `_resolve` only
  carries the chosen volume today; reuse the same `markPermanentlySkipped` callback signal already
  wired for Step 16 rather than adding a new channel.
- `NoMatchFound`: "No search results", "Couldn't confirm the cover match", "Permanently skipped
  (never asked)", "No series name on file" — one per distinct code path, per §2's new fallback this
  no longer includes "auto-choose found nothing" when interactive (that's now `Applied`/
  `SkippedByUser` via the fallback).
- `Applied`: null, or a short confirmation ("Matched \"<series>\" #<n>") — decide during planning.

### §3.2 UI — a new blocking modal, interactive runs only

New `ScrapeBatchSummaryDialogView`/`ScrapeBatchSummaryDialogViewModel`, shown through the existing
`NativePluginModalHostViewModel`/`modalHost.ShowAsync` mechanism (same as the two review dialogs),
at the end of `ScrapeCoordinator.ScrapeIssuesAsync`'s **interactive** branch only — unattended/
scheduled runs keep using the Activity Center job result exclusively, matching the existing
headless-automation gate (no new modal ever appears there).

Layout: 4 count labels/chips (Applied/Skipped/No match/Failed) at the top, matching CE's
FinishForm's role but richer; below, a per-bucket expandable list (or a single flat list groupable
by outcome — decide the exact widget during planning, `Expander` per bucket is the simplest fit for
this app's existing `Border.card`/`Expander` component vocabulary) showing `BookLabel` + `Reason`
per row. Single OK button, no cancel (matches CE). Buckets with zero entries are omitted or shown
collapsed-empty (decide during planning) rather than an empty `Expander`.

## Open items for planning (not decided here, need file-level investigation)

- Exact `DataGrid` styling include (`Fluent.xaml`) placement in `App.axaml` and whether it needs
  scoping to avoid style bleed into the rest of the compiled-bindings app.
- Whether resetting `DataGrid`'s sort state after `SetCandidates` needs an attached behavior or can
  be done directly in code-behind on the existing `SetCandidates`/constructor call sites.
- Exact reason strings and whether they're centralized in one place (e.g. a `ScrapeOutcomeReasons`
  static class) vs. inlined at each `outcomes.Add(...)` call site.
- Dialog width/height adjustments now that both review dialogs host a multi-column table instead of
  a single-column card list — current sizes (640×420 series, 640×380 issue) were sized for the old
  layout.
