# App-wide motion system — Implementation Plan

*Implements: [2026-10-17-app-wide-motion-system-design.md](2026-10-17-app-wide-motion-system-design.md)*

## Step 1: Shared policy and persisted preference
**Files:** `src/Paperbunkr.App/Theming/Motion/` (new policy types and Windows preference adapter); `src/Paperbunkr.Data/Entities/AppSettings.cs`; `src/Paperbunkr.Data/PaperbunkrDbContext.cs`; a new EF migration and `PaperbunkrDbContextModelSnapshot.cs`; policy tests under `src/Paperbunkr.App.Tests/Theming/`.
**What:** Add System/On/Off preference and one resolver for motion class, effect quality, battery, and platform state. Windows reads `SPI_GETCLIENTAREAANIMATION`; failed or unsupported reads are unavailable and resolve System to animations enabled. Convert old `ReducedMotion=true` to On and `false` to System. Keep the old column readable during compatibility handling.
**Depends on:** none.
**Verify:** focused policy and migration tests; build `Paperbunkr.Data` and app.

## Step 2: Preferences and live application
**Files:** `src/Paperbunkr.App/Services/ThemeService.cs` and partials; `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs`; `src/Paperbunkr.App/Views/Preferences/AppearanceSection.axaml`; appearance/preferences tests.
**What:** Replace the binary Reduce motion switch with a System/On/Off selector, persist it, publish effective state live, and expose whether System is available. Keep a compatibility wrapper for existing callers until all call sites migrate.
**Depends on:** Step 1.
**Verify:** preference persistence, live-update, fallback, and old-value conversion tests.

## Step 3: Route existing motion through the resolver
**Files:** `src/Paperbunkr.App/Controls/MotionTokens.cs`, `src/Paperbunkr.App/Theming/Motion/ThemeMotionRuntime.cs`, `src/Paperbunkr.App/Services/ThemeService.Layouts.cs`, `src/Paperbunkr.App/Services/NavigationTransitionCoordinator.cs`, `src/Paperbunkr.App/Views/ReaderPageVisualHandler.cs`, `src/Paperbunkr.App/Views/PageCanvas*.cs`, `src/Paperbunkr.App/Views/SplashWindow.axaml.cs`, `src/Paperbunkr.App/Controls/{AccordionPanel,AnimatedStackPanel,EntranceAnimation,ParallaxBackdrop,ScrollReveal,SmoothScrollViewer,ToastPresenter}.cs`.
**What:** Make all code-driven motion and theme motion consult the same policy. Preserve reader style/speed and implement reduced-motion behavior without overwriting user reader choices. Ensure interrupted animations return to rest and handlers detach cleanly.
**Depends on:** Steps 1–2.
**Verify:** policy-precedence, theme-quality/battery, reader-transition, navigation, splash, and control tests.

## Step 4: Central tokens and expressive interaction motion
**Files:** `src/Paperbunkr.App/Styles/AppTokens.axaml`, `src/Paperbunkr.App/Styles/Primitives.axaml`, plus every style/view found by the motion inventory that declares app-owned transitions (`FormControls.axaml`, `ReadingListsChrome.axaml`, `Typography.axaml`, `Views/Home/HomeStyles.axaml`, `Views/DetailBand.axaml`, `Views/ChangelogEntryView.axaml`, `Views/EventMapView.axaml`, and other confirmed matches).
**What:** Define semantic durations/easings/spring presets and migrate hover, press, focus, selection, panel, and navigation transitions. Use larger controlled movement with damped spring on hover/press/panels, directional navigation without bounce, and no layout-driving animation.
**Depends on:** Step 3.
**Verify:** XAML compile/build, targeted style and reduced-motion tests, and visual review at compact/default/wide sizes.

## Step 5: Meaningful content entrances and theme triggers
**Files:** `src/Paperbunkr.App/Controls/EntranceAnimation.cs`, `src/Paperbunkr.App/Controls/AnimatedStackPanel.cs`, relevant list/screens discovered by inventory, `src/Paperbunkr.App/Theming/Motion/ThemeMotionDefinition.cs`, `ThemeMotionRuntime.cs`, and theme authoring/validator tests.
**What:** Add validated `Pressed`, `Selected`, `Enter`, and `Exit` theme triggers while retaining existing JSON behavior. Gate list staggering to explicit meaningful loads/filter results and disable it for realization, recycle, and scroll paths.
**Depends on:** Steps 3–4.
**Verify:** parser/diagnostic and trigger tests; list lifecycle tests proving virtualization does not retrigger entrances.

## Step 6: Audit, compatibility review, and roadmap note
**Files:** all motion inventory matches not handled above; `docs/paperbunkr-todo.md` if roadmap-relevant status should be recorded after verified implementation.
**What:** Search for raw durations/easings, animation creation, reduced-motion checks, and bypasses. Classify intentional exceptions, remove duplicate policy paths, complete the Avalonia pre-delivery motion/accessibility checklist, and update the roadmap with verified status and commit reference when available.
**Depends on:** Steps 1–5.
**Verify:** run affected targeted test classes, then `dotnet test src/Paperbunkr.App.Tests --filter "Speed!=Slow"`; build the solution once; manually inspect normal and reduced-motion flows and platform preference availability where supported.
