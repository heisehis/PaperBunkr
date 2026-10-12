# "Has a proposed value" smart-list field and "Commit proposed values" (piece C)

Date: 2026-10-11. Part of [the CE preset audit](2026-10-04-ce-preset-audit-overview-design.md). Decisions: overview Q8 (field **and** bulk action).

## What the source does

`Proposed Values.py` (a ComicRack script, the `findProposedValues` plugin matcher in 2 of the supplied smart lists) returns the books where, for any of `series, volume, count, format, number, title, year`
(or the comma list it is given; none valid = all), CE has a **proposed** value (`ComicBook.Proposed*`, parsed from the file name) while the real field is empty. The workflow list
"1.1 Commit Proposed Values" feeds those books to ComicRack's own *Commit Proposed Values* command, which copies the proposed value into the real field.

## What we have

Paperbunkr's replacement for `Proposed*` is `MetadataProposal` (`MetadataProposalField` Title, Format, Volume, Number, Count, Year, ...). A filename-inferred value is stored as a proposal, and the stored `Issue` field stays null;
`Effective*()` reads the **Accepted** proposal when the stored field is empty. Under the default Automatic policy the scanner creates them already Accepted; under Prompt they wait as Pending in Needs Review.

## Design

- **"Proposed" means: an `Accepted` proposal for the field, with a non-empty value, on an issue whose stored field is empty.** Pending proposals are not counted: they are not in effect yet, and writing them out would skip the review
  the user chose. Series-scoped proposals and the `Series` field are out (a series proposal moves the issue between series; it is not a value to write into a field).
- **Smart-list field `HasProposedValue`** (appended to `SmartListField`; special-cased in `SmartListQueryBuilder` like `Duplicate`). Operator `Is` = has one, `IsNot` = has none; the usual per-condition Not also works.
  The condition's **value** is the field list: comma/semicolon separated names from `title, format, volume, number, count, year`; unknown names (including `series`) are ignored; no valid name = every field. Case-insensitive.
- **Plugin mapping.** `PluginMatcherRegistry.Default` registers `findProposedValues`: `MatchValue` becomes the field list. A CE list using it now imports with this condition instead of a warning.
- **Pure helper `ProposedValues`** (`Paperbunkr.Data/Metadata`): `ParseFields`, `Pending(issue, fields)` (the committable fields with their values), `Commit(issue, fields)` (writes the stored fields, returns how many it set; a Count/Year that does not parse is skipped).
  The smart-list evaluator and the action share it, so "matches the list" and "will be committed" cannot drift.
- **Bulk action "Commit proposed values"** in the Library menu for issues (as built: in the *Tidy up* submenu): acts on the selected books that have something to commit, as one undo step (`EditIssuesWithHistory`), queues the file write-back like any edit, and says
  what it did ("Committed proposed values on 12 books · Ctrl+Z to undo", or "Nothing to commit" when none had any). No confirm: it only fills empty fields and is undoable.
- **Editor.** The condition row shows the field-list box for `HasProposedValue` (the value box, with a hint) and hides the operator picker like Duplicate (a Not toggle remains).

## As built: the undo snapshot

The proposed values are read in their own query and applied to books loaded **without** their proposals. With the proposals loaded, `Effective*()` already reads the value, so the undo step's before/after snapshots (and the file write-back check) saw no change;
a test caught it. Every later action that reads proposals to decide what to write (Complete count, Guess format from name) follows the same two-read shape.

## Non-goals

Pending proposals; Series proposals; a Library Health queue; committing from the Needs Review screen (it has its own accept flow).

## Testing

Pure helper tests; smart-list evaluation over a seeded library (accepted vs pending vs stored-value cases, field lists, Not); the registry mapping; the action's effect, undo, and "nothing to commit"; the existing action-catalog tests still pass with the new entry.
Not on screen: the menu entry and the condition row.
