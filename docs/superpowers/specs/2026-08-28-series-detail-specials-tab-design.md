# Series Detail — Specials Tab

**Date:** 2026-08-28
**Status:** Approved, pending implementation
**Source doc:** Design session with Ehis (2026-08-28), following on from the Kavita comparison
research recorded in project memory (`event-section-planning` — Kavita comparison, 2026-08-28).
Kavita's own specials-detection behavior verified against its wiki
(`wiki.kavitareader.com/guides/scanner/managefiles`, `/guides/metadata/comics`) and its
`RelationKind` list (`wiki.kavitareader.com/guides/features/relationships`) rather than assumed.
CE's own vocabulary re-verified against `_reference/ComicRackCE/ComicRack/Output/DefaultLists.txt`
per the CLAUDE.md standing rule — CE has **no** Special/IsSpecial concept anywhere in
`ComicBook.cs`/`ComicInfo.cs` (confirmed by grep), so this whole feature is a deliberate
Kavita-inspired deviation, not CE parity, same footing as `MediaRelation` (Phase 3) before it.

## Context

Kavita's series detail page (screenshots: `localhost:5000/library/2/series/279`) shows a dedicated
**Specials** tab alongside Volumes/Issues/Related, holding one-shots, annuals, and other issues
that don't belong in the main numbered flow. Kavita determines membership three ways: no
volume/chapter parses from the filename at all, an explicit `SP##` filename marker, or a
recognized value in ComicInfo.xml's `Format` field. Of the three, only the last has a clean
Paperbunkr analog — Paperbunkr already has `Issue.Format` (ported from CE's `ComicInfo.Format`,
confirmed `ComicRack.Engine/ComicInfo.cs`) and `FormatSignalCatalog` classifying it
(`docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md`).
Paperbunkr's `ComicNameInfo`/`ComicScanner` (`src/Paperbunkr.Engine/`) can also fail to parse a
`Number`, but that's a much noisier signal here than in Kavita — a null `Number` is already how
this codebase represents "not yet catalogued," not "special" — so this phase deliberately narrows
to Format only, per Ehis's call.

`FormatSignalCatalog.Defaults` is the wrong dictionary to reuse directly for this, despite the
overlap: it classifies *event-suggestion* strength (`FormatSignalStrength.None/Weak/Strong`), a
different axis. `Giant`/`King`/`1/2`/`Preview` are `Weak` event signals but should still pull an
issue into Specials; `Trade Paper Back`/`Hardcover`/`Director's Cut`/`Sketch`/`Web Comic`/`Black &
White` are `FormatSignalStrength.None` (no event signal at all) yet `Director's Cut` and `Trade
Paper Back` are exactly the kind of thing Kavita pulls into its Specials tab. The two catalogs
answer genuinely different questions ("does this suggest a bigger crossover?" vs. "does this
belong outside the series' normal numbered flow?") and needs its own vocabulary.

## Scope

### `SpecialFormatCatalog`

New file, `src/Paperbunkr.Data/Metadata/SpecialFormatCatalog.cs` — same field-descriptor-catalog
idiom as `FormatSignalCatalog`/`RelationTypeCatalog`, but a flat case-insensitive set rather than a
strength dictionary:

```csharp
public static class SpecialFormatCatalog
{
    /// <summary>
    /// The subset of CE's own shipped Format vocabulary (<see cref="FormatSignalCatalog.CeDefaultFormats"/>)
    /// that also appears, under an equivalent name, in Kavita's real special-triggering Format list
    /// (verified against wiki.kavitareader.com/guides/metadata/comics). CE's other 9 values (1/2,
    /// Black & White, Giant, King, Minus 1, Sketch, Preview, Web Comic, Hardcover) are real CE
    /// values but NOT special-triggering under Kavita's own logic, so they're deliberately absent.
    /// </summary>
    private static readonly string[] CeOverlap =
    {
        "Special", "Director's Cut", "Annual", "Epilogue", "One Shot", "Prologue", "Trade Paper Back",
    };

    /// <summary>
    /// Values Kavita treats as special-triggering that CE's DefaultLists.txt does NOT ship at all -
    /// confirmed absent by grep, so these are a deliberate addition to Paperbunkr's Format
    /// vocabulary, not a CE port. Bundled into this phase per Ehis's call (scope question 3).
    /// </summary>
    private static readonly string[] KavitaOnlyAdditions =
    {
        "Reference", "Box Set", "Anthology", "Omnibus", "Compendium", "Absolute",
        "Graphic Novel", "GN", "FCBD", "Giant Size",
    };

    public static readonly IReadOnlySet<string> Values =
        new HashSet<string>(CeOverlap.Concat(KavitaOnlyAdditions), StringComparer.OrdinalIgnoreCase);

    public static bool IsSpecial(string? format) =>
        !string.IsNullOrWhiteSpace(format) && Values.Contains(format);
}
```

### `Issue.IsSpecial()`

New extension in `src/Paperbunkr.Data/Metadata/IssueMetadataExtensions.cs`, same location/idiom as
`EffectiveNumber()`/`EffectiveVolume()`:

```csharp
public static bool IsSpecial(this Issue issue) => SpecialFormatCatalog.IsSpecial(issue.Format);
```

No new column, no migration — `Issue.Format` already exists and is already editable (Phase 4e's
combo-box editor). This phase is purely a read of existing data plus one new UI surface.

### Format autocomplete additions

`FormatSignalCatalog.CeDefaultFormats` stays exactly CE's real 16 values — untouched, since it's
documented as literally CE's shipped list and changing it would misrepresent it (CLAUDE.md standing
rule). The Issue Properties / Bulk Issue Properties Format combo-box's autocomplete source becomes
`FormatSignalCatalog.CeDefaultFormats.Union(SpecialFormatCatalog.KavitaOnlyAdditions, ...)` (case-
insensitive) instead of `CeDefaultFormats` alone, so the 10 new values are typeable/selectable
without polluting the "this is CE's list" comment on `CeDefaultFormats` itself. `KavitaOnlyAdditions`
needs to move from `private` to `internal`/`public static readonly` on `SpecialFormatCatalog` for
the editor to reach it.

### `DetailTabsViewModel` — Specials tab

Mirrors the existing Issues tab plumbing exactly (`src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs`):

- New `public ObservableCollection<IssueCardSample> Specials { get; }`, initialized alongside
  `Issues` in the constructor.
- `LoadSeries` splits at the same loop that builds `Issues` today: for each `issue in
  series.Issues.OrderByNumber()`, route into `Specials` instead of `Issues` when
  `issue.IsSpecial()` is true — Specials are pulled fully out of the numbered flow, not
  duplicated into both, matching the recommended UI-placement option and Kavita's own behavior
  (its Issues tab doesn't repeat Specials tab content either). Same `IssueCardSample` shape, same
  cover-cache/read-state logic, no new fields needed on the card.
- `ActiveTab` gets a fourth-ish value `"specials"` alongside `"issues"`/`"related"`/`"details"`/
  `"activity"`; `IsSpecialsTab => ActiveTab == "specials"`; `GoSpecialsCommand`/`GoSpecials()`
  following the exact `GoIssues`/`GoRelated` pattern at lines ~918-941 today.
- **Tab hidden entirely when empty** — `HasSpecials => Specials.Count > 0`, and the tab button's
  `IsVisible` binds to it (same idiom as `ShowIssuesTab` today, but computed rather than a
  settable property since it's data-driven, not screen-mode-driven). The overwhelming majority of
  series will have zero specials; a perpetually-empty 5th tab would be worse than Kavita's own UX,
  which does hide the Specials tab when a series has none.

### `DetailTabs.axaml`

New tab button, positioned between Issues and Related (matching Kavita's own tab order in the
screenshots: Issues → Specials → Related):

```xml
<Button Classes="tab" Classes.active="{Binding IsSpecialsTab}" Command="{Binding GoSpecialsCommand}" IsVisible="{Binding HasSpecials}">
    <StackPanel Orientation="Horizontal" Spacing="6">
        <TextBlock Text="Specials" />
        <TextBlock Classes="tabCount" Text="{Binding Specials.Count}" VerticalAlignment="Center" />
    </StackPanel>
</Button>
```

Tab **content** reuses the Issues tab's existing Poster/List/Card templates rather than a
parallel set — the chrome row's sort/filter/group controls and the 3 view-mode buttons stay
Issues-tab-only (a handful of specials rarely need sorting/grouping); the Specials tab content is
just the same tile/row `DataTemplate`s bound to `Specials` instead of `Issues`, in whichever view
mode the Issues tab is currently set to (`DetailIssueViewMode`, shared, not a second setting).
Selection/2D-arrow-nav/context-menu wiring (`OnIssueTilePointerPressed` etc.) already keys off
whichever `ItemsControl` raised the event, not a hardcoded `Issues` reference — confirm this at
implementation time; if it turns out to assume `Issues`, that's a small generalization, not a
redesign.

## Testing

- **`SpecialFormatCatalogTests`**: every `CeOverlap` and `KavitaOnlyAdditions` value resolves
  `IsSpecial` true; case-insensitive (`"annual"` matches `"Annual"`); a CE value that is *not*
  special-triggering (`"Hardcover"`, `"Giant"`, `"Sketch"`, `"1/2"`, `"King"`, `"Minus 1"`,
  `"Preview"`, `"Web Comic"`, `"Black & White"`) resolves false; null/empty/whitespace resolves
  false; an arbitrary user-typed value resolves false.
- **`IssueMetadataExtensionsTests`** (extend): `IsSpecial()` delegates correctly for an issue with
  `Format = "One Shot"` vs `Format = null` vs `Format = "Trade Paper Back"`.
- **`DetailTabsViewModelTests`** (extend): `LoadSeries` routes a Format-flagged issue into
  `Specials` and NOT into `Issues`; a series with zero special-format issues yields `HasSpecials ==
  false` and an empty `Specials` collection; `GoSpecialsCommand` sets `ActiveTab == "specials"` and
  `IsSpecialsTab == true`; existing Issues-tab tests (`OrderByNumber`, selection, view-mode
  persistence) still pass with a mixed series (some Format-flagged, some not) — total
  `Issues.Count + Specials.Count` equals `series.Issues.Count`.
- Regression: full `Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests` green.
- **Build gotcha reminder**: no new `x:Class` view is created here (the Specials tab reuses
  `DetailTabs.axaml`'s existing templates) — the CLAUDE.md AVLN2000 gotcha doesn't apply, but
  worth a plain `dotnet build` (not just incremental) before calling this done regardless, per
  that same doc's "0 Errors alone is insufficient proof" warning.

## Risks

- **Concurrent shared-working-tree edits.** `DetailTabsViewModel.cs` and `DetailTabs.axaml` are
  the exact files the just-shipped streaming redesign (P1, commits `0688490`…`8e2dc62`) rewrote
  most heavily, and Phase 4d-4g work may also have touched `DetailTabsViewModel` per that spec's
  own risk note. Re-check `git status`/`git log` on both files immediately before starting; park
  any WIP to scratchpad, never `git stash` (shared tree — see `project_paperbunkr_concurrent_sessions`).
- **A user's existing Format values may not exactly string-match.** Someone who typed `"One-Shot"`
  (hyphenated, Kavita's own variant spelling) instead of CE's `"One Shot"` won't trigger
  `IsSpecial`. Out of scope for this phase (exact-string catalog matches every other catalog in
  this codebase — `FormatSignalCatalog` has the identical limitation today); a future fuzzy-match
  pass is a separate, small follow-up if it turns out to matter in practice.

## Explicitly out of scope

The other two Kavita detection mechanisms (no-parsed-`Number` auto-detection, `SP##` filename
marker) — Format-only per Ehis's call; either could be a later addition if Format-tagging proves
too manual in practice. A manual per-issue `IsSpecial` override field — no schema change this
phase; if Format-based detection gets something wrong (the "title literally contains 'Special'"
class of bug Kavita itself hits — see `github.com/Kareadita/Kavita/issues/2308`), the fix is
correcting the issue's `Format` value, not a second source of truth. A Doujinshi-equivalent
`RelationType` value (noted in the Kavita comparison research as the one real relation-model gap)
— unrelated to this Specials-tab work, its own small follow-up if ever needed. Sort/group/filter
controls specific to the Specials tab — reuses the Issues tab's shared view-mode setting rather
than growing its own.
