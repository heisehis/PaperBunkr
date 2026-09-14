# Reader chrome redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-14-reader-chrome-redesign-design.md*

**Implementation-time correction to design §1 ("frosted glass"):** this codebase has no live
blur-behind mechanism for arbitrary/dynamic content. `BackdropBlurRenderer.cs` pre-renders a
Gaussian blur of a *known static bitmap* once (the manga-detail header's cover backdrop) — its own
doc comment explains why a live Avalonia `BlurEffect` over dynamic content doesn't work in this
Avalonia version (AvaloniaUI/Avalonia#11416, only blurs a small square). The Reader's floating
clusters sit over `PageCanvas`, which pans/zooms/scrolls continuously — there is no cheap way to
re-blur "whatever's currently under the cluster" every frame, and building one would be new,
high-risk infrastructure this spec never scoped. "Frosted glass" is therefore implemented as a
**translucent surface, no literal blur**: a low-alpha `Pb*` background + thin low-alpha border +
`BoxShadow`, same `floatingPanel` shape the clusters already use, just less opaque. This still reads
as "floating over the page" (the goal) without pretending to blur it.

## Step 1: ViewModel — `FitModeLabel` + reading-mode label rename
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit)
**What:**
1. Add, near the existing `FitMode` property (~line 548):
   ```csharp
   public string FitModeLabel => FitMode switch
   {
       ImageFitMode.Fit => "Fit Page",
       ImageFitMode.FitWidth => "Fit Width",
       ImageFitMode.FitHeight => "Fit Height",
       ImageFitMode.BestFit => "Best Fit",
       _ => "Original",
   };

   partial void OnFitModeChanged(ImageFitMode value) => OnPropertyChanged(nameof(FitModeLabel));
   ```
   Same idiom as `TagEditRowViewModel.OnWeightChanged` → `WeightText` from the Metadata editors redesign.
2. In `UpdateReadingModeState`'s switch (~line 1126-1135), rename per design §2 (drop the trailing `" ▾"` from every case — the pill row has no dropdown chevron):
   `RightToLeft`→"Right to Left", `TopToBottom`→"Top to Bottom", `VerticalContinuous`→"Longstrip (gapped)", `HorizontalContinuous`→"Horizontal Long Strip", `HorizontalContinuousRightToLeft`→"Horizontal Long Strip (RTL)", `Webtoon`→"Long Strip", default (`LeftToRight`)→"Left to Right".
**Depends on:** none
**Verify:** `dotnet build`. Grep `src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` for any assertion against the old label strings (` ▾`, "Vertical (Continuous)", etc.) before editing — none expected (tests target behavior, not display strings) but confirm rather than assume.

## Step 2: Thumbnail rail — retire hover-trigger, add explicit open toggle
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs`, `src/Paperbunkr.App/Views/ReaderScreen.axaml`, `src/Paperbunkr.App/Views/ReaderScreen.axaml.cs` (all edit)
**What:**
1. ViewModel: add `[ObservableProperty] private bool _isRailOpen;` and `[RelayCommand] private void ToggleRail() => IsRailOpen = !IsRailOpen;` (mirrors `IsDrawerOpen`/`ToggleDrawer` exactly).
2. `ReaderScreen.axaml.cs`: delete `_hoveringRailTrigger`, `_hoveringRailOverlay`, `OnRailHoverEntered`, `OnRailHoverExited`, `UpdateRailVisibility` (lines 175-212) — dead once the rail is an explicit toggle, not hover-state.
3. `ReaderScreen.axaml`:
   - Delete the `RailEdgeTrigger` Border (:179-180).
   - `RailOverlay`: remove its `PointerEntered`/`PointerExited` handlers; change `Classes="railOverlay hidden"` to bind the hidden state off the VM instead of a local literal class: `Classes.hidden="{Binding !IsRailOpen}"`.
   - Add a new `Button Classes="clusterIcon" Classes.active="{Binding IsRailOpen}" Command="{Binding ToggleRailCommand}"` into the **Page-turn cluster** (not Actions — design §6 places it there, paired with the dot-strip it complements), e.g. as `Grid.Column="0"` before the previous-chapter button, with an `fi:SymbolIcon` (suggest `Symbol="Grid"` or `"Image"` — confirm it exists in `FluentIcons.Common.Symbol` at implementation time the same way prior specs have).
**Depends on:** none
**Verify:** `dotnet build`. New test: `ToggleRailCommand` flips `IsRailOpen` from its `false` default.

## Step 3: Reading-mode / fit-mode pickers → segmented pills
**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit)
**What:**
1. Add a `segItem`/`.active` style block to `UserControl.Styles`, mirroring `DetailTabs.axaml`'s shape ([:249-277](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L249-L277) for structure, [:74-87](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L74-L87) for the style block) — mirrored locally per this file's own already-documented "styles don't share across files" convention, not referenced.
2. Reading-mode picker ([:409-420](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L409-L420)): replace the `Button.Flyout` with a horizontal `WrapPanel` of 7 `Button Classes="segItem"` — one per `ReadingMode` value, `Command="{Binding SetReadingModeCommand}"` + `CommandParameter="{x:Static entities:ReadingMode.X}"`, `Classes.active` via `ObjectConverters.Equal` against `EffectiveReadingMode` (same pattern `DetailTabs.axaml`'s `TierClass` already uses). `WrapPanel` not `StackPanel` — 7 renamed labels (some now longer, e.g. "Horizontal Long Strip (RTL)") won't fit one row in the cluster's available width; the corner cluster's own `Border` will need to tolerate the pill row wrapping to 2 lines without clipping (verify on-screen at the ~720px collapse boundary).
3. Fit-mode picker: apply the same treatment at **both** call sites — the primary View cluster ([:429-441](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L429-L441)) and the drawer's narrow-window fold-in duplicate ([:537-557](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L537-L557)) — 5 segments (`Original`/`Fit`/`FitWidth`/`FitHeight`/`BestFit`), `Command="{Binding SetFitModeCommand}"`, active check against `FitMode`.
4. Rebind both `TextBlock Text="{Binding FitMode}"` occurrences (:429, :545) to `{Binding FitModeLabel}` (Step 1's new property) — needed regardless of the pill conversion, since the pill row itself will use `FitModeLabel` too if any segment shows text (or `ReadingMode.ToString()`/enum name directly is fine for segment captions since design's per-value rename table *is* the segment caption; `FitModeLabel` is specifically for the collapsed/current-value display, not per-segment captions which come straight from the design's rename table via `x:Static` + hardcoded segment `Content`).
**Depends on:** Step 1 (`FitModeLabel` must exist first)
**Verify:** `dotnet build`. On-screen: pill row wrapping at narrow widths, both fit-mode locations stay in sync (same `FitMode`, two bound UIs).

## Step 4: Zoom — one unified pill + popover control
**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit)
**What:**
1. Replace the View cluster's zoom stepper+pill+flyout+conditional-`Slider` block ([:444-468](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L444-L468)) with a single `Button Classes="clusterPill"` (`🔍 {ZoomLevel:P0}`) whose `Flyout`/`Popup` contains: `ZoomOutCommand` button, a `Slider Value="{Binding ZoomLevel}"`, `ZoomInCommand` button, and the 5 existing preset buttons (`SetZoom100/125/150/200/400Command`, unchanged) — same commands as today, new composition only.
2. Slider `Minimum`/`Maximum`: the ViewModel's `ZoomLevel` setter already clamps per-mode (`0.5–4.0` continuous, `1.0–MaxZoom` paged, [ReaderScreenViewModel.cs:375-376](../../../src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs#L375-L376)) — no new bindable min/max surface needed (would be scope creep the design doc didn't ask for). Set the `Slider`'s own `Minimum="0.5" Maximum="4"` as a fixed superset in XAML; the setter's existing clamp still enforces the real per-mode ceiling if a paged-mode drag overshoots it, so the visible travel is slightly wider than the enforced range in paged mode — acceptable, flagged here rather than silently decided.
3. Apply the identical replacement to the drawer's fold-in zoom row ([:560-568](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L560-L568)), which today has no pill/popover at all (bare stepper) — it gets the same unified control as the primary cluster, per design §4's "both places" requirement.
**Depends on:** none
**Verify:** `dotnet build`. On-screen: popover opens/closes correctly in both paged and continuous mode, slider drag still clamps to the real per-mode range via the existing setter.

## Step 5: Reader Tools drawer — rearranged sections
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs`, `src/Paperbunkr.App/Views/ReaderScreen.axaml` (both edit)
**What:**
1. ViewModel: add `[ObservableProperty] private bool _isAdjustSectionExpanded;` (default `false`, collapsed) — same `IsDrawerOpen`/`IsRailOpen` pattern (deliberate open/close state, not hover), plus `[RelayCommand] private void ToggleAdjustSection() => IsAdjustSectionExpanded = !IsAdjustSectionExpanded;`.
2. `ReaderScreen.axaml` drawer ([:522-696](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L522-L696)):
   - Change the outer `Grid RowDefinitions="Auto,*"` to `"Auto,*,Auto"` — header stays `Grid.Row="0"`, the existing `ScrollViewer` (PAGE/ADJUST/TRANSITION) becomes `Grid.Row="1"`, BOOKMARKS moves to a new fixed `Grid.Row="2"` outside the scroll region (same "fixed header, only the middle scrolls" fix already applied to the header per the file's own prior on-screen-verified bug).
   - **PAGE** ([:571-605](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L571-L605)): replace the icon-button-pair + 2 `drawerRow` toggles with a `UniformGrid Columns="2"` of 4 icon+label toggle cells (Rotate CW, Rotate CCW, Auto-rotate, Double-page — `Double-page` cell keeps its existing `IsVisible="{Binding !IsContinuousMode}"`). Auto-scroll row + speed slider (:593-605) stays as-is below the grid, unchanged.
   - **ADJUST** ([:607-627](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L607-L627)): wrap the existing Brightness/Contrast/Saturation/Gamma/Reset `StackPanel` in an `IsVisible="{Binding IsAdjustSectionExpanded}"` panel; the section header becomes a clickable row (`▸`/`▾` glyph + "ADJUST", `Command="{Binding ToggleAdjustSectionCommand}"`) instead of a plain `TextBlock Classes="drawerSectionHeader"`. Slider content itself unchanged.
   - **TRANSITION** ([:629-640](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L629-L640)): collapse the separate `drawerSectionHeader` TextBlock + full-width `drawerRow` Button into one `Grid ColumnDefinitions="Auto,*"` row — "Transition" label in column 0, the existing value+`Flyout` button in column 1, unchanged `Flyout` contents.
   - **BOOKMARKS** ([:642-692](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml#L642-L692)): move the entire block (header, `ItemsControl`, empty-state text, prev/next row) into the new `Grid.Row="2"` footer, content and bindings unchanged — only its position in the tree moves, out of the scrollable `StackPanel`.
**Depends on:** none
**Verify:** `dotnet build`. On-screen: PAGE grid toggles work, ADJUST starts collapsed and expands/collapses, TRANSITION reads as one row, BOOKMARKS stays visible while PAGE/ADJUST scroll away, drawer's existing header-doesn't-scroll fix still holds.

## Step 6: Visual language — translucent clusters/drawer/rail restyle
**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit, `UserControl.Styles` + rail tile styles only)
**What:** Per this plan's own header note, "frosted glass" = translucent, not blurred. Add a local override (more specific than `Border.floatingPanel` from `Primitives.axaml`, so it wins without touching that shared file other screens also use):
```xml
<Style Selector="Border.floatingPanel.chromeCluster, Border.floatingPanel.readerDrawer">
    <Setter Property="Background" Value="{DynamicResource PbSurface3Brush}" />
    <Setter Property="Opacity" Value="0.92" />
</Style>
```
(exact alpha/brush an implementation-time visual-tuning call, verified on-screen over varied page art per the design doc's own testing note — start near 0.85-0.92 and adjust for legibility, not a fixed number to hit blindly). Add `Classes="readerDrawer"` to the drawer's root `Border` (:522) alongside its existing `Classes="floatingPanel"`. Restyle `Border.thumb`/`Border.railOverlay` (:30-41, :129-139) the same translucent direction, and swap `Border.thumb.selected`'s border to the same accent ring already used (already `PbAccentBrush` — confirm it reads as intended once the tile background itself is more transparent, may want a slightly heavier `BorderThickness` for contrast).
**Depends on:** none (purely additive styles, safe to do last after every structural change lands so there's no XAML to re-touch)
**Verify:** `dotnet build`. On-screen: legibility of every cluster/drawer/rail over both light and dark page art, both app skins.

## Step 7: Tests
**Files:** `src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit)
**What:** Following this file's existing per-test `new ReaderScreenViewModel(goBack: () => { })` pattern:
- `FitModeLabel` returns the right string for each of the 5 `ImageFitMode` values (table in Step 1), including default-unset case.
- `ToggleRailCommand` flips `IsRailOpen` from its default `false`.
- `ToggleAdjustSectionCommand` flips `IsAdjustSectionExpanded` from its default `false`.
**Depends on:** Steps 1, 2, 5
**Verify:** `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter "FullyQualifiedName~ReaderScreenViewModel"`

## Step 8: On-screen verification
**What:** Per this project's `.claude/worktrees/*` concurrent-session caveat (`git status`/`git worktree list` first) — open the Reader on a real comic: confirm both pickers show pill rows with the renamed labels and wrap correctly, zoom popover works in a paged and a continuous reading mode, the drawer's new PAGE grid/collapsed ADJUST/inline TRANSITION/pinned BOOKMARKS all behave, the rail opens only via its new Page-turn-cluster button (no more left-edge hover), and the translucent restyle stays legible over at least one light-background and one dark-background page.
**Depends on:** Steps 1-6
**Verify:** manual, no automated substitute.
