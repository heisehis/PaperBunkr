# App-wide motion system — Design

**Date:** 2026-10-17  
**Status:** Superseded 2026-10-20 by Motion system v2 (`2026-10-20-motion-system-v2-*`): its policy layer survives, its vocabulary was replaced. Originally implemented 2026-10-17; app build passes and focused policy, preference, theme, control, and overlay tests pass. Full fast suite was stopped early; two unrelated existing focus-ring assertions fail because MarkdownView clips its selectable text child.  
**Scope:** A coherent motion policy and staged migration for all Paperbunkr-owned UI motion.

## 1. Problem

Paperbunkr has motion spread across theme tokens, XAML transitions, page/view transitions, controls, composition effects, and code-behind. The current tokens provide only a small set of durations and one easing curve. Some effects are governed by `ReducedMotion`, others by theme effect quality, battery state, or local settings, and not every animation path shares the same policy. The result is difficult to tune consistently and makes a full-app style change risky.

The intended visual character is more expressive than the current “snappy and responsive” motion: larger but controlled movement, stronger damped spring feedback for hover/press/panels, and directional motion for navigation. Motion should feel connected and polished without bouncing content during page turns or making routine browsing noisy.

## 2. Goals and non-goals

### Goals

1. Give every app-owned animation one shared source for motion tokens, preferences, and quality policy.
2. Establish consistent motion classes for micro-interactions, panels, content changes, navigation, reader page turns, and continuous decoration.
3. Make an app-wide reduced-motion choice override every app-owned effect, including theme-authored motion.
4. Retain reader-specific transition style and speed while resolving them through the shared policy.
5. Support broad motion implementation by Paperbunkr developers while keeping theme-authored motion declarative, validated, and constrained to safe visual properties.
6. Migrate in stages so each family of effects can be verified independently.

### Non-goals

- Replacing Avalonia’s animation/composition systems or building a general-purpose animation editor.
- Letting theme JSON execute code or animate arbitrary layout/framework properties.
- Reworking the parked cosmetic pitches in this pass.
- Changing content semantics, reader navigation, or input action bindings.
- Making every item animate whenever a virtualized list realizes a container.

## 3. User decisions

The user approved the recommended choices in a grilling pass:

| Topic | Decision |
|---|---|
| Coverage | Audit all app-owned motion: hover/press/focus/selection, enter/exit, panels, list changes, screen navigation, reader transitions, and decorative effects. Content-specific reader effects remain distinct but use the shared policy. |
| Architecture | Centralize tokens and policy, then migrate in stages. Integrate the existing theme-authored motion runtime. |
| Visual character | More expressive, with larger movement and stronger spring effects. |
| Spring scope | Damped spring-like feedback for hover, press, and panels. Major navigation and page turns are directional and expressive without bounce. |
| User controls | One global motion policy, with no extra intensity sliders/presets. Keep the existing reader style and speed controls. |
| Reduced motion | One setting with System / On / Off; System is the default where the platform preference is available and reliable. It overrides all motion when active. |
| Performance | Performance mode drops decorative/costly effects but preserves essential interaction feedback. Battery throttles continuous effects. |
| Theme authoring | App code can animate any control/property needed with Avalonia, under shared policy. Theme declarations stay data-only and use a broad validated safe visual-property set. |
| Lists | Brief stagger for meaningful new/filtered results only; no entrance motion from routine virtualization or scrolling. |
| Theme triggers | Core UI states: load, hover, focus, pressed, selected, enter, and exit. |

## 4. Approaches considered

1. **Central motion system plus staged migration (recommended and approved).** One policy owns motion tokens, reduced-motion resolution, quality behavior, and shared transition helpers; existing effects migrate by family. This creates a consistent foundation while keeping each migration reviewable.
2. **Token cleanup only.** Retune `PbMotion*` durations/easing and leave independently implemented motion alone. This is cheaper, but cannot guarantee consistent accessibility or performance behavior and does not address the user’s full-app revamp.
3. **A full animation framework and extensive authoring controls.** Add a new abstraction/editor and expose detailed user/theme timing and intensity controls. This adds substantial surface area and duplicates Avalonia capabilities without a current need.

## 5. Experience and policy

### 5.1 Motion language

- **Micro feedback:** hover, press, focus, and selection use short, tactile changes. Hover can lift/scale subtly; press compresses; focus remains clear and accessible. Use spring-like easing with visible but damped overshoot, never repeated wobble.
- **Panels and drawers:** use a stronger, damped spring combined with opacity where appropriate. Movement follows the panel’s direction and ends at rest.
- **Meaningful content changes:** use restrained entrance/fade/translation and short stagger for newly loaded or filtered groups. Skip when items are only being virtualized, recycled, or scrolled into view.
- **Screen navigation:** use directional movement and/or fade with a clear relationship to navigation direction. No bounce on major transitions.
- **Reader page turns:** retain `None`, `Slide`, and `Crossfade`, and the per-reader speed value. Apply the global reduced-motion and quality rules to these styles; `None` remains no animation.
- **Continuous decoration:** low-priority effects (ambient motion, loops, pulses, parallax) yield first to battery and performance policies. Do not make an important state understandable only through motion.

The system defines semantic motion classes and validated presets (duration, easing/spring parameters, distance, and exit behavior) centrally. Exact numeric tokens and the spring implementation are implementation-plan details, and must respect Avalonia’s transition/animation/composition constraints and avoid animating layout-driving properties.

### 5.2 Reduced Motion setting

Replace the current binary reduced-motion preference with **System / On / Off**:

- **System** follows the OS preference when the platform reports a reliable value. If unavailable, resolve to motion enabled (equivalent to Off) and expose that System could not read an OS preference in diagnostics; do not silently claim OS synchronization.
- **On** disables or replaces non-essential movement and continuous effects across the app, theme motions, and reader transitions. Essential state feedback remains immediate and perceivable without movement.
- **Off** uses the selected motion policy, still subject to performance and battery tiers.

Changing the setting takes effect live. Persist it with existing app settings. Migrate old `true` to On and old `false` to System so existing installs receive the new System default; users can then choose Off explicitly. Since the old boolean cannot distinguish an untouched default from an explicit false choice, this mapping is a deliberate one-time compatibility trade-off.

### 5.3 Quality and battery policy

The global resolver receives the effective reduced-motion choice, effect quality, and battery state and provides one decision for each motion class:

1. Reduced Motion has highest priority and suppresses non-essential motion.
2. Performance quality removes decorative/costly effects and preserves essential short feedback, focus/selection state, and affordance clarity.
3. Balanced quality allows finite transitions but not continuous decorative motion.
4. High quality allows finite and continuous effects, except continuous effects are throttled when battery state requires it.

The existing `ThemeEffectQuality` behavior is the baseline to rationalize, not a second independent switch. All animation entry points, including XAML resources, composition animations, controls, theme runtime, and reader transitions, must ask the same policy or consume its resolved tokens.

### 5.4 Theme motion

Keep theme motion declarative and validate it at load time. Support the approved triggers (load, hover, focus, pressed, selected, enter, exit) and the existing safe visual family (opacity, translation, scale, rotation), with bounds on duration, distance, iteration, and chaining. Invalid declarations produce diagnostics and leave the element at rest. Theme motion must follow the same effective policy as built-in motion.

Paperbunkr code is not limited to the theme JSON allowlist: code-owned effects may use other Avalonia-supported visual properties when needed, provided they pass through shared policy and do not animate layout properties in ways that cause jank. Theme data never gains executable callbacks or unrestricted property access.

## 6. Architecture direction

Create or evolve one motion-policy service/runtime as the authoritative resolver for:

- user preference and OS preference resolution;
- motion classes and named timing/easing/spring tokens;
- effect quality and battery gating;
- reduced-motion substitutions;
- diagnostics/test seams for effective policy and started/skipped motion.

XAML should consume centralized resources for transitions. Code-driven motion should use shared helpers/adapters rather than duplicating durations, easing, or reduced-motion checks. Existing `ThemeMotionRuntime` should delegate policy decisions to this authority. Existing reader preferences remain reader-specific inputs to the resolver rather than a competing global motion system.

The implementation must preserve UI-thread safety, avoid per-frame UI-thread timers for composition animation, avoid detached controls being mutated during routed events, and ensure repeated state changes can interrupt/reverse cleanly without accumulating animation handlers.

## 7. Migration sequence

Implementation planning will inventory each existing animation site and map it to a motion class and owner. Migrate incrementally:

1. **Foundation:** policy model/service, preference persistence/migration, OS preference adapter where supported, shared tokens/helpers, and focused tests.
2. **Core interaction chrome:** buttons, cards, focus/selection, menus, hover/press and shared primitives.
3. **Panels and content changes:** drawers, accordions, toasts, meaningful list entrances/staggers, and screen-level transitions.
4. **Reader and navigation:** map existing reader styles/speeds and navigation/shared-element motion to shared policy; preserve their distinct behavior and reduced-motion guarantees.
5. **Theme runtime and decorative effects:** extend approved triggers, centralize gating, audit continuous effects/parallax and theme-declared motion.
6. **Audit and cleanup:** search the app for raw durations/easings/animation APIs and independent reduced-motion checks; document intentional exceptions and remove obsolete paths.

The phases may be reordered or split in the implementation plan based on dependency and test results, but no phase may leave two conflicting global policies active.

## 8. Compatibility and persistence

- Keep existing Reader `PageTransitionStyle` and `PageTransitionDurationMs`, including profile behavior. A global Reduced Motion preference takes precedence at playback time without rewriting the user’s reader choice.
- Preserve existing theme `motion` JSON compatibility; add triggers in a backward-compatible manner and retain current validation/diagnostic behavior.
- Preserve `ThemeEffectQuality` settings and theme-family override persistence unless the implementation plan identifies a migration that can retain values exactly.
- Migrate the boolean reduced-motion setting deterministically (`true` → On, `false` → System), retaining a safe fallback for old databases and settings files. This cannot preserve a distinction the old boolean did not store.

## 9. Validation and acceptance criteria

The system is complete when:

1. There is one effective policy used by all app-owned animation entry points; an audit finds no unexplained raw animation paths or separate reduced-motion behavior.
2. System/On/Off persists and applies live. System follows platform preference only where reliable; explicit On/Off always wins.
3. Reduced Motion suppresses non-essential screen, panel, reader, theme, and continuous motion while leaving state/focus feedback understandable.
4. Performance, Balanced, High, and battery behavior follow §5.3 consistently.
5. The expressive style is recognizable in hover/press/panel motion; navigation and page turns are directional without bounce; list stagger only occurs on meaningful content changes.
6. Reader style and speed remain user-controlled and compatible with existing saved settings/profiles.
7. Theme motion supports the approved triggers, rejects unsafe/out-of-range declarations with diagnostics, and remains data-only.
8. Motion interruptions, rapid repeated state changes, disposal, and detach/reattach do not leak handlers, strand a control away from rest, or crash.
9. Tests cover policy precedence, persistence migration, theme validation/triggers, motion interruption, reader override behavior, and key representative UI transitions. Visual review checks normal and reduced-motion behavior at representative screen sizes and effect-quality tiers.

## 10. Repository and CE findings

### Current Paperbunkr baseline (verified during design)

- `AppTokens.axaml` defines `PbMotionFast` (150 ms), `PbMotionSlow` (700 ms), `PbMotionStandard` (220 ms), `PbMotionLarge` (320 ms), and a shared `CubicEaseOut` resource.
- `ThemeService` currently writes zero durations for those tokens when its boolean `ReducedMotion` is on. The preference is a persisted `bool`.
- `ThemeMotionRuntime` runs theme-declared composition animations for opacity, translation, scale, and rotation. It currently supports load/hover/focus triggers and has its own quality/battery/reduced-motion gate.
- Theme quality is `High`, `Balanced`, or `Performance`; current runtime skips all theme motion in Performance, allows finite motion in Balanced, and allows continuous motion only in High when battery is not low.
- Reader page transitions already expose `None`, `Slide`, and `Crossfade`, with persisted duration and profile support.
- Motion is implemented in many separate places, including XAML transitions, `EntranceAnimation`, `AnimatedStackPanel`, parallax, `AccordionPanel`, scroll reveal, toasts, composition effects, `NavigationTransitionCoordinator`, shared-element transitions, and reader page animation.
- The Theme System v2 plan describes declarative motion as a later phase. This revamp should integrate its existing runtime and avoid implementing a competing theme motion engine.

### ComicRack CE parity check

The `_reference/ComicRackCE` source was checked for reader and shell animation behavior. CE supports reader fade, horizontal/vertical movement, and page-turn effects, with blending rate-limited unless `BlendWhilePaging` is enabled. It can animate display changes when enabled and supported by hardware. Dock panels slide; a shared `AnimatePanels` setting defaults on; `VisibilityAnimator` expands/collapses. Popup show/hide follows Windows menu animation (slide/blend, around 100 ms), and thumbnail fade-ins are enabled by default but can be disabled. Hover/press states exist without smooth hover animation. CE has no global reduced-motion preference; popup behavior follows the OS menu animation setting.

Paperbunkr deliberately adds a centralized global accessibility and quality policy beyond CE, while retaining compatible reader transition choices and panel/visibility concepts.

## 11. Spec self-review

- **Scope is bounded:** this is a shared policy plus staged migration, not an animation editor or theme-layout expansion.
- **Policy precedence is explicit:** Reduced Motion wins; quality and battery then gate decorative/continuous work; essential feedback remains available.
- **Reader compatibility is explicit:** local style/speed remain stored and distinct, with global policy applied at playback.
- **Theme safety is explicit:** theme JSON stays declarative and validated; developer code has broader implementation freedom.
- **Avalonia constraints are respected:** central resources/helpers can wrap existing Avalonia transitions, keyframe animations, and composition; implementation avoids layout-driving animation and per-frame UI-thread work. Exact API choices and OS preference detection are to be verified against the installed Avalonia/platform APIs during planning/implementation, not guessed here.
- **Migration is testable:** each family has clear policy mapping and acceptance criteria, and the final phase audits for bypasses.
- **Outstanding item:** exact motion distances, durations, spring damping/settling bounds, and platform support matrix for reading the OS preference must be made concrete in the implementation plan after checking supported APIs; they must remain within the approved expressive direction and not introduce extra user controls.
