# Reader: Double-Page Spread (Adaptive)

*Date: 2026-08-15. Next item from the Reader polish Beta backlog after page transition animations
(docs/alpha-todo.md's "Bonus, ahead of schedule" section: "Page layout (double-page spread), split-page
nav, remappable shortcuts, and auto-scroll remain open"). Split-page nav is a distinct backlog item,
not bundled here. Builds directly on the transition-animation infrastructure from
docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md.*

## 1. Scope

CE (`_reference/ComicRackCE`) was checked directly rather than assumed, per the standing CE-parity
rule. Its real feature is `PageLayoutMode { Single, Double, DoubleAdaptive }` (`ComicRack.Engine/
Display/PageLayoutMode.cs`). The actual pairing test (`ComicDisplayControl.GetImageInfo`, confirmed
from source) is **identical** for `Double` and `DoubleAdaptive` — two adjacent pages pair when
two-page mode is active, neither page is `ComicPageType.FrontCover`/`BackCover`
(`ComicPageInfo.IsSinglePageType`), the next page exists, and both images are portrait-or-square
(`width <= height` — neither is already a wide/landscape page scanned as one file). The *only*
difference between the two CE modes is how an un-pairable solo landscape page displays: plain
`Double` artificially pads its width to preserve consistent spread framing (`IsForcedDoublePage`);
`DoubleAdaptive` just shows it at natural size. That distinction is a narrow visual nicety, not a
different reading experience, so this design collapses CE's three modes into two:
`PageLayoutMode { Single, Double }`, where `Double` behaves like CE's `DoubleAdaptive` (no padding).

Checked `Paperbunkr.Engine` (the ported CE engine layer): `ComicPageInfo`/`ComicPageType`/
`ComicPagePosition` already exist there near-verbatim, but grep confirms **zero references anywhere
in `Paperbunkr.App`** — this per-page metadata is completely dormant at the app/reader layer today,
and there is no per-page entity/table in `Paperbunkr.Data.Entities` at all (only `Series`/`Issue`).
Wiring real per-page type/position data would be a much larger, separate lift. Re-read this project's
own prior design note on this exact topic (docs/open_items_resolved.md §4, written before any reader
code existed): "a display preference (per-series, with a per-issue escape hatch)" — that shape matches
`FitMode`'s already-shipped global/series/issue pattern exactly, not CE's per-page `PagePosition`.

**This pass ships:**
- Two layout modes (`Single`/`Double`), global default + per-series + per-issue override, matching
  `FitMode`'s shipped shape exactly.
- Cover detection: `pageIndex == 0` only (matches `ComicPageInfo`'s own constructor default
  `index == 0 ? FrontCover : Story`) — no new per-page metadata pipeline.
- A stateless, local adjacent-pair eligibility test (§3).
- Spread rendering reusing the existing single-image fit/zoom/pan math via a combined virtual size
  (§4).
- Full integration with the just-shipped page-transition-animation system, including spreads (§5).
- A reflow animation when layout mode or reading direction toggles while double-page is active (§6),
  reusing the same transition pipeline.

**Explicitly deferred, named rather than silently dropped** (CE research surfaced these mid-design,
both corrected after initially being mischaracterized to the user):
- **Per-page manual `Near`/`Far` override** — CE's escape hatch for pairing drift after an odd run of
  landscape pages. Real CE UI for this is `PagesView`, a dedicated page-list/grid screen with a
  right-click "set position" context menu (confirmed from source) — a new screen comparable in scope
  to the existing Bulk Issue Editing screen, plus new per-page schema. Gets its own follow-up spec.
- **CE's `DoublePageOverlap` hover effect** — turns out this is *not* mouse-hover-driven at all (an
  initial mischaracterization, corrected before design work started): it's an easing animation that
  plays on a `PageLayout`/`RightToLeftReading` change, which *is* in scope here (§6), just under its
  real behavior rather than the incorrectly-assumed one.
- Applies only under `LeftToRight`/`RightToLeft` reading modes. Continuous/webtoon modes ignore it
  entirely — already documented as intentional in `ReadingMode.cs`: "Double-page spread is
  deliberately NOT a value here... a display toggle orthogonal to reading mode."
- Manual rotation has no effect on a paired spread (§4) — auto-rotate already only ever targets
  landscape solo pages, which never pair, so this mostly falls out naturally rather than needing new
  per-half rotation-pivot math.

## 2. Data model

New enum `src/Paperbunkr.Data/Entities/PageLayoutMode.cs`: `{ Single, Double }`, doc comment pointing
at this spec — same shape as `ImageFitMode.cs`.

Checked the two existing override precedents directly rather than assuming they compose cleanly:
`ReadingMode` is `Series.ReadingMode` (non-nullable, own hardcoded C# default) + `Issue.
ReadingModeOverride` (nullable) — a two-tier Series+Issue chain with **no** `AppSettings` layer at
all. `FitMode` is `AppSettings.DefaultPageFitMode` (global) + `Issue.PageFitModeOverride` (nullable)
— a different two-tier AppSettings+Issue chain with **no** Series layer. Neither existing feature is
actually a three-tier precedent to copy verbatim; the three-tier shape decided on for this feature
(§1) is a new combination, so it needs its own explicit resolution rule rather than inheriting one by
analogy:

- `AppSettings.DefaultPageLayoutMode` (non-nullable, default `Single`) — new migration
  (`AddPageLayoutModeSettings`), enum-as-string column with `HasConversion<string>()`/
  `HasMaxLength(32)`/`HasDefaultValue`/`HasSentinel` (same treatment every enum-as-string `AppSettings`
  column gets, per `PaperbunkrDbContext.OnModelCreating`) - the bottom of the chain, always has a
  concrete value.
- `Series.PageLayoutMode` (**nullable**, default `null` = "use the global default") - unlike
  `Series.ReadingMode`, this has to be nullable for the chain to have three real, live-resolved tiers
  rather than the `AppSettings` layer only ever seeding a new row once.
- `Issue.PageLayoutModeOverride` (nullable) — same shape as `Issue.PageFitModeOverride`/
  `ReadingModeOverride`.

Effective mode resolves `Issue.PageLayoutModeOverride ?? Series.PageLayoutMode ?? AppSettings.
DefaultPageLayoutMode`, all three read live in `ReaderScreenViewModel.Load`/`RefreshDisplaySettings`
(same live-while-open treatment `PageTransitionStyle` already got). Consequence, called out
explicitly since it's a real behavior choice: changing the `AppSettings` default retroactively
affects every series/issue that hasn't set its own `Series.PageLayoutMode`/`Issue.
PageLayoutModeOverride` - not just newly-created series - consistent with this project's existing
bias (surfaced during the transition-animations work) toward settings taking effect live rather than
needing something reopened. `Series.PageLayoutMode` is editable wherever the plan ends up placing it
(a Series-level properties/bulk-edit surface, or a Reader-toolbar "remember for this series" action
alongside the per-issue override, matching the two entry points `FitMode`'s per-issue override and
`ReadingMode`'s per-series setting each already have their own way in) - left to the implementation
plan rather than over-specified here.

## 3. Pairing algorithm

For adjacent pages N and N+1, eligible to pair when: effective `PageLayoutMode == Double`, reading
mode is `LeftToRight`/`RightToLeft`, N != 0 (page 0 is always solo), N+1 < `PageCount`, and both
decoded bitmaps are portrait-or-square (`PixelSize.Width <= PixelSize.Height`) — byte-for-byte CE's
own test, confirmed from source, minus the type-based cover check (§1's `pageIndex == 0`
simplification replaces `IsSinglePageType`).

**Stateless and local, by design**: each decision only ever looks at its own immediate pair — no
whole-book prescan from the cover to track "parity." Named limitation: after an odd run of landscape
pages, this can occasionally land on a pairing "shifted" from what a full forward-scan would produce.
CE's own *automatic* behavior has the identical limitation — that's precisely why CE's manual
`PagePosition` correction exists (§1, deferred to its own spec). This isn't a shortcut that diverges
from CE's real automatic behavior, just an honest scope boundary consistent with deferring that
escape hatch.

`ReaderScreenViewModel` owns the decision, in the same place `GoToPage`/navigation already lives. On
each page-index change it decodes page N via the existing `_decoder.GetPage` (unchanged); if the
effective mode is `Double` and N+1 exists, it also decodes N+1 to check eligibility via a new pure
pairing-test function (file placement left to the implementation plan - same small-static-helper
shape as `PageTransitionMath`, test coverage in §7), exposing a new `CurrentPageSecondary`
(`Bitmap?`, null when not paired)
alongside the existing `CurrentPage`. Decoding N+1 even when it turns out not to pair isn't wasted —
`PageDecodeService`'s cache means it's already primed for whenever the reader actually turns there.

**Navigation stepping**: `PreviousPage`/`NextPage` (and by extension `GoLeft`/`GoRight`) step by 2 when
`CurrentPageSecondary is not null` (i.e., the page just left was paired), by 1 otherwise — mirrors
CE's `IsDoubleImage`-gated step exactly. Stepping backward applies the same local pairing test to the
pair immediately behind the current position to decide a 1- or 2-page step back, mirroring CE's
`DisplayPreviousPage` structure.

## 4. Rendering

Rather than inventing new fit/zoom/pan math, a spread reduces to the *existing* single-image math: a
**combined virtual pixel size** for the pair, computed via CE's own formula (confirmed from source,
`ComicDisplayControl.GetImageInfo`) — normalize both pages to a common height
(`max(height1, height2)`), sum their proportionally-scaled widths. That combined size feeds through
`ZoomPanMath.ComputeBaseScale`/`ClampPan` completely unchanged, exactly as if it were one wide image.
The resulting single `destRect` is then split into two side-by-side sub-rects, proportional to each
page's own scaled width within it — a new small pure function, alongside `ZoomPanMath`'s existing
methods.

This reuses essentially all of the existing fit-mode/zoom/pan/click-zone/hit-testing logic unchanged —
a spread is "a wider virtual page" everywhere except the final draw call, which now loops over 1 or 2
bitmaps instead of always 1. Architecturally reuses the `PageDrawPlan`/`ComputeDrawPlan`/`DrawBitmap`
split already built for transitions (docs/superpowers/specs/2026-08-13-reader-page-transition-
animations-design.md §3.2) — a spread's two sub-rects are just two more `PageDrawPlan`s drawn through
the same helper, no new draw primitive needed.

Reading direction decides left/right placement: in `LeftToRight`, the lower-index (primary) page sits
on the visual left; in `RightToLeft`, it sits on the right — the same spatial convention `PageCanvas`
already uses for its Left/Right commands (docs/superpowers/specs/2026-08-07-reader-rtl-navigation-
design.md §3).

**Named simplification**: manual rotation has no effect on a paired spread — the rotation control
still exists for solo pages, but while `CurrentPageSecondary is not null` it's inert rather than
building new per-half rotation-pivot math for two independently-rotatable panels.

## 5. Transition integration

`ReaderPageTransitionData` (docs/superpowers/specs/2026-08-13-reader-page-transition-animations-
design.md §3.2) gains `OldSecondaryBitmap`/`NewSecondaryBitmap` (both nullable — null on a side means
that side was/is solo). Both existing styles extend naturally, since a spread is already "just a
wider virtual page" per §4:

- **Slide**: the combined spread's split sub-rects (§4) both receive the *same* slide offset from
  `PageTransitionMath.SlideOffset` — the pair moves as one visual unit, not two independently-sliding
  halves.
- **Crossfade**: both bitmaps on a given side receive the same alpha from
  `PageTransitionMath.CrossfadeAlpha`.

A turn from solo into a spread (or the reverse) animates too — each side's draw plan(s) are computed
independently against that side's own bitmap count (1 or 2), exactly like today's independent
old/new computation, just no longer assuming exactly one bitmap per side. The per-bitmap `SKImage`
caching fix from the prior spec (docs/superpowers/specs/2026-08-13-reader-page-transition-animations-
design.md, the crossfade-choppiness fix) extends to up to 4 cached images (old primary/secondary, new
primary/secondary) instead of 2, same one-conversion-per-transition principle.

## 6. Reflow animation

Toggling `PageLayoutMode` (Single↔Double) or flipping RTL/LTR while double-page is active reuses the
exact same transition pipeline as a page-turn — `ReaderScreenViewModel` synthesizes a
`ReaderPageTransitionData` (old arrangement → new arrangement) directly, the same way `PageCanvas`
builds one from a `LeftCommand`/`RightCommand` turn, rather than a new animation mechanism. Always
**Crossfade**, regardless of the user's own page-turn `PageTransitionStyle` setting — CE's real
`DoublePageOverlap` behavior (§1, corrected from an initial mischaracterization) is fundamentally a
cross-dissolve/reflow, not a directional slide, and "which edge does a layout change slide from"
has no natural answer the way a page-turn direction does. Uses the same `PageTransitionDurationMs`.
If the user's transition style is `None`, the reflow still snaps instantly, consistent with that
setting turning the whole animation system off.

## 7. Testing

- New pure-function tests (mirroring `ZoomPanMathTests`/`PageTransitionMathTests`): pairing
  eligibility across portrait/portrait, portrait/landscape, index-0-cover, and page-count-boundary
  cases; combined virtual spread size computation; sub-rect splitting math; navigation step-size (1
  vs 2) in both directions.
- `ReaderScreenViewModelTests`: `CurrentPageSecondary` populated/null across landscape pages, the
  cover, and odd page counts; `PreviousPage`/`NextPage` stepping by 1 or 2 correctly;
  global/series/issue override read precedence (same shape as the existing `FitMode` override tests).
- Reflow-trigger tests at the message-construction level (old/new transition data built correctly on
  a layout-mode or RTL toggle) — not the actual animation, same "test the pure data, not the
  compositor" split already established for page transitions.
- **Manual-only, same standing caveat as every prior reader spec**: real-library visual pairing
  correctness, reflow feel, spread+transition combined visuals, and RTL spread ordering all need
  eyes-on verification — no unattended desktop GUI automation available for this project.

## 8. Explicitly not in this pass

Per-page manual `Near`/`Far` override and its page-list editor screen (§1 — own follow-up spec).
Split-page nav (separate backlog item). Any deeper `ComicPageType`/`ComicPagePosition` wiring beyond
`pageIndex == 0` cover detection. Per-half rotation for a paired spread. CE's plain `Double` mode's
solo-landscape-page width padding (collapsed into the single `Double` mode behaving like
`DoubleAdaptive`, per §1).
