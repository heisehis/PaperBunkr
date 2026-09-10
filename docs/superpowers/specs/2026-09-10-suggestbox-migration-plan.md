# SuggestBox migration — audit + plan (2026-09-10)

**STATUS: implemented.** All 15 files converted; `MultiValueAutoComplete` + both `WeightOptions` +
orphaned `*Options` arrays deleted; VM-wrapper tests added across ~10 test classes; App build +
targeted test suite green; XAML weave verified via splash render. On-screen click-through pending
(local shared dev DB is corrupted — unrelated). Cherry-picked foundation: `a03a29d` → `a3c0455`.

## Context

`a03a29d` ("Fix permanent UI-thread freeze on opening any metadata-editor dropdown", cherry-picked
here as `a3c0455`) shipped `Controls/SuggestBox` + its `Styles/FormControls.axaml` `ControlTheme`,
plus **two global freeze fixes** that stand on their own:

1. `FluentAvaloniaWorkarounds.SuppressColorValuesChangedHandler()` at startup — removes the root
   cause (FA's unconditional `ColorValuesChanged` → HighContrast `ResourcesChanged` → TwoWay
   `bool` `TemplateBinding` oscillation inside `Popup.Open()`).
2. `Win32PlatformOptions.OverlayPopups = true` — popups render in the owner window's surface, so
   there is no native popup HWND and no `PresentationSource.Dispose → Task.InternalWait`
   compositor-teardown hang.

That commit converted **only** the three metadata editors: `IssuePropertiesScreen`,
`BulkIssuePropertiesScreen`, `ReadingListPropertiesOverlay` (+ `TagEditRowViewModel.WeightText` /
`WeightNames`).

**Because of fixes 1 + 2, no other site needs `SuggestBox` for correctness.** This migration is a
consistency / idiom decision. The user has chosen to convert **every** remaining
`ComboBox` / `AutoCompleteBox` under `src/Paperbunkr.App/Views` ("Everything (1–14)"). This doc
records the exact per-site work and the per-site cost, so individual sites can still be vetoed at
plan review.

## `SuggestBox` API (from `Controls/SuggestBox.cs`)

- `Text` — `string?`, TwoWay by default.
- `Suggestions` — `IEnumerable<string>?`. **String only. No item template, no object items,
  no icon rows.** `FilteredItems` is `ObservableCollection<string>`.
- `Watermark` — `string?`.
- `IsMultiValue` — `bool`. Completes only the segment after the last comma (Genre/Tags/creator
  lists). Mirrors the deleted `MultiValueAutoComplete` behavior.
- `IsStrict` — `bool`. Text is not user-editable; the drop-down is the only way to set it. Focus
  opens the list. For closed enums / closed option sets.

### Established VM-wrapper pattern (from `TagEditRowViewModel`)

For any picker whose model value is **not** a `string`:

```csharp
// string view of the enum for the string-only SuggestBox
public string XxxText
{
    get => Xxx.ToString();                 // or Option.Label / Option.DisplayName
    set { if (Enum.TryParse<TEnum>(value, out var p)) Xxx = p; }   // unknown text ignored
}

private static readonly string[] XxxNamesCache = Enum.GetNames<TEnum>();
public string[] XxxNames => XxxNamesCache;  // instance passthrough — {Binding}, never {x:Static}

partial void OnXxxChanged(TEnum value) => OnPropertyChanged(nameof(XxxText));
```

XAML: `<pbc:SuggestBox IsStrict="True" Text="{Binding XxxText}" Suggestions="{Binding XxxNames}" />`
(`pbc` = `xmlns:pbc="clr-namespace:Paperbunkr.App.Controls"` — already declared in the 3 converted
files; add it where missing.)

Object option sets (`RoleOption`, `RelationTypeOption`, `ArcSourceOption`, …) use the same shape
but map on a **display string**: `get => Selected?.Label`, `set => Selected = Options.First(o =>
o.Label == value)`. This is safe **only when the display string is a stable unique key** — see
per-site risk notes below.

## Full inventory — 15 files

Legend: **S** = straight swap (already `string`, no wrapper) · **E** = enum wrapper · **O** =
object→string wrapper · **KEEP?** = flagged, decide at review.

| # | File | Instances | Binds to | Strategy | Notes / risk |
|---|------|-----------|----------|----------|--------------|
| 1 | `LibraryScreen.axaml` (add-issue overlay, ~1189) | 1 `AutoCompleteBox` | `NewIssueSeriesName : string` + `ExistingSeriesNames : IEnumerable<string>` | **S** | free-text, not strict. Clean. Last `AutoCompleteBox` outside the 3 editors — same original freeze exposure. |
| 2 | `Preferences/AppearanceSection.axaml` (~97) | 1 `ComboBox` | `SelectedFontFamily : string?` + `FontFamilies : ObservableCollection<string>` | **S** + `IsStrict` | family must exist. `OnSelectedFontFamilyChanged` hook already present — keep. Type-to-filter is a real UX win here. |
| 3 | `Preferences/ReaderSection.axaml` (37, 49, 56, 113, 124) | 5 `ComboBox` | `DefaultPageFitMode`/`ImageFitMode`, `DefaultPageLayoutMode`/`PageLayoutMode`, `PageTransitionStyle`, `ImageBackgroundMode` — 4 enums; `BackgroundColor : string` + `BackgroundColorPresets : string[]` — 1 string | **E** ×4, **S** ×1 | line 124 preset box is half of a compound row (preset `SuggestBox` + freeform hex `TextBox`) — keep both, preset is non-strict so a hex typed in it still round-trips. VM: `PreferencesScreenViewModel`. |
| 4 | `Preferences/AutomationSection.axaml` (36, 88) | 2 `ComboBox` | `ScheduledTaskNotificationLevel` enum (`PreferencesScreenViewModel`); `Mode : ScheduleMode` enum (per-row VM — find it) | **E** ×2 | keep `AutomationProperties.AutomationId="ScheduledTaskNotificationLevel"`. Row VM wrapper needs its own `OnModeChanged`. |
| 5 | `Preferences/AdvancedSection.axaml` (~32) | 1 `ComboBox` | `RenderingBackend : RenderBackend` enum | **E** | VM: `PreferencesScreenViewModel`. "restart to apply" note unaffected. |
| 6 | `DetailScreen.axaml` (~58) + `MangaDetailScreen.axaml` (~127) | 2 `ComboBox.contentTypePicker` | `SelectedContentType : ContentType` enum (`DetailScreenViewModel`, `MangaDetailScreenViewModel`) | **E** ×2 | `contentTypePicker` `Style` is colours/padding only — **no `ItemTemplate`**, so nothing rich is lost. The `Style` selector `ComboBox.contentTypePicker` must be retargeted to `pbc|SuggestBox.contentTypePicker` (check every setter is a `SuggestBox`/`TemplatedControl` property; `PlaceholderText`→ n/a, drop). Identical wrapper on both VMs — consider a shared `ContentTypePickerText` mixin or just duplicate (they don't share a base). |
| 7 | `ActivityDrawerView.axaml` (150, 152) | 2 `ComboBox` | `HistoryKind : ActivityHistoryKindOption`, `HistoryAge : ActivityHistoryAgeOption` — enums; `HistoryKindOptions`/`HistoryAgeOptions : Array` (`ActivityCenterViewModel`) | **E** ×2 | `Enum.GetNames(typeof(T))` — options are exposed as non-generic `Array` today; wrapper can expose `string[]`. Enum names are dev-facing (`LastDay` etc.) — check they read OK or add a `Label`. |
| 8 | `Preferences/KeyboardShortcutsSection.axaml` (~59) | 1 `ComboBox` | `PendingAddOption : KeyOption`, `AvailableKeyOptions : IReadOnlyList<KeyOption>` (`KeyBindingRowViewModel`) | **O** + `IsStrict` | `KeyOption` — check it has a stable unique display (key name). `AvailableKeyOptions` is filtered live (`.Where(o => !BoundKeys.Contains(o))`) so `XxxNames` must recompute with it — raise `XxxText`/`XxxNames` change from the same spots that already raise `AvailableKeyOptions`. |
| 9 | `SmartScreen.axaml` (140, 144, 153, 158) | 4 `ComboBox.conditionPicker` per condition row | `SelectedField : FieldOption`, `SelectedSearchMode`, `SelectedVirtualTag`, `SelectedOperator` — all objects; `FieldOptions`/`OperatorOptions`/`SearchModeOptions`/`VirtualTagOptions` per-row (`SmartListConditionViewModel`) | **O** ×4 **KEEP?** | Dense inline rule-builder. `FieldOption`/`OperatorOption` are closed catalogs with labels → mappable. **`VirtualTagOptions` are user-defined tag names — can collide / are dynamic.** `OperatorOptions` recomputes when `SelectedField` changes (already raises `OnPropertyChanged(nameof(OperatorOptions))`) — wrappers must piggy-back. `conditionPicker` `Style` (line 34) retarget. 4 wrappers × live interdependence = the highest-risk file. Recommend: convert Field/SearchMode/Operator, **keep `VirtualTag` as `ComboBox`** (or accept name-key). |
| 10 | `ReadingScreen.axaml` (292, 388, 433, 493) | 4 `ComboBox` **with `ItemTemplate`** | `SelectedArcSource : ArcSourceOption` ×2, `BulkRole`/`SelectedRoleOption : EventMembershipRoleOption` ×2 | **O** ×4 **KEEP?** | `ArcSourceOption(Key, DisplayName, …)` — `Key` is a stable unique key; map on `DisplayName` (also unique in the registry) → safe. `EventMembershipRoleOption.All` + `.Label` — closed, safe. **Cost: the `ComboBox.ItemTemplate` (source icon + name) is lost — dropdown becomes plain text, and the closed field loses its icon.** User has accepted this. |
| 11 | `DetailTabs.axaml` (460, 560, 659) | 3 `ComboBox` **with `ItemTemplate`** | `SelectedRelationTypeOption : RelationTypeOption`, `MetadataProviderOptions` selection, `TrackerServiceOptions : TrackingService` enum | **O** ×2 + **E** ×1 **KEEP?** | `RelationTypeOption.All` — closed, `.Label` safe. `TrackingService` enum — safe. **`MetadataProviderOptions`** — check shape; provider list may be plugin-dynamic. Icons in all three templates are lost. |
| 12 | `EventsScreen.axaml` (218, 347, 463, 530) | 4 `ComboBox` **with `ItemTemplate`** | role / relation-type option objects (`EventsScreenViewModel` + row VMs; `EventRelationTypeOptions`, `RoleOptions`) | **O** ×4 **KEEP?** | Same `RelationTypeOption` / role-option catalogs as #10/#11 — mappable & safe. Icon templates lost. |
| 13 | `CollectionPropertiesOverlay.axaml` (169, 187, 205, 229, 291) | 5 `ComboBox` **with `ItemTemplate`** | `SelectedIssueSmartList` / `SelectedSeriesSmartList` / `SelectedNovelSmartList` (SmartList objects) ×3; `SelectedRelationTypeOption` / `SelectedSeriesRelationTypeOption` ×2 | **O** ×2 safe + **O** ×3 **KEEP?** | **The 3 SmartList pickers bind to user-named SmartLists loaded from the DB — names are not unique and the list is dynamic.** A string-key wrapper here silently picks the first name match. Strongly recommend **keep the 3 SmartList pickers as `ComboBox`**; convert only the 2 `RelationTypeOption` pickers. |
| 14 | `NewReadingListOverlay.axaml` (86, 113) | 2 `ComboBox` (`ItemTemplate`) | `SelectedArcSource : ArcSourceOption`; `SelectedStoryEvent : StoryEventOption` | **O** ×1 safe + **O** ×1 **KEEP?** | `ArcSourceOption` safe (as #10). **`StoryEventOption` — user-named story events from the DB, collide-able, dynamic.** Recommend keep the StoryEvent picker as `ComboBox`. |
| 15 | (`IssuePropertiesScreen`, `BulkIssuePropertiesScreen`, `ReadingListPropertiesOverlay`) | — | — | **done in `a3c0455`** | listed for completeness. |

### Flagged pickers — DECISION (2026-09-10): convert all, first-match semantics

| Picker | Semantics accepted |
|--------|--------------------|
| `CollectionPropertiesOverlay` — 3 SmartList pickers | first option whose name == text wins; a duplicate-named SmartList is unreachable via the picker |
| `NewReadingListOverlay` — StoryEvent picker | same |
| `SmartScreen` — VirtualTag picker | same |

User chose "Everything (1–14)" then confirmed first-match semantics for these three. No picker
stays `ComboBox`.

## Dead code to remove (after the swaps, once grep is clean)

- `src/Paperbunkr.App/Behaviors/MultiValueAutoComplete.cs` — `a3c0455` already removed every
  `beh:MultiValueAutoComplete.Enabled` usage. Verify `grep -rn "MultiValueAutoComplete"
  src/Paperbunkr.App` → only the file itself, then delete.
- `src/Paperbunkr.App.Tests/MultiValueAutoCompleteTests.cs` — delete with it.
- `IssuePropertiesScreenViewModel.WeightOptions` (line ~317) — `a3c0455` switched the row to
  `WeightText`/`WeightNames`; confirm no XAML/`.cs` ref, then delete.
- `ReadingListPropertiesScreenViewModel.WeightOptions` (line ~46) — same check, then delete.
- Also grep for the `beh:` xmlns declaration left orphaned in the 3 editor `.axaml` files.

## Tests (`SuggestBoxTests`-style — pure logic only, per the project's headless/manual split)

One test class per new wrapper group, asserting:
- `XxxText` get returns the current enum/option's string form.
- `XxxText` set with a valid name changes the model value.
- `XxxText` set with garbage is a no-op (value unchanged).
- `XxxNames` contains exactly the expected set (and, for the live-filtered ones — #8 KeyOption,
  #9 Operator — that it tracks the source list after a dependency change).
- Round-trip: set model value → `XxxText` reflects it (via the `OnXxxChanged` partial).

New/extended test files (tentative):
- `PreferencesScreenViewModelTests` — Reader (#3 ×4), Advanced (#5), Automation notif level (#4a).
- new `ScheduledTaskRowViewModelTests` (or wherever `Mode` lives) — #4b.
- `DetailScreenViewModelTests` / `MangaDetailScreenViewModelTests` — #6.
- `ActivityCenterViewModelTests` — #7.
- `KeyBindingRowViewModelTests` — #8 (extend; already exists).
- `SmartListConditionViewModelTests` — #9.
- `ReadingScreenViewModelTests` / `ReadingListItemRowViewModelTests` — #10.
- `DetailTabsViewModelTests` — #11.
- `EventsScreenViewModelTests` — #12.
- `CollectionPropertiesScreenViewModelTests` — #13.
- `NewReadingListViewModelTests` — #14.

## Execution order

1. **Group S** (#1, #2, part of #3) — no VM change, XAML only. Build + weave-verify.
2. **`contentTypePicker` / `conditionPicker` `Style` retargeting** proof — do #6 first as the
   canary for "restyle a classed `ComboBox` as a `SuggestBox`"; if a setter doesn't map cleanly,
   surface before doing #9.
3. **Group E** (#3 rest, #4, #5, #7) — `PreferencesScreenViewModel` + small VMs, all enums.
   One build after each file.
4. **Group O — safe** (#8 field/searchmode/operator, #10, #11, #12, #13 relation-type only, #14
   arc-source only).
5. **Group O — flagged** (#9 VirtualTag, #13 SmartLists, #14 StoryEvent) — only if confirmed.
6. **Dead-code sweep.**
7. Full `dotnet test` for the App.Tests VM projects (per memory: run targeted, not the whole
   suite — it mass-flakes headless).
8. `docs/alpha-todo.md` note; update this file's status.

## Build discipline (CLAUDE.md)

Every new/edited `.axaml` that introduces a control usage: build immediately, and if XAML compile
fails after `CoreCompile` produced output, `rm src/Paperbunkr.App/obj/Debug/net10.0/Paperbunkr.App.dll`
(+ `.pdb`) before rebuild — never a bare retry. Verify the weave by launching the exe, not by
"0 Errors". `SuggestBox` itself is already compiled (`a3c0455` built clean), so no *new*
`x:Class`/View is added here — the `AVLN2000` first-compile trap does not apply, but the
stale-dll masking trap still does.

## On-screen verification (no computer-use on this project)

Hand the user a checklist: open each converted dropdown, confirm (a) no freeze, (b) the list
shows, (c) selection round-trips, (d) `IsStrict` pickers reject typing, (e) `IsMultiValue` splice
still works in the 3 editors (regression guard). `FreezeWatchdogService` now auto-captures a
stack to `%AppData%\Paperbunkr\logs\freeze-stack-*.txt` if anything does wedge.
