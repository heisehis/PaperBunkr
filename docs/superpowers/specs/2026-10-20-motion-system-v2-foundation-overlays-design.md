# Motion system v2 — Foundation and Overlays (sub-projects A + B) — Design

**Date:** 2026-10-20
**Status:** Implemented 2026-10-20 (plan: `2026-10-20-motion-system-v2-foundation-overlays-plan.md`). App builds; Data migration
tests, motion policy/spring/overlay lifecycle tests, the 19 XAML lint rules and ~400 targeted view/real-window tests pass. **User-verified on screen 2026-10-21.**

**Implementation deviations (deliberate):**
- §3.2 spring ω is 7.6, not 7.2: at 7.2 the end residual was 0.53 % (over the 0.5 % target); 7.6 peaks at 3.0 % at 62 % with 0.24 % residual.
- §5.1 lifecycle lives in a shared `Controls/Reveal.cs` (`Reveal.Apply` / `controls:Reveal.IsOpen`) used by `OverlayShell` and both drawers;
  the drawers needed the same exit and it fixed a pre-existing bug (the closed Reading Lists drawer was transparent but still Tab-reachable).
- §5.1 focus release: Avalonia 12.1 has no public clear-focus, so a closing surface disables itself at Animation priority and re-enables in
  the same tick (verified: clears focus exactly as hiding did, no frame renders between).
- §5.4 the entrance hooks every `Popup` (`Controls/PopupMotion.cs`, direction from `Placement`), not just `PopupFit` ones - only 14 of ~35
  popups carry PopupFit, and context menus/flyouts/SuggestBox are Popups too. Tooltips excluded; `PopupMotion.Enter="False"` opts out.
- §6 `TransitionTokensOnly` exempts the splash's easing (built before/without app resources, where a StaticResource curve throws).
- Theme JSON easing `backOut` now plays the damped spring; `spring` added as a name.
- Known limitation: during a 120 ms exit, directional (arrow-key XY) focus search could in principle still pick a control in the closing
  surface (tab order and pointer are already cut). Focus is cleared, so the next key starts from tab order.
**Supersedes in part:** [2026-10-17 app-wide motion system](2026-10-17-app-wide-motion-system-design.md). Its policy layer
(preference resolution, quality tiers, battery, `ThemeMotionRuntime` gating) stays. Its motion *vocabulary* (one curve, four
durations, `BackEaseOut` as "spring") is replaced here.
**Input:** the ChatGPT-drafted *PaperBunkr Animation System — Screenshot-Based Implementation Blueprint* (2026-10-20). It was
audited against the repository; §2 records which parts were adopted and which were already built, wrong or missing.

## 1. Programme decomposition

Motion v2 is five sub-projects. Each one gets its own spec → plan → build cycle, in this order:

| | Sub-project | Covers |
|---|---|---|
| **A** | **Foundation** *(this spec)* | Roles, curves, tokens, the four-mode preference, the reduced/off swaps, the slow-motion knob, lint, fixing existing violations |
| **B** | **Overlays** *(this spec)* | Every `OverlayShell` dialog, drawers, toasts, dropdown popups, context menus and flyouts |
| C | Navigation | Lateral route settle (replaces the full-width `PageSlide`), drill in/out, intent supersession, hardening the cover flight |
| D | On-page | Cards, tab indicator, expanders, entrance stagger (one-shot everywhere, capped tail), Home Spotlight, Insights charts |
| E | Reader chrome | Info/Tools panels, controls show/hide, slider feel. Page-turn styles stay user choices (Q5) |

A and B ship together: B is where A's roles first become visible, and B has no meaning without A.

**Decisions already settled for C–E** (recorded here so the later specs don't reopen them):
- Q2: switching between lateral screens becomes fade plus a 12 px upward settle over 220 ms. The shell stays still, and direction only matters for drill-in and back.
- Q5: the reader keeps None/Slide/Crossfade and the saved duration. Only the global Reduced/Off modes override them, and only at playback.

## 2. Audit — what exists, and why it reads as lackluster

All of this was verified in the working tree on 2026-10-20 against Avalonia 12.1.1.

| Finding | Location | Consequence |
|---|---|---|
| The "spring" is `BackEaseOut` on every `Button`'s RenderTransform and on Library cover hover | `Styles/Primitives.axaml:49`, `Views/LibraryScreen.axaml:123` | Every click overshoots past 1.0 on release, and covers wobble. A spring should settle once; this rebounds on every input |
| `Button:pressed → scale(0.96)` applies to every Button, including menu rows, tabs, sidebar rows and wide cards | `Primitives.axaml:59` | A 300 px row shrinks by 12 px. Press feedback is not proportional to control size |
| `OverlayShell` opens and closes by setting `IsVisible = IsOpen` | `Controls/OverlayShell.cs:137` | All 30+ in-window dialogs, including plugin modals and Welcome/What's New, hard-cut. Dropdowns, menus and flyouts have no motion either |
| Only two legacy overlays animate (`Border.modalScrim`/`modalCard`) | `Primitives.axaml:96-121` | Two motion systems for one job |
| Reduced Motion writes `TimeSpan.Zero` to every token | `Services/ThemeService.cs:590-593` | It cuts instead of switching to a fade. "Reduced" behaves like "Off" |
| Every transition shares one curve (`CubicEaseOut`); `MotionAwarePageTransition` creates its own easing objects | `AppTokens.axaml:192`, `Theming/Motion/MotionAwarePageTransition.cs:29` | No hierarchy: entrances, exits and position changes all feel the same |
| Duration drift: XAML says 150/280/380/520 ms, and `MotionTokens` falls back to 220 ms for Standard | `AppTokens.axaml`, `Controls/MotionTokens.cs:20` | No single source of truth |
| Drawers travel 380 px (Activity drawer, Reading Lists side drawer) | `Views/ActivityDrawerView.axaml:60`, `Styles/ReadingListsChrome.axaml:323` | Long, slow travel for a panel that is also faded and hit-test-gated anyway |
| `BoxShadowsTransition` on the reader's bookmark glow | `Views/ReaderScreen.axaml:199` | Banned by the design language (it tears focus rings) |
| Width transitions on the sidebars and the reader's dot strip | `Views/MainWindow.axaml:71,92`, `ReaderScreen.axaml:137` | Layout runs every frame. Legitimate for the sidebars, but undocumented |

**Blueprint reconciliation:**
- **Adopted:** its motion contract (immediate input, new intent supersedes old, no replay on container recycle, logical state is authoritative, each animated property has one owner), its 90/120/180/220/280 ms ladder, its distance ladder, the dialog 0.98 scale, its four-mode setting, and its verification-scenario style.
- **Already built, so not rebuilt:** the motion policy and System preference (`MotionPolicy`), the cover flight (`SharedElementTransitionService`, Library ↔ Detail ↔ Reader), and reader transition styles with profiles.
- **Rejected:** "no overshoot by default" (it conflicts with the 10-17 decision; Q1 settles it), the Instant/Fade/Slide rename of reader styles (Q5), and "Full/Reduced/Off" as a brand-new setting (it is a migration of the existing one, §4).
- **Missing from the blueprint, added here:**
  - overlays as the first target;
  - actual curve definitions;
  - exit/enter asymmetry;
  - separate fade and move timing, so Reduced can keep one and drop the other;
  - lint enforcement;
  - a slow-motion knob for tuning by eye.

## 3. Motion vocabulary

### 3.1 Roles

Every animation in the app belongs to exactly one role. A role fixes its timing, curve, travel and how it degrades.

| Role | Used for | Fade | Move | Enter curve | Exit |
|---|---|---|---|---|---|
| **Acknowledge** | Press, toggle, checkbox, selection colour | 90 ms | 90 ms | `PbMotionMoveEase` | same, symmetric |
| **Hover** *(sub-role of Acknowledge)* | Hover colour, card/cover lift | 120 ms | 160 ms | colour: `PbMotionEase`; lift: `PbMotionSpring` | 120 ms, `PbMotionExitEase` |
| **Reveal** | Dialogs, drawers, toasts, popups, menus | 180 ms | 180 ms | `PbMotionEase` (decelerate) | 120 ms, `PbMotionExitEase`; popups/menus have no exit (Q11) |
| **Replace** | Tab content, result sets, view-mode swaps | 160 ms | none | `PbMotionEase` | the outgoing content fades over the same 160 ms |
| **Travel** | Routes, the cover flight *(spec C)* | 220 ms | 220 ms (flight 280 ms) | `PbMotionEase`; flight `PbMotionMoveEase` | reverse cue on back |
| **Ambient** | Backdrop crossfades, Spotlight choreography, continuous decoration | 360 ms | 360 ms | `PbMotionMoveEase` | — |

**Rules that apply to every role:**
- An exit lasts about 65% as long as its entrance. Leaving should never take longer than arriving.
- Staggers belong to spec D, but the cap is fixed here: 20 ms per group, at most 6 groups, and the whole choreography finishes within 320 ms.

### 3.2 Curves

| Key | Definition | Use |
|---|---|---|
| `PbMotionEase` *(existing key, new value)* | `SplineEasing` 0.05, 0.7, 0.1, 1.0 (strong decelerate) | Everything that **enters**. Redefining this key upgrades all ~57 existing transitions at once |
| `PbMotionExitEase` *(existing key, new value)* | `SplineEasing` 0.3, 0.0, 0.8, 0.15 (accelerate) | Everything that **leaves** |
| `PbMotionMoveEase` *(new)* | `SplineEasing` 0.2, 0.0, 0.0, 1.0 (standard) | Position or size changes between two on-screen states: press, flight, ambient |
| `PbMotionSpring` *(new)* | `DampedSpringEasing`: ζ = 0.745, ω = 7.6 (normalised time) | **Only** hover lift and panel arrival (Q1) |

**`DampedSpringEasing`** is a small `Easing` subclass in `Theming/Motion/`. It computes the underdamped step response:

```text
x(t) = 1 − e^(−ζωt) · (cos(ω_d·t) + (ζω/ω_d)·sin(ω_d·t)),   ω_d = ω·√(1−ζ²)
```

It returns exactly 1 for t ≥ 1. With these constants:
- it overshoots once, by about 3.0%, peaking at about 62% of the duration;
- the residual at t = 1 is about 0.25%, so the final snap is invisible;
- there is no second visible bounce.

These numbers are the test assertions (§7).

`SplineEasing` has been in Avalonia since 0.10. The plan's first step confirms it is in 12.1.1. If it isn't, a 20-line cubic-Bézier `Easing` subclass is the fallback; that is not a design change.

`BackEaseOut`, `ElasticEase` and `BounceEase` are removed everywhere and banned by lint (§6).

### 3.3 Distances and scales

Translate distances come from one set: **1, 2, 3, 4, 6, 8, 12, 20, 24 px**.
- 4 px: popups and menus.
- 6 px: local entrances.
- 8 px: toasts.
- 12 px: routes.
- 20 px: drawers and panels.
- 24 px: Ambient only.
- 1–3 px: hover nudges.

Scales:
- **press** 0.97;
- **dialog entry** 0.98;
- **hover lift** `translateY(-3px) scale(1.02)`, on the cover artwork only, never on the hit-tested Button (the existing flicker rule in `LibraryScreen.axaml` stays).

Avalonia's transform mini-language can't bind a resource, so these values are literals in styles. The distance lint (§6) is what keeps them on the scale.

### 3.4 Tokens

`ThemeService.ApplyMotionResources` writes all of these from `MotionPolicy.Resolve` (§4.2). Each one is a `TimeSpan` application resource:

| Token | Full | Reduced | Off |
|---|---|---|---|
| `PbMotionAcknowledge` | 90 | 90 *(colour only, see §4.3)* | 0 |
| `PbMotionHoverFade` | 120 | 100 | 0 |
| `PbMotionHoverMove` | 160 | 0 | 0 |
| `PbMotionRevealFade` | 180 | 100 | 0 |
| `PbMotionRevealMove` | 180 | 0 | 0 |
| `PbMotionRevealExit` | 120 | 80 | 0 |
| `PbMotionReplace` | 160 | 100 | 0 |
| `PbMotionTravelFade` | 220 | 120 | 0 |
| `PbMotionTravelMove` | 220 | 0 | 0 |
| `PbMotionFlight` | 280 | 0 | 0 |
| `PbMotionAmbient` | 360 | 0 | 0 |

**The key idea is splitting fade time from move time.** A Reveal transition animates `Opacity` on the `*Fade` token and `RenderTransform` on the `*Move` token. Under Reduced, the move token is zero, so the transform snaps while opacity is still near 0. The surface appears as a short fade with no travel, and no style needs to know which mode is active.

**Legacy aliases.** `PbMotionFast`, `PbMotionStandard`, `PbMotionLarge` and `PbMotionSlow` stay as resources, resolved to Hover-fade (120), Reveal-fade (180), Flight (280) and Ambient (360). Nothing breaks, and existing transitions pick up v2 timing immediately. New XAML uses the role tokens. A baseline lint (§6) counts legacy-token uses, and the count may only go down; specs C–E migrate the rest. `MotionTokens.Get*` gains role getters, and its stale fallbacks are deleted: they read from the policy, not from hardcoded numbers.

## 4. Preference and policy

### 4.1 The setting (Q4)

Preferences › Appearance › **Motion** (the existing `SuggestBox` at `AppearanceSection.axaml:640`, relabelled) offers four choices:
- **System:** follows Windows' "Show animations" through the existing `SPI_GETCLIENTAREAANIMATION` read, so animations off means Reduced and on means Full. If the OS value can't be read, it resolves to Full, and the description says the OS preference was unavailable, as it does today.
- **Full:** every effect allowed by quality and battery.
- **Reduced:** no travel, scale, flight, stagger or ambient motion; short opacity fades only.
- **Off:** no animation at all; final states apply immediately.

**Persistence.** `MotionPreferenceMode` becomes `System, Full, Reduced, Off`, stored as a string as today (`PaperbunkrDbContext.cs:2043`). A data migration rewrites stored values. **Watch out: the old string `"Off"` means "reduced motion off", which is *Full*.** The mapping is:

| Stored today | Becomes |
|---|---|
| `"On"` | `"Reduced"` |
| `"Off"` | `"Full"` |
| `"System"` | unchanged |

Down migration reverses it, and `Reduced`/`Off` both map back to `"On"`. The legacy `ReducedMotion` bool keeps being written (`true` for Reduced and Off) for the old-settings fallback path. The migration test must start from a database that contains old `"Off"`. The up-down-up antipattern memory applies: test against real old values, not round-tripped ones.

### 4.2 Resolution (Q7)

`MotionPolicy` gains `MotionRole` (Acknowledge, Hover, Reveal, Replace, Travel, Ambient) and one entry point:

```text
MotionSpec Resolve(MotionRole role)  →  { Fade, Move, Exit, AllowsTransform, Enabled }
```

Precedence, highest first:
1. **Off:** every duration is 0 and `AllowsTransform` is false.
2. **Reduced:** the Reduced column of §3.4. `AllowsTransform` is false for every role.
3. **Performance quality:** Travel resolves to its Reduced form (crossfade, no travel); Ambient resolves to instant. Acknowledge, Hover, Reveal and Replace stay at full strength, because they are cheap opacity and transform changes.
4. **Balanced:** everything allowed except continuous Ambient loops.
5. **High:** everything allowed. Low battery throttles **only** continuous Ambient (the existing behaviour).

**Changes to existing code:**
- The old `MotionClass` maps onto roles, and its callers are migrated: EssentialFeedback → Acknowledge, Standard → Reveal/Replace/Travel by call site, Decorative and Continuous → Ambient. The enum is then deleted, so two vocabularies never coexist.
- `ThemeMotionRuntime` gates theme-declared triggers through `Resolve`: load/enter goes through Reveal, hover/focus/pressed through Hover/Acknowledge, continuous through Ambient.
- Themes cannot retune role timing (Q12).

### 4.3 Held transforms under Reduced/Off

The fade/move split covers transitions *between* states. It doesn't cover a transform *held* while a state is active, such as press `scale(0.97)` or hover lift. With a zero move token, those would snap rather than disappear.

`ThemeService` therefore toggles one root class on every app `Window`: `motionReduced` (Reduced and Off) or nothing. Shared styles add exactly two overrides under that class, which reset the press and hover-lift transforms to `none`. Press feedback under Reduced is then the existing colour change on the Acknowledge token. No other style may branch on motion mode.

### 4.4 Slow-motion knob

At startup, the environment variable `PB_MOTION_SCALE` (a float, clamped to 0.1–10, default 1) is read once and multiplies every duration `Resolve` returns, including the spline and spring timings and the overlay exit timer. It is logged once at startup when it isn't 1. This is a tuning tool, not a user setting: there is no UI and no input action. The user's on-screen check (§7) runs at `PB_MOTION_SCALE=5`.

## 5. Overlays (sub-project B)

### 5.1 `OverlayShell` lifecycle (Q8, Q9)

One change animates every in-window dialog: all 30+ shells in `MainWindow.axaml`, plus the shells in `ContinuityCompareOverlay`, `ReadingListPageView`, `AboutSection` and `SmartTemplateGallery`. `DialogCard` needs no changes.

**Visual states** live in the shell's template styles (`Styles/Overlays.axaml`):
- **Closed:** scrim opacity 0; card opacity 0, `scale(0.98)`.
- **Open** (`.open`): scrim and card opacity 1, card `scale(1)`.
- Opacity uses `PbMotionRevealFade` with `PbMotionEase`, and the card's transform uses `PbMotionRevealMove`.
- On close, the shell switches the opacity transitions to `PbMotionRevealExit` with `PbMotionExitEase`, through a `.closing` class whose transitions override the open ones.

**Lifecycle**, driven by `IsOpenProperty.Changed`:
1. **Open:** `IsVisible = true`, realise deferred content (the existing `DeferContent` path), then add `.open`. Avalonia transitions animate from the actual current value, so this works whether or not a frame has rendered in between. This is the behaviour `SharedElementTransitionService` already relies on.
2. **Close:**
   - The view-model state has already changed; nothing waits.
   - In the same tick, remove `.open`, add `.closing` and set `IsHitTestVisible = false`.
   - Take the shell out of focus traversal, without the disabled-style flash that `IsEnabled = false` would cause; the plan picks the mechanism and a test pins it.
   - Focus restoration runs immediately, as it does today.
   - When `RevealExit` has elapsed, set `IsVisible = false` if the shell is still closing.
3. **Reopen mid-exit:** a generation counter invalidates the pending collapse. Removing `.closing` and adding `.open` retargets each transition from its current value, so the dialog reverses where it is rather than restarting.
4. **Zero exit duration** (Off, or an unattached shell): collapse synchronously, exactly as today.
5. **Detached from the visual tree mid-exit** (window closing, view torn down): finalise to `IsVisible = false` and drop the pending timer. Never leave a ghost scrim behind.

The collapse is deferred by a timer and never runs inside a routed event. It respects the CLAUDE.md "don't detach a control while it is raising an event" rule by construction.

**Stacking (Q10).** Only the top shell animates. The shell underneath doesn't move; the top shell's own scrim fading in provides the dimming. No cross-shell coordination code is needed.

**Retiring the legacy pair.** `Border.modalScrim`/`modalCard`, the two hand-rolled discard-changes and tour-offer overlays, move onto `OverlayShell`, and their styles are deleted. That leaves one dialog motion system.

### 5.2 Drawers

The Activity drawer (`ActivityDrawerView.axaml`) and the Reading Lists side drawer (`ReadingListsChrome.axaml`, `Border.rlDrawer`) already gate on opacity and hit testing. Their closed transform changes from 380 px to **20 px** toward their own edge. They use the Reveal tokens: fade on `RevealFade`, transform on `RevealMove` with `PbMotionSpring` for arrival (Q1, "panel arrival"), and exit on `RevealExit` with `PbMotionExitEase` via a `.closing` state.

### 5.3 Toasts

`ToastPresenter` enters by rising 8 px and fading in (Reveal). It exits by fading only, over `RevealExit`. Its removal timer reads `RevealExit` instead of `GetStandard()`.

### 5.4 Popups, menus and flyouts (Q11)

Popups animate **in only**, matching CE, whose menus follow the Windows menu animation: a short show and no lingering close.

**Dropdowns.** Every dropdown already carries `controls:PopupFit.ToWindow="True"` (design language). `PopupFit` gains the entrance:
- when the popup's child attaches, it starts at opacity 0, offset 4 px toward the anchor, then animates to rest on `RevealFade`/`RevealMove` with `PbMotionEase`;
- the direction comes from the placement `PopupFit` already resolves (opened below the anchor → drops 4 px down; opened above → rises);
- closing is Avalonia's immediate removal.

**Context menus and flyouts.** `MenuFlyoutPresenter` and `FlyoutPresenter` (`Styles/Menu.axaml`) get the same entrance through a style-set attached property, so the shared `MenuFlyout` mechanism needs no per-menu change.

**Not changed:** tooltips (Fluent default) and the `SuggestBox` open/close *behaviour* (CLAUDE.md input rules). Its dropdown gets the entrance through `PopupFit` like every other dropdown.

## 6. Lint (Q13: fix, don't baseline)

These rules are added to `XamlConsistencyLintTests`. Every current violation is fixed in this programme, with no allow-list. The one exception is the legacy-token counter, which is a down-only baseline by design.

| Rule | Checks | Fixes it forces now |
|---|---|---|
| `NoOvershootEasings` | No `BackEase*`, `ElasticEase*` or `BounceEase*` in `.axaml` or `.cs` (excluding `DampedSpringEasing.cs`) | `Primitives.axaml:49` (Button → Acknowledge + `PbMotionMoveEase`), `LibraryScreen.axaml:123` (cover → Hover-move + `PbMotionSpring`) |
| `NoBoxShadowsTransition` | No `BoxShadowsTransition` anywhere | Reader bookmark glow: a fixed-shadow glow layer whose **Opacity** animates on Acknowledge |
| `LayoutTransitionsJustified` | A `Width`/`Height`/`Margin`/`Padding` transition needs a preceding XML comment containing `motion: reflow` and a reason | Sidebars (`MainWindow.axaml:71,92`) and the reader dot strip (`ReaderScreen.axaml:137`) get the comment; their content must reflow |
| `TransitionTokensOnly` | Transition `Duration` is a `PbMotion*` DynamicResource and `Easing` is a `PbMotion*` StaticResource, with no inline easing elements | The two inline `BackEaseOut` blocks above |
| `TranslateDistancesOnScale` | Translate literals in `RenderTransform` setters fall in the §3.3 set (plus 0) | Drawers 380 → 20 (§5.2); Library hover −5/1.04 → −3/1.02 |
| `PressScaleScoped` | `scale(...)` under `:pressed` only on the compact families: `iconButton`, `primary`, `secondary`, `ghost`, `danger`, `segItem`, `card`/`clickCard` | Remove the global `Button:pressed` rule; add it per family at 0.97 (Q3). Rows, tabs and menu rows get colour only |
| `LegacyMotionTokenBaseline` | Count of `PbMotionFast/Standard/Large/Slow` uses ≤ the recorded baseline | — (down-only) |

The `paperbunkr-design-language` skill's Motion section is updated with the roles, curves, the distance scale and these rules. It is the reference future UI work reads.

## 7. Testing and verification

**Unit tests** (fast set):
- `DampedSpringEasing`:
  - Ease(0) = 0 and Ease(1) = 1;
  - the peak is 1.030 ± 0.005 at t = 0.62 ± 0.05;
  - no sample after the peak goes below 0.995 or above the peak;
  - the residual at t = 0.99 is within 0.005 of 1.
- `MotionPolicy.Resolve`: the full role × {System on/off, Full, Reduced, Off} × {High, Balanced, Performance} × battery matrix against §3.4 and §4.2; `PB_MOTION_SCALE` multiplication and clamping.
- Migration (Data.Tests): old `"On"` → Reduced, old `"Off"` → Full, `"System"` unchanged, and down migration, all starting from a database seeded with the old strings.
- `OverlayShell` lifecycle (headless, real styles via `FoundationStyleTests.WithFoundation`):
  - open sets the visible and `.open` state;
  - close makes the shell non-hit-testable and out of traversal immediately, while `IsVisible` stays true until the exit elapses;
  - reopening mid-exit leaves it open with no collapse;
  - in Off mode, close collapses synchronously;
  - detaching mid-exit finalises;
  - the view model's `IsOpen` round-trips without awaiting.
- Existing tests that assume an instant collapse run with the policy set to Off through the test seam rather than being rewritten to wait.
- The new lint rules, plus a run of the existing 12.

**Render check.** PNGs of a `DialogCard` mid-open (via a test seam that pins transition progress) in the default, Daylight and Matrix themes, confirming token colours and no clipped card at 1366×768.

**On-screen check by the user**, at `PB_MOTION_SCALE=5` and then at 1. This is the gate; every polish pass so far found something only the real app showed.
1. Open and close Confirm, Issue Properties (a heavy deferred dialog) and Welcome. Close and immediately reopen.
2. Open a dialog from a dialog (a plugin's nested modal).
3. Activity drawer, Reading Lists drawer, a toast.
4. A Library toolbar dropdown opening below and one opening above its anchor; a right-click context menu.
5. Hover a Library cover (one settle, no wobble). Press a primary button and a menu row: the button shrinks, the row only changes colour.
6. Switch Motion to Reduced (fades only, no travel or shrink), then Off (everything instant). Each change takes effect live without a restart.
7. Set effect quality to Performance: dialogs and popups still animate.

Then run the `avalonia-pro-max/review-checklist`, as CLAUDE.md requires.

**Performance.** The new motion is opacity and render-transform only, on one surface at a time. The plan includes one profiling pass on the user's hardware (i7-4710HQ / GTX 860M): the first open of Issue Properties, checking that the entrance starts after deferred content is realised rather than stalling mid-fade.

## 8. Out of scope here

Route transitions, intent supersession and cover-flight hardening (C). Card and tab-indicator choreography, entrance stagger, Spotlight and charts (D). Reader chrome (E). New user-facing motion controls beyond the four-mode setting. Theme-tunable timing. Exit animations for popups and menus.

## 9. Spec self-review

- **Placeholders:** none. Every token, curve constant, distance and lint rule has a concrete value. The two plan-time confirmations (that `SplineEasing` exists in 12.1.1, and the mechanism for removing a closing shell from focus traversal) each have a stated fallback or test requirement, not an open design question.
- **Consistency:**
  - The Reduced column (§3.4) matches §4.2 precedence and §4.3 held-transform handling.
  - The popup entrance-only rule (§5.4) matches the role table, where Reveal has no exit for popups.
  - The drawer spring use matches Q1's scope.
- **Scope:** A + B form one plan: foundation first, overlays second, with lint fixes spanning both. C–E are explicitly deferred, and their settled decisions are recorded in §1.
- **Ambiguity resolved:**
  - The old `"Off"` string collision is spelled out (§4.1).
  - "Only the top shell animates" needs no coordination code (§5.1).
  - Legacy tokens are aliases, not removed (§3.4).
