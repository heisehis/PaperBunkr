# Navigation Transition System

**Status:** Design. Not yet implemented.
**Sub-project 1 of 2** in the "full app chrome animations" effort. Sub-project 2 (Chrome & Content
Motion Polish — staggered list entrance, hero parallax, sidebar slide, breadcrumb/banner slides,
toast slide-in, Activity Center audit) gets its own spec and is deliberately **not** covered here.

**Extends** [2026-08-24 Navigation Shell & Motion System](2026-08-24-navigation-shell-motion-system-design.md),
which built lateral (rail-to-rail) screen transitions and explicitly deferred drill-down motion.
This spec picks up that deferred piece and adds shared-element ("hero") transitions on top.

## Background

Motion in the app today:

- **Tokens:** `PbMotionFast` (~150ms), `PbMotionSlow` (~700ms), `PbMotionEase` (`CubicEaseOut`),
  defined in [App.axaml](../../../src/Paperbunkr.App/App.axaml). `SkinService.ApplyReducedMotion`
  (and the startup `ApplySkin` path) overwrite `PbMotionFast`/`PbMotionSlow` to `TimeSpan.Zero`
  when the Preferences → Appearance "Reduced motion" toggle is on.
- **Lateral screens** (Home, Library, Books, Smart Lists, Reading Lists, Continuity, Preferences):
  one `TransitioningContentControl` bound to `MainViewModel.ActiveScreenContent`, `PbScreenSlide`
  (`PageSlide`) with direction from `IsTransitionReversed` (rail-order comparison in
  `OnCurrentScreenChanging`).
- **Drill-down screens** (`Detail`, `MangaDetail`, `Reader`, `BookReader`, `PdfReader`,
  `BookDetail`): six separate, always-instantiated `ContentControl`s in
  [MainWindow.axaml](../../../src/Paperbunkr.App/Views/MainWindow.axaml), each toggled by its own
  `IsVisible="{Binding IsX}"`. Switching to or between them is an **instant cut**.
- **Navigation history** ([NavigationHistory service, per the 2026-08-30 app-shell spec](2026-08-30-app-shell-navigation-history-design.md)):
  a drill-down back/forward/breadcrumb stack. Fresh drill-downs call `PushEntry`; Back / Forward /
  breadcrumb jumps call `ReplayEntry`. This already distinguishes "going deeper" from "going back."

So: no motion exists on any drill-down navigation, and the marquee interaction — opening an issue
from the Library grid — is a hard cut from a grid of covers to a hero banner showing the same
cover.

## Goal

1. Drill-down navigation gets a **push / pop** transition (fade + subtle scale), direction driven
   by the existing `NavigationHistory` state.
2. The **cover art** performs a true shared-element flight between the Library grid tile and the
   Detail hero (and the Reader first page) — **forward and back**. Opening an issue: the tile's
   cover detaches, flies, and grows into the hero. Going back: it flies home into its grid slot.
3. Everything honors the existing reduced-motion preference.

Fidelity decision (from brainstorming): **hybrid** — the *cover image* gets the real
geometry-matched flight; everything else around it is a cross-fade. Not a full shared-element
system for arbitrary content.

## Architecture

Three new pieces, all in the App layer:

| Piece | Responsibility |
|---|---|
| `SharedElement` (attached properties) | Declarative participation — a screen marks its cover element with a key + hands over the decoded image |
| `ISharedElementTransitionService` | Snapshots the outgoing element, animates a clone in an overlay layer to the incoming element's rect |
| `NavigationTransitionCoordinator` | Sequences a drill-down navigation: capture → swap content → fly → clean up; honors reduced-motion |

`MainViewModel` stays visual-free — it invokes the coordinator through a callback, the same way it
already takes `ShowToast` / `NavigateBack` / etc. as constructor `Action`s.

### 1. Motion tokens

Add to `App.axaml`:

```xml
<x:TimeSpan x:Key="PbMotionStandard">0:0:0.22</x:TimeSpan>  <!-- screen cross-fade, push/pop -->
<x:TimeSpan x:Key="PbMotionLarge">0:0:0.32</x:TimeSpan>     <!-- cover flight -->
```

`PbMotionEase` (`CubicEaseOut`) is reused for enter/flight. Exits use ~70% duration by convention
(consumer-set), no separate easing token.

`SkinService` gets `DefaultMotionStandard` / `DefaultMotionLarge` consts and zeroes both in **both**
places it already handles `PbMotionFast`/`PbMotionSlow` — the startup `ApplySkin` path (~line 128)
and `ApplyReducedMotion` (~line 251). `SkinServiceTests` gains coverage for the two new keys
mirroring the existing `PbMotionFast`-zeroing assertions.

### 2. Drill-down transition shell (fork B1)

The six drill-down `ContentControl`s in `MainWindow.axaml` collapse into **one**
`TransitioningContentControl` bound to a new `MainViewModel.ActiveDrillDownContent` property
(returns whichever of `Detail`/`MangaDetail`/`Reader`/`BookReader`/`PdfReader`/`BookDetail` matches
`CurrentScreen`, else `null`), `IsVisible="{Binding !IsLateralScreen}"`. This mirrors exactly what
the 2026-08-24 spec did for the lateral group — the drill-down ViewModels are unchanged, only which
one the control's `Content` points at changes. Per-screen state (scroll, loaded data) is unaffected.

**This was chosen over unifying the lateral + drill-down groups into a single control (fork B2).**
B2 is architecturally cleaner (one content pipeline) but reopens the working lateral navigation and
every lateral screen's code-behind for a seam — the Library→Detail hand-off between the two
controls — that the cover-flight overlay already covers visually. The `NavigationTransitionCoordinator`
abstracts "the screen changed" behind one call, so unifying later, if it's ever worth it, is cheap.

**Transition:** a `CompositePageTransition` combining `CrossFade` (`PbMotionStandard`) with a
`PageSlide`-style vertical offset + scale:

- **Push** (going deeper): incoming screen `Opacity` 0→1, `scale` 1.02→1.0, `translateY` +8px→0,
  over `PbMotionStandard` / `CubicEaseOut`.
- **Pop** (going back): outgoing screen `Opacity` 1→0, `scale` 1.0→0.98, over ~70% of
  `PbMotionStandard`.

Push vs pop is a new `MainViewModel.DrillTransitionKind` enum
(`{ Push, Pop }`, plus a `None` for reduced-motion / design-time), set alongside the existing
`IsTransitionReversed` assignment:

- `PushEntry` path (fresh `GoDetailForSeries`, `GoReaderForIssue`, …) → `Push`
- `ReplayEntry` path (`NavigateBack`, `NavigateForward`, `NavigateToBreadcrumbIndex`) → `Pop` when
  the history cursor moves toward the root, `Push` when it moves away from it (Forward re-deepening)

A small `IPageTransition` selector (or a bindable swap of the `TransitioningContentControl.PageTransition`)
picks the push vs pop variant from `DrillTransitionKind`. The kind→variant mapping is pure C# and
unit-tested.

**Lateral ↔ drill-down crossover:** when `IsLateralScreen` flips, the losing
`TransitioningContentControl`'s container just hides (instant, unanimated — `TransitioningContentControl`
animates *content* changes, not its own visibility). This is invisible in practice: the incoming
drill-down screen fades in over its own opaque background, and the cover flight plays on the overlay
above both. The coordinator does not attempt to cross-fade the two containers against each other.

### 3. `SharedElement` attached properties

New static class `Paperbunkr.App.Controls.SharedElement` (or `.Xaml`):

| Property | Type | Set on | Meaning |
|---|---|---|---|
| `Key` | `string?` | the cover `Image` in each participating screen | Identity across screens. Bound, e.g. `"cover:{IssueId}"`. The Library tile and the Detail hero for the same issue resolve to the same string. |
| `ImageSource` | `IImage?` | same element | The already-decoded cover bitmap, handed to the service for cloning. No re-decode, no `RenderTargetBitmap` in v1. |

On `AttachedToVisualTree` an element with a non-null `Key` registers
`(key → WeakReference<Visual>, () => ImageSource)` with the service; on `DetachedFromVisualTree` it
unregisters. A virtualized-away tile is simply not in the tree and therefore not registered — the
service treats "no registration for this key" as "fall back to cross-fade" (see edge cases).

Participants:

| Screen | Element | Notes |
|---|---|---|
| [LibraryScreen.axaml](../../../src/Paperbunkr.App/Views/LibraryScreen.axaml) poster tile | cover `Image` | `Key="cover:{IssueId}"`, `ImageSource` = the tile's bound cover bitmap. Only the tile whose id matches the navigation target ever resolves. |
| [DetailHero.axaml](../../../src/Paperbunkr.App/Views/DetailHero.axaml) | hero art | Same two props. `MangaDetailScreen` shares `DetailHero`, so it is covered with no extra work. |
| [ReaderScreen](../../../src/Paperbunkr.App/Views/ReaderScreen.axaml) | first-page surface | **Cut during implementation** (2026-09-05), per the standing "cut if disproportionate" allowance - not because it looked janky on screen, but because `PageCanvas`/`ReaderScreenViewModel` expose no public first-page `Bitmap` or fit-rect today; getting one out cleanly is real new plumbing on the app's highest-traffic screen, not a XAML-only hookup like the other two participants. Reader still gets the plain push/pop cross-fade from Step 6. Revisit as a small, separate follow-up if it's ever worth the dedicated plumbing. |

Non-participants — drill push/pop fade+scale only, no shared element: `BookDetail`, `BookReader`,
`PdfReader` (books have no Library-grid cover tile to morph from).

### 4. `ISharedElementTransitionService`

```csharp
public interface ISharedElementTransitionService
{
    void RegisterOverlayHost(Panel host);       // MainWindow's TransitionOverlay calls this on load
    void Register(string key, Visual element, Func<IImage?> imageAccessor);
    void Unregister(string key, Visual element);

    void CaptureOutgoing(string key);           // snapshot source rect (TransformToVisual → overlay),
                                                // image, corner radius; call BEFORE the content swap
    Task<bool> FlyToIncomingAsync(string key, TimeSpan duration, Easing easing, CancellationToken ct);
                                                // AFTER the swap. Polls ~250ms for the incoming
                                                // element to register and lay out non-zero.
                                                //   found  → clone Border+Image into the overlay at
                                                //            the source rect, animate RenderTransform
                                                //            (translate+scale) + CornerRadius to the
                                                //            dest rect, await, remove clone → true
                                                //   absent → false (caller already cross-faded)
    void Cancel();                              // nav interrupted mid-flight: yank clone, abort await
}
```

**Overlay host:** a `Panel x:Name="TransitionOverlay"` added to `MainWindow`'s root grid, layered
**above** the lateral/drill screens and the breadcrumb, **below** the modal overlays (Migration,
Reading-List properties, Collection properties, Activity drawer). `IsHitTestVisible="False"`,
`ClipToBounds="False"`. It registers itself with the service on load.

**Clone:** a `Border` (for the interpolated `CornerRadius` + clip) wrapping an `Image`
(`Stretch="UniformToFill"`). Square-ish tile → wide hero crop is handled by `UniformToFill` + the
`Border` clip; the clip rect interpolates alongside the transform. `RenderTransformOrigin` is
top-left so the translate/scale math is a plain affine.

**Rect math is a pure function**, unit-tested with no visual tree:

```csharp
public static class SharedElementFlightMath
{
    public static SharedElementFlight ComputeFlight(
        Rect source, Rect destination, CornerRadius sourceRadius, CornerRadius destRadius);
    // → { TranslateX, TranslateY, ScaleX, ScaleY, start/end CornerRadius, start/end clip }
    // zero-size source or destination → a sentinel "no flight" result the service treats as absent
}
```

### 5. `NavigationTransitionCoordinator`

```csharp
public sealed class NavigationTransitionCoordinator
{
    public NavigationTransitionCoordinator(
        ISharedElementTransitionService service,
        Func<bool> isReducedMotion);

    public async Task RunAsync(DrillTransitionKind kind, string? sharedKey, Action swapContent)
    {
        if (isReducedMotion())
        {
            swapContent();
            return;
        }

        if (sharedKey is not null)
            service.CaptureOutgoing(sharedKey);

        swapContent();   // synchronous: sets CurrentScreen + PushEntry/ReplayEntry on the
                         // history, which triggers the TransitioningContentControl cross-fade

        if (sharedKey is not null)
            await service.FlyToIncomingAsync(sharedKey, largeToken, ease, cancellation);
    }
}
```

`MainViewModel` holds it as `Func<DrillTransitionKind, string?, Action, Task> _runDrillTransition`,
default a no-op that just invokes `swapContent` synchronously (tests, design-time). The real
callback is wired at composition time in `App`/`MainWindow`.

Each drill-down navigation method in `MainViewModel` (`GoDetailForSeries`, `GoReaderForIssue`,
`NavigateBack`, `NavigateForward`, `NavigateToBreadcrumbIndex`, `GoToRootScreen`, …) changes from
directly mutating `CurrentScreen` + history to:

```csharp
_ = _runDrillTransition(kind, sharedKey, () => { /* exactly today's body:
                                                    set CurrentScreen, PushEntry/ReplayEntry */ });
```

Fire-and-forget is fine — the `swapContent` closure runs synchronously before the first `await`, so
`CanNavigateBack` / breadcrumb state update immediately, unchanged from today. `sharedKey` is
`"cover:{issueId}"` for issue-bearing navigations between Library/Detail/Reader, `null` otherwise.

**Back-trip realization:** on a cover-morph *back* into the Library grid, `LibraryScreen`
code-behind must `ScrollIntoView` the target issue before the service polls for it (reuse the
existing A-Z-indexer `ScrollIntoView` path). The coordinator signals this via an existing or new
"about to navigate back to me" hook on `LibraryScreenViewModel`; if the item still can't be
realized (filtered out of the current view, wrong granularity), the poll times out and the
transition is a plain cross-fade.

## Edge cases

| Situation | Behavior |
|---|---|
| Destination element never registers within the poll window (still virtualized, filtered out) | `FlyToIncomingAsync` → `false`; cross-fade already happened; no clone, no crash |
| Navigation interrupted mid-flight (Back mashed) | `Cancel()` removes the clone, `CancellationToken` aborts the await, next transition starts clean |
| `ImageSource` null at capture (cover not decoded yet) | Skip the flight, plain cross-fade |
| Reduced motion on | Coordinator early-returns after `swapContent()`; no capture, no clone, no poll — instant cut, same as the lateral system under reduced motion |
| Same issue open in two places (Library tile + already-open Detail) | Key includes the issue id; only the element on the *outgoing* screen is captured and only the one on the *incoming* screen is flown to — the service tracks role by which screen is transitioning, not by registration order |
| Design-time / unit tests | `_runDrillTransition` default invokes `swapContent` synchronously; no service, no overlay |

## Testing

**Pure / unit:**
- `SharedElementFlightMath.ComputeFlight` — translate/scale/radius/clip for assorted rect pairs,
  aspect-ratio mismatch, zero-size guards → sentinel
- `DrillTransitionKind` resolution — push vs pop from `NavigationHistory` cursor movement
  (deeper / back / forward-redeepen / to-root)
- `NavigationTransitionCoordinator` (mock `ISharedElementTransitionService`):
  - reduced motion → `swapContent` called once, zero service calls
  - destination absent (`FlyToIncomingAsync` → false) → `RunAsync` completes, content still swapped
  - cancel mid-flight → clone-removal path invoked, no exception
- Existing `MainViewModel` navigation + `NavigationHistory` tests — unchanged; the default no-op
  `_runDrillTransition` invokes `swapContent` synchronously so history/back-forward assertions still
  hold

**Manual / on-screen** (standing limitation: no unattended desktop GUI automation in this
environment — same caveat as every prior motion/reader spec):
- The actual cover flight, forward and back, Library ↔ Detail and Detail ↔ Reader — feel, timing,
  aspect handling, no flicker at hand-off
- Each of the six drill-down screens: navigate in and out, confirm no crash and layout settles
- Reduced-motion toggle actually collapses the drill transition and suppresses the flight
- FlaUI: assert each drill-down screen reaches a steady visual-tree state after navigation (no
  stuck clone in the overlay, `TransitionOverlay` empty at rest)

## Out of scope / deferred

- **Sub-project 2** — staggered list entrance, hero parallax, contextual sidebar slide + content
  cross-fade, breadcrumb / discard-banner reveal-slide, toast slide-in, Activity Center drawer
  audit. Independent surface work, its own spec.
- **`RenderTargetBitmap`-snapshot shared elements** for arbitrary non-image content — the service
  seam (`Func<IImage?>` accessor, `Border`+`Image` clone) is shaped so this can be added later, but
  nothing needs it now. v1 is cover images only.
- **Fork B2** (unifying the lateral + drill-down `TransitioningContentControl`s) — reconsider only
  if a concrete need appears; the coordinator makes it a contained change.
- **Reader page-turn animations** — a separate, older system (`PageTransitionStyle`), untouched
  here. Only the *entry* into the Reader as a drill-down screen is in scope.
- **BookReader / PdfReader internal paging** — same: they get the drill push/pop, not internal
  changes.

## Open implementation-time calls (not design decisions)

- Whether `DrillTransitionKind`→transition-variant is a real `IPageTransition` selector class or a
  bound swap of `TransitioningContentControl.PageTransition` — both are fine.
- Exact poll mechanism inside `FlyToIncomingAsync` (`LayoutUpdated` subscription vs a short
  dispatcher timer loop) — whichever is cleaner against Avalonia 12's layout timing.
- Whether the Reader destination rect comes from the fit-math model or `PageCanvas` bounds — decided
  during implementation based on what the Reader layout model exposes without new plumbing.
- How `AsyncCoverImage` (or the tile's current cover control) exposes its decoded `Bitmap` for the
  `ImageSource` accessor — a small addition to that control, shape TBD in code.
