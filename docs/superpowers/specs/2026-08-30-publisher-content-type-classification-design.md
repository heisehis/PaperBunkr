# Publisher-based ContentType classification

## Problem

`Series.ContentType` (Comic/Manga/Manhua/Manhwa/Unknown) is currently classified at scan time from
two signals, in order: the embedded ComicInfo `Manga` field, then a `LanguageISO` heuristic
(docs/superpowers/specs/2026-08-23-language-iso-content-type-heuristic-design.md). Neither signal
is present for a large share of real-world files — no embedded metadata at all, or embedded
metadata missing `LanguageISO`. `Issue.Publisher` is a third, independent signal: publishers like
Marvel/DC/Boom! are unambiguously Western comics, and Viz/Shueisha/Square Enix are unambiguously
Japanese manga. This spec adds publisher as a third classification step, plus a periodic sweep to
retroactively classify series that predate this feature or were scanned before publisher data was
available.

CE has no equivalent — its own classification is limited to the flat `Manga` yes/no field
(docs/onboarding.md §6). This is a deliberate Paperbunkr-only extension, not CE parity.

## Non-goals

- No manual "Verify & Repair Content Type" Preferences command (unlike the just-shipped cover
  Verify/Repair pair) — scan-time + periodic sweep cover the need.
- No UI for editing the publisher→ContentType lookup table. It's a static, code-only list for v1,
  extendable by a future developer session the same way `LanguageIsoClassifier`'s table is.
- No attempt to reliably distinguish Manhua from Manhwa from publisher alone beyond a thin starter
  list — `LanguageIsoClassifier`'s `zh`/`ko` split remains the primary signal for those two
  categories; publisher only helps for the small set of unambiguous cases.

## Design

### `PublisherContentTypeClassifier`

New static class, `src/Paperbunkr.Data/Metadata/PublisherContentTypeClassifier.cs`, same shape as
the existing `LanguageIsoClassifier`:

```csharp
public static bool TryClassify(string? publisher, out ContentType contentType, out ReadingMode readingMode)
```

Backed by an ordered list of `(string Key, ContentType ContentType, ReadingMode ReadingMode)`.
Matching is case-insensitive `Contains` (publisher strings vary in the wild — "Viz Media",
"VIZ Media LLC", "Shueisha Inc." — and a `Contains` match against a reasonably-specific key like
"Viz Media" or "DC Comics" catches all of them without needing every literal variant listed).
Returns `false` (and leaves the out params at their defaults) when no key matches.

Reading-mode mapping matches `LanguageIsoClassifier`'s existing convention exactly, so the two
classifiers never disagree on what a given `ContentType` implies:

| ContentType | ReadingMode |
|---|---|
| Comic | LeftToRight |
| Manga | RightToLeft |
| Manhua | Webtoon |
| Manhwa | Webtoon |

Starter lookup table (all easily extended later — this is the actual data, not just an example):

- **Comic:** Marvel, DC Comics, Image Comics, Boom! Studios, IDW, Valiant, Dynamite,
  Archie Comics, Oni Press, Vertigo, WildStorm, AfterShock, Black Mask, Top Cow
- **Manga:** Viz, Viz Media, Shueisha, Shogakukan, Kodansha, Square Enix, Kadokawa, Seven Seas,
  Vertical Comics, Yen Press, Denpa, One Peace Books
- **Manhwa:** WEBTOON, LINE Webtoon, Lezhin, Ize Press, D&C Media, Redice Studio
- **Manhua:** Kuaikan Manhua, Bilibili Comics

Deliberately excluded (ambiguous, would risk a confident-looking wrong answer): Dark Horse
(publishes both Western comics and licensed manga), Tapas and Kakao Piccoma (mixed-content
platforms spanning manhwa/manhua/webcomics, not a single ContentType).

Keys are picked to be specific enough that plain `Contains` doesn't false-positive on unrelated
publisher strings (e.g. "DC Comics" rather than bare "DC").

### Scan-time integration

`LibraryFolderScanner.ScanAllAsync` ([LibraryFolderScanner.cs:179-211](../../../src/Paperbunkr.App/Services/LibraryFolderScanner.cs)) already
runs a two-step classification chain, both steps guarded by `isNewSeries` (never touches a series
that already exists, protecting a user's manual edit or an earlier scan/migration result — see
that method's own doc comment for the full rationale). Publisher slots in as a third step, same
guard, same position in the per-issue `if`/`else if` chain — i.e. it only ever runs for the first
issue attached to a brand-new series, identical granularity to the two existing steps:

1. Embedded `Manga` field, if present and not `Unknown` — wins outright (deliberate, not inferred)
2. **New:** `PublisherContentTypeClassifier.TryClassify(issue.Publisher, ...)` — falls through here
   when the `Manga` field is absent/`Unknown`
3. `LanguageIsoClassifier.TryClassify(issue.LanguageISO, ...)` — falls through here when publisher
   doesn't match either

`issue.Publisher` is already populated by this point in the loop (`CeLibraryMigrator.MapStoryFields`
runs first, line 181).

### Periodic sweep

Mirrors the shape of the just-shipped periodic cover-verification sweep
(`MainViewModel.PeriodicCoverVerificationAsync`, `AppSettings.LastCoverVerificationUtc`):

- New `AppSettings.LastContentTypeSweepUtc` column (nullable `DateTime?`) + EF migration.
- New pure function `ShouldRunContentTypeSweep(DateTime? lastRunUtc, DateTime nowUtc) : bool`,
  7-day interval, unit-testable without waiting on real elapsed time — same contract as
  `ShouldRunCoverVerification`.
- New `MainViewModel.PeriodicContentTypeSweepAsync`, invoked alongside the existing cover-sweep
  call at startup. Silent by design (no toast). The timestamp only advances on full completion, so
  an interrupted pass retries next launch.
- Sweep body: query all `Series` where `ContentType == ContentType.Unknown`, `Include(Issues)`.
  For each, iterate its issues in existing order and take the first `Issue.Publisher` that
  `PublisherContentTypeClassifier.TryClassify` resolves confidently; apply that `ContentType`/
  `ReadingMode` to the series. Series where no issue's publisher matches are left untouched
  (still `Unknown`, still visible in Needs Review).

This is what actually fixes libraries scanned before this feature existed, or files whose
`Publisher` was populated by a later `SyncMetadataAsync` run — the scan-time hook only ever helps
issues discovered *after* this ships.

### Why no separate "guessed" flag or Needs Review interaction

`NeedsReviewViewModel.RefreshContentTypeItems` already surfaces every series with
`ContentType == Unknown` as a review item — that's the entire mechanism, no separate flag tracks
"guessed vs. deliberate." A series the publisher classifier resolves (at scan time or via the
sweep) simply stops being `Unknown` and falls out of that query for free, identical to how the
already-shipped `LanguageIsoClassifier` step behaves today. No new design needed here.

## Testing

- `PublisherContentTypeClassifierTests` — each starter-list key resolves to the right
  `ContentType`/`ReadingMode` pair (including realistic variants like "VIZ Media LLC"), an unknown
  publisher returns `false`, and a couple of deliberately-excluded ambiguous names (Dark Horse,
  Tapas) also return `false`.
- Extend `LibraryFolderScannerTests` with a case where `Manga` is absent/`Unknown`, `LanguageISO`
  is absent, but `Publisher` matches — asserts the series lands on the classifier's `ContentType`/
  `ReadingMode`, and that an *existing* series is never touched by a later scan even when its first
  new issue's publisher would otherwise match (guard parity with the existing two steps).
- `ShouldRunContentTypeSweepTests` — same shape as the existing cover-verification interval tests
  (never run yet → true; run recently → false; run >7 days ago → true).
- `MainViewModelTests` — periodic sweep fires alongside cover verification and advances
  `LastContentTypeSweepUtc` only on completion, mirroring the existing cover-sweep test.
