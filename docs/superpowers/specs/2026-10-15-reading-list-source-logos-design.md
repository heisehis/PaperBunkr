# Reading List source marks

**Date:** 2026-10-15
**Status:** Approved and implemented
**Phase:** 2 of the ordered Reading Lists / Continuity improvements

## Goal

Make a source-linked Reading List's provider recognizable at a glance without changing the Reading Lists layout or hiding its source name.

## Repository findings

- `ReadingList.Source` stores source keys; `ReadingListSourceRegistry` provides stable display names for seven built-in sources.
- The Reading List detail view model already exposes the source name for arc-linked lists.
- Local service marks already exist in `Assets/Marks/Services` for ComicVine and Metron. Metron-curated Reading Lists use the same provider identity as Metron arcs.
- The asset source inventory records no usable service logo for CBRO, ReadingOrders.com, or ReadThingsRight. No ComicArc mark is present either.
- ComicRack CE does not provide a provider-logo behavior for its native reading lists; this is a PaperBunkr-only enhancement.

## Decisions

- Show a bundled source mark beside the readable source name in the Reading List detail header.
- Keep the name visible for accessibility, narrow layouts, and mark-loading failures.
- Reuse the Metron mark for both `Metron` and `MetronLists` source keys.
- Use only a source's actual bundled brand mark. Keep sources without a found mark text-only until an appropriate asset is found; do not invent a logo or substitute a publisher icon.
- Keep the mark beside the source name in the detail header; do not add marks to Reading Lists cards, rows, or navigation.

## Asset inventory

| Built-in source | Mark state |
|---|---|
| ComicVine | Existing bundled mark |
| Metron | Existing bundled mark |
| Metron reading lists | Reuse Metron mark |
| Comic Book Reading Orders (CBRO) | Missing; find a suitable source asset |
| ComicArc | Missing; find a suitable source asset |
| ReadingOrders.com | Missing; find a suitable source asset |
| ReadThingsRight | Missing; find a suitable source asset |

When a missing source's asset is found, record its provenance/license/trademark note in `Assets/Marks/SOURCES.md` and use the same packaged-resource conventions as existing service marks. Until then, its source name remains visible without a mark.

## UI behavior

Place the mark next to the existing “via <source>” identity in the Reading List detail header. Preserve current source labels, including the fallback for unknown source keys. The mark is decorative; accessible naming comes from the visible text. No remote requests, credentials, or logo downloads occur while a list is viewed.

## Testing

- ComicVine and Metron source keys resolve their bundled marks.
- Metron-curated lists resolve the Metron mark.
- Missing and unknown source keys render readable text without a broken-image placeholder.
- Detail-header layout remains usable when the mark is unavailable or the source name is long.
- Existing Reading Lists card and navigation views remain unchanged.

## Out of scope

Automatic Reading List tags; event metadata and artwork; logos for publishers or trackers; browsing or downloading logos at runtime; changing source names or source registry keys.

