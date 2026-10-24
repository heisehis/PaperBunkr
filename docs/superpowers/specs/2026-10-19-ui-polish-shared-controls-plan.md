# UI polish, sub-project 2: Shared controls — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-19-ui-polish-shared-controls-design.md*

Subskills: `avalonia-styling` (Style vs ControlTheme, `/template/` selectors), `avalonia-pro-max/components` (button, chip, card anatomy),
and `avalonia-pro-max/review-checklist` before sign-off.

Facts established while surveying, which the design text didn't capture:

- **Button colours are theme component roles** (`Theming/ThemeComponentRegistry.cs`: `button.secondary`, `button.ghost` and `chip` border
  default to `border.default`). The faint outline (design Q4) is applied by changing those role defaults to `text.muted`, the vocabulary
  token that maps to `PbTextFaint`. The `AppTokens` fallbacks change to match. A theme that authors its own border still wins.
- **`border.strong` was considered and rejected:** it measures 2.6:1 on Daylight, Overcast and Windows 11, while faint is at least 3.1 everywhere.
- **FluentAvalonia's Button theme paints hover and pressed backgrounds on `/template/ ContentPresenter`.** Every transparent family pins
  those states. That painted background is what caused the doubled "selected" look on Series detail (shot 8).
- **`countPill` keeps its 2026-09-07 user-directed look** (solid surface0 with accent text, readable over cover art). The design text is
  corrected.
- **`StatusBadge` is an existing control** (`Badges.axaml`). `Border.statusBadge` matches its look.
- **Sidebar accent bar:** drawn as a `BorderThickness="3,0,0,0"` left border, transparent when inactive and accent when active, not as an
  inset `BoxShadow`. A `BoxShadows` string can't take a `DynamicResource` colour, and the always-present transparent bar keeps the text
  aligned. The old `:focus-within` border is removed; the app-wide focus adorner already rings sidebar items.

## Step 1: Tokens, role defaults and `Styles/Controls.axaml`
**Files:**
- `Styles/AppTokens.axaml`: `PbControlHeight` 28, `PbControlHeightSm` 22, `PbControlBorderBrush` (faint, Dynamic), and the
  `PbButtonSecondaryBorder` / `PbButtonGhostBorder` / `PbChipBorder` fallbacks moved to faint.
- `Theming/ThemeComponentRegistry.cs`: the three border defaults → `text.muted`.
- `Styles/Controls.axaml` (new): all eight families.
- `Styles/Primitives.axaml`: `Button.primary` / `.secondary` / `.ghost` move out to `Controls.axaml`.
- `App.axaml`: include `Controls.axaml` after `Primitives.axaml`.

**Verify:** the app builds; `ThemeGoldenResourceTests`. Goldens are regenerated only for the three border keys after checking the diff.

## Step 2: Migration script
**Files:** every `.axaml` under `src/Paperbunkr.App`, rewritten by a scratch script with a dry run first.
- **Class rename map:** see the design §1–8.
- **Local style blocks deleted:** any block whose selector's first compound targets a retired class or a family class (states included) and
  that's outside `Styles/`. Blocks that style *content inside* a family control are kept.
- **Per-use overrides stripped** on family buttons and chips; `.sm` is added where `FontSize` ≤ 11 or `Height` ≤ 24.
- **Retired family definitions in `Styles/*.axaml`** are deleted: `ContinuityChrome` / `ReadingListsChrome` `headerAction`, `DetailChrome`
  tab and card, `Badges` `tagPill`, `ScreenChrome` `statCard`.

**Depends on:** Step 1. **Verify:** the build and the lint test (Step 4).

## Step 3: C# and test references
**Files:**
- `Services/Input/TabStrip.cs`: it keeps matching `tab`, `segToggle`, `ipTab` and `prefNavItem`. `ipTab` has no remaining users after Step 2,
  but stays harmlessly.
- C# that adds or tests renamed classes: `"pbChip"`, `"tagPill"`, `"segItem"`, plus a grep for every retired name.

**Depends on:** Step 2. **Verify:** build.

## Step 4: Guard rails and tests
**Files:**
- `src/Paperbunkr.App.Tests/Theming/XamlConsistencyLintTests.cs`: three new rules (design § Guard rails).
- `src/Paperbunkr.App.Tests/Theming/SharedControlsStyleTests.cs` (new): headless assertions plus a render sheet written to `PB_RENDER_OUT`.

**Verify:** a targeted run of the lint tests, `SharedControlsStyleTests`, `FoundationStyleTests`, `ThemeGoldenResourceTests`, the `TabStrip`
tests, and the view tests for the most-changed XAML.

## Step 5: Build, review the renders, clean up
- Fresh XAML weave (delete the obj DLL if any XAML error occurs).
- Review the PNGs in three themes, then shut down build and test processes.
- Update `docs/paperbunkr-todo.md` and the design text (countPill, sidebar bar).
- **Not verifiable here:** the on-screen check in the running app.
