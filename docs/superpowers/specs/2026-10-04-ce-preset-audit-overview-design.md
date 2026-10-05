# CE preset audit — overview and decomposition

Date: 2026-10-04. Status: design approved in chat 2026-10-04; sub-specs A and B written, C–F still to write.

## Where this came from

The user supplied three archives gathered from Reddit while looking for a Data Manager preset: a personal `Enliqhtened.dat` (two versions),
10 Library Organizer `.lop` profiles (2.1.8 / 2.1.11), 96 comicbookreadingorders-derived reading-order `.cbl` files (5,701 books), 154 CE smart-list
`.cbl` files (a numbered "workflow"), `Proposed Values.py`, and a dozen Stonepaw/Xelloss IronPython scripts. The audit compared each against what the
app already has. Nothing in the archives is a generic preset; the value is in the formats and the ideas.

## Decomposition

Six independent pieces, each its own spec → plan → build. Order: **A → B → C → E → D → F**.

| | Piece | Spec |
|---|---|---|
| A | CE smart-list `.cbl` import, four series-level fields, `Published` date field, plugin-matcher mapping hook | [2026-10-04-smart-list-cbl-import-design.md](2026-10-04-smart-list-cbl-import-design.md) |
| B | Library Organizer `.lop` profile import | [2026-10-04-lop-profile-import-design.md](2026-10-04-lop-profile-import-design.md) |
| C | `HasProposedValue` smart-list field + bulk "Commit proposed values" | not written |
| E | `SameXDifferentY` smart-list condition; bulk "Complete Count" and "First page is an ad" | not written |
| D | Scanner-name list and filename-noise regexes (`ScanCreditCatalog`) | not written |
| F | Generic Format rules (Annual, Director's Cut) as starter presets; trimmed reading orders as test fixtures | not written |

C and E register entries in A's plugin-matcher hook, so A lands first.

## Decisions taken (the grilling round)

- One spec per piece. Native reimplementation only; no IronPython host.
- The `.dat`: ship only the generic Format rules (F). No Data Manager `.dat` importer; the franchise taxonomy is one person's data.
- Reading orders: not bundled (scraped from comicbookreadingorders.com, a redistribution question). A trimmed handful serve as fixtures. Users get them through the existing source and folder import.
- A smart-list file that uses a matcher we cannot evaluate is imported without that condition and an Activity Center warning names the list and the condition. It is never failed whole.
- All four numeric series-level matchers are added (max year, max count, max gap size, last number).
- Smart-list `.cbl` import goes through the existing `.cbl` import paths and is detected by content. Smart lists have no folders, so imports are flat.
- `.lop` base folders are kept; imported profiles open for review and never run or schedule themselves.
- Scanner/noise lists: built-in defaults plus the user's own additions; code is reimplemented from behavior, not copied.
- `SameXDifferentY` is a smart-list condition, not a Library Health check (the workflow uses it as a smart list in 9 places).

## Facts that changed the plan while researching (all verified against source)

1. CE has **eight** series-level matchers, not four: four numeric (the ones we add) and four date (last added / opened / released / published time). Only the numeric
   four are in scope. The files use `SmartListSeriesMaxNumbertMatcher` for "last number"; the typo is CE's own `XmlType`.
2. CE's `And` group is a **pipeline**: each matcher sees only the books the previous matchers kept (`MatcherSet.Match`). `ComicBookPluginMatcher` receives that narrowed list.
   Our engine evaluates one issue at a time, so a set-scoped condition (`SameXDifferentY`) cannot be a plain per-issue test. This is piece E's central design question,
   and the reason A only reserves the hook.
3. Five of the ten `.lop` profiles use `ExcludeMode="Only"`: organize **only** books that match the rules. Our profile has only "exclude when matched". Importing the rules without
   inverting them would move the wrong books. Piece B inverts by De Morgan and refuses to import a profile whose rules it cannot translate.
4. Our `Sanitizer` illegal-character table already equals the plugin default, and every supplied profile uses the default. A per-profile table is therefore **not** added (YAGNI);
   a profile that differs is reported. This drops the "new column and migration" mentioned in chat.
5. "Move First Page to End" is really three steps: page 1 becomes the cover, page 0 an ad, and the ad is moved to the end. Piece E builds the first two as a bulk action; the physical reorder
   is dropped because reordering would mean rewriting the archive.

## Not verified

None of this has been built. Every behavioral claim above comes from reading `_reference/ComicRackCE`, the supplied plugin source, and the app's current code, not from running anything.
