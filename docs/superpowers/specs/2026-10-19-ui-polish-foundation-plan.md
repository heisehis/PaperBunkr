# UI polish, sub-project 1: Foundation — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-19-ui-polish-foundation-design.md*

Governing subskills: `avalonia-pro-max/design-system` (tokens), `avalonia-styling` (Style vs ControlTheme, `/template/` part
selectors), `avalonia-pro-max/review-checklist` (sign-off). Precedent for the lint test:
`src/Paperbunkr.App.Tests/Theming/ThemeHardcodedColorLintTests.cs`. Precedent for headless token loading:
`FocusTestHarness.UseAppTokens`.

The working tree carries other sessions' uncommitted edits in 44 `.axaml` files. Every edit below is in place and line-scoped, with no
reformatting and no reverting.

## Step 1: Tokens and type roles
**Files:** `src/Paperbunkr.App/Styles/AppTokens.axaml` (edit), `src/Paperbunkr.App/Styles/Typography.axaml` (edit)
**What:**
- `PbFontSizeMicro` 9.5 → 10, `PbFontSizeOverline` 10.5 → 10, `PbFontSizeMeta` 11.5 → 12, `PbFontSizeControl` 12.5 → 13.
- New `PbFontSizeSection` 15 and `PbFontSizeTitle` 18.
- New `CornerRadius PbFieldRadius` `4,4,0,0`.
- New classes `pbTextSection`, `pbTextTitle`, `pbTextMeta`, `pbTextOverline`. `pbTextCaption` foreground goes from faint to muted.
**Depends on:** none
**Verify:** build; `FoundationStyleTests` (Step 7).

## Step 2: FluentAvalonia bridge
**Files:** `src/Paperbunkr.App/Styles/FluentBridge.axaml` (new `ResourceDictionary`), `src/Paperbunkr.App/App.axaml` (merge it after
`AppTokens.axaml`)
**What:**
- Override the `TextControl*` and `ComboBox*` keys listed in design §4. Each value is a `SolidColorBrush` with a `{DynamicResource Pb*Color}`
  colour (no hex), plus the `Thickness` keys.
**Depends on:** none
**Verify:** `FoundationStyleTests` resolve the keys and follow a theme switch.

## Step 3: Form control styles and the shared field label
**Files:** `src/Paperbunkr.App/Styles/FormControls.axaml`, `src/Paperbunkr.App/Styles/ScreenChrome.axaml` (`searchBox`)
**What:**
- `TextBox`: `CornerRadius PbFieldRadius`, an italic `PART_Placeholder`, and an `:error` underline.
- `TextBox.searchBox`: all states keep a full rounded border via `/template/ Border#PART_BorderElement`.
- `NumericUpDown`, `AutoCompleteBox`, `ComboBox` and `SuggestBox` move to `BorderThickness 0,0,0,1`, `PbFieldRadius` and a muted border.
- Global `TextBlock.fieldLabel` (overline role).
**Depends on:** Steps 1 and 2
**Verify:** headless render (Step 7).

## Step 4: Popups, tooltips and dialog pieces
**Files:** `src/Paperbunkr.App/Styles/Overlays.axaml`
**What:**
- `FlyoutPresenter` and `ToolTip` styles.
- `OverlayShell` ✕ moved to `Margin 0,12,12,0`, with `AutomationProperties.Name="Close"`.
- `Border.dialogCard` (`.sm`, `.md`, `.lg`), `TextBlock.dialogTitle`, `dialogSubtitle`, `Border.dialogFooter`, `Border.inlineError`,
  `TextBlock.errorText`.
**Depends on:** Step 1
**Verify:** build; headless render.

## Step 5: Mechanical sweeps
**Files:** every `.axaml` under `src/Paperbunkr.App` except `bin/` and `obj/`, applied by a scratch-folder script (not committed to the
repo) that prints a per-file change count.
**What:**
1. **Font sizes:** literal `FontSize="x"`, both as an attribute and in `<Setter Property="FontSize" Value="x"/>`, mapped by Q16.
2. **Radii:** literal uniform `CornerRadius` 3–6 → `{DynamicResource PbRadiusSm}`, 7, 8 and 10 → `PbRadius`, 9 → `PbRadiusChip`.
   The same mapping applies inside `Setter Value`. `{StaticResource PbRadius|PbRadiusSm|PbRadiusLg}` → `DynamicResource`.
3. **Faint text:** `PbTextFaintBrush` → `PbTextMutedBrush` on `<TextBlock`, `<SelectableTextBlock` and `<Run` elements, and in
   `Setter Foreground` whose enclosing `Style Selector` targets `TextBlock`.
4. **Field labels:** delete the local `Style Selector="TextBlock.fieldLabel"` blocks in the 12 views. Any setter a view had beyond the
   global one gets reported for manual review.
5. **Page margin:** in `Views/*Screen.axaml`, `Margin` or `Padding` of the form `28,a,28,b` → `24,a,24,b`, and `28,a` → `24,a`.
**Depends on:** Steps 1 and 3 (the global `fieldLabel` must exist before the local copies go)
**Verify:** `XamlConsistencyLintTests`; build; `git diff --stat` per file looks proportionate.

## Step 6: Badge colour back to badges
**Files:** `src/Paperbunkr.App/Views/DetailBand.axaml`
**What:** the left stripe and the Virtual Tags chip move from `PbBadge*` to accent tokens.
**Depends on:** none
**Verify:** build; on-screen check.

## Step 7: Tests
**Files (new):** `src/Paperbunkr.App.Tests/Theming/XamlConsistencyLintTests.cs`, `src/Paperbunkr.App.Tests/Theming/FoundationStyleTests.cs`
**What:**
- `XamlConsistencyLintTests`: the five rules in design §8, in the same file-walking shape as `ThemeHardcodedColorLintTests`.
- `FoundationStyleTests`: headless, loads `AppTokens`, `FluentBridge` and `FormControls`, and asserts key resolution and theme-switch
  following. It also has a render helper that writes form-sample PNGs for Default, Daylight and Matrix to the folder named by
  `PB_RENDER_OUT` (skipped when the variable is unset).
- Also update any existing test that asserted an old literal size or radius. Grep showed none in C#; re-check after the sweep.
**Depends on:** Steps 1–6
**Verify:** `dotnet test --filter "FullyQualifiedName~XamlConsistencyLintTests|FullyQualifiedName~FoundationStyleTests|FullyQualifiedName~ThemeHardcodedColorLintTests|FullyQualifiedName~ThemeGoldenResourceTests" --blame-hang-timeout 3m`

## Step 8: Build, verify, clean up
- `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`. Watch for AVLN errors, and use the weave-safe rebuild from CLAUDE.md if one
  shows up.
- Run the Step 7 filter, then the existing classes that render touched views
  (`FullyQualifiedName~BrandMarkRenderSmokeTests|FullyQualifiedName~IssueCardSampleTests|FullyQualifiedName~DetailTabsViewModelTests`).
- Render the PNGs, review them, then `dotnet build-server shutdown` and stop leftover testhost and compiler processes.
- Update `docs/paperbunkr-todo.md` with the status.
- **Not verifiable here without permission:** the on-screen check in the running app (design § Verification).
