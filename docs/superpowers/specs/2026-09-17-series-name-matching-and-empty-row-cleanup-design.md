# Series/Event Name Matching + Empty-Row Library Cleanup — Design

Date: 2026-09-17. Status: design, pending user review (grilling rounds 1-5, all answered in chat;
this doc reflects the final settled scope). Not yet approved for `writing-plans`.

---

## Why now (context)

User reported the Library screen showing several separate "Cataclysm" tiles that looked like one
series split apart, and asked whether series matching was failing on punctuation (`:` vs `-`)
because of a case-sensitivity bug.

**First-pass investigation (later superseded):** `LibraryFolderScanner.seriesByName`
([LibraryFolderScanner.cs:167](../../../src/Paperbunkr.App/Services/LibraryFolderScanner.cs))
already uses `StringComparer.OrdinalIgnoreCase` — not a case bug. But it *is* an exact-string
dictionary key with no punctuation normalization, so an embedded `ComicInfo.xml` `Series` field
(`"X: Y"`) and a filename-parsed fallback for a different issue of the same series (`"X - Y"`,
common since `:` is illegal in Windows filenames) would genuinely spawn two `Series` rows. Real
latent defect, kept in scope (Q14) — but not what caused the screenshot.

**Correction, mid-grilling:** the user clarified the Cataclysm tiles are legitimately *separate*
series (distinct minis/tie-ins under one crossover), not a duplication bug — regressing that
distinction is exactly the failure mode `SeriesSplitDetector` (2026-08-31,
[[project_paperbunkr_series_identity_scan_fixes]]) was built to prevent for the Warhammer 40k
anthology case. The actual bug: Paperbunkr's brand-new StoryEvent/Continuity auto-populate feature
(`docs/superpowers/specs/2026-09-17-storyevent-continuity-autopopulate-design.md`, shipped earlier
today) fails to recognize that punctuation-variant `Issue.StoryArc` tags belong to the same
crossover event.

**CE-parity check (standing rule) — this is the finding that reshaped the spec:** CE has a real,
shipped answer to "does this external name match my library name":
`ComicInfo.SeriesEquals(a, b, CompareSeriesOptions)`
(`_reference/ComicRackCE/ComicRack.Engine/ComicInfo.cs:1579`), used by CBL reading-list import
(`ComicIdListItem.cs:181-196`) as a **3-tier cascade**, loosening only on miss:

1. Exact, case-insensitive.
2. `+ IgnoreVolumeInName` — strips `rxVolume = \bv(ol(ume)?)?\.?\s?\d+\b\s*`.
3. `+ StripDown` — strips `rxSpecial = [^a-z0-9]|\bthe\b|\band\b` (every non-alphanumeric
   character — including `:`, `-`, and spaces — plus the whole words "the"/"and").

Under tier 3, `"Cataclysm: The Ultimates"` and `"Cataclysm - The Ultimates"` both canonicalize to
`"cataclysmultimates"`. This supersedes an earlier, invented normalization rule considered
mid-session (fold only dash/colon variants, double-spaces, `&`/`and`) — that guess was rejected in
favor of CE's actual, tiered, already-proven algorithm once found.

Same exact-string bug pattern (case-insensitive compare, no punctuation fold) was independently
confirmed at three more sites during grilling:

- `StoryArcGroupingResolver.GetCandidates` —
  [StoryArcGroupingResolver.cs:110](../../../src/Paperbunkr.Data/Metadata/StoryArcGroupingResolver.cs)
  (`arcKey = arcName.ToLowerInvariant()`)
- `StoryEventResolver.GetOrCreate` —
  [StoryEventResolver.cs:21](../../../src/Paperbunkr.Data/Metadata/StoryEventResolver.cs)
- `ContinuityResolver.GetOrCreate` —
  [ContinuityResolver.cs:132](../../../src/Paperbunkr.Data/Metadata/ContinuityResolver.cs)
- `ReadingListMatcher.FindExisting` —
  [ReadingListMatcher.cs:29](../../../src/Paperbunkr.Data/ReadingLists/ReadingListMatcher.cs) — a
  CBL/CSV import naming a series `"Cataclysm: The Ultimates"` misses a locally-stored
  `"Cataclysm - The Ultimates"` `Series` and falls through to `ResolveOrCreatePlaceholder`, silently
  spawning a duplicate placeholder `Series` + `Issue` instead of matching the real one — the
  worst-consequence instance of this bug family, since it corrupts library data rather than just
  under-grouping a UI list.

Separately, the user asked to fold in a second, unrelated-cause but same-symptom-class ask: the
Library grid can show a "ghost" `Series` card (a name that clearly indicates the content was
deleted/moved/renamed, but the row and its zero `Issue`s linger) and a blank `Issue` card (opens to
nothing). Confirmed via code that nothing today cleans either up —
`LibraryDeletionHelper.RemoveIssue` ([LibraryDeletionHelper.cs:30](../../../src/Paperbunkr.App/Services/LibraryDeletionHelper.cs))
never touches the parent `Series`, so even today's Library Health "Remove Missing File" flow leaves
an empty `Series` behind once its last `Issue` is removed.

## Approaches considered

**Name matching:**
1. *(rejected)* Ad-hoc, narrow punctuation fold invented for this bug (dash/colon variants only) —
   lower false-positive risk than CE's cascade, but not grounded in the project's own CE-parity
   standing rule, and would need its own from-scratch validation instead of reusing an algorithm CE
   has already run against real-world libraries for years.
2. *(rejected)* Aggressive single-pass slug normalize (casefold + strip all punctuation) applied
   unconditionally, no tiering — same end state as CE's tier 3 alone, but skips CE's tier 1/2 exact
   checks, so it pays tier-3's false-positive risk (`"Batman"` / `"The Batman"` collapsing together)
   on *every* comparison instead of only on a tiered miss.
3. **(chosen)** Adopt CE's literal 3-tier cascade as a shared utility, applied only on a miss at
   each of the 5 identified sites. Real CE parity, tiered so exact matches never pay the
   false-positive cost, and one implementation instead of five near-duplicates.

**Empty-row cleanup:**
1. *(rejected)* Automatic silent delete at end-of-scan — matches `SeriesSplitDetector`'s posture,
   but deleting a `Series`/`Issue` row is destructive in a way splitting isn't (splitting only
   ever *adds* a row), so silent-and-automatic is the wrong default here.
2. *(rejected)* Fold into the existing Missing Files list in Library Health — the two are related
   but not the same defect (missing = file gone; empty = row/tree gone, or file present but
   unreadable), and conflating them would make Missing Files harder to reason about.
3. **(chosen)** New standalone "Empty Rows" sub-section in Library Health, manual review + confirm,
   same Relink/Remove/Dismiss-per-item shape the tab already uses for Missing Files.

## Goals

- Fix the 5 confirmed exact-match sites to fold CE-cascade-equivalent names, without merging any
  `Series` that a human hasn't confirmed are the same (learned from the Warhammer 40k regression
  risk).
- Give the user a way to fix already-existing `Series` splits from this bug class (backward), not
  just prevent new ones (forward).
- Surface and let the user clear "ghost" `Series` (zero issues) and blank `Issue` cards (unreadable
  archive) that nothing currently detects.

## Non-goals (v1)

- No automatic/silent merging of `Series` under any circumstance — every merge from this spec is
  human-confirmed (Q4).
- No change to `SeriesSplitDetector`'s existing anthology-split behavior — that feature solves the
  opposite problem (one `Series` row covering unrelated titles) and is unaffected by this spec.
- No re-litigating `MergeSeriesInto`'s existing merge semantics (numbering-key dedup on merge) —
  reused as-is.
- No content-hash / deep integrity check for the "corrupt archive" empty-issue case — just "can an
  `ImageProvider` open it and does it report at least one page," same shallow check
  `LibraryHealthService.VerifyAsync` already does for `File.Exists`.

---

## Shared `TitleNormalizer` utility

New `Paperbunkr.Data.Metadata.TitleNormalizer` (static), CE-parity port of
`ComicInfo.SeriesEquals`/`rxVolume`/`rxSpecial`:

```csharp
internal static class TitleNormalizer
{
    private static readonly Regex RxVolume = new(@"\bv(ol(ume)?)?\.?\s?\d+\b\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxSpecial = new(@"[^a-z0-9]|\bthe\b|\band\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool NamesMatch(string a, string b, bool ignoreVolume = true)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (ignoreVolume)
        {
            string av = RxVolume.Replace(a, "").Trim(), bv = RxVolume.Replace(b, "").Trim();
            if (string.Equals(av, bv, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return string.Equals(StripDown(a), StripDown(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Canonical fold-everything key — CE's tier-3 <c>rxSpecial</c> strip. Used directly as a grouping key (StoryArc candidates) and as the final cascade tier (point lookups).</summary>
    public static string StripDown(string s) => RxSpecial.Replace(s, "");
}
```

`ignoreVolume` defaults on for series-name sites (`LibraryFolderScanner`, `ReadingListMatcher`,
where "Vol. 2" suffixes are real) and is passed `false` at the arc/event/continuity sites (a no-op
tier there, so simplest to just skip it rather than pay a pointless regex pass).

## Fix 1 — `LibraryFolderScanner` scan-time series match (forward)

At the existing exact-miss branch
([LibraryFolderScanner.cs:211](../../../src/Paperbunkr.App/Services/LibraryFolderScanner.cs)),
before falling through to "create new `Series`": if no exact key hit, scan `seriesByName`'s existing
keys for a `TitleNormalizer.NamesMatch` hit and attach there instead. Same shape as the existing TPB
`StripCollectionWording` fold immediately below it in the same method — extends the same
"exact-miss retry, existing-series-only" pattern, never uses the normalized form to name a
brand-new `Series`.

## Fix 2 — "Find Similar Series" (backward, on-demand, manual review)

New Preferences → Library Health sub-section, alongside Missing Files and the new Empty Rows
section (Fix 5). On-demand only (Q9) — no auto-run at end-of-scan, since this needs human judgment
every time, not just after every scan.

- **Detection**: group all `Series` by `TitleNormalizer.StripDown(Name)`; any group with 2+ `Series`
  is a candidate pair/set.
- **Review list**: each candidate group shown with member `Series` names + issue counts. User picks
  which two (or more) to merge and which is the *target* (surviving name) — no automatic
  target-selection, unlike Fix 4's automatic-grouping case, because this is explicitly a
  human-in-the-loop action per Q4.
- **Merge mechanics**: reuse `NeedsReviewViewModel.MergeSeriesInto`'s existing logic
  ([NeedsReviewViewModel.cs:363](../../../src/Paperbunkr.App/ViewModels/NeedsReviewViewModel.cs))
  — same numbering-key dedup, same `context.Series.Remove(source)`. Extracted to a shared location
  (`Paperbunkr.App.Services` or kept in `NeedsReviewViewModel` and called from the new view model —
  implementation detail for `writing-plans`) rather than duplicated.
- Explicitly **not** wired into `NeedsReviewViewModel`'s existing `SeriesConflict` UI (Q10) — new
  standalone list, since that pipeline is scoped to same-issue embedded-vs-filename mismatches, a
  different trigger even though the merge mechanics are shared.

## Fix 3 — `ReadingListMatcher.FindExisting`

Apply `TitleNormalizer.NamesMatch` (full cascade, `ignoreVolume: true`) as a fallback when the exact
`string.Equals` at
[ReadingListMatcher.cs:29](../../../src/Paperbunkr.Data/ReadingLists/ReadingListMatcher.cs) misses,
before falling through to `ResolveOrCreatePlaceholder`. Directly prevents CBL/CSV import from
spawning a duplicate placeholder `Series` + `Issue` over a punctuation mismatch against a real,
already-owned series.

## Fix 4 — `StoryArcGroupingResolver` / `StoryEventResolver` / `ContinuityResolver`

- **`StoryArcGroupingResolver.GetCandidates`**: group key becomes
  `(TitleNormalizer.StripDown(arcName), publisherKey)` instead of raw `arcName.ToLowerInvariant()`.
  The `dismissed` set and `existingMemberIssueIdsByName` lookup
  ([StoryArcGroupingResolver.cs:89-100](../../../src/Paperbunkr.Data/Metadata/StoryArcGroupingResolver.cs))
  must be re-keyed the same way, or a dismissal/existing-membership recorded under one spelling
  won't suppress a differently-punctuated variant of the same arc — same defect class, would
  reintroduce the bug one layer down if missed.
- **Candidate display name** (Q19): once a group folds multiple raw spellings together, the
  candidate's `ArcName` (and the `StoryEvent.Name` created on Accept) is the raw spelling carried by
  the **most member issues** in the group — ties broken by earliest year, consistent with the
  existing member-ordering tiebreak at
  [StoryArcGroupingResolver.cs:139](../../../src/Paperbunkr.Data/Metadata/StoryArcGroupingResolver.cs).
- **`StoryEventResolver.GetOrCreate`** / **`ContinuityResolver.GetOrCreate`**: on exact-match miss,
  retry with `TitleNormalizer.NamesMatch(existing.Name, trimmed, ignoreVolume: false)` before
  creating a new row.

## Fix 5 — Empty Rows (Library Health, new sub-section)

General hygiene sweep (Q8), independent of Fixes 1-4 — covers empties from any cause (manual
delete, aborted import, a rename that orphaned the old row), not just merge fallout.

**Empty series** — `Series` with zero `Issue`s (Q5/Q11: row-count check only; a `Series` whose
issues are all *confirmed-missing-but-not-yet-removed* is out of scope for v1, since running
Library Health's existing "Remove All Confirmed Missing" already collapses that case into this one).

**Empty issue** — either:
- `Issue.FilePath` is null/empty, or
- the file exists but is unreadable/zero-page. New `Issue.IsContentEmpty` (bool, default `false`) —
  distinct from `FileIsMissing`, since the file being *present-but-corrupt* needs a different fix
  path (Remove, not Relink) than "gone."

**Detection — piggybacks on `LibraryHealthService.VerifyAsync`** (Q12), not a second full-library
pass: for every issue that already passes the existing `File.Exists` check, additionally call
`PageDecodeCore.TryOpenProvider(issue.FilePath)`
([PageDecodeCore.cs:50](../../../src/Paperbunkr.App/Services/PageDecodeCore.cs)) — the same
provider-open primitive the reader already uses to decode pages. `null` return, thrown exception, or
`.Count == 0` sets `IsContentEmpty = true`; a successful open with `Count > 0` clears it. Cheap
relative to the sweep's existing per-file cost (opens an archive header, doesn't decode any image
bytes).

**UI**: one combined "Empty Rows" sub-section (Q13) in Library Health, same manual, no-auto-delete,
per-item Relink-N/A/Remove/Dismiss shape as Missing Files — empty series get Remove/Dismiss only (no
Relink concept for a row with nothing to relink to); empty issues get the full
Relink/Remove/Dismiss set, since a corrupt archive can be replaced with a working file at the same
path.

**Avalonia note** (per project's mandatory UI-touching-phase rule): this list is built from the same
`ItemsControl` + per-row action-button pattern Missing Files already uses safely. The one governing
constraint, already documented in this project's own CLAUDE.md build/runtime-gotcha section and
originally traced through `avalonia-events` (routed-event bubbling): a Remove/Dismiss button's
command must **not** synchronously mutate the bound `ObservableCollection` (or close a containing
`Popup`) while the triggering `Click` is still routing through that same row — defer via
`Dispatcher.UIThread.Post`, exactly like every other per-row delete button fixed under that gotcha
(2026-09-12, ~15 files). Flagging here so the implementer checks this new list against that pattern
before calling the UI done, per `avalonia-pro-max/review-checklist`.

## Data model

```csharp
// Issue
public bool IsContentEmpty { get; set; }  // default false
```

One migration, `AddIssueIsContentEmpty`. No other schema changes — "Find Similar Series" and "Empty
Rows" are both pure queries over existing tables (`Series`, `Issue`), no new persisted state beyond
this one flag.

## Testing

- `TitleNormalizerTests`: cascade tiers independently (exact hit short-circuits before StripDown
  runs), `StripDown` matches CE's `rxSpecial` behavior on the colon/dash/en-dash/`&`-vs-"and"
  cases, `ignoreVolume` tier folds `"Vol. 2"`/`"v2"` variants and is skippable.
- `LibraryFolderScannerTests`: new case — two files for the same series, one embedded `"X: Y"`, one
  filename-fallback `"X - Y"`, land on one `Series`; existing TPB-fold tests unaffected (same
  isolation pattern already used in this file, per
  [[project_paperbunkr_series_identity_scan_fixes]]).
- `ReadingListMatcherTests`: CBL entry naming a punctuation-variant of an existing series resolves
  to the real `Series`/`Issue`, not a new placeholder.
- `StoryArcGroupingResolverTests`: two punctuation-variant `StoryArc` tags produce one candidate;
  dismissal/existing-membership re-keying verified with a variant spelling; display-name tiebreak
  (most-members-wins, then earliest-year).
- `StoryEventResolverTests` / `ContinuityResolverTests`: exact-miss + StripDown-hit reuses the
  existing row instead of creating a duplicate.
- New `PreferencesScreenViewModelTests` (or a new `LibraryHealthViewModelTests` if that split
  happens first): "Find Similar Series" groups correctly and only merges on explicit confirm; "Empty
  Rows" lists zero-issue series and `IsContentEmpty` issues, Remove/Dismiss work, no Relink offered
  for an empty series.
- `LibraryHealthServiceTests`: `VerifyAsync` sets `IsContentEmpty` for an unreadable/zero-page file
  and clears it when the file becomes readable again, without touching `FileIsMissing`'s own state
  machine.
- Migration test for `AddIssueIsContentEmpty`, following the hand-fixed-`Down()` convention from
  [[project_paperbunkr_migration_rollback_orphan_column_bug]] if this column is ever added alongside
  another table in the same migration.
- Manual: reproduce the original report (two files, same real series, one `:`, one ` - ` embedded
  Series/StoryArc) and confirm both the scan-time fold and the StoryEvent candidate grouping now
  treat them as one; run "Find Similar Series" against pre-existing split data; open Empty Rows with
  a manually-emptied `Series` and a zero-byte-payload `.cbz` present.
