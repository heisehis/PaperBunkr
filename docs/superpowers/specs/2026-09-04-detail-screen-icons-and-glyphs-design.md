# Detail-screen icons & glyphs

**Date:** 2026-09-04
**Status:** Approved, ready for planning
**Scope:** Comic `DetailScreen` + `MangaDetailScreen` and the shared controls they compose
(`DetailHero`, `DetailBand`, `DetailTabs`, `Styles/DetailChrome.axaml`); the Home hero spotlight's
one action button; an app-wide icon-size-token sweep; and two new `BrandMark` families
(reading-status, scanlation-group).

**Revision (2026-09-04):** after the first draft, the user pulled three items from "out of scope"
into scope — Home hero spotlight button icon (§2), app-wide icon-size token adoption (§7), and a
new `BrandMark` family for reading-status **and** scanlation-group (§8). Book detail screen stays
out of scope.

## Goal

Add icons and metadata glyphs across both series-detail screens so the currently text-heavy
chrome reads faster and matches the "modern, reactive" direction the detail-screen streaming
redesign (docs/superpowers/specs/2026-08-28-detail-screens-streaming-redesign-design.md) already
set. Purely cosmetic — no new metadata field, default, or behavior, so no ComicRack CE parity
concern (the detail screens are already a deliberate from-CE redesign).

## Constraints / foundation

- **One icon family.** The app already standardizes on FluentSystemIcons via
  `FluentIcons.Avalonia` (`fi:SymbolIcon Symbol="…"`). Everything here uses that. No new package;
  no Lucide / Material / hand-rolled `PathIcon` (`avalonia-pro-max/icons-imagery`: "Pick one icon
  family per app").
- **Glyphs / marks** reuse the existing `pbc:BrandMark` control (`MarkFamily` =
  Service/Publisher/Format/AgeRating/Language, docs/superpowers/specs/2026-08-28-brand-metadata-
  iconography-design.md). No changes to `BrandMark`/`MarkResolver` internals.
- **Colour** always through `DynamicResource` semantic tokens (`PbTextFaintBrush`,
  `PbSuccessBrush`, `PbAccentBrush`, …) — never hardcoded, so runtime skinning keeps working
  (the 2026-08-30 review-checklist audit caught hardcoded hex on these screens before).
- **Accessibility:** decorative status glyphs get `ToolTip.Tip` and
  `AutomationProperties.AccessibilityView="Raw"`; no icon-only interactive control is introduced
  (every button that gains an icon keeps its text label).

## 1. Icon-size tokens

Add to `App.axaml` beside the existing `PbFontSize*` doubles (same precedent — those were added
by the 2026-08-30 `avalonia-pro-max/review-checklist` audit to kill magic numbers). Values are the
`avalonia-pro-max/icons-imagery` recommended scale:

```xml
<x:Double x:Key="PbIconSizeXs">14</x:Double>   <!-- dense chip / inline-with-caption (was 12, bumped 2026-09-04) -->
<x:Double x:Key="PbIconSizeSm">16</x:Double>   <!-- inline with control text -->
<x:Double x:Key="PbIconSizeMd">18</x:Double>   <!-- standalone button glyph -->
<x:Double x:Key="PbIconSizeLg">24</x:Double>   <!-- tab / nav item -->
<x:Double x:Key="PbIconSizeXl">40</x:Double>   <!-- empty-state hero glyph -->
```

Plain `x:Double`, not skinned by `SkinService` (same as `PbFontSize*`). All new markup in this
change references a token, never a literal `FontSize`. The app-wide sweep of *existing* literals
onto this scale is §7.

## 2. Hero action buttons + back link

- `Models/DetailHeroAction.cs`: add a trailing optional parameter
  `FluentIcons.Common.Symbol? Icon = null`. All four existing call sites (`DetailScreenViewModel`,
  `MangaDetailScreenViewModel`, `BookDetailScreenViewModel`, `HomeSpotlightHeaderSource`) keep
  compiling unchanged; Book/Home simply pass no icon and render text-only (no regression).
- `Views/DetailHero.axaml`: the `Actions` `ItemsControl.ItemTemplate` button becomes
  `icon + label` — a leading `fi:SymbolIcon` (size `PbIconSizeSm`, `Foreground` inherited from the
  button) shown only when `Icon` is non-null, then the existing `TextBlock`/`Content`.
  Implementation note: bind icon visibility off a computed `bool HasIcon`/nullable check; a
  `SymbolIcon` with a null `Symbol` must not render a blank box.
- `DetailScreenViewModel.Actions`:
  - primary — `Symbol.Play` on the Read / Continue / Re-read button (focused issue **and** the
    series-level fallback).
  - `Symbol.Edit` on the Edit button.
  - `Symbol.Image` on Change Cover.
- `MangaDetailScreenViewModel.Actions`: same three (`Play` / `Edit` / `Image`).
- `HomeSpotlightHeaderSource`: the single `"Read now"` action gets `Symbol.Play`. (Book detail's
  actions stay iconless — out of scope — and simply render text-only through the same template.)
- `Styles/DetailChrome.axaml` `Button.backLink` is shared by both screens; both `.axaml` files
  set `Content="← Back to Library"`. Replace the literal arrow with a leading
  `fi:SymbolIcon Symbol="ArrowLeft"` (size `PbIconSizeSm`) + `TextBlock Text="Back to Library"`.
  Do this by giving `backLink` buttons real content markup in each screen (`StackPanel`
  orientation Horizontal), not by touching the style.

## 3. Tab strips

Two separate strips render `Classes="tab"` buttons:
- `Views/DetailTabs.axaml` — Issues / Specials / Related / Details / Activity
- `Views/MangaDetailScreen.axaml` — Chapters / Related / Details / Activity

Each tab button's content becomes `StackPanel` Horizontal: leading `fi:SymbolIcon`
(size `PbIconSizeSm`, `Foreground` inherited so it dims/undims with the existing
`.tab` / `.tab.active` `Foreground` setters), then the existing label `TextBlock`, then the
existing `TextBlock.tabCount` badge where present.

| Tab | Symbol |
|---|---|
| Issues | `BookOpen` |
| Specials | `Star` |
| Chapters | `TextBulletListLtr` |
| Related | `Link` |
| Details | `Info` |
| Activity | `Pulse` |

(Verify each name against the `FluentIcons.Common.Symbol` enum during implementation; substitute
the nearest sibling if a name differs, e.g. `BookOpen` vs `Book`.)

## 4. Issue / chapter tile status glyphs

Shared `IssueCardSample` tile templates live in `DetailTabs.axaml`'s `UserControl.Resources`
(`IssuePosterTileTemplate`, `IssueListRowTemplate`, `IssueCardTileTemplate`) — used by **both**
the Issues and Specials tabs. Manga chapter rows are `ChapterRowSample` in
`MangaDetailScreen.axaml`.

Add a computed, unit-testable property rather than burying state logic in XAML converters:

- `IssueCardSample.TileGlyph` → enum `{ None, Read, InProgress }`
  (`IsRead` → `Read`; else `IsInProgress` → `InProgress`; else `None`).
- `ChapterRowSample.ShowReadGlyph` → `bool` (`IsRead && !IsInProgress`).

Rendering:
- **Poster tile:** a `CheckmarkCircle` (`PbSuccessBrush`, size `PbIconSizeMd`) pinned
  top-right inside the cover `Border` when `TileGlyph == Read`. Existing in-progress accent bar
  and read-dim opacity unchanged.
- **List row:** widen `ColumnDefinitions` with a new leading ~20px cell holding a
  `CheckmarkCircle` (read, success) or `CircleHalfFill`/`ClockAlarm`-style half glyph
  (in-progress, accent) or nothing.
- **Card tile:** a small `CheckmarkCircle` (`PbIconSizeSm`, success) before `FullTitle` when
  read.
- **Chapter row:** a faint `Checkmark` (`PbTextFaintBrush`, `PbIconSizeSm`) in a spare grid
  column when `ShowReadGlyph`, sitting with the existing `Bookmark` / `Warning` glyphs.

All four are decorative: `ToolTip.Tip` ("Read" / "In progress") +
`AutomationProperties.AccessibilityView="Raw"`.

## 5. Details-tab rows + actions (`DetailTabs.axaml`) + manga chapter chrome

- **Section labels** get a leading `fi:SymbolIcon` (size `PbIconSizeSm`, `PbTextFaintBrush` to
  match the label):
  | Label | Symbol |
  |---|---|
  | `PUBLISHER` | `Building` |
  | `READING MODE` | `BookArrowClockwise` (fallback `ArrowClockwise`) |
  | `External Metadata` | `Globe` |
  | `Trackers` | `CloudSync` (fallback `Cloud`) |
- **`railAdd` buttons:** replace the literal `"+ "` prefix in `+ Add to Continuity`,
  `+ Add to Collection`, `+ Link External Metadata`, `+ Link for Tracking` with a leading
  `fi:SymbolIcon Symbol="Add"` + text (`StackPanel` content). `Sync to Trackers` gets a leading
  `Symbol="ArrowSync"` (fallback `ArrowClockwise`).
- **View-mode segment** (`Button.segItem` Poster / List / Card): leading icon **kept with** the
  text label — `Grid` / `TextBulletListLtr` / `ContentView` (fallback `Album`).
- **Reading-mode toggle:** `DetailTabsViewModel.SetReadingModeLabel` currently appends a literal
  `" ▾"` to every label string. Drop the `▾` from the strings and render a real
  `fi:SymbolIcon Symbol="ChevronDown"` (size `PbIconSizeSm`) after the button text in
  `DetailTabs.axaml` (`avalonia-pro-max/icons-imagery`: "no chars as icons"). Update the
  `DetailTabsViewModelTests` `ReadingModeLabel` assertions accordingly.
- **Manga chapter filter pills:** the sort pills (`By Number` / `By Date`) get leading
  `TextSortAscending` / `CalendarLtr`; the sort-direction pill keeps its existing
  `ChapterSortDirectionGlyph` but that glyph string becomes a real `fi:SymbolIcon`
  (`ArrowSortDown` / `ArrowSortUp`) bound off a new `bool ChapterSortAscending`. Filter pills
  (`All` / `Unread` / `Bookmarked` / `Missing`) stay **text-only** — four short pills in a row
  already crowd, and the user chose not to icon every pill.

## 7. App-wide icon-size token sweep

Replace every literal `fi:SymbolIcon FontSize="<n>"` across `src/Paperbunkr.App/Views/*.axaml`
and `src/Paperbunkr.App/Styles/*.axaml` with the nearest §1 token via
`FontSize="{StaticResource PbIconSize…}"`:

| current literal | → token |
|---|---|
| 9, 9.5, 10, 11, 12, 12.5 | `PbIconSizeXs` (12) |
| 13, 14 | `PbIconSizeSm` (14) |
| 15–17 | `PbIconSizeMd` (16) |
| 18–24 | `PbIconSizeLg` (20) |
| 25+ (two empty-state glyphs: 34, 40) | `PbIconSizeXl` (32) |

This normalizes ~40 ad-hoc sizes onto five steps. Individual glyphs shift by ≤4px; none sit in a
fixed-size cell, so no layout breaks — but this is a deliberate visual normalization, not a
zero-diff rename (unlike the 2026-08-30 `PbFontSize*` audit). One grep confirms none are missed:
`rg 'SymbolIcon[^>]*FontSize="\d' src/Paperbunkr.App` must return nothing afterward. `BrandMark`'s
own `MarkSize`-driven glyph sizing is untouched (it's a control property, not a literal).

Not swept: `TextBlock`/`Button` `FontSize` literals (those are type-scale, covered by
`PbFontSize*` already), and `MarkSize` on `BrandMark` call sites (a different axis).

## 8. New `BrandMark` families: reading-status + scanlation-group

Both are `MarkKind.Glyph` results — no new SVG assets, no alias TSVs.

### 8a. `MarkFamily.ReadingStatus`

- `Controls/BrandMark.cs`: add `ReadingStatus` to the `MarkFamily` enum and a case in
  `Rebuild()`'s `switch` → `MarkResolver.Instance.ResolveReadingStatus(Value)`.
- `Services/MarkResolver.cs`: `public MarkSpec ResolveReadingStatus(string? value)` — parses the
  `ReadingStatus` enum name (the value every call site already produces via
  `series.ReadingStatus.ToString()`) and returns a glyph + friendly label + colour:

  | `ReadingStatus` | Glyph | Label | Colour (hex, = token) |
  |---|---|---|---|
  | `Reading` | `BookOpen` | Reading | `#E0995A` (`PbAccentTextColor`) |
  | `ReReading` | `ArrowSync` | Re-reading | `#E0995A` |
  | `Completed` | `CheckmarkCircle` | Completed | `#5FA889` (`PbSuccessColor`) |
  | `Paused` | `PauseCircle` | On Hold | `#D7AC4C` (`PbBadgeColor`) |
  | `Dropped` | `DismissCircle` | Dropped | `#D96C6C` (`PbDangerColor`) |
  | `Planned` | `Clock` | Planned | `#77726A` (`PbTextFaintColor`) |
  | `Unknown` / unparseable | — | — | `MarkSpec.None` (renders nothing) |

  Literal hex mirrors the existing age-rating path (colours live in `age-rating-aliases.tsv` as
  hex, not tokens — the resolver is Avalonia-resource-free by design). A code comment ties each
  hex to its token name.

- **Per-glyph colour** needs a small `BrandMark` template addition — today
  `Styles/Marks.axaml`'s glyph `fi:SymbolIcon` binds `Foreground="{TemplateBinding Foreground}"`
  with no per-spec override. Add a `GlyphBrush` direct-property output computed in `Rebuild()`
  from `spec.Foreground` when it parses as a hex colour (reuse the existing `TryBrush` helper),
  and bind the glyph's `Foreground` to `GlyphBrush` with
  `TargetNullValue={TemplateBinding Foreground}`. SVG `ThemeTint` path is unaffected
  (`$theme` is not a hex, so `GlyphBrush` stays null there).

- **Surfaces:**
  - `IDetailHeaderSource` gains `string? ReadingStatus => null;` (default implementation on the
    interface, so `BookDetailScreenViewModel` / `HomeSpotlightHeaderSource` need no change).
    `DetailScreenViewModel` and `MangaDetailScreenViewModel` override it to
    `series.ReadingStatus == ReadingStatus.Unknown ? null : series.ReadingStatus.ToString()`,
    raising `PropertyChanged` in `LoadSeries`.
  - `Views/DetailHero.axaml`: a `pbc:BrandMark Family="ReadingStatus" Value="{Binding ReadingStatus}"
    ShowText="True" MarkSize="{StaticResource PbIconSizeSm}"` on the meta line row, visible only
    when `ReadingStatus` is non-null.
  - `Views/DetailBand.axaml` inline meta row: same `BrandMark`, bound to a new
    `DetailBandViewModel.ReadingStatusValue` (`string?`) + `HasReadingStatus` set in
    `LoadSeries` / `LoadIssue`, placed just after `StatusText` (publication status) — the two are
    different axes (Ongoing/Complete vs Reading/Completed) and both are useful.
  - Note: the detail screens have **no setter** for `ReadingStatus` (Library's context menu does);
    display-only here is consistent with the band's other derived read-only fields. Adding a
    setter is a separate follow-up.

### 8b. `MarkFamily.ScanGroup`

- `Controls/BrandMark.cs`: add `ScanGroup` to `MarkFamily` + the `Rebuild()` case →
  `MarkResolver.Instance.ResolveScanGroup(Value)`.
- `Services/MarkResolver.cs`: `public MarkSpec ResolveScanGroup(string? value)` → `MarkSpec.None`
  when blank, else `new MarkSpec(MarkKind.Glyph, Glyph: Symbol.PeopleTeam, Text: value!.Trim())`
  (no colour override → inherits `Foreground`). Keeps whatever `ScanInformation` already carries
  as the label; the glyph just gives the row a scannable "this is a group" affordance.
- **Surface:** `Views/MangaDetailScreen.axaml` chapter row — replace the bare
  `<TextBlock Classes="chapterMeta" Text="{Binding ScanInformation}" .../>` with
  `<pbc:BrandMark Family="ScanGroup" Value="{Binding ScanInformation}" ShowText="True"
  MarkSize="{StaticResource PbIconSizeXs}" IsVisible="{Binding HasScanInformation}"
  Foreground="{DynamicResource PbTextFaintBrush}"/>`. Add the `pbc` namespace to that file.

## 9. Testing

- `DetailHeroActionTests` (new or extend existing hero tests): `DetailScreenViewModel`,
  `MangaDetailScreenViewModel`, `HomeSpotlightHeaderSource` expose the expected `Icon` per action
  and per selection state (focused-issue primary is `Play`, etc.).
- `DetailTabsViewModelTests`:
  - `IssueCardSample.TileGlyph` returns `Read` / `InProgress` / `None` for the matching
    `IsRead` / `ReadFraction` inputs.
  - updated `ReadingModeLabel` assertions (no trailing `▾`).
- `MangaDetailScreenViewModelTests`: `ChapterRowSample.ShowReadGlyph` +
  `ChapterSortAscending` toggle.
- `MarkResolverTests`: `ResolveReadingStatus` — each enum name → expected `Glyph` + `Text`;
  `Unknown` / garbage → `MarkSpec.None`. `ResolveScanGroup` — blank → `None`, a group name →
  `Glyph` + trimmed text.
- `DetailScreenViewModelTests` / `MangaDetailScreenViewModelTests`: `ReadingStatus` header
  property is `null` for an `Unknown` series and the enum name otherwise.
- `BrandMarkRenderSmokeTests`: extend with a `ReadingStatus` + a `ScanGroup` case (both new
  glyph-family call sites, matching that file's existing per-family smoke coverage).
- Icon rendering itself is a visual pass — verified in the running app, same as every prior
  detail-screen polish pass.
- Full `Paperbunkr.App.Tests` must stay green; `avalonia-pro-max/review-checklist` run before
  calling it done.

## Out of scope

- `BookDetailScreen` chrome (hero buttons / tabs / tiles). It inherits the `DetailHeroAction.Icon`
  slot and the `IDetailHeaderSource.ReadingStatus` default (null), so it renders exactly as today
  — no regression, no new icons.
- A reading-status **setter** on the detail screens (Library has one; this is display-only).
- New mark families beyond the two in §8; SVG assets / alias tables for them.
- Any metadata model / persistence change (`ReadingStatus` already exists and is already written
  from Library + the reader).
- `TextBlock`/`Button` `FontSize` literals (type scale, not icon scale).

---

# Part 2 (2026-09-04): brand-mark coverage, Book detail, actionable reading-status

Follow-up round. The user asked to "take it further" — specifically: surface the existing
`BrandMark` families (Service / Publisher / Format / AgeRating / Language) everywhere they still
render as plain text; bring `BookDetailScreen` into the icon treatment (dropped in Part 1); and
make the reading-status glyph **actionable** — a setter on **both** the hero and the band.

## P2-A. Fill the plain-text gaps with `BrandMark` (comic + manga)

`Views/DetailTabs.axaml`, Details tab:

| Surface | Now | -> |
|---|---|---|
| External Metadata link chips | `<TextBlock Text="{Binding ProviderLabel}"/>` | `<pbc:BrandMark Family="Service" Value="{Binding ProviderLabel}" ShowText="True" MarkSize="{StaticResource PbIconSizeSm}"/>` |
| Tracker link chips | `<TextBlock Text="{Binding Service}"/>` | `<pbc:BrandMark Family="Service" Value="{Binding ServiceName}" ShowText="True" .../>` — add a `ServiceName` string to `TrackerLinkSample` (BrandMark.Value is `string?`) |
| Metadata provider `ComboBox` (`MetadataProviderOptions`) | plain enum text | `ComboBox.ItemTemplate` -> `<pbc:BrandMark Family="Service" Value="{Binding}" ShowText="True"/>` |
| Tracker service `ComboBox` (`TrackerServiceOptions`) | plain enum text | same |
| Metadata / Tracker search-result rows (`AniListMatchSample` / `TrackerMatchSample`) | title + tier badge | prepend a mark-only `<pbc:BrandMark Family="Service" ShowText="False" MarkSize="{StaticResource PbIconSizeSm}"/>` bound to the VM's `SelectedMetadataProvider` / `SelectedTrackerService` (provider is a screen-level choice, not per-row) |

`Views/DetailBand.axaml` inline meta row — **new Language flag**:
- `<pbc:BrandMark Family="Language" Value="{Binding LanguageIso}" ShowText="False" MarkSize="13"
  IsVisible="{Binding HasLanguage}" VerticalAlignment="Center"/>` placed after the AgeRating mark.
- `DetailBandViewModel` gains `LanguageIso` (`string?`) + `HasLanguage`, set in `LoadIssue` from
  `issue.LanguageISO`; in `LoadSeries` from the single distinct `Issue.LanguageISO` across the
  series (blank when issues disagree or none set). `Series` has no language column - per-issue only.

Publisher / Format / AgeRating marks already render in the band (Part 1 left them). Manga detail
inherits the same shared band - no separate change.

## P2-B. `BookDetailScreen` icon/glyph pass

`Views/BookDetailScreen.axaml` + `ViewModels/BookDetailScreenViewModel.cs`:
- `Actions`: book mode - `Continue` -> `Symbol.Play`, `Edit` -> `Symbol.Edit`, `Reveal in Explorer`
  -> `Symbol.FolderOpen`, `Export Annotations` -> `Symbol.ArrowExportLtr` (verify; fallback
  `Symbol.Save`). Series mode - `Edit series` / `Edit all books` -> `Symbol.Edit`.
- Back link: strip the literal "arrow + space" prefix from both `BackLabel` assignments in the VM
  (~line 325 and ~line 429); wrap the `Button` content with a leading
  `<fi:SymbolIcon Symbol="ArrowLeft" .../>` + `<TextBlock Text="{Binding BackLabel}"/>`.
  **Update the 3 `BookDetailScreenViewModelTests` assertions** (drop the leading arrow).
- Section titles (`TextBlock.sectionTitle`): leading `PbIconSizeXs` glyph, `PbTextFaintBrush` -
  `READING PROGRESS` -> `BookClock` (fallback `Clock`), `CHAPTERS` -> `TextBulletList`,
  `BOOKMARKS` -> `Bookmark`.
- `FinishedToggleLabel` button -> leading `Symbol.CheckmarkCircle`. Bookmark rows -> leading
  `Symbol.Bookmark` (`PbTextFaintBrush`).
- **Format mark**: add rows to `Assets/Marks/format-aliases.tsv` - `epub`, `pdf`, `fb2`, `mobi`,
  `azw3`, `cbz`, `cbr` - each with a `symbol` column value (`BookOpen` / `DocumentPdf` /
  `Document` ... verify names) + a label. Then the band's existing `BrandMark Family="Format"`
  renders a real glyph for book formats; also feed `Band.FormatText = FormatBadge` in
  `BookDetailScreenViewModel.LoadBook` so the band shows it (today it only sets `Band.StatusText`).
- Books have **no `ReadingStatus`** (it's a `Series` field; books use the `Finished` toggle) -
  `IDetailHeaderSource.ReadingStatus` / `ReadingStatusPicker` stay `null` for the book VM, no
  reading-status UI on this screen.

## P2-C. Reading-status setter - hero **and** band

A shared picker, embedded in both places:

- **`ReadingStatusPickerViewModel`** (new, `ViewModels/`): owns the write.
  - `IReadOnlyList<ReadingStatusOption> Options` - one per `ReadingStatus` value incl. a
    "Not set" / clear entry; each `{ ReadingStatus Value, string Label, Symbol Glyph, bool IsChecked }`
    (labels/glyphs from `MarkResolver.ResolveReadingStatus`'s own table so hero mark, band mark and
    menu all read identically).
  - `string? CurrentValue` (enum name, `null` for `Unknown`) + `bool HasStatus`.
  - `[RelayCommand] void Set(ReadingStatus)` - writes `series.ReadingStatus`, updates `CurrentValue`,
    raises change. Constructed with `(int seriesId, Func<PaperbunkrDbContext> ctxFactory, Action onChanged)`
    - the `onChanged` lets the host refresh the hero `MetaLine` / band value; a `ctxFactory` seam
    keeps it unit-testable (same pattern as `DetailTabsViewModel`).
- **`ReadingStatusPicker.axaml`** (new UserControl - **add the `.axaml.cs` code-behind in the same
  step**, per CLAUDE.md's AVLN2000 gotcha): a flat `Button` (`Cursor=Hand`) whose content is the
  `<pbc:BrandMark Family="ReadingStatus" Value="{Binding CurrentValue}" ShowText="True"/>` when
  `HasStatus`, else a faint `<fi:SymbolIcon Symbol="BookOpen"/> + "Set status"` ghost. `Button.Flyout`
  -> `<Flyout>` (not `MenuFlyout`/`ContextMenu` - those don't render in this Avalonia build; match
  `BookDetailScreen`'s working delete-confirm `Button.Flyout` pattern) containing an `ItemsControl`
  of `Options`, each a flat button = glyph + label + a check for `IsChecked`, `Command` = `SetCommand`.
- **`IDetailHeaderSource`**: add `ReadingStatusPickerViewModel? ReadingStatusPicker => null;`
  (default member, like `Synopsis`). `DetailScreenViewModel` / `MangaDetailScreenViewModel`
  construct one in `LoadSeries` (replace the prior) and return it; `BookDetailScreenViewModel`
  / `HomeSpotlightHeaderSource` inherit `null`.
- **`DetailHero.axaml`**: replace the read-only reading-status `BrandMark` (added in Part 1) with
  `<views:ReadingStatusPicker DataContext="{Binding ReadingStatusPicker}"
  IsVisible="{Binding ReadingStatusPicker, Converter={x:Static conv:ObjectConverters.IsNotNull}}"/>`.
- **`DetailBand.axaml`**: same control; `DetailBandViewModel` gains a
  `ReadingStatusPickerViewModel? ReadingStatusPicker` set by the host alongside `LoadSeries`
  (the band stays DB-free - it only holds the reference the host built). Replace the Part 1
  read-only band `BrandMark` with the picker.
- Deliberate Paperbunkr deviation - CE has no reading-status concept; this extends
  `2026-08-19-metadata-model-reading-status-design.md`'s field to the detail screen, same as
  Library's tile context menu already does.

## P2-D. Testing

- `MarkResolverTests`: `ResolveFormat("epub")` / `"pdf"` etc -> `MarkKind.Glyph` (or `SvgAsset`),
  non-`None`.
- `DetailBandViewModelTests`: `LanguageIso` / `HasLanguage` after `LoadIssue` (set) and
  `LoadSeries` (single vs mixed).
- `ReadingStatusPickerViewModelTests` (new): `Options` checked-state tracks `CurrentValue`;
  `SetCommand` writes the DB row and flips `CurrentValue` / `HasStatus`; "Not set" clears to
  `Unknown`.
- `DetailScreenViewModelTests` / `MangaDetailScreenViewModelTests`: `ReadingStatusPicker` is
  non-null after `LoadSeries`; invoking its `SetCommand` round-trips and the hero
  `MetaLine`/`ReadingStatus` + `Band` reflect it.
- `BookDetailScreenViewModelTests`: back label has no leading arrow (update 3 existing); `Actions`
  carry the expected `Icon`s; `ReadingStatusPicker` is `null`.
- Full suite is env-flaky (see [[project_paperbunkr_full_suite_headless_flake]]) - verify via
  targeted `--filter` runs + a launch smoke check.

---

# Part 3 (2026-09-04): sizing + BrandMark render quality

- **Glyph scale bumped one stop** - `PbIconSize{Xs..Xl}` = 14/16/18/24/40 (was 12/14/16/20/32).
  One `App.axaml` edit scales every `fi:SymbolIcon` in the app.
- **BrandMark SVG render quality** (user: provider/publisher logos "terrible quality" on hi-DPI):
  - `SvgMarkRenderer.Render` fits by **height**, not longest side, so wide publisher wordmarks
    render at full display resolution.
  - `BrandMark.Rebuild` supersamples **×4 (min 64px)** into the renderer, was ×2.
  - `Styles/Marks.axaml` `<Image>` gets `RenderOptions.BitmapInterpolationMode="HighQuality"`.
  - `MarkSize` literals on the detail band / Details tab / ReadingStatusPicker bumped ~2px.
- Tests unchanged - `SvgMarkRendererTests` portrait-aspect assertion still holds under height-fit;
  80/80 in the `SvgMarkRenderer|BrandMark|MarkResolver` slice.

---

# Part 4 (2026-09-04): Kavita-style hero metadata badge row

User wants publisher / format / age-rating (+ status, year, language) shown as icon chips in the
hero foreground next to the title (Kavita reference), not tucked in the band below. **Pages and
reading-time badges explicitly cut.**

- **`Models/DetailMetaBadge.cs`** (new) - `record DetailMetaBadge(string Text, Symbol? Icon, MarkFamily? Mark, string? MarkValue)`
  with non-null projections `IconGlyph` / `MarkOrDefault` for the compiled bindings, `IsMark`, and a
  static `Build(publisher, statusLabel, isComplete, year, format, ageRating, languageIso)` →
  ordered list (publisher mark, status glyph, year glyph, format mark, age-rating mark, language flag;
  each omitted when its source is blank).
- **`IDetailHeaderSource`**: `IReadOnlyList<DetailMetaBadge> MetaBadges => Array.Empty<...>();` +
  `bool HasMetaBadges => MetaBadges.Count > 0;` (default members).
- **`DetailHero.axaml`**: the `MetaLine` `TextBlock` row becomes a `WrapPanel` = the
  `ReadingStatusPicker` + an `ItemsControl` of `MetaBadges` (each a `Border.heroBadge` chip holding
  either a `BrandMark` or `fi:SymbolIcon`+text). `MetaLine` text kept as the fallback, shown only
  when `!HasMetaBadges` (Home spotlight).
- **Host VMs**: `DetailScreenViewModel` (LoadSeries + RefreshForSelection - series aggregate vs
  focused issue), `MangaDetailScreenViewModel` (LoadSeries), `BookDetailScreenViewModel` (format
  mark + year only) build `MetaBadges` via `DetailMetaBadge.Build`. `HomeSpotlightHeaderSource`
  unchanged (inherits empty → keeps its one-line `MetaLine`).
- **Band cleanup**: the inline meta row drops Publisher / Status / Year / Format / AgeRating /
  Language / ReadingStatus rendering (all now in the hero); `ReadingStatusPicker` is hero-only now
  (P2's dual placement collapses). The band VM properties stay populated (no VM-test churn); only
  the XAML that rendered them in `DetailBand.axaml` is removed. Band keeps: content-type picker,
  synopsis, tag groups, virtual tags, SpecialMarks.
- **Tests**: `DetailMetaBadge.Build` unit test (ordering + omit-when-blank); `MetaBadges` contents
  per host VM after LoadSeries / focus; targeted `--filter` runs.
