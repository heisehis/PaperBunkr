# Publisher mark expansion and rating mark audit

**Status:** Draft for user review

**Date:** 2026-10-22

## Goal

Bundle and integrate all 121 collected MangaBaka publisher icons. In addition, add 50 western comic-publishing marks that are genuinely absent from Paperbunkr's complete bundled publisher asset set and are distinct from the MangaBaka publishers, including Bad Egg Publishing. Audit existing comic age-rating marks, add any missing marks from the ComicRack CE set, and map similar rating labels to the correct rating artwork.

## Current state

- `Assets/Marks/Publishers` contains 18 curated SVG marks.
- `Assets/Icons/Publishers` contains 751 ComicRack CE raster marks. A candidate only counts as missing after checking both sets and their naming/alias conventions.
- `Assets/Marks/AgeRatings` already contains all 8 ESRB marks.
- The app has no Australian age-rating mark assets. The ComicRack CE reference has 6 Australian marks: `G`, `PG`, `M`, `MA15+`, `R18+`, and `X18+`.
- `age-rating-aliases.tsv` distinguishes `Mature 17+` from `M`. The labels are ComicInfo text values, not a new rating-system field.
- The project already has a source and license log at `Assets/Marks/SOURCES.md`.
- The MangaBaka archive contains 121 unique logo files: 14 SVG, 57 PNG, 46 JPEG, and 4 WebP. Its manifest includes publisher IDs, names, source URLs, and downloaded filenames; the extra 500-language-filter crawl found no additional logo URLs.

## MangaBaka icon bundle

- Add all 121 collected MangaBaka icon originals to the app's bundled publisher asset set. Keep publisher IDs and original filenames as source identity and include a machine-readable manifest with the original URL and rights/provenance status for every file. MangaBaka's dataset license does not grant rights to third-party logo artwork; record each logo as third-party brand art unless a source-specific license is established.
- Integrate the icons into publisher resolution using their publisher names as aliases. Preserve the app's current user-icon, historical CE icon, and curated SVG behavior; after those existing sources, use a MangaBaka asset before the generic letter/text fallback.
- Preserve original SVG, PNG, and JPEG files. The current raster index handles PNG/JPEG and the curated mark pipeline handles SVG. Preserve the 4 original WebP files in the bundled source pack and include PNG rendering copies for runtime display, with the conversion recorded in the manifest; do not auto-trace or alter the visible artwork.
- Where an imported MangaBaka publisher is also covered by an existing app asset, retain the MangaBaka original in the bundle and provenance manifest, but use the existing curated or CE asset according to the precedence above. The icon collection is additive and does not replace existing publisher marks.

## Publisher roster

The following 50 western publishers are the acquisition target. They were selected from current western comics publisher catalogs and known independent publishers, then screened against the curated SVG directory, all 751 ComicRack CE icon filenames, and the MangaBaka archive names. This is a filename-level screen, not the final eligibility test: implementation must repeat the check using the app's actual publisher resolution and alias rules before adding each entry. A match discovered at that stage means the roster member is not eligible and must be replaced with another verified missing publisher to keep the delivered count at 50.

1. Bad Egg Publishing
2. Bad Idea
3. Absolute Comics
4. Artist Elite Publishing
5. Cave Pictures Publishing
6. CEX Publishing
7. Clover Press
8. Critical Entertainment
9. Crown Books
10. CSN Press
11. Good Trouble Comics
12. Graphitti Designs
13. Harper Alley
14. Headless Shakespeare Press
15. Holiday House
16. Insight Editions
17. IPI Comics
18. Kong Comics
19. Legends Comics
20. Loma Vista
21. Massive Publishing
22. Off Register Press
23. Opus Comics
24. Pow Pow Press
25. Prana Publishers
26. Prestel Publishing
27. PS Artbooks
28. REKCAH Comics
29. Renegade Game Studios
30. Rocketship Entertainment
31. Seven Stories Press
32. Silver Sprocket
33. Tripwire
34. Wake Entertainment
35. Warrant Publishing
36. Zombie Love Studios
37. Alien Books
38. American Mythology Productions
39. Blood Moon Comics
40. 2510 Press
41. Cat Head Comics
42. Golden Books
43. Graphic Universe
44. Hermes Press
45. Penthouse Comics
46. Storm King Productions
47. Strangers Publishing
48. The Nib
49. Zdarsco
50. Breaking Glass Comics

## Western-logo sourcing and app integration

- For the 50 additional western marks, prefer official publisher artwork; use Wikimedia Commons when it provides a suitable, well-attributed source. Keep source SVGs as SVGs and source PNG/JPEG files as rasters; do not auto-trace or invent replacement marks. The existing publisher icon index accepts PNG/JPEG raster files. If a suitable source exists only in an unsupported raster format, use an official or Commons PNG/JPEG rendition if available; otherwise record the blocker rather than claiming that mark is integrated.
- Record the source URL, copyright/license status, and any required attribution in `Assets/Marks/SOURCES.md`.
- Integrate SVGs into `Assets/Marks/Publishers` and source-faithful raster marks into the existing publisher icon pack, with canonical names and aliases so `MarkResolver` resolves them consistently. Avoid changing unrelated fallback behavior.
- Bad Egg Publishing must have a canonical publisher row and an app-resolvable mark.
- If an official/Commons logo cannot be obtained for a roster entry, document the blocker and do not silently substitute a text or letter mark while claiming 50 procured logos.

## Rating mark mapping

Keep the existing 8 ESRB assets unchanged. Add the 6 Australian assets using the CE reference PNGs or a suitable official source, recording provenance. Route these raster marks through the existing `MarkSpec.Raster` rendering path; no new image decoder is required. Map exact ComicInfo labels as follows:

| ComicInfo label | Rating mark |
| --- | --- |
| `Early Childhood` | ESRB Early Childhood |
| `Everyone` | ESRB Everyone |
| `Everyone 10+` | ESRB Everyone 10+ |
| `Kids to Adults` | ESRB Kids to Adults |
| `Teen` | ESRB Teen |
| `Mature 17+` | ESRB Mature 17+ |
| `Adults Only 18+` | ESRB Adults Only 18+ |
| `Rating Pending` | ESRB Rating Pending |
| `G` | Australia G |
| `PG` | Australia PG |
| `M` | Australia M |
| `MA15+` | Australia MA15+ |
| `R18+` | Australia R18+ |
| `X18+` | Australia X18+ |

The `Mature 17+` and `M` entries must remain distinct. Preserve the existing ComicInfo text values and do not add a rating-system field or prefix.

## Scope boundaries

- The previously collected MangaBaka logo archive remains the provenance source for the bundled MangaBaka icon set.
- All 121 MangaBaka archive icons are bundled separately from the 50 new western publisher marks; no MangaBaka logo counts toward the additional 50.
- No new rating-system selector, database field, or ComicInfo value is introduced.
- No publisher mark is auto-traced from a raster.
- No unrelated app behavior or visual redesign is included.

## Acceptance checks

- The completed publisher acquisition contains exactly 50 distinct marks absent from the baseline asset set, and includes Bad Egg Publishing.
- All 121 MangaBaka logos are present in the app's bundled publisher asset set with publisher-ID identity and provenance; MangaBaka logos do not reduce the separate 50-logo western target.
- Each mark has an app-resolvable canonical publisher entry and a provenance record.
- The six Australian rating values resolve to their respective Australian marks; existing ESRB values still resolve to their ESRB marks.
- `Mature 17+` resolves to ESRB Mature and `M` resolves to Australian M.
- Review the marks at their actual rendered app size for legibility, crop, transparency, and source fidelity; do not run the full test suite unless requested.

## Source context

- Comic Previews publisher catalog, used as one roster discovery source: https://comicpreviews.com/publishers
- Indie Comic Alliance publisher roster, used as an additional discovery source: https://indiecomicalliance.com/press/press-release-7-7-26/
- MangaBaka dataset terms, which distinguish dataset licensing from third-party logo rights: https://mangabaka.org/data/api
- Bad Egg Publishing official site: https://badegg.co/pages/about
- ComicInfo schema references are documented in `2026-08-28-brand-metadata-iconography-design.md` and the existing rating alias table.
