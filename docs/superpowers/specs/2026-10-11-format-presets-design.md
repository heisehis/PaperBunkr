# Format from names, and real reading orders as fixtures (piece F)

Date: 2026-10-11. Part of [the CE preset audit](2026-10-04-ce-preset-audit-overview-design.md). Decisions: overview Q2 (only the generic Format rules of the `.dat`) and Q3 (reading orders are fixtures, not bundled).

## What the source has

Of the 449 rulesets in the supplied Data Manager `.dat`, the generic ones are the `Formats` group: *Annual* when the series, title or file name contains "Annual"; *Director's Cut* when the series or title says so; and a cleanup that removes "Annual" from the series name once the format is Annual.
The workflow's "6.1 Auto tagged based on Filenames" lists do the same by hand for TPB, One-Shot and Black & White. The rest of the file is one person's series-to-franchise table.

## What we had

Format is proposed from what the file-name parser recognises and can be set by hand or from a provider. Nothing sets it from the series name or title.

## Design

- **`FormatGuesser`** (`Paperbunkr.Data/Metadata`): an ordered list of `FormatRule(Format, Pattern)`, the starter presets: Director's Cut, Trade Paper Back, One Shot, Black & White, Annual, most specific first, matched as whole words, ignoring case, against the series name, the title and the file name (not its folders).
  The format names are ComicRack's own default vocabulary, already the Format field's suggestions. A book that already has a format, stored **or proposed**, gets no guess.
- **Bulk action "Guess format from name"** (Library menu, under *Tidy up*, for books and series): sets the format of the selected books that have none and whose names give one away. One undo step, file write-back, a toast.
- **Not carried over:** removing "Annual" from the series name. A series here is an entity, not a text field: renaming it per book would move the book to another series, which is what Merge series and the scanner's own folding are for.
- **Reading orders:** four of the supplied event reading orders (1 to 6 books) are test fixtures for the reading-list import, which must still treat them as reading lists now that a `.cbl` can also be a smart list. None is shipped in the app.

## Non-goals

A Data Manager `.dat` importer; user-editable format rules (the list is data, so that is a small step if wanted); bundling reading orders.

## Testing

Each rule with its spellings, look-alikes that must not match, rule order, the three names, "already has a format" (stored and proposed), every rule's format being a known ComicRack format name; the action (sets, skips, undo, nothing found); the real reading-order files (kind, order across series, the name inside the file).
