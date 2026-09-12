# Issue.AlternateCount — closing the last Alternate-Series field gap

**Date:** 2026-09-12
**Status:** Approved, ready for planning
**Scope:** Beta backlog (Library browsing extras / metadata completeness), not Alpha P0–P7.

## 1. Background

CE's ComicInfo.xml crossover/tie-in tracking is three fields: `AlternateSeries` (string),
`AlternateNumber` (string), `AlternateCount` (int — the total issue count of that alternate
series, exactly mirroring how the main `Count` field is the total issue count of the primary
`Series`). CE's own tooltip for the column (`ComicBrowserControl.cs:790`) is literal: *"Total
issues of the crossover/story."*

Paperbunkr ported `AlternateSeries`/`AlternateNumber` fully — entity columns, Issue Properties
editor fields (incl. a token-insert button on `AlternateSeries`), sort, group, and
ComicInfo.xml write-back. `AlternateCount` was never ported. This is not a new discovery: it was
named and explicitly deferred twice already —

- [`2026-08-07-bulk-issue-editing-design.md:92`](2026-08-07-bulk-issue-editing-design.md) —
  "a real CE field that was simply never ported to Paperbunkr's `Issue` schema - a pre-existing
  gap, not something this spec introduces or needs to fix."
- [`IssueToComicInfoMapper.cs:14`](../../../src/Paperbunkr.Data/CeMigration/IssueToComicInfoMapper.cs) —
  listed among the ComicInfo.xml elements Paperbunkr doesn't model.
- Resurfaced 2026-09-12 while scoping the Library sort/group axes work
  ([`2026-09-12-library-sort-group-axes-design.md`](2026-09-12-library-sort-group-axes-design.md)),
  which explicitly split `AlternateCount` off as its own future item rather than fix it inline.

This spec closes that gap.

**Not new plumbing:** `Paperbunkr.Engine.ComicInfo` (the ported CE class) already carries
`AlternateCount` — it's a straight port of CE's `ComicInfo.cs`. The gap is entirely in the App/Data
bridge layer (`Issue` entity, `CeLibraryMigrator`, `IssueToComicInfoMapper`) and the UI/query
surfaces built on top of it.

## 2. What ships

1. **`Issue.AlternateCount` (`int?`)** — new EF-mapped column, nullable, matching the existing
   `Issue.Count` convention (not CE's WinForms-era `-1` sentinel).
2. **ComicInfo.xml round-trip:**
   - `CeLibraryMigrator` reads `info.AlternateCount` into `issue.AlternateCount` (mirrors the
     existing `AlternateSeries`/`AlternateNumber` lines at
     [`CeLibraryMigrator.cs:521-522`](../../../src/Paperbunkr.Data/CeMigration/CeLibraryMigrator.cs)).
   - `IssueToComicInfoMapper` writes `issue.AlternateCount` back out (mirrors
     [`IssueToComicInfoMapper.cs:39-40`](../../../src/Paperbunkr.Data/CeMigration/IssueToComicInfoMapper.cs)),
     and its class-doc comment listing `AlternateCount` as unmodeled is corrected.
3. **Issue Properties editor:** a new "ALTERNATE COUNT" field immediately after "ALTERNATE NUMBER"
   in `IssuePropertiesScreen.axaml` (currently line 307) — same `NumericUpDown` +
   `NullableDecimalStringConverter` idiom as the existing "COUNT" field (line 260), bound to a new
   `AlternateCountText` string-wrapper property on `IssuePropertiesScreenViewModel`, following the
   same pattern as `CountText`. No autocomplete/vocab (numeric field, matches `Count`'s own lack of
   one).
4. **Library sort:** `IssueListSortField.AlternateCount`, inserted after `AlternateNumber` in the
   enum; `IssueListFieldCatalog.SortFields` entry using `SortStrategies.Numeric(r => r.AlternateCount)`
   (identical shape to the existing `Count` entry); a `Col(...)` display-string entry
   (`r.AlternateCount?.ToString(CultureInfo.InvariantCulture)`, matching `Count`'s).
5. **Library group:** `IssueListGroupField.AlternateCount`, inserted after `AlternateNumber` in the
   enum; grouped using CE's own **exact fixed ranges** — `0-20 | 21-50 | 51-100 | 101-200 | 201-500
   | 501-1000 | >1000` — not `Count`'s ad hoc per-exact-value `NumericBucket`. This is a deliberate,
   evidence-grounded choice: CE's `ComicBookGroupAlternateCount` and `ComicBookGroupOpenCount` are
   both literal subclasses of the same `ItemGroupCount` base, sharing the same `"CountGroups"`
   resource string — i.e. CE itself treats these two fields as the same grouping concept. Paperbunkr
   already ported that exact bucket table for `OpenCount`
   ([`IssueListFieldCatalog.cs:324-328`](../../../src/Paperbunkr.App/Models/IssueListFieldCatalog.cs)) —
   `AlternateCount`'s grouper reuses that same table (extracted to a shared name, e.g.
   `CountRanges`/`CountBucketKey`/`CountBucketOrder`) rather than duplicating it or the `Count`
   field's unrelated per-value scheme. `OpenCount` is a non-nullable `int` so it never needed CE's
   8th `"Unspecified"` bucket (dropped when it was ported); `AlternateCount` is `int?`, so `null`
   (never scanned/set) maps to that 8th CE bucket, re-added here as `"Unspecified"` — same label CE
   itself uses (`CountGroups` resource's 8th entry), ordered first.
6. **Bulk Issue Editing:** `AlternateCount` added to `BulkFieldRegistry` as
   `Numeric("Alternate Count", Main, i => i.AlternateCount, (i, v) => i.AlternateCount = v, min: 0)`
   — same shape as the existing `Count` entry — reversing the 2026-08-07 spec's exclusion now that
   its blocking gap (no `Issue` column) is fixed. The class-doc comment listing `AlternateCount`
   among deliberately-absent fields is updated to remove it and note it shipped here.
7. **Smart Lists:** `SmartListField.AlternateCount` added as `SmartListDataType.Number`
   (`i => i.AlternateCount ?? -1`), mirroring the existing `SmartListField.Count` entry exactly.
   CE-grounded: `ComicBookAlternateCountMatcher` is a real numeric Advanced-Search matcher in CE.
8. **`IssueListRow`:** new `AlternateCount` property (`int?`), populated from `issue.AlternateCount`
   alongside the existing `AlternateSeries`/`AlternateNumber` properties.
9. **EF migration** (name: `AddIssueAlternateCount`) — adds the nullable `int` column. `Down()` is a
   real `DropColumn` (not a no-op): this is a brand-new EF-mapped column with no prior unmapped-orphan
   collateral-damage risk, so it doesn't fall under the no-op-`Down()` rule documented on
   `AddNavRailHoverExpandEnabled` — it matches `AddReadingEventLog`'s precedent instead (a genuine,
   safe-to-drop new column).

## 3. Explicitly out of scope

- **`Series`-level fields, `Manga`, `EnableProposed`** — still excluded from Bulk Editor for the
  same reasons the 2026-08-07 spec gave; unrelated to this gap.
- **Any change to `Paperbunkr.Engine.ComicInfo`** — it already has the field; nothing to add there.
- **A dedicated "IssueEdition"/variant-cover concept** — investigated and ruled out during
  brainstorming: CE's `AlternateSeries`/`AlternateNumber`/`AlternateCount` trio is a
  crossover/tie-in-numbering mechanism, not a variant-cover/printing/format concept. No such CE
  concept exists to port; if Paperbunkr wants variant-cover tracking later, that's an unrelated,
  net-new feature with no CE precedent, out of scope here.

## 4. Testing

Extend, don't duplicate structure:
- `IssueToComicInfoMapperTests` — round-trip case for `AlternateCount`.
- A `CeLibraryMigrator`-side test asserting `AlternateCount` reads through from `ComicInfo`.
- `IssueListFieldCatalogTests` — sort + group (bucket-boundary cases: 20/21, 1000/1001, and
  `null` → `"Unspecified"`) for the new fields, same shape as the existing `OpenCount` bucket
  tests.
- A migration test (`Up`/`Down`) alongside the other single-column migration tests.
- `BulkFieldDescriptor`/bulk-editor VM tests — one new case for `AlternateCount` mixed-value/diff-merge,
  matching the existing `Count` coverage.
- `SmartListCatalog`/Smart List VM tests — one new case for the `Number` field type, matching `Count`.

No new migration-history risk: this is one nullable column, no unmapped-column interactions, no
default-value backfill needed (existing rows get `NULL`, meaning "unknown," which is correct — CE
migration never populated this field before now either).

## 5. On-screen verification

Standing caveat, same as every other Library/editor change in this backlog: no computer-use
available this session. The Issue Properties field placement and Library sort/group toolbar entries
need a manual click-through pass before being marked verified.
