# Publisher logos — user icon folder, publisher gaps, fill from siblings — design

Date: 2026-10-04. Status: **built, uncommitted.** Prompted by "I dropped ~257 icons but only see the original 12".
Builds on `2026-09-25-publisher-icons-and-reader-textures-design.md` (the bundled CE pack) and `2026-08-28-brand-metadata-iconography-design.md`.

**CE parity:** the filename rules are CE's (`#`/`,` aliases, `(1977-2004)` / `(1990)` / `(2024_12)` eras, `map.ini`), reused unchanged for the user folder.

## What was actually wrong
Nothing in the resolver. All 807 name keys in the bundled pack (`Assets/Icons/Publishers`, byte-identical to CE's 751 files bar one rename) resolve to an image.
The library simply has 16 distinct publishers: 11 curated SVGs + Webtoon (CE raster) show a logo; Penguin Group, Harry N. Abrams and Games Workshop have no logo anywhere;
**441 of ~4,050 issues have no publisher at all** (mostly manhwa/manga scans with no ComicInfo publisher, plus a few comic series). Icons for publishers the library does not contain have nothing to attach to.
Also: the bundled pack is compiled into the app, and the app had **no** runtime folder for user icons.

## 1. User icon folder
- `%APPDATA%\Paperbunkr\publisher-icons` (`PublisherIconFolder`), read **recursively** at startup and on **Reload icons**. PNG / JPG / JPEG (SVG is not a user format - the SVG renderer is asset-only).
- Indexed with the same `PublisherIconIndex` rules as the bundled pack (aliases, eras, `map.ini` in that folder).
- **Precedence: user icons first** - before the era raster, the curated SVG and the CE raster - on the reasoning that an icon you put there is one you want to see.
- Loaded by path (a rooted path is opened directly, never through `Uri`, so a `#` in an alias filename is not read as a fragment). `PublisherIconBitmaps` memoises by path, so replacing a file's content needs a restart.
- Library Health > Review > **Publisher logos**: the path, a count, *Open folder* (creates it first), *Reload icons*.

## 2. Publisher gaps (Library Health, on demand)
`PublisherCoverage.Scan` (publisher = `Issue.Publisher`, else `Series.Publisher`): publishers with no logo (name, issues; most first), issues with no publisher, and per series the
publisher its *other* issues name (the most common; **a tie is ambiguous and is not suggested**).

## 3. Fill missing publishers from the rest of the series
`PublisherCoverage.FillFromSiblings`: blank issues of a series take the suggested publisher; never overwrites; skips ties and series with nothing to copy; sets `Issue.PublisherEntityId` through
`PublisherResolver.GetOrCreate`. Confirmed first; **database only - files are not rewritten** (metadata write-back stays its own switch). Series with no publisher anywhere are listed for the bulk editor.

## 4. Letter chips for three publishers
Alias rows (Penguin Random House, Abrams, Games Workshop) so they get a coloured letter chip instead of plain text. **These are not logos** - no real brand artwork was invented; drop the real files in the user folder.

## Not done / out of scope
- A fill for series with no publisher anywhere (manhwa/manga scans): there is nothing local to infer from. Setting `Publisher` from a tracker match (AniList / MangaBaka) is a separate piece.
- SVG user icons; watching the folder for changes (use *Reload icons*); an in-app icon picker.
