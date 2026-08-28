# Metadata Model — Phase 4g: Age Progression

**Date:** 2026-08-27
**Status:** Approved, pending implementation
**Source doc:** Design session with Ehis (2026-08-27). Age boundaries verified directly against
ComicRack CE's real shipped defaults (`_reference/ComicRackCE/ComicRack/Output/DefaultLists.txt`,
`[Book Ages]` section) rather than assumed, cross-checked against Wikipedia's Golden/Silver/Bronze/
Modern Age articles for the commonly-cited academic boundaries where they diverge from CE's own.

## Context

`Issue.BookAge` is free text, ported from CE's `ComicBook.BookAge` (a "Book Collection" field,
verified: `ComicRack/Dialogs/ComicBookDialog.cs`), already present in Paperbunkr's schema and wired
into `SmartListCatalog`, but dormant — no editor UI, no fixed taxonomy, and CE's own field is
genuinely free text too (an autocomplete combo, not an enum).

CE ships a real default list for it (`DefaultLists.txt`, `[Book Ages]`):

```
Platinum (1897-1937)
Golden (1938-55)
Silver (1956-69)
Bronze (1970-79)
Modern (1980-Now)
```

This is the functional source of truth this phase builds on — not Wikipedia's commonly-cited
boundaries (Golden 1938-1956, Silver 1956-1970, Bronze 1970-1985, Modern 1985-present), which agree
closely with CE on the Golden/Silver seam (both land on 1956) but diverge meaningfully on
Bronze/Modern (CE: split at 1980; Wikipedia: split at 1985). Per this project's standing rule to
verify against real CE behavior rather than assume, and because CE-migrated libraries' existing
`BookAge` free-text values (where populated) will already match CE's own boundaries, CE's
five-stage list is what actually classifies an issue. Wikipedia's boundaries aren't discarded,
though — see the confidence and display handling below, which is deliberately built to surface them
rather than silently pick one convention and hide the other.

Unlike Copper Age (considered and rejected during this design's discussion — fan/dealer usage, no
fixed boundary, no source Paperbunkr ingests tags it consistently, and its rough range overlaps the
already-disputed Bronze/Modern seam), Platinum Age is CE's own real shipped default and a
long-established scholarly term predating Golden Age — it's included.

## Scope

### `ComicAge` enum and `ComicAgeCatalog`

```csharp
public enum ComicAge { Platinum, Golden, Silver, Bronze, Modern }

public sealed record ComicAgeInfo(
    string DisplayName,
    int CeStartYear,
    int? CeEndYear,          // null for Modern (open-ended, "Now")
    string? CommonlyCitedRange // display-only text, e.g. "commonly cited elsewhere as 1970-85"
);

public static class ComicAgeCatalog
{
    public static readonly IReadOnlyDictionary<ComicAge, ComicAgeInfo> All = new Dictionary<ComicAge, ComicAgeInfo>
    {
        [ComicAge.Platinum] = new("Platinum Age", 1897, 1937, "sometimes dated 1897-1938"),
        [ComicAge.Golden]   = new("Golden Age", 1938, 1955, "commonly cited as 1938-1956"),
        [ComicAge.Silver]   = new("Silver Age", 1956, 1969, "commonly cited as 1956-1970"),
        [ComicAge.Bronze]   = new("Bronze Age", 1970, 1979, "commonly cited elsewhere as 1970-1985"),
        [ComicAge.Modern]   = new("Modern Age", 1980, null, null),
    };

    public static ComicAge? FromYear(int year); // CE boundaries, see resolver below
}
```

Field-descriptor-dictionary shape, consistent with `RelationTypeCatalog`/`FormatSignalCatalog`
([[phase4e-format-signal-suggestions]]). `CommonlyCitedRange` exists purely for display (era
pickers, progression-bar tooltips) — it never drives classification logic, so Wikipedia's
boundaries are genuinely "incorporated" as context without a second, conflicting classification
path.

### `BookAgeResolver`

New file, `src/Paperbunkr.Data/Metadata/BookAgeResolver.cs`:

```csharp
public static class BookAgeResolver
{
    public static (ComicAge? Age, decimal Confidence, string? Reason) Resolve(Issue issue);
}
```

Resolution order:
1. If `Issue.BookAge` matches one of CE's five default labels (case-insensitive, ignoring the
   parenthetical year range — i.e. matching on `"Golden"` whether the stored text is `"Golden"` or
   CE's full `"Golden (1938-55)"`), return that `ComicAge` at `Confidence = 1.0m` — an explicit
   user/CE-migrated value is authoritative.
2. Else if `Issue.Year` is set, use `ComicAgeCatalog.FromYear`. Years in the disputed 1980-1984
   window return `ComicAge.Modern` (CE's answer) but at `Confidence = 0.6m` with
   `Reason = "1980-84 is Modern per ComicRack CE's own boundaries, but commonly cited elsewhere as
   still Bronze Age"` — every other year returns `Confidence = 1.0m` with no caveat, since only that
   five-year window is genuinely disputed between the two source conventions being reconciled here.
3. Else (`BookAge` unset, unrecognized text, and no `Year`), return `(null, 0m, null)` — no guess.

This is a **read/display-time computation, not a stored/backfilled column** — unlike
`MetadataProposal`'s stored-and-reviewed shape, there's no `Issue.ProposedBookAge` this phase.
Storing and reviewing a proposal for every issue in a library on first load is a heavier mechanism
than a progression bar needs; `BookAgeResolver.Resolve` runs on demand wherever an age is displayed.
Revisit as a real `MetadataProposal` integration if a bulk "review all inferred ages" workflow turns
out to be wanted later — not built speculatively now.

### Family scoping: graph-driven

```csharp
public static class SeriesFamilyResolver
{
    public static IReadOnlyList<Series> GetFamily(PaperbunkrDbContext context, int seriesId);
}
```

A family is the connected component reachable from the given series by following `MediaRelation`
edges (via `MediaRelationResolver.GetRelatedSeries`, breadth-first, no depth limit but with a
visited-set guard against cycles — two series can legitimately be mutual `Related`/`Similar` to each
other) plus every series sharing any of its `Continuity` rows (via
`ContinuityResolver.GetOtherSeriesSharingContinuity`), unioned and deduplicated. No new entity or
join table — this is a pure query over data [[phase3-media-relations]] and [[phase4a-continuity]]
already populate, per the decision (during this design's discussion) to ship the graph-driven
approach now and treat a first-class `Character` entity as separate, deferred future work rather
than a dependency of this phase.

**Explicitly not character-aware this phase**: an issue in an unrelated one-shot that was never
linked via `MediaRelation` and shares no `Continuity` with the chosen series won't appear in its
family, even if the same character appears in both. This is the known, accepted gap the
Character-entity deferral leaves open — documented here rather than silently glossed over, so a
future session doesn't rediscover it from scratch.

### UI: Age Progression mode

Third mode on the Story Events screen's mode switcher ([[phase4f-continuity-browse]] introduces the
switcher itself): **Events | Continuities | Timeline**.

- Entry point: pick a series (reusing the same series-search shape used throughout this codebase)
  to seed `SeriesFamilyResolver.GetFamily`.
- The resulting issues, across every series in the family, are bucketed by `BookAgeResolver.Resolve`
  and laid out as a horizontal timeline: one labeled section per `ComicAge` present (skipping ages
  with zero issues in this family, rather than always showing all five), issues ordered by
  `Year`/`Month`/`Day` within each section, each showing its cover thumbnail and read/unread state
  (reusing `ReadingStatus`/`OpenCount`, already on `Issue` — no new read-tracking needed).
- Hovering or selecting an era section shows `ComicAgeInfo.CommonlyCitedRange` as a tooltip when
  non-null (Platinum/Golden/Silver/Bronze; Modern has none since CE and Wikipedia don't meaningfully
  disagree on its open-ended nature). An issue resolved at reduced confidence (the 1980-84 window)
  shows a small inline indicator with its `Reason` text on hover, rather than looking identical to a
  confidently-placed issue.
- Clicking any issue opens it in the reader, same as every other issue-grid instance in this app.

This view is deliberately read-only this phase — it's a "where am I in this timeline"
browsing/reading-order aid, not a new place to edit `BookAge` (that editor lives on Issue
Properties/Bulk Issue Properties, same as `Format`'s new editor in [[phase4e-format-signal-
suggestions]]).

## Testing

- `ComicAgeCatalogTests`: `FromYear` boundaries match CE exactly at each seam (1937/1938, 1955/1956,
  1969/1970, 1979/1980); the 1980-1984 window is covered by the resolver test below, not this one
  (catalog itself has no confidence concept).
- `BookAgeResolverTests`: an explicit CE-label `BookAge` value wins over `Year` even when they'd
  disagree; a recognized label ignores its own parenthetical range text; an unrecognized `BookAge`
  string with a `Year` set falls back to year inference; a 1982 issue resolves to `Modern` at
  `Confidence = 0.6m` with the disputed-window reason text; a 1990 issue resolves to `Modern` at
  `Confidence = 1.0m` with no reason text; no `BookAge` and no `Year` returns `(null, 0, null)`.
- `SeriesFamilyResolverTests`: a family includes series reachable by a multi-hop `MediaRelation`
  chain (A->Sequel->B->SpinOff->C all appear from querying A); a mutual-relation cycle doesn't
  infinite-loop; series sharing only a `Continuity` (no direct `MediaRelation`) are included; a
  series with neither relations nor continuities returns a family of just itself.
- `EventsScreenViewModelTests` (Timeline mode): selecting a seed series populates era-bucketed
  sections with only non-empty ages shown; issues within a section are year-ordered; a
  reduced-confidence issue's indicator is present; clicking an issue opens the reader.

## Explicitly out of scope

Copper Age, or any age taxonomy beyond CE's five stages — considered and rejected during this
design's discussion (see Context). A first-class `Character` entity and character-aware family
scoping — deferred as a separate future item, not this phase; `SeriesFamilyResolver` documents the
resulting gap above rather than papering over it. Storing `BookAge` inference as a reviewable
`MetadataProposal` — a heavier mechanism than this phase's read-time resolver needs; revisit if a
bulk-review workflow is wanted later. Editing `BookAge` from the Timeline view itself (edits happen
on Issue Properties, same as every other field). A progression bar scoped to Continuity or
global-library level — explicitly decided against per-character/series-family scoping earlier in
this design's discussion; those other scopes remain plausible future modes on the same Timeline view
if wanted, not built speculatively now.
