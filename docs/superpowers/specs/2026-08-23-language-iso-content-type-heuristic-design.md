# LanguageISO content-type heuristic (§7 pipeline, slice 1)

**Date:** 2026-08-23
**Status:** Approved, pending implementation
**Backlog ref:** `docs/onboarding.md` §7 ("Content-type classification"), step 3 of its 4-step
pipeline. Explicitly Beta-scoped (`docs/alpha-todo.md`'s "Content-type classification & manga
metadata scraping (onboarding.md §7/§9)" entry) — this is the first slice of that pipeline, chosen
because it needs no new provider/network dependency, unlike steps 1-2 (tracker/AniList-driven,
scoped as a follow-on slice).

## Context

`Series.ContentType` (`Comic`/`Manga`/`Manhua`/`Manhwa`/`Unknown`) currently gets set only via (a)
one-time CE migration and (b) the scan-time detection shipped 2026-08-16
(`docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md`), which reads
embedded `ComicInfo.xml`'s `<Manga>` field (`MangaYesNo`) for brand-new series only, via
`CeLibraryMigrator.MapMangaField`. That field is often absent even when other embedded metadata
exists — CE's own `<LanguageISO>` field is a much more commonly populated signal for the same
underlying fact (a comic's origin), per §7's step 3: "no network match → local heuristic using
`LanguageISO` (already a populated CE field, free — `ja→Manga`, `ko→Manhwa`, `zh→Manhua`)."

Confirmed against CE's real source (`_reference/ComicRackCE/ComicRack.Engine/ComicBook.cs:2933`,
`GetIsoCulture`) rather than assumed, per this project's standing rule: `LanguageISO` is a
free-form string resolved via `CultureInfo`, lowercased before lookup — not a fixed enum, and not
guaranteed to be a bare two-letter code (`"ja-JP"`, `"zh-Hant"`, `"zh-CN"` are all valid values a
real file might carry). Matching must normalize via `CultureInfo`, not exact string comparison.

**Real-world reading-direction correction made during design:** the existing 2026-08-16 feature's
"default new manga-family classification to RightToLeft" convention does not hold for Manhua/Manhwa
— Korean (manhwa) and Chinese (manhua) comics are predominantly left-to-right/Western-paged in
convention, and modern digital manhwa/manhua specifically is overwhelmingly published in
edge-to-edge vertical-scroll (webtoon) format, unlike paged Japanese manga. Per user direction, this
heuristic's ReadingMode defaults differ by origin rather than reusing manga's RightToLeft default
uniformly.

## Design

### `LanguageIsoClassifier` (new, `src/Paperbunkr.Data/Metadata/LanguageIsoClassifier.cs`)

Static class in the `Paperbunkr.Data.Metadata` namespace — same home as `MetadataLinkResolver`,
since this is step 3 of the same §7 pipeline that class serves step 2 of.

```csharp
public static bool TryClassify(string? languageIso, out ContentType contentType, out ReadingMode readingMode)
```

Normalizes `languageIso` via `CultureInfo.GetCultureInfo(languageIso).TwoLetterISOLanguageName`,
catching `CultureNotFoundException` (blank, null, or unparseable input) and returning `false` with
`contentType`/`readingMode` left at their default values. Mapping table:

| Two-letter ISO code | `ContentType` | `ReadingMode` |
|---|---|---|
| `ja` | `Manga` | `RightToLeft` |
| `ko` | `Manhwa` | `Webtoon` |
| `zh` | `Manhua` | `Webtoon` |
| anything else | — | — (method returns `false`) |

`Webtoon` (not `VerticalContinuous`) chosen per user direction: edge-to-edge vertical scroll with
no visible page gap matches how most digital manhwa/manhua is actually formatted and published
today, versus `VerticalContinuous`'s visible-gap convention (see `ReadingMode.cs`'s own doc comment
distinguishing the two).

### Scanner wiring (`src/Paperbunkr.App/Services/LibraryFolderScanner.cs`)

Directly beside the existing embedded-`<Manga>` block (currently lines 153-158), as an `else if` on
the same `isNewSeries` guard:

```csharp
if (isNewSeries && embeddedInfo.Manga != MangaYesNo.Unknown)
{
    var (contentType, readingMode) = CeLibraryMigrator.MapMangaField(embeddedInfo.Manga);
    series.ContentType = contentType;
    series.ReadingMode = readingMode;
}
else if (isNewSeries && LanguageIsoClassifier.TryClassify(issue.LanguageISO, out var languageContentType, out var languageReadingMode))
{
    series.ContentType = languageContentType;
    series.ReadingMode = languageReadingMode;
}
```

Precedence: the embedded `Manga` field always wins when present and usable (`MangaYesNo` is a
direct, deliberate classification by whoever wrote the `ComicInfo.xml`; `LanguageISO` is inferred).
The language heuristic only fires when `Manga` was `Unknown`/absent. Same `isNewSeries` guard as the
2026-08-16 feature, for the same reason documented there: `ContentType`'s `Unknown` sentinel value
is itself a valid deliberate user choice (selectable in Bulk Edit/the series picker), so "was this
series new to this scan" — not "is `ContentType` still `Unknown`" — is the only guard that can't
silently clobber a real prior choice on a later re-scan.

`issue.LanguageISO` is already populated by the `CeLibraryMigrator.MapStoryFields(embeddedInfo,
issue)` call immediately above this block, so this heuristic only ever fires for files with embedded
`ComicInfo.xml` present — same practical scope as the field it falls back from. Filename-only
parsing (`ComicNameInfo`) carries no language signal, so a file with no embedded metadata at all
correctly falls through to `Unknown`, same as today.

## Testing

**Direct unit tests** for `LanguageIsoClassifier`, `Theory`-based, mirroring
`CeLibraryMigratorTests.MapMangaField_MatchesDocsSection6Table`'s existing pattern:
- `ja` → `Manga`/`RightToLeft`; `ko` → `Manhwa`/`Webtoon`; `zh` → `Manhua`/`Webtoon`
- Culture variants: `ja-JP`, `zh-Hant`, `zh-Hans`, `zh-CN`, `ko-KR` all normalize to the same result
  as their bare two-letter code
- Negative cases: `en` (unmapped real language), `null`, `""`, and a garbage string (e.g.
  `"not-a-culture"`) all return `false` and leave the `out` parameters at their defaults

**Scanner-level tests** in `LibraryFolderScannerTests.cs`, mirroring the existing
`ScanAllAsync_EmbeddedMangaYesAndRightToLeft_*` tests:
- A new series with embedded `LanguageISO=ja` and no `<Manga>` tag → `ContentType.Manga` /
  `ReadingMode.RightToLeft`
- A new series with embedded `LanguageISO=ko` → `ContentType.Manhwa` / `ReadingMode.Webtoon`
- Precedence: a new series with both an embedded `<Manga>` tag *and* a disagreeing `LanguageISO`
  (e.g. `Manga=No` + `LanguageISO=ja`) → the `Manga` field's mapping wins, `LanguageISO` is ignored
- An *existing* series (already `Comic`) scanned again with a new issue carrying `LanguageISO=ja` →
  `ContentType` is untouched (mirrors `ScanAllAsync_EmbeddedManga_ExistingSeries_NeverOverwritesItsContentType`)

## Explicitly out of scope

- `SyncMetadataAsync` re-detection — same reasoning as the 2026-08-16 spec's own "Explicitly out of
  scope" section: it already has a "never overwrite an existing issue" contract, and this heuristic
  doesn't change that boundary.
- No manual "detect from language" UI trigger — this slice is purely the scan-time fallback.
- Steps 1-2 of §7's full pipeline (existing `TrackingLink` / AniList-or-MangaUpdates search-and-confirm)
  are a separate, later slice — this spec covers step 3 (and by omission, step 4: `Unknown` when
  nothing matches) only.
