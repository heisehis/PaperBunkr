# Library scroll smoothness — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md*

Same worktree and branch as the search work (`claude/library-search-perf`), on top of it; both sets of changes are
uncommitted there. Paths are under `src/`. Subskills consulted: `avalonia-graphics-animation` (transitions, frame-driven
animation), `avalonia-input-interaction` (wheel events), `avalonia-testing`.

## Run the harness

```
dotnet build src/Paperbunkr.ScrollHarness/Paperbunkr.ScrollHarness.csproj
dotnet run --no-build --project src/Paperbunkr.ScrollHarness -- --stub-drawing --label <name>
dotnet run --no-build --project src/Paperbunkr.ScrollHarness -- --label <name>
```

Two configurations, both needed:
- **`--stub-drawing`**: Skia rasterization stubbed. What remains is layout, template realization, bindings, composition
  serialization, and applying finished decodes: the UI-thread cost that causes hitches. This is the primary number.
- **default (real Skia CPU raster)**: adds software raster of the whole viewport per scroll step. The real app rasterizes on
  GPU/render thread, so treat it as a relative render-cost indicator, not an absolute frame time.
- `--no-shadow`, `--no-entrance` are measurement-only switches (never shipped) used to price the shadow and the entrance
  animation before deciding anything about them. `--issues N --series N` scale the library.

## Baseline (Step 0, Debug build, 3,000 issues / 500 series, Poster grid, Issue granularity)

Realized: 42 cards, **~26 visuals per card**. Frame = simulated 60 Hz (16.7 ms budget). "busy" = UI-thread time per frame.

**UI-thread cost (`--stub-drawing`)**, two runs each, ranges:

| Scenario | busy p50 | busy p95 | frames > 16.7 ms | realize changes | entrance timers armed |
|---|---|---|---|---|---|
| slow drag, 8 px/frame | 0.8–1.2 ms | 7–14 ms | 8–11 of 240 | 12 | 49 |
| medium, 40 px/frame | 0.7–0.8 ms | 41–57 ms | 36–38 of 240 | 68 | 245 |
| fling, 160 px/frame | 29–33 ms | 65–88 ms | 73–74 of 120 | 118 | 497 |
| wheel notches, 96 px every 6th frame | 0.06 ms | 21–27 ms | 15 of 240 | 26 | 98 |

Reading: the cost is **realizing and recycling cards**, about 4 ms per newly realized card in Debug. Frames that cross no row
boundary are cheap. Entrance timers armed ≈ one per realized card (each recycle arms one), but removing them (`--no-entrance`)
changed the numbers only a little (fling p50 33 → 30 ms), so entrance is worth fixing for correctness and battery, but it is not
the main cost.

**Render cost (default, real Skia CPU raster)**: busy p50 72–90 ms for every scrolling scenario. The blurred cover shadow is
about 17 ms of ~68 (`--no-shadow`: 68 → 51 ms). This is software raster of the full viewport; on the GPU path it is far lower, so
the shadow is *not* judged a hotspot from this number alone (spec, Q5: ask the user with before/after only if it matters).

**Pipeline counters**: pop-in reads 0 % here because the un-paced harness frames take long enough for the async decodes to
land inside the frame; it becomes meaningful only once frames are fast, so it is re-measured after the UI-thread cost drops.
Decodes started == applied, none wasted, at every speed (the harness recycles slowly relative to decode speed; wasted decodes
appear when frames get fast enough to outrun the decoder).

Limits: Debug build; headless; GPU not measured. Relative before/after is the tool. The user's on-screen check is the acceptance.

## Steps

### Step 0: Harness and counters — **done**
Files: `Paperbunkr.ScrollHarness/*` (new, not in `Paperbunkr.sln`), `Paperbunkr.App/Services/CoverPipelineStats.cs` (new),
counters in `AsyncCoverImage`, `EntranceAnimation`, `VirtualizingWrapPanel`, `InternalsVisibleTo` for the harness in
`Paperbunkr.App.csproj`. Verify: builds, runs, baseline recorded above.

### Step 1: One-shot entrance animation (D2)
Files: `Controls/EntranceAnimation.cs`, `ViewModels/LibraryScreenViewModel.cs` (pulse), `Paperbunkr.App.Tests/EntranceAnimationTests.cs`.
What: entrance window opened when `Enabled` becomes true; `Prepare` animates only inside it. VM pulses false→true on entrance
triggers (Full, SortGroup, view-mode change). Verify: unit tests (inside/outside window, Reduced Motion, no timers armed for a
recycle after the window), harness `anim timers` column drops to ~the first burst only.

### Step 2: Panel invalidates only when the realized range changes
Files: `Controls/VirtualizingWrapPanel.cs`, `Controls/VirtualizingWrapGridMath.cs`, tests. What: compute wanted range from the
viewport; skip `InvalidateMeasure` when unchanged; allocation-free realize; viewport-relative buffer rows. Verify: pure-math
tests for the range-equality rule; harness `measures` column drops from one per frame to ~one per row crossing.

### Step 3: `GridCoverCache` + `CoverDecodeQueue`, rewire `AsyncCoverImage`
Files: `Services/GridCoverCache.cs`, `Services/CoverDecodeQueue.cs` (new), `Views/AsyncCoverImage.cs`, tests. What: byte-budget
display-size cache (bucketed widths, never disposes on eviction), newest-first bounded queue with dequeue-on-recycle, priorities,
capped applies per tick. Verify: unit tests (budget/LRU/bucket, ordering, bound, dequeue, dedup), harness decode/wasted/MB.

### Step 4: Prefetch
Files: `Controls/VirtualizingWrapPanel.cs` (range event), `Services/CoverPrefetcher.cs`, `Models/ICoverKeyProvider.cs`, cards implement it,
tests. Verify: range math tests; harness pop-in and wasted decodes at medium/fast.

### Step 5: Card trims (each kept only if measured)
Files: `Views/LibraryScreen.axaml`, code-behind. Order by expected value; each change re-measured with the harness and kept
only if it helps: settings-driven visibility via ancestor classes instead of per-card `MultiBinding`s; on-demand dog-ear,
plugin-overlay and selection parts. Shadow only by asking the user with before/after.

### Step 6: Wheel easing and the "Smooth scrolling" toggle
Files: `Controls/SmoothWheelScroll.cs` (+ pure `WheelEasingMath`), Preferences Behavior section, `AppSettings.LibrarySmoothScroll`
(additive column + migration), static push-cache, tests. Verify: pure-function tests; on-screen check by the user.

### Step 7: Re-measure, regression, notes
Harness before/after for every step recorded below; `Paperbunkr.App.Tests` targeted subsets; `CHANGELOG.md` entry.

## Results
Final harness run, same setup as the baseline (Debug, `--stub-drawing`, 3,000 issues / 500 series, two runs each; a first run
after a cold build is noisy, so it is discarded). "Base" is the Step 0 table above.

| Poster grid | Base | After |
|---|---|---|
| medium 40 px/frame, busy p50 / p95 / frames over budget | 0.7-0.8 / 41-57 / 36-38 of 240 | 0.55-0.74 / 34-48 / 32-35 of 240 |
| fling 160 px/frame, busy p50 / p95 / frames over budget | 29-33 / 65-88 / 73-74 of 120 | 16-19 / 65-70 / 59-66 of 120 |
| entrance timers armed (fling) | 497 | 0 |
| measures per fling | one per frame | one per row crossing (118, equal to realize changes) |

| Tiles | Base | After |
|---|---|---|
| fling, busy p50 | 92 | 65-81 |
| medium, busy p50 | not recorded | 4.5-5.2 |

Cover pipeline: pop-in 0 %, wasted decodes 0, grid cache 18-84 MB at the harness's small window (budget 300 MB). With a modelled slow
disk (`SimulatedDecodeDelayMs = 25`) the old path showed ~0-1 % pop-in and burned more UI time; the grid pipeline stayed at 0 %.

Honest reading: the entrance-timer, panel-invalidation, cover-cache and prefetch work removes the avoidable costs, and the fling
median roughly halves for Poster. The remaining floor is realizing a card (about 4 ms per new card in Debug; ~18-24 visuals per card
even after LazyPart), so Tiles fling still misses a 16.7 ms frame in Debug/headless. That is a Debug, software, headless figure; the GPU
release build was not measured. The user's on-screen check is the acceptance for whether it is now smooth.

### Deviations from the plan as written

- **`GridMode` is a literal attached property, not a binding.** A `DecodeWidth` binding landed after `SourceId`, which started a legacy
  decode and then a grid decode for every card. A literal is set before the bindings attach.
- **Decode workers = clamp(cores x 2, 4, 8).** Three workers produced pop-in under the slow-disk model.
- **`LazyPart` decorator** builds a card's optional pieces (dog-ear peek, selection checkbox, plugin overlay, rating badge) only while
  they are active; `IsVisible` is false when inactive so `StackPanel.Spacing` is unaffected. Kept because it was measured
  (optional parts were ~85 % of a card's visuals); the shadow was not changed (about 17 of ~68 ms in software raster only).
- **Wheel easing is `SmoothScrollViewer`, a `ScrollViewer` subclass that hooks its content**, not `SmoothWheelScroll`. Measured: the wheel
  event is bubble-only and `ScrollContentPresenter` handles it in its own class handler, so a tunnel handler, an override, and a handler
  on the presenter or the viewer never ran first. Only the content is reached before the presenter. Stock notch is 50 px per delta
  unit (measured 50/100/150 for 1/2/3), reused as the eased notch distance. Only whole-number vertical deltas with no modifiers are
  eased; touchpad/precise deltas, scrollbar drag, keyboard, zoom, touch and Reduced Motion fall through. The harness cannot show the
  easing curve (its frames run at 120-400 ms, so sampled paths look instant); the math and controller state machine are unit-tested
  (`WheelEasingTests`, 21) and the feel is for the user's on-screen check. Only the Poster/Tiles `ScrollViewer` is eased, not the List and
  Details `ListBox`es.
- **Setting name is `AppSettings.SmoothScrolling`** (not `LibrarySmoothScroll`), exposed in the Library toolbar's view popup ("Smooth
  scrolling" row) rather than Preferences, next to the other view toggles.
- **Migration `AddSmoothScrolling` is hand-written and additive.** HEAD's model snapshot has drifted (tracker columns exist in the
  snapshot but not on `AppSettings` in this branch), so a scaffolded migration would have tried to drop them. The migration test ignores
  `PendingModelChangesWarning` for that reason. When this merges with the branch that carries the tracker columns, regenerate the
  snapshot line and confirm one clean migration chain.

### Regression

- `Paperbunkr.App.Tests` targeted subset (WheelEasing, LazyPart, GridCoverCache, CoverDecodeQueue, CoverPrefetcher, EntranceAnimation,
  VirtualizingWrapGridMath, LibrarySearch/LibraryView pipeline, BulkObservableCollection, SuggestionCandidateList, IssueListSortGroupSpec,
  CoverImageCache, LibraryScreen): **364 passed, 0 failed.**
- `Paperbunkr.Data.Tests` `*Migration*`: 66 failures, **identical at the branch base `ccf910a` in a clean checkout (66 failed / 17
  passed there; 66 failed / 19 passed here, the two extra being the new `AddSmoothScrollingMigrationTests`).** Cause is the snapshot drift
  described above (`PendingModelChangesWarning` on `Migrate()`); not introduced or fixed by this work.
- Not run: the full `App.Tests` suite (it mass-fails under headless already, see the full-suite-headless-flake note).
