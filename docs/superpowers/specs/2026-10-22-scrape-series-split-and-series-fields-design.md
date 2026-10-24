# Scraping: split mismatched comics out of a series, and fill series-level fields

Date: 2026-10-22. Follows the Scraper & Organizer A/B/C specs (2026-10-21). Decided in a grilling round (Q1-Q7, all recommendations taken).

## Problem
- Paperbunkr's `Series` is one row shared by every comic in it; ComicRack CE's "Series" is per-book text. The scraper copied CE's per-book write
  onto the shared row (`tracked.Series.Name = volume.Name`), so matching one comic to a different source series renamed the whole library series:
  "Secret Wars" and "Secret Wars 2099" stayed bundled, and matching the Halloween Special renamed all of "The Amazing Spider-Man".
- A series keeps one provider link per source (`ComicMetadataExternalId`, EntityKind Series); the second, different match overwrote the first.
- The scraper writes only Status and Summary at series level; Publisher, Genre, Sort Name, Imprint and Age Rating exist on `Series` (from the manga
  work) but were never filled for comics.

Facts (survey 2026-10-22): a rescan never moves a known file between series (only "Resync Series from File" does, by ComicInfo `<Series>`, which
write-back takes from `Series.Name`); no split/move function exists; `SeriesMergeHelper.MergeInto` drops series links, tags and titles.

## Decisions
1. **Split (Q1):** applying a match checks the comic's series' link for that source. A different source series moves the comic out instead of
   renaming. A series with no link takes the first match's link.
2. **Where it goes (Q2):** the library series already linked to the matched source series, else one with exactly that name, else a new series that
   copies content type, reading mode, reader settings and embedded-sync from the original and takes the source's name and series fields.
3. **Rename only when the match agrees (Q3):** the series' link equals the match, or this is its first link. The name before the first rename is
   kept as an alternate title (`SeriesTitleType.BeforeScrape`, appended).
4. **Existing mixed series (Q4):** "Restore name from before scraping" on a series that has a `BeforeScrape` title; re-scraping a mixed series
   splits it under rule 1. No library-wide scan (we never stored per-comic source series).
5. **Merging back (Q5):** a manual merge marks the target "keep together" (`KeepTogether` in `Series.LockedFields`); its comics are never split
   and its link is never overwritten. "Let scrapes split this series" clears it. Merge now carries the source's provider links (where the target
   has none for that source), alternate titles and tags.
6. **Series fields (Q6):** Publisher and Imprint (both sources), Genre, Sort Name and Age Rating (Metron) are written to the series under the
   existing field toggles and "Overwrite existing", never over a locked field (`MangaFieldLocks`), and only by a match that owns the series (rule 3).
7. **Status and Summary (Q7):** unchanged (fill when blank / Unknown).

## Testing
Data tests: split to a linked series, to a same-name series, to a new one (fields copied); rename only on agreement; `BeforeScrape` kept and
restored; keep-together never splits nor relinks; merge carries links/titles/tags and sets keep-together; series fields fill per toggles and
locks; dry-run shows the move and writes nothing. App: menu items appear only when they apply.
