# Same-X-different-Y, Complete Count, and "First page is an ad" (piece E)

Date: 2026-10-11. Part of [the CE preset audit](2026-10-04-ce-preset-audit-overview-design.md). Decision: overview Q10 (a smart-list condition, not a Library Health check).

## 1. `SameXDifferentY` smart-list condition

### What the source does (`SameXDifferentY.py`, a ComicRack script; 9 of the 154 supplied lists use it)

Arguments: **X** (a `|`-separated list of attributes) and **Y** (another list, may be empty). It receives the books the earlier matchers kept, groups them by the values of the X attributes, drops groups of one,
and returns every book of each remaining group: all of them when Y is empty (so "same X" = duplicates), otherwise only the groups where, for some Y attribute, two books differ. An attribute name is tried as a
ComicRack book property first and as a custom value second; a book with none of the X attributes joins no group. A missing custom value reads as the text "None" on the Y side and adds nothing to the key on the X side.
Examples in the files: `comicvine_issue|Format` with no Y (same issue scraped twice in one format), `comicvine_volume` with Y `Format` (a series whose books disagree about their format), `Series|Volume|Format|Number` with Y `comicvine_volume`.

### Design

- **Field `SameXDifferentY`** (appended to `SmartListField`; special-cased in `SmartListQueryBuilder`). `Value` is X, `Value2` is Y, both `|`-separated. `Is` = in a flagged group, `IsNot` / the Not flag = not.
- **Attribute names** resolve, ignoring case, to a smart-list field (by its enum name or its label: `Series`, `Format`, `Volume`, `Number`, `Publisher`, ...) read as text, otherwise to a custom value of that name. A name that is neither is ignored, as in the script.
- **Scope: the whole library.** CE ran the script over the books the earlier rules in its list had kept. In 8 of the 9 supplied uses it is the first rule, where "the books so far" is the whole library, and that is what the condition uses (like `Duplicate`). A list where the script is **not** first (the ninth use, inside a group that follows other rules) is imported with a
  warning that it now looks at the whole library (it may flag more books than ComicRack did) instead of being refused.
- The groups are computed once per snapshot, per condition, only when a list uses the field.
- **Registered** as the `SameXDifferentY` plugin matcher in `PluginMatcherRegistry.Default` (`MatchValue` is X, `MatchValue2` is Y).
- **Editor:** two text boxes ("same: Series|Volume" and "but different: Format"); the operator picker is hidden (Not negates).

## 2. "Complete Count" bulk action (`CompleteCount.py`, "Complete series count with the last number of the series")

For the selected books whose **series is marked Completed**, set the book's Count from the series' highest issue number. Faithful to the script, minus its accidents:

- A group is a series and volume (the script grouped by the `comicvine_volume` custom value, which not every library has).
- A number counts when it is a number below 1000 (the script's "out of numbering" guard); the highest of them is the group's max.
- If the max is above 30, "numbering jumps" are looked for: between neighbouring numbers, a step of more than 12 issues per month of publication (`|Δnumber| / (|Δmonth| + 1)` as whole-number division, months from year*12 + month) marks a jump; the smallest number before a jump is the group's jump point.
  Books numbered at or before the jump point get the jump point as their Count; the others get the max.
- A book numbered more than 1 above the Count it would get (an "out of numbering" book, in practice one numbered 1000 or more) is given its own number. A book whose number is not a number gets the series' last number: the script sets
  that before it tries to read the number. A series with no numbered book at all is left alone (the script wrote 1 there through an error path, which is not carried over).
- Fileless placeholders for missing issues do not count towards a series' last number.
- One undo step, queued file write-back, a toast saying how many books changed ("Nothing to change" when none).
- Library menu entry for books and series, in the *Tidy up* submenu (as built: the five fix-ups from this audit share one submenu so the right-click menu stays short).

## 3. "First page is an ad" bulk action (`Move First Page to End`, `Remove Scanner Credits`)

The scripts mark page 1 an advertisement and page 2 the front cover (and Move First Page to End then moves page 1 to the end of the file's page list). We tag pages per issue (`IssuePage`) and never rewrite archives, so the action does the two tags and not the move:
page 0 becomes `Advertisement`, page 1 `Cover`. A confirm ("Mark the first page of 12 books as an ad and the second as the cover?") because the reader then skips the first page; it can be undone page by page from the reader or Library Health. Books with fewer than two pages are skipped.
There is no undo step (page tags are not part of the metadata history); the confirm says so.

## Non-goals

A pipeline model for set conditions that are not first in their list (the supplied lists never need it); reordering pages inside the archive; a Library Health check for mismatched formats.

## Testing

Pure helpers for the groups and the count; smart-list evaluation over a seeded library; the import mapping and the not-first warning; both actions' effects, undo (Complete Count) and refusal paths. Not on screen: the two menu entries and the condition row.
