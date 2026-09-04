# Detail-screen icons & glyphs — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md*

## Status (2026-09-04)

Steps 1–11 implemented + unit tests written. **Both `Paperbunkr.App` and `Paperbunkr.App.Tests`
build with 0 errors.** All feature tests pass in targeted runs:
`--filter MarkResolverTests|BrandMarkRenderSmokeTests|IssueCardSampleTests|DetailScreenViewModelTests|DetailTabsViewModelTests|MangaDetailScreenViewModelTests`
→ 184–192 passed / 0 failed across several runs; a 191-test slice adding `LibraryScreenViewModelTests`
etc. also green.

**Full-suite run (Step 12) not obtainable right now** — the entire `dotnet test` invocation
mass-fails (~1030–1080 / 1669) via an Avalonia headless-bootstrap thread-affinity crash that
**reproduces on a pristine `git worktree` at committed HEAD** (2–3 concurrent Claude sessions were
building at the time). Environmental, not this feature. See
`[[project_paperbunkr_full_suite_headless_flake]]`. Re-run the full suite when the machine is quiet.

One `App.axaml.cs` edit here is **not** part of this feature: added `using Paperbunkr.Data.Entities;`
to unblock a concurrent session's incomplete "behavior-settings-batch2" code that referenced
`ActivityJobKind` without the import. Harmless; drop it if that session adds its own.

**Deviation from design:** Step 4 back-link icon applied to comic + manga only, **not**
`BookDetailScreen` — its back label is a data-bound string with a `"← "` prefix and 3 tests
(`BookDetailScreenViewModelTests`) assert on it; icon-ifying it is out of the "Book detail chrome is
out of scope" boundary.


Ordered so each step compiles and tests green on its own. Steps 1–2 are prerequisites for the
markup steps; the rest are largely independent.

FluentIcons `Symbol` names in the design are best-effort — **verify each against the
`FluentIcons.Common.Symbol` enum as you write it** (IDE completion / build error) and substitute
the nearest sibling (e.g. `Book` for `BookOpen`, `ArrowClockwise` for `ArrowSync`). The design's
fallback column is the first substitute to try.

---

## Step 1: Icon-size tokens
**Files:** `src/Paperbunkr.App/App.axaml` (edit)
**What:** Add `PbIconSizeXs=12 / Sm=14 / Md=16 / Lg=20 / Xl=32` as `<x:Double>` resources next to
the `PbFontSize*` block (~line 85), with the one-line comment from design §1.
**Depends on:** none
**Verify:** `dotnet build src/Paperbunkr.App`. No test.

## Step 2: `DetailHeroAction.Icon` + hero template
**Files:** `src/Paperbunkr.App/Models/DetailHeroAction.cs` (edit),
`src/Paperbunkr.App/Views/DetailHero.axaml` (edit)
**What:**
- Add trailing param `FluentIcons.Common.Symbol? Icon = null` to the `DetailHeroAction` record
  (`using FluentIcons.Common;`). All 4 existing call sites still compile (named args / positional
  up to `IsEnabled`).
- In `DetailHero.axaml` the `Actions` `ItemsControl.ItemTemplate` button: replace
  `Content="{Binding Label}"` with an explicit `<StackPanel Orientation="Horizontal" Spacing="6">`
  containing a leading `<fi:SymbolIcon Symbol="{Binding Icon}" FontSize="{StaticResource PbIconSizeSm}"
  IsVisible="{Binding Icon, Converter={x:Static ObjectConverters.IsNotNull}}"/>` then
  `<TextBlock Text="{Binding Label}"/>`. Add `xmlns:fi="using:FluentIcons.Avalonia"`.
  Confirm a null `Symbol` with `IsVisible=false` renders nothing (no blank glyph box); if it still
  reserves space, gate via a `HasIcon` bool on the record instead.
**Depends on:** Step 1
**Verify:** `dotnet build`; existing `DetailScreenViewModelTests` / `MangaDetailScreenViewModelTests`
/ `HomeScreenViewModelTests` stay green.

## Step 3: Hero action icons per screen (comic, manga, home)
**Files:** `src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/HomeSpotlightHeaderSource.cs` (edit)
**What:** pass `Icon:` on each `DetailHeroAction` —
- comic `Actions`: primary (both focused-issue and series-fallback) `Symbol.Play`; Edit
  `Symbol.Edit`; Change Cover `Symbol.Image`.
- manga `Actions`: `Play` / `Edit` / `Image`.
- home `_actions`: `"Read now"` → `Symbol.Play`.
**Depends on:** Step 2
**Verify:** new asserts in `DetailScreenViewModelTests` (extend the
`FocusingOneIssue_PrimaryHeroAction…` test + a series-mode test), `MangaDetailScreenViewModelTests`,
`HomeScreenViewModelTests` — `Actions[n].Icon` is the expected `Symbol`.

## Step 4: Back link
**Files:** `src/Paperbunkr.App/Views/DetailScreen.axaml` (edit),
`src/Paperbunkr.App/Views/MangaDetailScreen.axaml` (edit),
`src/Paperbunkr.App/Views/BookDetailScreen.axaml` (edit — check it uses the same `backLink`)
**What:** replace `Content="← Back to Library"` with a `StackPanel` (icon `ArrowLeft`
`PbIconSizeSm` + `TextBlock "Back to Library"`). `Styles/DetailChrome.axaml` `Button.backLink`
style untouched. Add `xmlns:fi` where missing. (Book detail: only the back link changes there —
still in scope as a shared-control consistency fix, not new chrome.)
**Depends on:** Step 1
**Verify:** `dotnet build`; manual — arrow shows, click still navigates back.

## Step 5: Tab-strip icons
**Files:** `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit),
`src/Paperbunkr.App/Views/MangaDetailScreen.axaml` (edit)
**What:** each `Button Classes="tab"`: wrap content in `StackPanel Orientation="Horizontal"
Spacing="7"` = leading `fi:SymbolIcon` (`PbIconSizeSm`, no explicit Foreground so it inherits the
`.tab` / `.tab.active` setters) + existing label `TextBlock` + existing `TextBlock.tabCount`.
Symbols: Issues `BookOpen`, Specials `Star`, Chapters `TextBulletListLtr`, Related `Link`,
Details `Info`, Activity `Pulse` (verify names; `Book`/`List`/`Link`/`Info`/`Pulse` fallbacks).
**Depends on:** Step 1
**Verify:** `dotnet build`; manual — icons dim/undim with active state, counts still show.

## Step 6: Issue-tile status glyph (`TileGlyph`)
**Files:** `src/Paperbunkr.App/Models/IssueCardSample.cs` (edit),
`src/Paperbunkr.App/Views/DetailTabs.axaml` (edit)
**What:**
- Add `public enum IssueTileGlyph { None, Read, InProgress }` (new file
  `Models/IssueTileGlyph.cs`) and `IssueCardSample.TileGlyph` computed:
  `IsRead ? Read : IsInProgress ? InProgress : None`.
- Poster tile template: a `fi:SymbolIcon Symbol="CheckmarkCircle"` (`PbSuccessBrush`,
  `PbIconSizeMd`) `HorizontalAlignment=Right VerticalAlignment=Top` `Margin="0,4,4,0"` inside the
  cover `Grid`, `IsVisible` when `TileGlyph == Read` (use `ObjectConverters.Equal` +
  `ConverterParameter`, the pattern already used in this file). `ToolTip.Tip="Read"`,
  `AutomationProperties.AccessibilityView="Raw"`.
- List row template: prepend a `20`-wide column; `fi:SymbolIcon` `CheckmarkCircle` (success) when
  `Read`, `CircleHalfFill` (accent) when `InProgress`, else collapsed.
- Card tile template: small `CheckmarkCircle` (`PbIconSizeXs`, success) before the `FullTitle`
  `TextBlock` when `Read`.
**Depends on:** Step 1
**Verify:** `DetailTabsViewModelTests` — `TileGlyph` returns each value for the matching
`IsRead`/`ReadFraction` inputs (build `IssueCardSample` directly). Manual: read tiles show ✓.

## Step 7: Details-tab section labels + `railAdd` / segment / reading-mode chrome
**Files:** `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit),
`src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/DetailTabsViewModelTests.cs` (edit)
**What:**
- Section labels (`PUBLISHER`, `READING MODE`, `External Metadata`, `Trackers`): leading
  `fi:SymbolIcon` (`PbIconSizeSm`, `PbTextFaintBrush`) per design §5 table.
- `railAdd` buttons (`+ Add to Continuity`, `+ Add to Collection`, `+ Link External Metadata`,
  `+ Link for Tracking`): content → `StackPanel` with leading `Symbol.Add` + text (drop the
  literal `+ `). `Sync to Trackers` → leading `Symbol.ArrowSync`.
- `Button.segItem` Poster/List/Card: prepend `Grid` / `TextBulletListLtr` / `ContentView` icon,
  keep text. (`segItem` content is currently a plain string — switch to `StackPanel`.)
- Reading-mode toggle: in `DetailTabsViewModel.SetReadingModeLabel`, drop the trailing `" ▾"`
  from all five label strings. In `DetailTabs.axaml` add a trailing
  `fi:SymbolIcon Symbol="ChevronDown"` (`PbIconSizeXs`) after the toggle button's text (wrap its
  `Content` binding in a `StackPanel`, or put the icon as a sibling next to the `Button`).
- Update `DetailTabsViewModelTests` `ReadingModeLabel` assertions (`"Left to Right"` not
  `"Left to Right ▾"`, etc.).
**Depends on:** Step 1
**Verify:** updated `DetailTabsViewModelTests`; `dotnet build`; manual.

## Step 8: Manga chapter chrome — sort pills, sort-direction glyph, read glyph
**Files:** `src/Paperbunkr.App/Views/MangaDetailScreen.axaml` (edit),
`src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Models/ChapterRowSample.cs` (edit),
`src/Paperbunkr.App.Tests/MangaDetailScreenViewModelTests.cs` (edit)
**What:**
- `MangaDetailScreenViewModel`: replace `string ChapterSortDirectionGlyph` ("↑"/"↓") with
  `Symbol ChapterSortDirectionSymbol => ChapterSortDirection == SortDirection.Ascending ?
  Symbol.ArrowSortUp : Symbol.ArrowSortDown` (keep raising it in
  `OnChapterSortDirectionChanged`). In XAML the sort-direction pill's `Content` becomes a
  `fi:SymbolIcon Symbol="{Binding ChapterSortDirectionSymbol}"` (`PbIconSizeXs`).
- Sort pills `By Number` / `By Date`: leading `TextSortAscending` / `CalendarLtr` icon + text.
  Filter pills (All/Unread/Bookmarked/Missing) unchanged.
- `ChapterRowSample`: add `public bool ShowReadGlyph => IsRead && !IsInProgress;`. Chapter-row
  grid: a faint `fi:SymbolIcon Symbol="Checkmark"` (`PbTextFaintBrush`, `PbIconSizeXs`) in a spare
  column when `ShowReadGlyph`, beside the existing `Bookmark` / `Warning` icons.
**Depends on:** Step 1
**Verify:** `MangaDetailScreenViewModelTests` — `ChapterSortDirectionSymbol` flips on
`ToggleChapterSortDirectionCommand`; `ChapterRowSample.ShowReadGlyph` truth table. `dotnet build`.

## Step 9: `BrandMark` per-glyph colour + `ReadingStatus` / `ScanGroup` families
**Files:** `src/Paperbunkr.App/Controls/BrandMark.cs` (edit),
`src/Paperbunkr.App/Services/MarkResolver.cs` (edit),
`src/Paperbunkr.App/Styles/Marks.axaml` (edit),
`src/Paperbunkr.App.Tests/MarkResolverTests.cs` (edit),
`src/Paperbunkr.App.Tests/BrandMarkRenderSmokeTests.cs` (edit)
**What:**
- `MarkFamily` enum += `ReadingStatus`, `ScanGroup`.
- `BrandMark.Rebuild()` `switch` += both cases → `ResolveReadingStatus(Value)` /
  `ResolveScanGroup(Value)`.
- `BrandMark`: add `GlyphBrushProperty` direct-property (`IBrush?`), computed in `Rebuild()` via
  the existing `TryBrush(spec.Foreground)` (non-null only when `spec.Foreground` is a `#hex`).
- `Marks.axaml`: the glyph `fi:SymbolIcon` `Foreground` → bind `GlyphBrush` with
  `TargetNullValue={TemplateBinding Foreground}` (via `{Binding GlyphBrush, RelativeSource=
  {RelativeSource TemplatedParent}, TargetNullValue=...}` — mirror the existing `ChipBackground`
  binding shape in this file).
- `MarkResolver.ResolveReadingStatus(string?)`: `Enum.TryParse<ReadingStatus>` → the design §8a
  table (glyph + label + hex, hex commented with its `Pb*Color` token name); `Unknown` /
  parse-fail → `MarkSpec.None`.
- `MarkResolver.ResolveScanGroup(string?)`: blank → `None`; else
  `new MarkSpec(MarkKind.Glyph, Glyph: Symbol.PeopleTeam, Text: value.Trim())`.
- Tests: `MarkResolverTests` per-enum + blank/garbage; `BrandMarkRenderSmokeTests` one
  `ReadingStatus` + one `ScanGroup` render case.
**Depends on:** none (pure resolver/control; no screen wiring yet)
**Verify:** `MarkResolverTests`, `BrandMarkRenderSmokeTests`, full `Paperbunkr.App.Tests`.

## Step 10: Reading-status mark on hero + band; scan-group mark on chapter rows
**Files:** `src/Paperbunkr.App/ViewModels/IDetailHeaderSource.cs` (edit),
`src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs` (edit),
`src/Paperbunkr.App/Views/DetailHero.axaml` (edit),
`src/Paperbunkr.App/Views/DetailBand.axaml` (edit),
`src/Paperbunkr.App/Views/MangaDetailScreen.axaml` (edit),
`src/Paperbunkr.App.Tests/DetailScreenViewModelTests.cs`,
`src/Paperbunkr.App.Tests/MangaDetailScreenViewModelTests.cs`,
`src/Paperbunkr.App.Tests/DetailBandViewModelTests.cs` (edits)
**What:**
- `IDetailHeaderSource`: add `string? ReadingStatus => null;` (default member, like `Synopsis`).
- `DetailScreenViewModel` + `MangaDetailScreenViewModel`: override `ReadingStatus` →
  `_readingStatus` field set in `LoadSeries` to
  `series.ReadingStatus == ReadingStatus.Unknown ? null : series.ReadingStatus.ToString()`;
  `OnPropertyChanged(nameof(ReadingStatus))` there (add to the existing raise sites / hero
  refresh).
- `DetailHero.axaml`: `controls:BrandMark Family="ReadingStatus" Value="{Binding ReadingStatus}"
  ShowText="True" MarkSize="{StaticResource PbIconSizeSm}"
  IsVisible="{Binding ReadingStatus, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"` on
  the meta-line `StackPanel` (row 1, after `MetaLine` — restructure that `TextBlock` into a
  horizontal `StackPanel` if needed).
- `DetailBandViewModel`: `ReadingStatusValue` (`string?`) + `HasReadingStatus`, set in
  `LoadSeries` and `LoadIssue` (issue → `issue.Series?.ReadingStatus`).
- `DetailBand.axaml`: `pbc:BrandMark Family="ReadingStatus" …` in the inline meta row right after
  the `StatusText` `TextBlock`, gated on `HasReadingStatus`.
- `MangaDetailScreen.axaml`: swap the chapter-row `ScanInformation` `TextBlock` for
  `pbc:BrandMark Family="ScanGroup" Value="{Binding ScanInformation}" ShowText="True"
  MarkSize="{StaticResource PbIconSizeXs}" Foreground="{DynamicResource PbTextFaintBrush}"
  IsVisible="{Binding HasScanInformation}"`. Add `xmlns:pbc="using:Paperbunkr.App.Controls"`.
**Depends on:** Step 9 (needs the new families); Step 2/3 don't block this.
**Verify:** `DetailScreenViewModelTests` / `MangaDetailScreenViewModelTests` — `ReadingStatus`
null for `Unknown`, enum name otherwise; `DetailBandViewModelTests` — `HasReadingStatus` /
`ReadingStatusValue` after `LoadSeries` / `LoadIssue`. Manual: set a series to "Reading" from the
Library context menu, open its detail screen, confirm the amber book glyph in hero + band.

## Step 11: App-wide icon-size token sweep
**Files:** every `src/Paperbunkr.App/Views/*.axaml` and `src/Paperbunkr.App/Styles/*.axaml` with a
literal `fi:SymbolIcon … FontSize="<number>"`.
**What:** replace each literal with `{StaticResource PbIconSize…}` per the design §7 bucket table.
Do it as a mechanical pass: `rg -n 'SymbolIcon[^>]*FontSize="\d' src/Paperbunkr.App` to enumerate,
edit each, then re-run the grep — **must return nothing** (except deliberately-kept giant
empty-state ones, which also move to `PbIconSizeXl`). Leave `MarkSize=` and `TextBlock`/`Button`
`FontSize` alone.
**Depends on:** Step 1. Do this **last** so the earlier steps' new markup (already token-based)
isn't re-touched.
**Verify:** `dotnet build`; full `Paperbunkr.App.Tests` green; launch the app and eyeball a few
dense screens (Library toolbar, Issue editor overlay, Activity drawer) for icon-size regressions.

## Step 12: Review-checklist pass + full suite
**Files:** none (QA)
**What:** run `~/.claude/skills/avalonia/avalonia-pro-max/review-checklist/SKILL.md` against the
diff — focus on: no hardcoded hex in XAML (the resolver hex in Step 9 is the one allowed
exception, and it's commented), every decorative glyph has `AccessibilityView="Raw"` + tooltip,
every icon colour is `DynamicResource`/inherited, `dotnet build -t:Rebuild` weave actually runs
(CLAUDE.md gotcha), full `dotnet test src/Paperbunkr.App.Tests` + `src/Paperbunkr.Data.Tests`.
**Depends on:** all.
**Verify:** checklist clean; both test projects green; app launches and both detail screens +
Home render correctly in light and dark skins.

---

## Test strategy summary

- **Unit (xUnit, existing per-VM/-resolver test classes):** `DetailHeroAction.Icon` plumbing;
  `IssueCardSample.TileGlyph`; `ChapterRowSample.ShowReadGlyph`; `ChapterSortDirectionSymbol`;
  `ReadingModeLabel` (no `▾`); `MarkResolver.ResolveReadingStatus` / `ResolveScanGroup`;
  `IDetailHeaderSource.ReadingStatus` / `DetailBandViewModel` status props.
- **Render smoke (`BrandMarkRenderSmokeTests`, Avalonia.Headless):** `ReadingStatus` + `ScanGroup`
  families render without throwing.
- **No new headless screen tests** — icon layout is a visual concern; verified by launching the
  app (both detail screens, Home, light+dark), consistent with every prior detail-screen polish
  pass in this project.
- **Regression:** full `Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests` after Step 11 and Step 12.

---

# Part 2 plan — brand-mark coverage + Book detail + actionable reading-status
*Implements Part 2 of docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md*

Verify every FluentIcons `Symbol` name against the enum as you write it (probe project in
scratchpad, or IDE completion); substitute the nearest sibling on a miss.

## P2-1: Format aliases for book formats
**Files:** `src/Paperbunkr.App/Assets/Marks/format-aliases.tsv` (edit)
**What:** append tab-separated rows: `epub`/`pdf`/`fb2`/`mobi`/`azw3`/`cbz`/`cbr` with a `symbol`
column (`BookOpen` for epub/fb2/mobi/azw3, `DocumentPdf` for pdf, `Document`/`Album` for cbz/cbr -
verify names) and a `label` (EPUB / PDF / ...). No new SVG assets.
**Depends on:** none
**Verify:** `MarkResolverTests` - `ResolveFormat("epub")` etc is `MarkKind.Glyph`, non-`None`.

## P2-2: Service marks on the Details tab (external-metadata + tracker link chips + pickers)
**Files:** `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit),
`src/Paperbunkr.App/Models/TrackerLinkSample.cs` (edit - add `string ServiceName => Service.ToString();`)
**What:** per design P2-A table - swap the four `TextBlock`s for `pbc:BrandMark Family="Service"`;
give both provider `ComboBox`es an `ItemTemplate` with a service mark; prepend a mark-only
`BrandMark` (bound to `SelectedMetadataProvider` / `SelectedTrackerService`) to the two
search-result row templates.
**Depends on:** none
**Verify:** `dotnet build`; manual - AniList/MangaBaka logos show on link chips + dropdowns.

## P2-3: Language flag in the band
**Files:** `src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs` (edit),
`src/Paperbunkr.App/Views/DetailBand.axaml` (edit)
**What:** `LanguageIso` (`string?`) `[ObservableProperty]` + `HasLanguage` +
`OnLanguageIsoChanged` raise; set in `LoadIssue` (`issue.LanguageISO`) and `LoadSeries`
(single distinct `Issue.LanguageISO`, else null). Add the `BrandMark Family="Language"` after the
AgeRating mark, gated on `HasLanguage`.
**Depends on:** none
**Verify:** `DetailBandViewModelTests` - `LanguageIso`/`HasLanguage` for set / mixed / none.

## P2-4: `ReadingStatusPickerViewModel` + `ReadingStatusOption`
**Files:** `src/Paperbunkr.App/ViewModels/ReadingStatusPickerViewModel.cs` (new),
`src/Paperbunkr.App/Models/ReadingStatusOption.cs` (new)
**What:**
- `ReadingStatusOption` record: `ReadingStatus Value`, `string Label`, `Symbol Glyph`, `bool IsChecked`.
- `ReadingStatusPickerViewModel(int seriesId, Func<PaperbunkrDbContext> ctxFactory, Action? onChanged = null)`
  - a public parameterless-ish convenience ctor `(int seriesId, Action? onChanged)` delegating with
    `PaperbunkrDb.CreateContext`.
  - `CurrentValue` (`string?`), `HasStatus`, `ObservableCollection<ReadingStatusOption> Options`
    rebuilt whenever current changes; labels/glyphs pulled from a shared helper (extract
    `MarkResolver.ResolveReadingStatus`'s table into a `static (Symbol, string) ReadingStatusGlyph
    .For(ReadingStatus)` the resolver also calls, so there's one source of truth).
  - `[RelayCommand] void Set(ReadingStatus value)` - opens context, sets `series.ReadingStatus`,
    `SaveChanges`, updates `CurrentValue`/`HasStatus`/`Options`, invokes `onChanged`.
- Seed current from the DB in the ctor.
**Depends on:** P2-1 unrelated; needs the shared glyph-table helper (small refactor of MarkResolver).
**Verify:** `ReadingStatusPickerViewModelTests` (new) - option check-state, `Set` round-trips,
"Not set" -> `Unknown`.

## P2-5: `ReadingStatusPicker` UserControl (+ code-behind, same step)
**Files:** `src/Paperbunkr.App/Views/ReadingStatusPicker.axaml` (new),
`src/Paperbunkr.App/Views/ReadingStatusPicker.axaml.cs` (new - minimal
`partial class ReadingStatusPicker : UserControl { public ReadingStatusPicker() => InitializeComponent(); }`)
**What:** `x:DataType="vm:ReadingStatusPickerViewModel"`. Flat `Button` (`Classes` reuse where
possible), content = `pbc:BrandMark Family="ReadingStatus" Value="{Binding CurrentValue}"
ShowText="True"` when `HasStatus`, else ghost `fi:SymbolIcon "BookOpen"` + "Set status".
`Button.Flyout` -> `Flyout` -> `ItemsControl ItemsSource="{Binding Options}"`; item template =
flat button (glyph + label + `Checkmark` when `IsChecked`), `Command={Binding
$parent[ItemsControl].((vm:ReadingStatusPickerViewModel)DataContext).SetCommand}`
`CommandParameter="{Binding Value}"`.
**Depends on:** P2-4. **First fresh `x:Class` this round - build with `-t:Rebuild` or `rm` the App
dll after, and launch to confirm the XAML weave (CLAUDE.md gotcha).**
**Verify:** `dotnet build -t:Rebuild src/Paperbunkr.App`; launch, open a series detail, click the
status chip -> flyout appears, pick a value -> persists + chip updates.

## P2-6: Wire the picker into `IDetailHeaderSource` + hero + band + host VMs
**Files:** `src/Paperbunkr.App/ViewModels/IDetailHeaderSource.cs`,
`src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs`,
`src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs`,
`src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs`,
`src/Paperbunkr.App/Views/DetailHero.axaml`, `src/Paperbunkr.App/Views/DetailBand.axaml` (edits)
**What:**
- `IDetailHeaderSource`: `ReadingStatusPickerViewModel? ReadingStatusPicker => null;`.
- `DetailScreenViewModel` / `MangaDetailScreenViewModel`: build `_readingStatusPicker` in
  `LoadSeries` with `onChanged` = re-run the reading-status refresh (`_readingStatus` +
  `OnPropertyChanged(nameof(IDetailHeaderSource.ReadingStatus))` + `Band.ReadingStatusValue`);
  expose via the interface member. Pass it to `Band` (new
  `DetailBandViewModel.ReadingStatusPicker` settable ref, set right after `Band.LoadSeries`).
- `DetailBandViewModel`: hold `ReadingStatusPicker` (nullable) + `HasReadingStatusPicker`;
  the Part-1 `ReadingStatusValue`/`HasReadingStatus` stay as the fallback when no picker (book mode).
- `DetailHero.axaml` + `DetailBand.axaml`: replace the Part-1 read-only reading-status `BrandMark`
  with `<views:ReadingStatusPicker DataContext="{Binding ReadingStatusPicker}" IsVisible="{Binding
  ReadingStatusPicker, Converter={x:Static conv:ObjectConverters.IsNotNull}}"/>`; keep a
  fallback read-only `BrandMark` for the band when `ReadingStatusPicker is null` but
  `HasReadingStatus` (book detail path - though book has none, harmless).
**Depends on:** P2-4, P2-5.
**Verify:** `DetailScreenViewModelTests` / `MangaDetailScreenViewModelTests` - `ReadingStatusPicker`
non-null post-`LoadSeries`, `SetCommand` round-trips into hero + band; `BookDetailScreenViewModelTests`
- `ReadingStatusPicker` null.

## P2-7: Book detail icon/glyph pass
**Files:** `src/Paperbunkr.App/Views/BookDetailScreen.axaml`,
`src/Paperbunkr.App/ViewModels/BookDetailScreenViewModel.cs`,
`src/Paperbunkr.App.Tests/BookDetailScreenViewModelTests.cs` (edits)
**What:** design P2-B - hero action `Icon`s; back link `ArrowLeft` glyph + strip `"<- "` from both
`BackLabel` assignments; section-title glyphs; `FinishedToggleLabel` + bookmark-row glyphs;
`Band.FormatText = FormatBadge` in `LoadBook`. Update the 3 `BackLabel` test assertions.
**Depends on:** P2-1 (format mark).
**Verify:** updated `BookDetailScreenViewModelTests`; `dotnet build`; manual - book detail chrome.

## P2-8: Review-checklist + targeted test sweep + launch
**What:** run `avalonia-pro-max/review-checklist` over the P2 diff (icon family, semantic colours,
`AccessibilityView="Raw"` on decorative glyphs, `AutomationProperties.Name` on the picker button,
no hardcoded hex except the documented resolver table). Targeted `dotnet test --filter` across
`MarkResolverTests|BrandMarkRenderSmokeTests|DetailBandViewModelTests|ReadingStatusPickerViewModelTests|DetailScreenViewModelTests|MangaDetailScreenViewModelTests|BookDetailScreenViewModelTests`
+ `Paperbunkr.Data.Tests`. Launch, click through all three detail screens' status chips + marks in
light and dark.
**Depends on:** all.

## Part 2 status (2026-09-04)

P2-1..P2-8 implemented. Both projects build 0 errors (`-t:Rebuild`, so the new
`ReadingStatusPicker` UserControl's XAML weave ran). Targeted tests green: 208+ passed across
`MarkResolverTests` / `BrandMarkRenderSmokeTests` / `DetailBandViewModelTests` /
`ReadingStatusPickerViewModelTests` (new) / `DetailScreenViewModelTests` /
`MangaDetailScreenViewModelTests` / `BookDetailScreenViewModelTests` / `DetailTabsViewModelTests` /
`IssueCardSampleTests`. Updated `BookDetailScreenViewModelTests` for the moved back-label (`←`
dropped) and Format-mark slot change. Full suite still env-flaky (see
`[[project_paperbunkr_full_suite_headless_flake]]`).

Minor scope trim: per-row service mark on the metadata/tracker **search-result** rows skipped
(the provider ComboBox above them now shows the mark; per-row was redundant clutter). Link chips +
ComboBoxes done.

## Part 4 status (2026-09-04)

Implemented. New `Models/DetailMetaBadge.cs`, `IDetailHeaderSource.MetaBadges`/`HasMetaBadges`,
hero WrapPanel of chips, band inline-row stripped to the content-type picker + special chips,
reading-status picker now hero-only. Host VMs (`DetailScreenViewModel` rebuilds on issue focus via
`UpdateBandIssueMarks`, `MangaDetailScreenViewModel`, `BookDetailScreenViewModel`) build the badge
list; `HomeSpotlightHeaderSource` inherits empty. New `DetailMetaBadgeTests` + badge assertions in
the 3 detail-VM test classes. `Series.IsComplete` is a computed getter - set `Status =
SeriesStatus.Completed` in tests. Annual-format issue = a "special", so focus it via
`Tabs.Specials`, not `Tabs.Issues`.
