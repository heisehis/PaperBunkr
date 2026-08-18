# Metadata Model — Phase 2b: Series Reassignment Proposals

**Date:** 2026-08-17
**Status:** Approved, pending implementation plan
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md` (79-section architectural spec),
§37/§38 ("Proposed Metadata") - the same source as
[Phase 2a](2026-08-17-metadata-model-phase2a-metadata-proposals-design.md), which deferred `Series`
specifically because it's a mandatory FK in Paperbunkr, not a free-text shadow field like CE's.

## Context

2a shipped `MetadataProposal`/`MetadataResolutionPolicy`/`EffectiveValueResolver` for 6 scalar
`Issue` fields (`Title`/`Format`/`Volume`/`Number`/`Count`/`Year`), each following the same shape:
a field is sometimes blank, a filename-parsed fallback fills it in as a reviewable proposal instead
of a silent direct write.

`Series` doesn't fit that shape. `Issue.SeriesId` is a required FK, resolved unconditionally at
scan time (`LibraryFolderScanner.ScanAll`): embedded ComicInfo.xml's `Series` name wins if present,
else the filename-parsed name, else `"Unknown"` - there's no "blank, needs a fallback" moment the
way `Number`/`Volume`/`Year` had. Confirmed by reading the actual scanner code before designing
this, not assumed.

**The real trigger this phase targets**: embedded metadata and filename can disagree about the
series name (a mislabeled scan, a wrong bundled ComicInfo.xml, an aggressively-abbreviated
filename). Today that disagreement is invisible - the embedded name silently wins and the filename
alternative is discarded. This phase surfaces that alternative as a reviewable proposal instead.

**Accepted, deliberate tradeoff** (discussed and confirmed, not silently built around): this phase
uses exact case-insensitive name matching (no fuzzy-matching against the existing
`SeriesNameMatcher`/`SeriesConflict` machinery CE migration already has), and `Automatic` policy
applies the reassignment immediately, including deleting the source series if it becomes empty. In
combination, a scan where the filename parses to "Batman" but embedded metadata says
"Batman (2016)" will silently move the issue to a `Batman` series (creating one if needed) and can
delete the `Batman (2016)` series if that was its only issue. This mirrors real risk CE's own
Shadow/Proposed system had (no fuzzy protection either) rather than inventing a new safety net
Number/Volume/Year didn't get either.

## Scope

### Schema

**No migration needed.** `MetadataProposalField` gains a `Series` member. It's stored via
`HasConversion<string>().HasMaxLength(32)` (2a's convention) - a new allowed string value on an
already-generic `TEXT` column, not a schema change.

### The architectural split: read-time vs. write-time proposals

2a's 6 fields are **read-time**: `EffectiveValue` computes live from `issue.Field ??
AcceptedProposalValue(...)` on every read, no mutation happens until (if ever) the user edits the
real field directly.

`Series` can't work that way - `Issue.SeriesId` is never null, so there's no fallback to compute at
read time. Accepting a Series proposal is a **write-time** action instead: it immediately moves the
issue to a different `Series` row. This is the one real new concept this phase introduces; there is
no `EffectiveSeries()` resolver, and none is needed.

### Trigger (scanner only)

In `LibraryFolderScanner.ScanAll`, right after `seriesName` is resolved (today: embedded wins, else
filename, else `"Unknown"`): if `embeddedInfo?.Series` is non-blank **and** `nameInfo.Series` is
non-blank **and** the two differ (case-insensitive), create a `MetadataProposal`:

```
Field = Series
CurrentValue = embeddedInfo.Series.Trim()   // what the issue is actually attached to
ProposedValue = nameInfo.Series.Trim()      // the filename-parsed alternative
Source = FilenameParser
Confidence = 0.6m                            // same constant 2a uses for this source
Status = per AppSettings.MetadataResolutionPolicy, same as every other field this phase
```

No embedded info at all → `seriesName` is already filename-derived → no alternative to propose → no
proposal. Matches today's plain-fallback behavior exactly when there's nothing to compare against.

### `SeriesReassignmentResolver.Apply`

New shared method, `src/Paperbunkr.Data/Metadata/SeriesReassignmentResolver.cs` (its own file, not
folded into `IssueMetadataExtensions.cs` - unlike that file's pure, side-effect-free resolvers, this
one mutates the database, a meaningfully different contract worth keeping visually separate).

**Two real bugs found and fixed during implementation, in order:**

1. **First attempt**: had the scanner decide *up front* which series to attach a mismatched issue
   to - `filenameSeriesName` when `Automatic` policy would auto-accept, `embeddedSeriesName`
   otherwise - reasoning that calling `Apply` on an unsaved entity (bug 2, below) couldn't work
   inline. This broke the app's actual, pre-existing, load-bearing behavior: **embedded metadata
   always wins**, unconditionally, confirmed by two regression tests
   (`ScanAllAsync_EmbeddedComicInfo_WinsOverMisleadingFilename`,
   `ScanAllAsync_EmbeddedComicInfoMissingAField_FallsBackToFilenameForThatFieldOnly`) that started
   failing the moment this landed. The fix: the issue is *always* attached to the embedded-derived
   series first, exactly as before this phase existed - a mismatch produces a proposal
   alongside that, it never changes the initial attachment. Caught by running the existing test
   suite immediately after the change, not assumed correct.
2. **The reason attempt 1 existed at all**: `SeriesReassignmentResolver.Apply` resolves the
   *source* series via `issue.SeriesId`, which reads as `0` (meaningless) on a brand-new `Issue`
   that hasn't been saved yet (`Id`/`SeriesId` are both `0` until `SaveChanges`). The actual fix
   isn't to skip `Apply` for the scanner - it's to call it *after* the batch's own `SaveChanges`
   instead of inline per-file. The scanner now collects this scan's auto-accepted `Series`
   proposals into a list while looping (issues still get attached to their embedded-derived series
   normally), calls `context.SaveChanges()` once as before, then applies each collected
   reassignment as a genuine second step - the same way an accepted Number/Volume/Year proposal
   takes effect *on top of* a real starting value rather than pre-empting it, which is what this
   phase should have looked like from the start.

With both fixed: `Apply` is called from two places - `LibraryFolderScanner`, post-save, for each
`Automatic`-accepted proposal from that scan; and `NeedsReviewViewModel`'s existing
`ResolveProposal`, via one new conditional branch (when `proposal.Field == MetadataProposalField.Series`
and the action is Accept, call the resolver in addition to flipping `Status`/`ResolvedAt`). Every
other field keeps doing exactly what it does today. Reject needs no extra branch for `Series`
either - the issue simply stays on whatever series it's already on. `CurrentValue` is a historical
snapshot ("what it used to be attached to"), not a live current-value indicator - true even for an
already-`Accepted` row, which is exactly what the review UI's existing "(already applied)" badge
(2a) is for.

`Apply(context, proposal)` does, given the issue currently referenced by `proposal.IssueId` (looked
up fresh at accept-time, not from a stale snapshot):

1. Find an existing `Series` whose `Name` matches `proposal.ProposedValue` case-insensitively, or
   create one (`new Series { Name = proposal.ProposedValue }` - matching the scanner's own existing
   creation shape exactly, including leaving `SortName` unset, not `ReadingListMatcher.CreateSeries`'s
   slightly different convention of also setting `SortName`).
2. Reassign the issue's `Series` navigation to that target, then `SaveChanges()`.
3. *Then*, separately: if the source series now has zero issues, delete it (a second `SaveChanges()`)
   - same behavior `NeedsReviewViewModel.MergeSeriesInto` already has for a `SeriesConflict` merge,
   applied here to a single-issue move instead of a bulk merge.

**A third implementation bug, found and fixed after the two above**: originally steps 2-3 shared one
`SaveChanges()` call at the end. That silently produced a library with **zero** series at all - not
caught by reasoning about the code, caught by a failing test. Root cause: when `targetSeries` is
brand new, its `Id` is still `0` until it's actually persisted; checking "is the source series now
empty" against the database (`context.Issues.Any(...)`) *before* that save meant the query still saw
the issue's *old* `SeriesId` on disk, and relied on excluding the issue itself by `Id` - fragile, and
in the failing run, wrong. Splitting into two `SaveChanges()` calls - reassign-and-save, *then*
check-and-maybe-delete-and-save - removes the ambiguity entirely: by the time the emptiness check
runs, the database is already the source of truth for "which issues does the source series actually
have now," full stop.

**A fourth, more subtle bug**, found only once a test mirrored the scanner's *exact* object-graph
shape rather than a simpler hand-built one: setting `issue.Series = targetSeries` alone silently
no-op'd - `issue.SeriesId` came back completely unchanged after `SaveChanges()` - whenever the
*source* series' `Issues` collection navigation was already loaded/tracked (exactly what
`LibraryFolderScanner` does via `series.Issues.Add(issue)` when it first attaches a new issue). EF
Core reconciles a reference navigation (`Issue.Series`) against its inverse collection navigation
(`Series.Issues`) during change detection, and the untouched collection side won, effectively
reverting the reassignment. The fix updates both sides explicitly:
`sourceSeries?.Issues.Remove(issue); targetSeries.Issues.Add(issue);` alongside
`issue.Series = targetSeries;`. A simpler isolated test (no collection navigation populated) passed
fine and would never have caught this - worth remembering next time a "should be equivalent" repro
doesn't reproduce a real failure: the object-graph shape mattered, not just the entity values.

No duplicate-blocking against the target series' existing issues (e.g. a same-`EffectiveNumber`
collision) - deliberately out of scope this phase. This app already has a separate "Duplicate"
Smart List field for surfacing that after the fact; bespoke blocking here would duplicate that
mechanism for an edge case, not solve a real gap.

## Testing

- `LibraryFolderScannerTests`: embedded/filename Series mismatch creates a proposal with the right
  `CurrentValue`/`ProposedValue`, for both `Automatic` (already reassigned) and `Prompt` (issue
  stays on the embedded-derived series until accepted) policy. No mismatch (names match, or no
  embedded info at all) creates no proposal - regression coverage for the common case.
- New resolver tests (`SeriesReassignmentResolverTests` or added to an existing Data.Tests file):
  target series already exists (found, not duplicated); target doesn't exist (created); source
  series empties out and is deleted; source series has other issues left and survives.
- `NeedsReviewViewModelTests`: Accept on a `Series`-field proposal actually moves the issue (not
  just flips `Status`); Reject on a `Series`-field proposal leaves `SeriesId` untouched.

## Explicitly out of scope

Fuzzy name matching / `SeriesConflict` integration (the accepted tradeoff above). Duplicate-issue
blocking on reassignment. Any UI beyond the existing "Metadata Proposals" Needs Review section
(its `CurrentValue`/`ProposedValue` display already works unchanged for series names - confirmed
by re-reading `MetadataProposalRowViewModel`/the AXAML template before writing this, not assumed).
Manual user-initiated reassignment (Issue Properties/Bulk Edit "move to a different series" action)
- this phase is scanner-triggered only, per the trigger-scenario decision above; a manual action
is a natural, separable follow-up that could reuse the same `Apply` resolver. `MetadataDescriptor`/
sort-group retargeting (2c, unrelated).
