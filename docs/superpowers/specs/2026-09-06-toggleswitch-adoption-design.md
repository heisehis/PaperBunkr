# Animated Toggles: ToggleSwitch Adoption — Design

Converts Paperbunkr's plain `CheckBox` boolean-setting controls to Avalonia's `ToggleSwitch`
(pill/sliding-thumb visual) app-wide, and wires its animation to the existing Reduced Motion
setting - closing a real gap found during research: neither `CheckBox` nor `ToggleSwitch` currently
respects Reduced Motion at all. Inspired by Komikku's settings screens (screenshots shared in
chat), which use this exact control shape throughout. Sub-project B of the Komikku-inspired
settings work - Design A (Connections dialog redesign) is a separate, independent spec.

Date: 2026-09-06. Status: design, approved via grilling round in chat (B1/B2 both answered). Not
yet approved for `writing-plans`.

---

## Why now (context)

**Confirmed via direct code check:**

- Paperbunkr uses `FluentAvaloniaTheme` (`App.axaml:197`), not stock Avalonia `FluentTheme`. No
  app-level `ControlTheme`/`Style` override exists for either `CheckBox` or `ToggleSwitch` anywhere
  in `src/Paperbunkr.App/Styles/*.axaml` - every current `CheckBox` in the app uses
  FluentAvalonia's unstyled default template. One existing `ToggleSwitch` precedent already exists,
  also unstyled: `AutomationSection.axaml:38-39`.
- FluentAvalonia's default `CheckBox`/`ToggleSwitch` templates already animate out of the box (check-
  glyph transition, thumb slide) - this is baseline behavior needing no new code to exist at all.
  What's actually missing is **Reduced Motion respecting it**: `ReducedMotion` is wired as a global
  `DynamicResource` swap (`SkinService.ApplyReducedMotion`, `src/Paperbunkr.App/Services/SkinService.cs:251-262`,
  overwriting `Application.Current.Resources["PbMotionFast"]` etc.), and every other animated
  element in this codebase (`Styles/Primitives.axaml`, `Styles/Typography.axaml`) binds its
  `Transitions` `Duration` to `{DynamicResource PbMotionFast}` so it picks the swap up live. Neither
  `CheckBox` nor `ToggleSwitch` does this today, since neither has an app-level style to bind from -
  toggling Reduced Motion currently has **zero effect** on any checkbox/switch animation anywhere.
- The `avalonia-pro-max/motion` subskill (consulted per this project's mandatory UI-foundation
  rule) explicitly calls out "no reduced-motion path" as a common accessibility mistake, and
  recommends exactly the binds-to-a-live-swappable-resource pattern this codebase already uses
  everywhere else - this spec brings toggles in line with that existing convention, not inventing a
  new one.

**Confirmed app-wide `CheckBox` inventory** (39 occurrences outside Preferences, plus Preferences'
own ~20-ish settings toggles across ~10 files): **no `IsThreeState`/`bool?`-bound checkbox exists
anywhere in the app** - `ToggleSwitch`'s lack of an indeterminate state is not a blocker anywhere.
The real, confirmed split is by *UI role*, not by any technical constraint:

| Role | Stays `CheckBox` | Converts to `ToggleSwitch` |
|---|---|---|
| Persistent boolean setting/filter | - | Preferences (~20, all tabs), `LibraryToolbar.axaml` (9 filter/display toggles), `ActivityDrawerView.axaml:150`, `UpdateAvailableOverlay.axaml:26`, `ReaderSettingsSheet.axaml:148`, `IssuePropertiesScreen.axaml:406`, `SmartScreen.axaml:393`, `PluginScreen.axaml:104` |
| Per-row/tile multi-select | `LibraryScreen.axaml` (10), `BooksScreen.axaml:128`, `EventsScreen.axaml` (4), `ReadingScreen.axaml` (2) | - |
| "Apply/stage this field" gate (Bulk editing) | `BulkIssuePropertiesScreen.axaml:72`, `BulkSeriesPropertiesScreen.axaml:70` (`IsStaged`), `BulkBookPropertiesOverlay.axaml` (4: Apply Author/Summary/Published date/Series) | - |

The Bulk Book Properties "Apply *" checkboxes are functionally identical to Bulk Issue/Series
Editing's `IsStaged` row checkboxes (gate whether a field applies to the bulk edit) even though
they're a different file/screen - grouped with the "stays `CheckBox`" bucket for visual consistency
within that one feature family, not converted on their own just because they happen not to use the
literal `IsStaged` property name.

## Goals

- New `ToggleSwitch` `ControlTheme` (or targeted `Style` selector) in
  `src/Paperbunkr.App/Styles/FormControls.axaml` overriding only the thumb's `Transitions.Duration`
  to `{DynamicResource PbMotionFast}` - track/thumb colors, sizing, and every other visual stay
  FluentAvalonia's defaults (already `SystemAccentColor`-driven and skin-aware per `App.axaml`'s own
  comment - no new color tokens needed).
- Every row in the "Converts to `ToggleSwitch`" bucket above changes control type, one-for-one:
  `IsChecked` binding preserved exactly, `AutomationProperties.AutomationId` preserved exactly (so
  existing UI-automation tests keep working unchanged).
- `CheckBox`'s inline `Content="..."` label has no equivalent slot on `ToggleSwitch` (its
  `OnContent`/`OffContent` sit *beside* the thumb showing on/off state text, not a leading label) -
  every converted row's label text moves to an adjacent `TextBlock`, matching how most rows are
  already laid out (label + control side by side), with `OnContent`/`OffContent` left empty -
  matching Komikku's own bare-switch look (its screenshots show no inline on/off text either).
- B2 confirmed **app-wide** scope, not just Preferences - the two buckets above are the actual
  scope boundary, not "Preferences vs. everything else."

## Non-goals (v1)

- No change to any per-row/tile selection or bulk-edit "stage this field" checkbox - confirmed
  different UI role, not a settings toggle, staying `CheckBox` regardless of visual consistency
  arguments either way.
- No new color/track-style customization beyond wiring the existing motion token - this is an
  animation-behavior fix riding on a control-type swap, not a broader toggle re-skin.
- No change to `CheckBox`'s own (already-existing, un-wired) default transition - `CheckBox` stays
  wherever it stays, unmodified, since Reduced Motion never applied to it before this spec either
  and fixing that for a control this spec is actively moving away from isn't worth the churn.

## Testing

- This is a pure control-type + styling change with no new ViewModel logic anywhere - `IsChecked`
  bindings and command wiring are untouched. Existing tests (which exercise ViewModel
  properties/commands, not XAML control types) should need zero changes; a full targeted test run
  after the swap confirms nothing broke incidentally.
- New: extend the existing `ReducedMotion_Change_PersistsToAppSettings_AndAppliesLive` test
  (`PreferencesScreenViewModelTests.cs:264`, which already asserts `PbMotionFast`/`PbMotionSlow`/
  `PbMotionStandard`/`PbMotionLarge` all become `TimeSpan.Zero` in `Application.Current.Resources`
  when Reduced Motion is toggled on) with the same assertion pattern - since this spec doesn't add
  a new resource key, just a new consumer of `PbMotionFast`, the existing test already proves the
  resource swap this style relies on; no new resource-level test is needed, only the manual
  on-screen check below that the new style actually *reads* it.
- Manual: toggle Reduced Motion on, confirm switches snap instantly; off, confirm the thumb visibly
  slides. Spot-check one converted row per bucket (a Preferences setting, a `LibraryToolbar` filter,
  `ReaderSettingsSheet`) and confirm the label still reads correctly beside the switch. Confirm a
  per-row selection checkbox (Library grid) and a bulk-edit stage checkbox are visually unchanged.
