# Scanner credits from file names (piece D)

Date: 2026-10-11. Part of [the CE preset audit](2026-10-04-ce-preset-audit-overview-design.md). Decision: overview Q9 (built-in lists plus the user's additions; reimplement, don't copy).

## What the source does

ComicRack's "Scan Information From Filename" script reads a book's file name, takes the **last** bracketed piece that is not on a blacklist of noise (formats, ratings, months, languages, "of 4", variants...)
and is not already a value of one of the book's own fields, falls back to a list of known scanner names, and adds the result to the book's Scan Information (sorted, comma separated). It ships a 94-name
scanner list and a 67-entry regex blacklist, with no licence.

## What we had

`Issue.ScanInformation` is an editable field, sortable and groupable, with a smart-list field. Nothing ever filled it from a file name.

## Design

- **`ScanCreditCatalog`** (`Paperbunkr.Data/Metadata/ScanCredits`): known scan/release group names (plain data) and the words that mean "not a scanner". The word list and the matching rules are **our own**, written
  from the behaviour above: a bracket is noise when every word in it is a noise word, a number, a size (`1280px`) or a rating (`17+`, `MA15+`); months come from the invariant culture and languages from a short list of names and codes.
- **`ScanCreditSettings`**: the user's additions (more scanner names, more ignored words) in `scan-credits.json` next to the database, like `keymap.json`. No database column, so no migration. A missing or broken file is "no additions".
- **`ScanCreditDetector`**: bracketed pieces `(...)` / `[...]` and the tail after a final `__` are candidates, read from the end. A candidate is a credit when, after trimming noise words from its ends, at least two characters with a letter remain,
  and it does not repeat something the book says about itself (any text field, the entries of its list fields, anything in parentheses in its series name or titles). A known group name is kept whole. With no usable bracket, a known group name found as a whole word in the file name is used.
  Nothing is guessed: no candidate, no result (the script wrote "Unknown"; we do not).
- **Bulk action "Find scanner from file name"** (Library menu, under *Tidy up*): adds the credit to Scan Information beside what is there, sorted, unless it is already listed (ignoring case). One undo step, the usual file write-back, a toast.
  No "Scanner:" prefix (the script's default): the field is already Scan Information.
- **Preferences → Organize & Scrape → Filters & names → Scanner credits**: two boxes, one entry per line, saved by the page's own Save. Findable from Preferences search.

## Non-goals

Running at import time (it is a deliberate action, as in ComicRack); the script's "Unknown" marker and tag prefix; editing the built-in lists (they can only be added to).

## Testing

Detector cases for the usual release-name shapes, noise-only names, known names with and without brackets, own-field exclusion, user additions; merge rules; the settings file round trip and a broken file; the action (adds, skips, undo, user words), the menu entry, and the Preferences save/load. Not on screen.
