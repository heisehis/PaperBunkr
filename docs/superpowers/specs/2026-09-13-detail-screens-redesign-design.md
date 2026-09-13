# Detail screens redesign — Hero, Issues grid, Details/Related/Activity tabs

Date: 2026-09-13
Status: Approved for implementation (via visual companion brainstorming session)

## Scope and how it was decided

User picked "Detail screens" over "Metadata editors" as the first of two separable subsystems to redesign (metadata editors — Issue Properties / Bulk Issue Properties — are a separate future spec). Within Detail screens, four redesign drivers were named up front (visual style, information architecture, specific pain points, Comic/Manga parity) and then narrowed through a visual-companion session (mockups pushed to a local browser tab, user clicked/typed reactions) down to four concrete areas:

1. **Hero band** — "cluttered ... cramped ... tacked on"
2. **Issues grid** (poster tiles) — sizing/density and read-state legibility
3. **Details tab** — flagged directly: "a lot in it ... scrolling, hierarchy"
4. **Related tab** — "visual only" polish, explicitly no structural complaint
5. **Activity tab** — not flagged by the user; treated as visual-polish-only by inference (same as Related), confirmed with the user before writing this doc

**Correction from the brainstorming session itself:** the visual-companion mockups approximated a hero band from a description of the current UI, not a read of the actual file. Having now read [DetailHero.axaml](../../../src/Paperbunkr.App/Views/DetailHero.axaml) directly, the real current hero is *already* the cinematic full-bleed-backdrop treatment the mockups called "Editorial banner" (built in the 2026-08-28 streaming redesign) — backdrop image, vignette gradient, a 142×206 cover with shared-element transition support, title, a badge row, and an actions row. It is not the plain flat-gradient panel the mockups showed. This section replaces the mockup's illustrative "before/after" with the real one.

## 1. Hero band

**Real defect** (not what the mockup implied): `DetailMetaBadge.Build` ([DetailMetaBadge.cs:29](../../../src/Paperbunkr.App/Models/DetailMetaBadge.cs#L29)) returns every non-blank badge with no cap — Publisher, Status, Issue Count, Unread, Year, Format, Age Rating, Language is up to **8** badges, rendered in a `WrapPanel` ([DetailHero.axaml:82-108](../../../src/Paperbunkr.App/Views/DetailHero.axaml#L82-L108)) that wraps onto a second/third line on a typically-loaded series. That wrapping against a fixed `HeroHeight="360"` ([DetailHero.axaml.cs:17](../../../src/Paperbunkr.App/Views/DetailHero.axaml.cs#L17)) is the concrete "cluttered/cramped" cause — not the overall hero structure, which the visual-companion session ended up re-approving as-is once the real screenshot was accounted for.

**Fix**: cap the badge row at **3** visible badges with a "+N more" overflow toggle, same expand/collapse *convention* `DetailBandGroupViewModel` uses for the Genre/Teams/Locations/Characters chip groups ([DetailBandGroupViewModel.cs:85-100](../../../src/Paperbunkr.App/ViewModels/DetailBandGroupViewModel.cs#L85-L100)) but not that exact class - three separate view-models (`DetailScreenViewModel`/`MangaDetailScreenViewModel`/`BookDetailScreenViewModel`) each build their own `MetaBadges` today, so the expand state needs one small shared type rather than duplicated `IsExpanded` fields in three places:

```csharp
// Paperbunkr.App.Models
public partial class DetailMetaBadgeGroup : ObservableObject
{
    private readonly IReadOnlyList<DetailMetaBadge> _all;
    public DetailMetaBadgeGroup(IReadOnlyList<DetailMetaBadge> all) => _all = all;

    [ObservableProperty] private bool _isExpanded;
    public IReadOnlyList<DetailMetaBadge> Visible => IsExpanded ? _all : _all.Take(3).ToList();
    public int OverflowCount => Math.Max(0, _all.Count - 3);
    public bool HasOverflow => OverflowCount > 0;
    public string MoreLabel => IsExpanded ? "show less" : $"+{OverflowCount} more";
    [RelayCommand] private void ToggleExpand() => IsExpanded = !IsExpanded;
}
```

`IDetailHeaderSource.MetaBadges` changes type from `IReadOnlyList<DetailMetaBadge>` to `DetailMetaBadgeGroup` (still defaulting to an empty-backed instance so Home's non-override stays a no-op); `DetailHero.axaml`'s `ItemsControl ItemsSource="{Binding MetaBadges}"` becomes `ItemsSource="{Binding MetaBadges.Visible}"` plus a small "+N more" `Button` bound to `MetaBadges.MoreLabel`/`ToggleExpandCommand`, styled like the existing `heroBadge` chips. Priority order for which 3 show by default: Publisher, Status, then Format — these are the three a user scans for first (who published it, is it done, what physical/digital form) per `DetailMetaBadge.Build`'s existing ordering. Year/Language/Age Rating/Issue Count/Unread move into the overflow.

`IDetailHeaderSource.MetaBadges` ([IDetailHeaderSource.cs:45](../../../src/Paperbunkr.App/ViewModels/IDetailHeaderSource.cs#L45)) defaults to an empty list via a default interface member, and only three view-models actually override it: `DetailScreenViewModel` (Western), `MangaDetailScreenViewModel`, and `BookDetailScreenViewModel` — confirmed by grep, not assumed. The Home spotlight header (`HomeSpotlightHeaderSource`) does **not** override it, so it never shows badges at all (falls back to the plain `MetaLine` string) and is unaffected by this change — an earlier draft of this doc incorrectly claimed Home benefits too; it doesn't populate `MetaBadges` in the first place. The badge cap applies to all three real detail screens (Comic, Manga, **and Book** — wider than the Details-tab/Issues-grid changes below, which are Comic/Manga-only since Book uses an entirely separate `BookDetailScreenViewModel` with no `DetailTabsViewModel`).

No other hero structural change - action buttons (`DetailHeroAction`/`Classes="detailAction"` with existing primary/ghost distinction, [DetailHero.axaml:118-135](../../../src/Paperbunkr.App/Views/DetailHero.axaml#L118-L135)) already have the hierarchy the mockups were separately exploring; nothing there was named as broken once the real file was accounted for.

## 2. Issues grid (Poster view, Western Detail screen only)

Scoped to the Western `DetailScreenViewModel`'s Issues tab only. `MangaDetailScreenViewModel` uses a completely different chapter-list presentation (`ChapterRowSample`/`ChapterVolumeGroup`, list rows not poster tiles, `ShowIssuesTab = false`) — out of scope for this pass; no pain point was raised against it.

**Current**: `IssuePosterTileTemplate` ([DetailTabs.axaml:103-125](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L103-L125)) — 118×168 tile in a plain `WrapPanel`, with a `pbc:StatusBadge` (Read/InProgress) already top-right.

**Changes** (converged after two brainstorming rounds — first draft was rejected as "too big"):
- Tile size reduced from 118×168 to **92×131** (same 2:3-ish aspect ratio, ~22% narrower) so more fit per row in the same `WrapPanel` — no grid-column math needed, `WrapPanel` already reflows naturally at any width.
- **Existing** `StatusBadge` (Read/InProgress, top-right) is kept as-is — it was never named as a problem; the visual-companion mockup's "ribbon" was this session's own illustrative restyling, not something the user asked for in words. Not reinventing a working, unflagged element (YAGNI).
- **New**: a Format badge, bottom-left on the tile, small caps text (e.g. "ONE-SHOT", "OMNIBUS", "TPB", "ANNUAL") reading `Issue.Format` — this was the concrete addition the user asked for ("add more stuff to the cards like omnibus"). Only rendered when `Issue.Format` is non-blank; absent for a plain numbered issue (the overwhelming majority of tiles), so normal runs stay visually quiet.

**New model field**: `IssueCardSample.Format` (`string?`, `init`) — [IssueCardSample.cs](../../../src/Paperbunkr.App/Models/IssueCardSample.cs) currently has no such field; `BuildIssueCard` ([DetailTabsViewModel.cs](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs)) needs one more line: `Format = issue.Format,`. A `HasFormat` bool (mirroring `HasFile`'s existing pattern at [IssueCardSample.cs:54](../../../src/Paperbunkr.App/Models/IssueCardSample.cs#L54)) gates the badge's `IsVisible`.

`IssueListRowTemplate`/`IssueCardTileTemplate` (List/Card view modes) are unaffected — Format was never raised as missing there, and this pass is scoped to Poster view.

## 3. Details tab — split into "Info" / "Linking" sub-tabs, each with card sections

**Current**: one flat `StackPanel` under `IsDetailsTab` containing, in order: Publisher/Reading-Mode `Grid`, the Credits section, the Additional Details section (both added earlier this session), the plugin scraper view *or* External Metadata block, then the Trackers block ([DetailTabs.axaml:510-712](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L510-L712), post this-session's edits) — five distinct concerns with no visual separation beyond a `Margin="0,24,0,0"` gap between each.

**New structure**: an inner two-tab strip (`Info` / `Linking`), each item wrapped in a bordered card (`Border` with `PbBorderBrush`/`PbSurface2Brush`-style treatment already used elsewhere in this app, e.g. the Related tab's own add-continuity/add-collection expander borders at [DetailTabs.axaml:366-367](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L366-L367) — reusing an existing visual, not inventing one):

- **Info**: Publisher & Reading Mode (one card), Credits (one card), Additional Details (one card).
- **Linking**: the plugin scraper view *or* default External Metadata block (one card, `ShowComicScraperDetailUi`/`ShowDefaultSeriesDetailUi` unchanged), Trackers (one card).

New `DetailTabsViewModel` state: `ActiveDetailsSubTab` (string, `"info"`/`"linking"`, default `"info"`) with `IsInfoSubTab`/`IsLinkingSubTab` bool properties and a `GoInfoSubTabCommand`/`GoLinkingSubTabCommand` pair — same shape as the existing outer `ActiveTab`/`GoDetailsCommand` mechanism ([DetailTabsViewModel.cs:1646-1683](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1646-L1683)), just one level down. Reset to `"info"` in `LoadSeries` (mirrors `ActiveTab`'s own reset there).

No content or aggregation logic changes — this is a pure layout/grouping change around content this session already built.

## 4. Related tab — visual restyle only

No structural change ("visual only" per the user). Current layout ([DetailTabs.axaml:353-507](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L353-L507)): Continuities chips, Collections chips, an add-related-series expander, then up to five `PosterRail`s stacked (Related, Same Continuity, Same Collection, Same Event, More Like This).

Checked `PosterRail.axaml` directly - it has **no card/border wrapper today**, just a title line + a horizontally-scrolling row (each item already has its own `railCover` border, [PosterRail.axaml:26-32](../../../src/Paperbunkr.App/Views/PosterRail.axaml#L26-L32)).

**Correction from an earlier draft of this section**: every group on this tab gets the same card wrapper, not just the two editable ones - Continuities, Collections, and each of the five `PosterRail`s (Related, Same Continuity, Same Collection, Same Event, More Like This) all get wrapped in the Details tab's own bordered-card treatment, so the whole tab reads as one consistent set of cards rather than two styled sections plus five unstyled ones.

`PosterRail.axaml` itself is a reusable `UserControl` also used elsewhere (e.g. Home screen's own rails) - wrapping it in a card is done at the **call site** (`DetailTabs.axaml`, each `<views:PosterRail .../>` usage wrapped in its own `Border` card), not by changing `PosterRail.axaml`'s own template. That keeps every other consumer of `PosterRail` visually untouched - only the Related tab's usage gets the card, since only the Related tab was named for this redesign.

No new sections, no removed content, no changed data flow, no `DetailTabsViewModel` logic changes for this tab - purely wrapping existing blocks in cards at the XAML call-site level.

## 5. Activity tab — visual restyle only (assumed, confirmed)

Same treatment as Related — no structural change, no reordering, no new event types beyond what this session's earlier Activity-log-expansion work already added. Purely bringing its existing list rows in line with whatever spacing/color tokens the Details/Related restyle settles on.

## 6. Manga Detail screen — explicitly included

Three of the five areas above already apply to `MangaDetailScreenViewModel` because it shares the exact same components as the Western screen, not by extension - restating explicitly since it was raised as a scope question:

- **Hero band** (§1): `DetailHero.axaml` is the same control; `DetailMetaBadgeGroup` cap applies here automatically (`MangaDetailScreenViewModel` is one of the three real `MetaBadges` overriders confirmed in §1).
- **Details/Related/Activity tabs** (§3/§4/§5): `MangaDetailScreenViewModel` embeds the same `DetailTabsViewModel`/`DetailTabs.axaml` with `ShowIssuesTab = false, ShowTabStrip = false` ([MangaDetailScreenViewModel.cs:50](../../../src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs#L50)) - the Info/Linking split and the Related-tab card treatment ship there unchanged, no separate work needed.

**New for this pass** — the Chapters tab (Manga's Issues-tab equivalent, [MangaDetailScreen.axaml:202-241](../../../src/Paperbunkr.App/Views/MangaDetailScreen.axaml#L202-L241)): each `ChapterVolumeGroup` gets the same bordered-card wrapper as the Details/Related sections, for the same visual consistency reason as §4's correction - one card per volume group, containing its `chapterRow` list, instead of a bare `TextBlock` header + unwrapped rows. `ChapterRowSample`'s own row content (number, title, New/read/bookmark/missing icons, scan-group mark, date, progress bar, [ChapterRowSample.cs:12-38](../../../src/Paperbunkr.App/Models/ChapterRowSample.cs#L12-L38)) is unchanged - no Format-badge equivalent here, since manga chapters deliberately carry no variant-cover/format concept by this screen's own original design rationale, and no complaint was raised against the row content itself, only asked to be "involved" in the broader restyle.

## Explicitly out of scope

- Metadata editors (Issue Properties / Bulk Issue Properties) — separate future spec, per the user's own scope-decomposition choice at the start of this session.
- Any new Activity event kinds, any new Related-tab content sources, any change to what aggregates into Credits/Additional Details — all "what data shows" questions were settled in the prior spec this session; this one is "how it's laid out."

## Testing

- `DetailMetaBadgeGroup` (new model, `Paperbunkr.App.Models`): unit tests - `Visible.Count == 3` and correct `OverflowCount` when constructed with 8 badges; `HasOverflow == false` and `Visible` returns all of them when constructed with ≤3; `ToggleExpand` flips `IsExpanded` and `Visible` then returns the full list.
- `IssueCardSample.Format`/`HasFormat`: unit test in `DetailTabsViewModelTests` - an issue with `Format = "Omnibus"` produces a card with `HasFormat == true` and the raw value; a blank-format issue produces `HasFormat == false`.
- `ActiveDetailsSubTab`: unit tests mirroring the existing `ActiveTab`/`IsDetailsTab` tests already in `DetailTabsViewModelTests` - defaults to `"info"`, `LoadSeries` resets it, `GoLinkingSubTabCommand` flips `IsLinkingSubTab`/`IsInfoSubTab` correctly.
- On-screen verification required for the actual visual result (tile size, card borders, badge overflow rendering) — no automated test substitutes for "does this look right"; same caveat as this session's earlier work about checking the shared working tree's state before launching the app.
