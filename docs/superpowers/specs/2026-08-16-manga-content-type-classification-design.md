# Manga/ContentType classification — editing & scan-time detection

**Date:** 2026-08-16
**Status:** Approved, pending implementation plan
**Backlog ref:** surfaced while scoping "pluggable sort/group strategies" (`docs/alpha-roadmap.md`'s
Library-browsing-extras sequence) — CE's `Manga` field turned out to already have a superior,
already-modeled home in Paperbunkr (`Series.ContentType`), just with no way to ever set it outside
CE migration. This spec closes that gap; sort/group work itself remains paused/separate.

## Context

CE's `Manga` field is a flat `MangaYesNo` (`Unknown`/`No`/`Yes`/`YesAndRightToLeft`) that conflates
origin/format classification with reading direction. Paperbunkr already made a better architectural
call here, documented in `ContentType.cs`'s own doc comment: a dedicated `Series.ContentType` enum
(`Comic`/`Manga`/`Manhua`/`Manhwa`/`Unknown`) distinguishing Japanese manga from Chinese manhua and
Korean manhwa — a real improvement CE never had — kept deliberately separate from `Series.ReadingMode`
(`LeftToRight`/`RightToLeft`/etc., already user-editable via the Reader's own toggle).

`CeLibraryMigrator.MapMangaField` (`src/Paperbunkr.Data/CeMigration/CeLibraryMigrator.cs:347-353`)
already establishes the exact mapping between CE's flat field and Paperbunkr's two-field split:

```csharp
public static (ContentType ContentType, ReadingMode ReadingMode) MapMangaField(MangaYesNo manga) => manga switch
{
    MangaYesNo.YesAndRightToLeft => (ContentType.Manga, ReadingMode.RightToLeft),
    MangaYesNo.Yes => (ContentType.Manga, ReadingMode.LeftToRight),
    MangaYesNo.No => (ContentType.Comic, ReadingMode.LeftToRight),
    _ => (ContentType.Unknown, ReadingMode.LeftToRight),
};
```

But this mapping only ever runs once, at one-time CE migration. Two real gaps follow from that:

1. **`ContentType` has zero user-facing edit UI anywhere** — not Bulk Edit, not Issue Properties, not
   anywhere else. A user classifying their own library (or correcting a migration guess) has no way
   to do it.
2. **The normal (non-migration) file scanner never reads `<Manga>` from `ComicInfo.xml` at all** —
   `LibraryFolderScanner.TryReadEmbeddedInfo` already returns CE's real `ComicInfo` object (via CE's
   own `IInfoStorage.LoadInfo`), whose `Manga` field is already populated by CE's XML
   deserialization — but nothing reads it. A freshly-scanned manga archive gets `ContentType.Comic`
   (the enum's default C# value — `Comic` is declared first) forever, with no correction path.

A related latent bug caught during design: since `ContentType`'s default value is `Comic`, not
`Unknown`, any logic gating on `ContentType == Unknown` to mean "not yet classified" is wrong for a
freshly-created `Series` — the correct gate is "was this series newly created in this operation,"
not "is its `ContentType` still the sentinel."

## 1. `BulkFieldDescriptor` gains `FieldKind.Enum`

`src/Paperbunkr.App/Models/BulkFieldDescriptor.cs`'s `FieldKind` enum (`Text`/`Boolean`/`Rating`)
gains `Enum`, plus an `Options` property (`IReadOnlyList<string>`) on the descriptor record for the
picker's choices — reusable by any future fixed-choice bulk field, not single-purpose to
`ContentType`. `Get`/`Set` need no signature change: they already operate as `Func<Issue, string?>`/
`Action<Issue, string?>`, and `ContentType` lives one hop away via the existing `Issue.Series`
navigation property:

```csharp
new("Content Type", Main, FieldKind.Enum, IsListField: false,
    i => i.Series.ContentType.ToString(),
    (i, v) => i.Series.ContentType = Enum.Parse<ContentType>(v ?? nameof(ContentType.Unknown)),
    Options: Enum.GetNames<ContentType>())
```

This is the **first** field in the bulk-edit registry where a selection of `Issue`s writes through
to each one's *owning* `Series` rather than the `Issue` itself — there's no existing precedent to
mirror (a prior assumption that `Series.Genre` already worked this way in Bulk Edit turned out to be
wrong; `Genre` reads/writes `Issue.Genre` directly, unrelated to `Series.Genre`). Confirmed
mechanically safe regardless: the existing per-issue bulk-save loop calling `Set` once per selected
issue naturally converges to "every touched series ends up at the new value," even when several
selected issues share a series (redundant-but-harmless repeated writes) or the selection spans
multiple series (each gets set once it's been touched). The one new UI need: since this can silently
reclassify series the user didn't explicitly multi-select (only issues), the Bulk Edit screen shows
"N series will be affected" before committing.

When the picked value is `Manga`/`Manhua`/`Manhwa`, a second conditional row appears for
`ReadingMode` (`LeftToRight`/`RightToLeft`), defaulting to `RightToLeft` — same `i.Series.ReadingMode`
reach-through, hidden entirely for `Comic`/`Unknown`. Per user direction: ContentType and ReadingMode
aren't fully independent in the UI — classifying something as manga-family is the natural moment to
also confirm its reading direction, mirroring why CE bundled them into one field in the first place,
just without conflating the two concepts the way CE did.

## 2. Series-level picker

New `MenuItem` "Set Content Type" as a sibling to the just-shipped "Show in Explorer" item, in the
same per-card `ContextMenu` already present across all 7 `LibraryScreen.axaml` view-mode templates
(docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md). Small flyout:
ContentType dropdown + the same conditional ReadingMode dropdown as §1, applied directly to the
right-clicked series.

## 3. "+ Add" manual-book flow

The same ContentType (+ conditional ReadingMode) pair added to the existing add-book flyout
(docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §2), applied to
the series the new placeholder issue resolves into (whether newly created or an existing match).

## 4. Scan-time detection

`LibraryFolderScanner.ScanAllAsync` (`src/Paperbunkr.App/Services/LibraryFolderScanner.cs`), right
after the existing `CeLibraryMigrator.MapStoryFields(embeddedInfo, issue)` call at line 113 — the
same function CE migration itself uses to apply embedded fields, already proven correct:

```csharp
if (isNewSeries && embeddedInfo is not null && embeddedInfo.Manga != MangaYesNo.Unknown)
{
    var (contentType, readingMode) = CeLibraryMigrator.MapMangaField(embeddedInfo.Manga);
    series.ContentType = contentType;
    series.ReadingMode = readingMode;
}
```

`isNewSeries` is a new local `bool`, set from the existing
`if (!seriesByName.TryGetValue(seriesName, out var series))` branch (currently inline, needs
capturing into a named variable) — guards against ever overwriting a value from a prior scan,
migration, or manual edit (§1/§2/§3) for a series that already exists, matching this file's
established "never clobber, embedded only wins into a gap" philosophy (`SyncMetadataAsync`'s
`onlyIfBlank: true` is the per-issue analog; "series is brand new" is the correct series-level
equivalent, not `ContentType == Unknown`, which is never true for a fresh `Series` per this spec's
Context section).

## Explicitly out of scope

`SyncMetadataAsync` (the "Sync Metadata" re-scan of already-linked issues) does **not** gain Manga
re-detection in this pass — it already has an explicit "never overwrite" contract for existing
issues, and retrofitting series-level Manga detection onto it raises the same "which of several
issues' embedded values wins" question CE migration's own mapping comment already flags as a
known simplification (`CeLibraryMigrator.cs:227-231`, "if they disagree, the first book's value
wins"). A full series-properties editor beyond this minimal ContentType/ReadingMode picker (per
prior user direction on the reveal-in-explorer/fileless-entries spec's own scoping) — Name, Publisher,
Genre, Summary, etc. stay unreachable as one editor, `ContentType` is a standalone addition to the
existing per-card context menu, not a new screen.
