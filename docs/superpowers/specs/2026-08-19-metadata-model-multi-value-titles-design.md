# Metadata Model: Multi-Value Series Titles

**Date:** 2026-08-19
**Status:** Approved, implemented

## Context

Second item from the same architecture-review roadmap as `2026-08-19-metadata-model-reading-status-
design.md`. Real, currently-missing gap the review identified: `Series.Name` is a single string, so
searching a native-script title (e.g. `進撃の巨人`) never finds a series whose primary name is the
localized title (`Attack on Titan`), and vice versa. No CE precedent (CE never modeled alternate
titles at all) - same "deliberate new feature" footing as `ReadingStatus`.

## Design

Deliberately the leanest version of the reviewed proposal's `Title` value object, per that same
review's own over-engineering finding against wrapping every title in a 5-dimension object
(value/language/script/trait/source): `SeriesTitle { Id, SeriesId, Value, Type }`, where `Type` is
`SeriesTitleType` (`Native`, `Romanized`, `Localized`) - deliberately provider-neutral, not AniList's
own `romaji`/`english`/`native` vocabulary, matching this codebase's standing rule that provider
terminology gets mapped, never copied directly into the domain.

`Series.Name` stays the single primary/sort title everywhere it already is - Library grid, sort,
Detail screen. `SeriesTitle` rows are purely additive alternates; nothing that already reads
`Series.Name` needed to change. `Series.Titles` cascade-deletes with the series, same shape as
`TrackingLinks`/`Categories`.

### Search

Wired into `LibraryScreenViewModel.MatchesSearch`'s `Series` and default (`All`) modes only - the
other five CE-parity modes (Writer/Artists/Descriptive/File/Catalog) stay untouched, since alternate
titles aren't a CE field. `LoadFromDatabase` gained `.Include(s => s.Titles)` alongside its existing
Issues/Categories/TrackingLinks includes.

### No standalone editor yet

Deliberately no "add a title" UI in this pass - an entity nothing can write to would be exactly the
speculative-dead-code pattern `IMetadataProvider`'s own doc comment warns against. The real writer is
the AniList search-and-link flow (docs/superpowers/specs/2026-08-19-metadata-model-anilist-search-
and-link-design.md, same session): linking a series to an AniList match populates `SeriesTitle` rows
from AniList's `romaji`/`english`/`native` title fields. A manual add/remove editor can follow later
if a real need shows up - same "ported early, wired up by the next real consumer" precedent as
`CursorList<T>` before Browse History needed it.

## Testing

`LibraryScreenViewModelTests.SearchQuery_MatchesAlternateSeriesTitle`: a series named "Attack on
Titan" with a `Native` `SeriesTitle` row of "進撃の巨人" is found by searching either string, in both
`SearchMode.All` and `SearchMode.Series`. Full suite (74 `LibraryScreenViewModelTests`, 668
`Paperbunkr.App.Tests` overall) passes.
