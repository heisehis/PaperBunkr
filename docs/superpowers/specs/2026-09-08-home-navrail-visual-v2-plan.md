# Home Screen & NavRail — Visual v2 — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-08-home-navrail-visual-v2-design.md*

## Corrections to the design doc (found during codebase survey — do not re-litigate, just build against reality)

- **`Controls/EntranceAnimation.cs` already exists**, fully built and already used by
  `LibraryScreenViewModel` (`Enabled`/`Index` attached properties on `ItemsControl`, `.entranceReady`/
  `.entered` style pair already in `Styles/Primitives.axaml:18-33`, reduced-motion-aware via
  `MotionTokens.IsReducedMotion()`). The design doc's §5 assumed this needed building from scratch —
  it doesn't. Step 6 below only adds Home's own `PlayEntranceAnimation` flag and XAML wiring, mirroring
  `LibraryScreenViewModel`'s exact pattern (a plain `[ObservableProperty] private bool`, set `true`
  and never explicitly reset — `EntranceAnimation.Prepare` consumes it once per container-preparation
  burst, so no reset is needed).
- **`SkinService` has no skin-change notification today** — no event, no messenger. The design doc's
  §3 claim that "`SkinService` already raises one" is wrong. Step 3 below adds a small new
  `event Action? SkinApplied` on `SkinService`, raised from `ApplySkin`/`ApplyPersistedSettings`.
- **Converter binding convention in this codebase is `{x:Static ns:Converter.Instance}`**, not
  `{StaticResource ...}` (confirmed via `Views/ReadingModeIconConverter.cs`, the closest existing
  precedent — converts to a `FluentIcons.Common` enum already). All 13 existing converters live flat
  in `Views/`, not a `Converters/` folder. Step 1 follows this exactly.
- **Reading the active skin's base color** doesn't need a new `SkinService` accessor —
  `Application.Current!.Resources["PbSurface0Color"]` is already a live, parsed `Avalonia.Media.Color`
  kept current by `SkinService.SetColorAndBrush` (`SkinService.cs:233-238`). Steps 2/3 read it directly
  rather than adding new `SkinService` surface.

## Step 1: NavRail icon glyphs, sizing, active-state variant

**Files:**
- `src/Paperbunkr.App/Views/BoolToIconVariantConverter.cs` (new)
- `src/Paperbunkr.App/Views/MainWindow.axaml` (edit)
- `src/Paperbunkr.App.Tests/BoolToIconVariantConverterTests.cs` (new)

**What:**
- New converter, mirroring `Views/ReadingModeIconConverter.cs`'s exact shape: `public sealed class
  BoolToIconVariantConverter : IValueConverter`, `public static readonly BoolToIconVariantConverter
  Instance = new();`, `Convert` returns `FluentIcons.Common.IconVariant.Filled` when `value is true`
  else `.Regular`, `ConvertBack` throws `NotSupportedException`.
- `MainWindow.axaml`:
  - Line 244: `Symbol="Star"` → `Symbol="Planet"` (Continuity).
  - Line 214: `Symbol="Book"` → `Symbol="Library"` (Library).
  - Line 222: `Symbol="Book"` → `Symbol="BookOpen"` (Books).
  - `Button.rail fi|SymbolIcon` style (lines 57-59): `FontSize` `20` → `22`.
  - Each of the 9 rail buttons' `fi:SymbolIcon` gets `IconVariant="{Binding IsX, Converter={x:Static
    views:BoolToIconVariantConverter.Instance}}"` where `IsX` is that button's existing active-state
    property (`IsHome`, `IsInsights`, `IsLibrary`, `IsBooks`, `IsSmart`, `IsReading`, `IsEvents`,
    `IsPreferences`, and `NavRailPinned` for the Pin button — matching what each button's
    `Classes.active` binding already uses).

**Depends on:** none.

**Verify:**
- New `BoolToIconVariantConverterTests.cs`: `Convert(true, ...) == IconVariant.Filled`,
  `Convert(false, ...) == IconVariant.Regular`, `ConvertBack` throws.
- Existing `src/Paperbunkr.App.UiTests/HomeScreenTests.cs` (`HomeRailButton`, `LibraryRailButton`
  automation-id assertions) must still pass unmodified — glyph/size/variant changes don't touch
  automation ids.
- `dotnet build` clean, 0 new warnings.
- Manual: user visually confirms Planet/Library/BookOpen glyphs, the size bump, and the Filled
  active-state swap, on at least one skin.

---

## Step 2: Spotlight-cover average-color helper

**Files:**
- `src/Paperbunkr.App/Services/SpotlightAccentSampler.cs` (new)
- `src/Paperbunkr.App.Tests/CoverWallRendererTests.cs` (edit — add a new test class in this file,
  since it's the established home for this layer's SkiaSharp-bitmap tests)

**What:**
- `internal static class SpotlightAccentSampler` with `internal static Color Sample(Bitmap cover)`:
  reuses `BackdropBlurRenderer.ToSkImage(Bitmap, PixelSize)` (already `internal`, same-assembly
  reuse, no visibility change needed) to get an `SKImage`, draws it scaled into a 1×1 `SKSurface`
  (same `SKSurface.Create`/`Canvas.DrawImage`/`Snapshot()` pattern `BackdropBlurRenderer.Render`
  already uses), reads the single resulting pixel via `SKPixmap.GetPixelColor(0, 0)`, converts to
  `Avalonia.Media.Color`. Returns a documented neutral fallback (e.g. mid-gray) if `cover` is
  degenerate (zero-size) — mirrors `CoverWallRenderer.Render`'s own null/empty guard style.
- This is standalone and has no other-file dependents yet — Step 4 wires it into `HomeScreenViewModel`.

**Depends on:** none.

**Verify:**
- New tests in `CoverWallRendererTests.cs` (or a same-folder sibling file if that reads cleaner at
  implementation time): build a `WriteableBitmap`, actually `Lock()` it and write a known solid BGRA
  fill (the existing `SolidCover` helper in that file allocates blank pixels only — extend it to
  write real pixel data, since no current fixture does this yet), assert `Sample(...)` returns that
  same color (within a small tolerance for color-space rounding). Also test the degenerate/empty case
  returns the documented fallback without throwing.
- `dotnet test --filter CoverWallRendererTests` green.

---

## Step 3: Masthead theme-compatibility fix (the Windows 11 bug)

**Files:**
- `src/Paperbunkr.App/Services/CoverWallRenderer.cs` (edit)
- `src/Paperbunkr.App/Services/SkinService.cs` (edit)
- `src/Paperbunkr.App/ViewModels/HomeScreenViewModel.cs` (edit)
- `src/Paperbunkr.App/Views/HomeScreen.axaml` (edit — fallback gradient only)
- `src/Paperbunkr.App.Tests/CoverWallRendererTests.cs` (edit — update for new signature + regression test)

**What:**
- `CoverWallRenderer.Render(...)` gains an `SKColor baseColor` parameter. Replace the 3 hardcoded
  literals with values derived from it:
  - `canvas.Clear(new SKColor(8, 8, 10))` (line 36) → `canvas.Clear(baseColor)`.
  - The `SKColor(8, 8, 10, 205)` scrim paint (line 89) → `baseColor` at the same alpha (205).
  - The vignette's `SKColor(6, 6, 6, 235)` end-stop (line 99) → a slightly darker derivative of
    `baseColor` at the same alpha (235) — e.g. scale RGB down ~25%, clamping at 0, so the vignette
    still reads as "toward black" on dark skins and "toward the skin's own dark end" on light ones
    without literally inverting to black on `windows_11`.
- `HomeScreenViewModel.BuildMastheadBackdrop()` (lines 285-321): read
  `Application.Current!.TryGetResource("PbSurface0Color", null, out var res)`, cast to `Color`,
  convert to `SKColor`, pass into the updated `CoverWallRenderer.Render(covers, size, baseColor)`
  call at line 320.
- `SkinService.cs`: add `public event Action? SkinApplied;`, raise it at the end of `ApplySkin`
  (after line 148's body) and `ApplyPersistedSettings` (after line 163's body).
- `HomeScreenViewModel` constructor: subscribe to the (injected) `SkinService.SkinApplied` event;
  handler re-invokes `BuildMastheadBackdrop()` and sets `MastheadBackdrop` again — no DB requery,
  since `BuildMastheadBackdrop` only reads already-loaded in-memory cover collections. This is what
  makes switching skins while already on Home update the masthead live.
- `HomeScreen.axaml:142-146` fallback `RadialGradientBrush`: swap the 3 literal `GradientStop`
  colors for `DynamicResource`s blending `PbSurface2Color`/`PbSurface3Color` toward
  `PbAccentSoftColor` (exact blend ratio tuned by eye at implementation time, per the design doc's
  own open question — target: near-identical to today on the 4 dark skins, genuinely light on
  `windows_11`).

**Depends on:** Step 2 only in the sense both touch `HomeScreenViewModel.BuildMastheadBackdrop()` /
the masthead call site — sequence after Step 2 to avoid rebasing, not a hard technical dependency.

**Verify:**
- Update the 2 existing `CoverWallRendererTests.cs` tests for the new `baseColor` parameter
  (pass a representative color, e.g. `SKColors.Black`, to keep today's behavior asserted).
- New regression test: call `CoverWallRenderer.Render(...)` with each of the 5 skins' actual
  `Surface0`/`Bg` hex values (from `SkinTheme`/`SkinColors`, converted to `SKColor`) and assert the
  output bitmap's sampled corner pixel is numerically closer to that skin's own tone than to the old
  hardcoded `#08080A` — this is the concrete, asserted regression test for the Windows 11 bug, not a
  "doesn't crash" check.
- New `SkinService` test (in `src/Paperbunkr.App.Tests/SkinServiceTests.cs`, the existing file):
  `SkinApplied` fires exactly once per `ApplySkin` call.
- `dotnet test` full suite green.
- Manual: user checks Home under `windows_11` and at least one dark skin, and confirms switching
  skins while already on Home updates the masthead without navigating away.

---

## Step 4: Ambient recolor + scoped Ken-Burns + wordmark jitter

**Files:**
- `src/Paperbunkr.App/ViewModels/HomeScreenViewModel.cs` (edit)
- `src/Paperbunkr.App/Views/HomeScreen.axaml` (edit)

**What:**
- `HomeScreenViewModel`: new `[ObservableProperty] private Color _spotlightAccentColor;` (or similar),
  recomputed via `SpotlightAccentSampler.Sample(...)` on the current spotlight's cover, inside the
  existing `OnSpotlightIndexChanged` handler (lines 118-122) and once in `LoadFromDatabase` alongside
  the existing `CurrentSpotlight`-changed notification (line 263).
- `HomeScreen.axaml` masthead scrim: blend `SpotlightAccentColor` ~30% into the existing
  `LinearGradientBrush` (lines 149-156) — exact binding mechanism (a converter producing a blended
  `Color`, or a small code-behind helper) decided at implementation time; keep the blend additive
  over the current skin-token gradient from Step 3, never replacing it.
- Ken-Burns: add a `RenderTransform` (`scale`+`translate`) on the masthead `Image`/backdrop `Border`
  (lines 137-148), `Transitions` on a ~20s duration with an eased curve, triggered (class toggle or
  transform re-set) whenever `CurrentSpotlight` changes or Home is freshly navigated to — not
  `IterationCount="Infinite"`. Reduced Motion: explicit check (`RenderTransform="none"` when
  `MotionTokens.IsReducedMotion()` is true) since this is transform-based and won't zero for free.
- Wordmark: verify the `PAPERBUNKR` `SplitText` instance (line 158-159) isn't `IsHitTestVisible="False"`
  — if it already isn't, no XAML change is needed, the jitter is templated behavior on `SplitText`
  itself.

**Depends on:** Step 2 (needs `SpotlightAccentSampler`), Step 3 (touches the same masthead XAML region).

**Verify:**
- No new unit-testable surface beyond Step 2's coverage of the sampler itself.
- Manual: user confirms the masthead recolors toward the featured cover, pans/scales once per
  spotlight rotation (not continuously), wordmark jitters on hover, and all of it freezes under
  Reduced Motion (Preferences → Appearance).

---

## Step 5: Spotlight hero hover-pause/resume

**Files:**
- `src/Paperbunkr.App/Views/HomeScreen.axaml` (edit)
- `src/Paperbunkr.App/ViewModels/HomeScreenViewModel.cs` (edit)

**What:**
- `HomeScreenViewModel`: expose `PauseSpotlightRotation()` / `ResumeSpotlightRotation()` calling
  `_spotlightTimer.Stop()` / `_spotlightTimer.Start()` (the existing field at line 40 — no prior
  pause/resume precedent on this timer, this is genuinely new).
- `HomeScreen.axaml`: add `PointerEntered`/`PointerExited` handlers on `Button.heroCard` (the hero
  wrapper, lines 192-ish) and on the dot `ItemsControl` (lines ~207-218), wired to the two new VM
  methods via code-behind event handlers (`HomeScreen.axaml.cs`) delegating to the bound VM.

**Depends on:** none.

**Verify:** Manual only — hover the hero card or a dot, confirm rotation stops; move the pointer
away, confirm it resumes after the normal 7s interval. Not meaningfully unit-testable (real
`DispatcherTimer` + pointer input); call this out explicitly rather than skip verification silently.

---

## Step 6: Shelf entrance-stagger + widened hover glow/scale-pop

**Files:**
- `src/Paperbunkr.App/ViewModels/HomeScreenViewModel.cs` (edit)
- `src/Paperbunkr.App/Views/HomeScreen.axaml` (edit)
- `src/Paperbunkr.App/Styles/Primitives.axaml` (edit)

**What:**
- `HomeScreenViewModel`: new `[ObservableProperty] private bool _playEntranceAnimation;` set `true`
  in the constructor — mirroring `LibraryScreenViewModel.PlayEntranceAnimation`'s exact pattern
  (never explicitly reset; `EntranceAnimation.Prepare` consumes it once per container-preparation
  burst, already reduced-motion-aware via `MotionTokens.IsReducedMotion()`).
- `HomeScreen.axaml`: on each shelf's `ItemsControl` (Continue Reading, Continue Reading — Books,
  Recently Added, Collections, each Because-You-Read row), add
  `controls:EntranceAnimation.Enabled="{Binding PlayEntranceAnimation}"` and set `Index` per realized
  container the same way `LibraryScreenViewModel`'s consumers already do it (container-prepared
  index binding — mirror that exact existing wiring, don't invent a new mechanism).
- `Styles/Primitives.axaml:189-197`: widen the `Border.posterTile:pointerover`/`:focus-visible`
  ring from `0 0 0 2 #66E0995A` to `0 0 0 4 #66E0995A` (matching the existing standalone `PbGlowRing`
  token's 4px spread at line 167, for consistency — the literal itself still can't reference that
  token directly, `BoxShadows` strings don't support markup extensions inline). Add a
  `RenderTransform: scale(1.02)` on the same `:pointerover`/`:focus-visible` selectors, with a
  `TransformOperationsTransition` on `PbMotionFast`.

**Depends on:** none (independent of Steps 2-5).

**Verify:**
- Full `dotnet test` — confirm no regression in existing `HomeScreenViewModelTests` (data/navigation
  assertions must still pass unmodified) or `HomeScreenTests.cs` FlaUI automation-id checks (adding
  attached properties shouldn't change automation ids).
- Manual: user confirms shelf cards stagger in on navigating to Home, hover shows the widened glow +
  subtle scale, and both are absent/instant under Reduced Motion.

---

## Step 7: Reduced-Motion pass + full verification sweep

**Files:** none new — a verification-only pass across Steps 1-6.

**What:** Walk every new animation introduced above against the design doc's §6 table and confirm
each one actually has a working off-switch, not just an assumed one:
- Shelf entrance-stagger / hover glow-pop → token-driven, already covered by Step 6's manual check.
- Masthead pan/scale → explicit check added in Step 4, re-verify directly.
- Wordmark jitter → inherited from `SplitText`, re-verify directly (this is the one place the design
  doc flagged "should already work" rather than confirmed — confirm it here).

**Depends on:** Steps 1-6.

**Verify:**
- `dotnet build` clean (0 new warnings), full `dotnet test` green, crash-free direct-exe launch —
  same bar as every prior UI-rework phase in this project.
- Manual, with Reduced Motion toggled on in Preferences → Appearance: confirm masthead pan/scale is
  fully absent, entrance-stagger and hover effects are instant (not just fast), and nothing regresses
  on any of the 5 skins, `windows_11` and at least one dark skin specifically per the theme-fix.
