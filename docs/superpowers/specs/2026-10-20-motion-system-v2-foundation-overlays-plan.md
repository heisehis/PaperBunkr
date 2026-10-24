# Motion system v2 — Foundation and Overlays — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md*

The tree is shared and dirty (`ThemeService.cs`, `AppTokens.axaml`, `Primitives.axaml` and `Overlays.axaml` already carry
uncommitted work), so every edit here is a targeted `Edit`, never a file rewrite. Builds and tests run one at a time (CLAUDE.md).

## Step 1: Four-mode preference + data migration
**Files:**
- `Paperbunkr.Data/Entities/MotionPreferenceMode.cs` (edit): `System, Full, Reduced, Off`.
- `Paperbunkr.Data/Migrations/20261020120000_MotionPreferenceFourModes.cs` + `.Designer.cs` (new, data only). The Designer is copied from `20261018093444_AddReaderPageFrameStyle.Designer.cs` with the attribute and class renamed; the model snapshot is unchanged.
- `Paperbunkr.Data.Tests/MotionPreferenceFourModesMigrationTests.cs` (new).
- `Paperbunkr.Data.Tests/AddSystemMotionPreferenceModeMigrationTests.cs` (edit: expected value after full migrate is now `Reduced`).

**What:** Up runs a single `CASE` (`'On'→'Reduced'`, `'Off'→'Full'`), so neither value is rewritten twice. Down maps `'Reduced'`/`'Off'→'On'` and `'Full'→'Off'`.

**Verify:** Data.Tests filtered to both migration classes, seeded from the old strings.

## Step 2: Roles, resolution, curves, slow-motion knob
**Files:**
- `Theming/Motion/MotionPolicy.cs` (edit):
  - add `MotionRole` (Acknowledge, Hover, Reveal, Replace, Travel, Ambient), `MotionSpec`, `Resolve(role)`, `AllowsMotion(role)` and `AllowsContinuous(batteryLow)`;
  - add a `PB_MOTION_SCALE` read with clamp;
  - delete `MotionClass` and `ShouldAnimate`.
- `Theming/Motion/DampedSpringEasing.cs` (new).
- `Theming/Motion/ThemeMotionRuntime.cs` (edit): `WouldPlay` goes through roles; `Configure(bool)`/`SetReducedMotion` map to `Reduced`/`Full`.
- Every former `MotionClass` call site (edit):
  - Travel: `NavigationTransitionCoordinator`, `SharedElementTransitionService`, `MotionAwarePageTransition`, `SmoothScrollViewer`, and `PageCanvas*` (Performance keeps them off, as today).
  - Replace: `AccordionPanel`, `AnimatedStackPanel`, `EntranceAnimation`, `AsyncCoverImage`, `LibraryPreviewPanel`.
  - Reveal: `ScrollReveal`, `SplashWindow` (finite), `PreferencesScreen.axaml.cs`.
  - Continuous Ambient: `App.axaml.cs`, `ParallaxBackdrop`, `MatrixRainOverlay`, `SplashWindow` ambient.
- `Paperbunkr.App.Tests/Theming/Motion/MotionPolicyTests.cs` (rewrite against roles) and `DampedSpringEasingTests.cs` (new).

**Depends on:** Step 1.

**Verify:** `MotionPolicyTests`, `DampedSpringEasingTests`, `ThemeMotion*` tests, and a build.

## Step 3: Tokens, aliases, root class, Preferences copy
**Files:**
- `Styles/AppTokens.axaml` (edit): role tokens, the four curves, and the legacy keys re-documented as aliases.
- `Services/ThemeService.cs` (edit):
  - `ApplyMotionResources` writes every role token plus the aliases from `Resolve`;
  - toggles the `motionReduced` class on every open `Window`;
  - keeps writing the legacy bool for `Reduced`/`Off`;
  - removes the old `Default*` constants.
- `Controls/MotionTokens.cs` (edit): role getters, no hardcoded fallbacks.
- `ViewModels/PreferencesScreenViewModel.cs` (edit): four descriptions.
- `Views/Preferences/AppearanceSection.axaml` (edit): the label reads "Motion".
- Windows opened later (Splash, plugin windows) get the class on open, through the same `ThemeService` helper.

**Depends on:** Step 2.

**Verify:** `PreferencesScreenViewModelTests` motion cases (a targeted filter, since this class is Slow) and a token-matrix test on `ThemeService`.

## Step 4: Interaction styles and existing violations
**Files:**
- `Styles/Primitives.axaml`:
  - Button transitions → Acknowledge + `PbMotionMoveEase`;
  - remove the global `:pressed` scale;
  - `motionReduced` overrides;
  - poster hover → spring.
- `Styles/Controls.axaml`: press at 0.97 on the compact families only.
- `Views/LibraryScreen.axaml`: cover lift −3 px / 1.02 on the Hover tokens with `PbMotionSpring`.
- `Views/ReaderScreen.axaml`: the bookmark glow becomes an opacity-animated fixed-shadow layer; the dot strip gets its reflow comment.
- `Views/MainWindow.axaml`: sidebar reflow comments.
- `Views/Home/HomeStyles.axaml`: verify −24 stays within the scale (Ambient).

**Depends on:** Step 3.

**Verify:** build; `SharedControlsStyleTests` and `FoundationStyleTests`.

## Step 5: OverlayShell lifecycle
**Files:**
- `Controls/OverlayShell.cs`:
  - `.open`/`.closing` states, generation counter, exit timer;
  - hit-test and focus-traversal cut on close;
  - finalise on detach;
  - a synchronous path when the exit duration is zero.
- `Styles/Overlays.axaml`: scrim and card states and transitions on the Reveal tokens.
- `Views/MainWindow.axaml`: discard-confirm and tour-offer move to `OverlayShell` + `DialogCard Size="Small"`.
- `Styles/Primitives.axaml`: delete the `modalScrim`/`modalCard` styles.
- `Paperbunkr.App.Tests/Controls/OverlayShellMotionTests.cs` (new).

**Depends on:** Step 3.

**Verify:** the new lifecycle tests, `DialogCardTests`, and the existing overlay tests (run with the policy set to Off through the test seam where they assume an instant collapse).

## Step 6: Drawers and toasts
**Files:**
- `Views/ActivityDrawerView.axaml`, `Styles/ReadingListsChrome.axaml`: 20 px travel, Reveal tokens, spring arrival, `.closing` exit.
- `Controls/ToastPresenter.cs` + the `entranceReady` exit style: fade-only exit on `RevealExit`.

**Depends on:** Step 3.

**Verify:** `ActivityDrawer*`, `ReadingListsScreenViewTests`, toast tests.

## Step 7: Popup, menu and flyout entrance
**Files:**
- `Controls/PopupFit.cs`: on `Opened`, an entrance from the resolved placement direction.
- A new attached `PopupMotion.Enter` on presenters.
- `Styles/Menu.axaml`: `MenuFlyoutPresenter`/`FlyoutPresenter` set it.

**Depends on:** Step 3.

**Verify:** a `PopupFit` direction unit test; a manual check on screen.

## Step 8: Lint rules
**Files:** `Paperbunkr.App.Tests/Theming/XamlConsistencyLintTests.cs`. Seven rules (spec §6), including the legacy-token baseline count.

**Depends on:** Steps 4–7 (so the rules start green).

**Verify:** the lint suite plus the existing 12 rules.

## Step 9: Docs
**Files:**
- `.claude/skills/paperbunkr-design-language/SKILL.md`: the Motion section.
- `docs/paperbunkr-todo.md`: a backlog entry.
- The spec's Status line.
- A memory note.

## Verification sweep
Build the App, then run the targeted classes above, the 12 + 7 lint rules and `ThemeHardcodedColorLintTests`. Never run the wide suite (memory). Then the user does the on-screen pass from spec §7 at `PB_MOTION_SCALE=5`.
