# Migration UX — Polish Pass

*Date: 2026-08-06. Extends the Migration UX shipped earlier the same day (docs/superpowers/specs/2026-08-06-migration-ux-design.md, see [[project-paperbunkr-migration-ux]] in memory) after manual review against a screenshot of the real flow running against the user's actual 371-series CE library. Three issues identified and prioritized by the user: fuzzy-match false positives, no remediation actions on Missing Files, no bulk actions for the Conflicts list.*

## 1. Fuzzy-match false positives

**Problem, confirmed from the real screenshot:** `SeriesNameMatcher.Similarity` (`src/Paperbunkr.Data/CeMigration/SeriesNameMatcher.cs`) is pure normalized-Levenshtein-ratio. Two names sharing a long common prefix score deceptively high regardless of what differs after it — "Wonder Woman" / "Wonder Man" scored 83% (above the 0.82 threshold) despite being entirely different characters/series.

**Fix:** blend character-level similarity with word-level similarity (Jaccard index over normalized word sets), weighted 70% character / 30% word:

```csharp
public static double Similarity(string a, string b)
{
    string normA = Normalize(a);
    string normB = Normalize(b);
    if (normA.Length == 0 && normB.Length == 0) return 1.0;

    double charSimilarity = CharSimilarity(normA, normB); // existing Levenshtein-ratio logic, unchanged
    double wordJaccard = WordJaccard(normA, normB);
    return (0.7 * charSimilarity) + (0.3 * wordJaccard);
}

private static double WordJaccard(string normA, string normB)
{
    var wordsA = new HashSet<string>(normA.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    var wordsB = new HashSet<string>(normB.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    if (wordsA.Count == 0 && wordsB.Count == 0) return 1.0;

    int intersection = wordsA.Intersect(wordsB).Count();
    int union = wordsA.Union(wordsB).Count();
    return union == 0 ? 1.0 : (double)intersection / union;
}
```

Threshold stays `0.82` (unchanged). Verified against the real screenshot's own data before writing this:

| Pair | Old (char-only) | New (blended) | Correct? |
|---|---|---|---|
| Wonder Woman / Wonder Man | 0.83 (flagged) | ≈0.68 (not flagged) | Yes — different characters |
| World War Hulks / World War Hulk | 0.93 (flagged) | ≈0.85 (still flagged) | Yes — real singular/plural duplicate |

`CharSimilarity` is the existing Levenshtein-ratio body extracted unchanged into its own method (no behavior change there) so `WordJaccard` can sit alongside it.

## 2. Missing Files remediation

**Schema:** `Issue.MissingAcknowledged` (bool, new) — "I know this one's missing, stop asking" without deleting data or faking a link.

**Each Needs Review "Missing Files" row gets three actions:**
- **Relink…** — `IFilePickerService.PickOpenFileAsync`, sets `Issue.FilePath`, clears `FileIsMissing` and `IsPlaceholder`.
- **Remove from library** — deletes the `Issue` row entirely. **First single-click-destructive real-data delete anywhere in the app** (every other list-removal action removes an association, not underlying data — Smart/Reading list items, series-conflict resolution). Gets a two-step inline confirm: button reads "Remove", first click flips it to "Confirm remove?" for ~3 seconds (or until clicked/another action taken), second click within that window actually deletes; otherwise it reverts to "Remove". No modal dialog — matches the app's existing lightweight, non-blocking interaction style elsewhere.
- **Dismiss** — sets `MissingAcknowledged = true`.

**Query change:** `NeedsReviewViewModel.RefreshMissingFileItems` adds `.Where(i => !i.MissingAcknowledged)` to what it already builds from the system "Missing Files" smart list. The system smart list itself is untouched — browsing it directly from the Smart Lists screen still shows every missing file regardless of acknowledgment, since acknowledgment is a review-queue concept, not a library-wide filter.

## 3. Bulk actions for Conflicts

Two buttons above the conflict list, added to **both** the in-flow Conflicts stage (`MigrationOverlay.axaml`'s Conflicts panel) and the Needs Review Series Conflicts section:
- **"Keep All Separate"** — resolves every currently-unresolved row in that list as KeepSeparate.
- **"Merge All Above 90%"** — fixed high-confidence threshold (not a slider, keeps the UI simple), resolves only rows at or above 90% similarity, leaving ambiguous ones for individual review.

Implementation: a static helper (e.g. `SeriesConflictRowViewModel.ApplyBulkAction(IEnumerable<SeriesConflictRowViewModel>, BulkAction)`) invoked by both `MigrationViewModel` (operating on its in-memory `Conflicts` collection, feeding the existing `_mergeDecisions`/`_keepSeparateDecisions` sets) and `NeedsReviewViewModel` (operating on persisted `SeriesConflict` rows, calling the existing `ResolveConflict` per row) — same UI affordance, each wired to its own resolution path rather than one sharing broken across two different data-backing models.

## Testing

- `SeriesNameMatcherTests`: add the Wonder Woman/Wonder Man (must NOT flag) and World War Hulks/World War Hulk (must still flag) cases as explicit regression tests, plus a couple more blended-score sanity checks (identical names → 1.0, completely unrelated names → near 0).
- New tests for `MissingAcknowledged` filtering in `NeedsReviewViewModel`-equivalent query logic (`Paperbunkr.Data.Tests`, following the existing SmartList test patterns).
- Bulk-action tests: "Keep All Separate" resolves N rows correctly; "Merge All Above 90%" only touches qualifying rows, leaves the rest pending.
