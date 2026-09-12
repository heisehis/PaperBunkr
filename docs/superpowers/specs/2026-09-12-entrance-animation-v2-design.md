# Entrance-animation v2 — Books, Smart Lists, Reading Lists

**Date:** 2026-09-12
**Status:** Approved, ready for planning
**Scope:** Beta backlog (chrome/content motion polish v2), not Alpha P0–P7.

## 1. Background

[`2026-09-07-chrome-content-motion-polish-design.md`](2026-09-07-chrome-content-motion-polish-design.md)
shipped the `EntranceAnimation` attached-property system
([`EntranceAnimation.cs`](../../../src/Paperbunkr.App/Controls/EntranceAnimation.cs)) for Library's 5
view modes only, explicitly deferring "any grid other than Library (Books, Smart Lists, Reading
Lists, Home carousels)" to a v2 follow-up. Home's slice already shipped separately
([`2026-09-08-home-navrail-visual-v2-design.md`](2026-09-08-home-navrail-visual-v2-design.md) §6).
This spec closes the remaining three: **Books**, **Smart Lists**, **Reading Lists**.

`EntranceAnimation` itself needs no changes to its public shape — `controls:EntranceAnimation.Enabled`
on any `ItemsControl`, wired to a per-screen `PlayEntranceAnimation` bool, set `true` at the exact
point a screen's real reload/filter/sort/group/switch happens. It reads the bound flag once per
container preparation (`ContainerPrepared`), so ordinary virtualized-scroll recycling never replays
it — the risk this spec has to manage per-screen is **stale funnels that fire on unrelated triggers**
(see Reading Lists below), the same category of pitfall `avalonia-pro-max/motion`'s own list-entrance
guidance calls out for virtualization recycle, just reached a different way here.

## 2. Per-screen findings (verified against current source)

| Screen | Grid shape | Virtualized? | Real trigger funnel(s) |
|---|---|---|---|
| **Books** ([`BooksScreen.axaml`](../../../src/Paperbunkr.App/Views/BooksScreen.axaml) / [`BooksScreenViewModel.cs`](../../../src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs)) | `WrapPanel` (ungrouped + per-group) | No | `Rebuild()` (line 513) — called directly by `OnSearchQueryChanged`/`OnSortFieldChanged`/`OnSortDirectionChanged`, and indirectly by `LoadFromDatabase()` (nav-in + a couple of post-mutation refreshes), which always ends by calling `Rebuild()`. **One funnel, no special-casing needed.** |
| **Smart Lists** ([`SmartScreen.axaml`](../../../src/Paperbunkr.App/Views/SmartScreen.axaml) / [`SmartScreenViewModel.cs`](../../../src/Paperbunkr.App/ViewModels/SmartScreenViewModel.cs)) | 3 parallel `controls:VirtualizingWrapPanel` grids (issue/series/novel results — mutually exclusive visibility) | Yes | `EnsureListLoaded()` (line 457, nav-in) and `RecomputeMatchCount()` (line 587/638, fires on every condition/group edit — the real "filter changed" trigger, analogous to Library's). **Two funnels, both genuine triggers, no mutation-conflation issue.** |
| **Reading Lists** (actual files: [`ReadingScreen.axaml`](../../../src/Paperbunkr.App/Views/ReadingScreen.axaml) / [`ReadingScreenViewModel.cs`](../../../src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs) — there is no separate `ReadingListScreen`) | Flat row list (`ItemsControl` over `Groups` → nested `ItemsControl` over `Rows`), default `StackPanel` | No | `LoadReadingList(int)` (line 574) — called from **24 sites**, only some of which are genuine list-switches. See §3. |

## 3. Reading Lists: distinguishing switch from mutation

`LoadReadingList(int readingListId)` is reused as both "the user switched to a (possibly different)
list" and "the currently-open list's own content changed, reload it to reflect that" — traced every
one of its 24 call sites against the method it's inside:

**Genuine switch/nav-in (should trigger entrance):**
- `EnsureListLoaded()` (lines 654, 662) — the nav-in funnel, called only from `MainViewModel`'s
  `GoReading`/workspace-restore paths (confirmed: its only callers are those three `MainViewModel`
  sites, never a mutation command).
- `MainViewModel.GoReadingWithList` (line 787) — opens Reading on a specific list from Story
  Events' "create reading list from continuity."
- `MainViewModel.OnNewReadingListCreated` (line 1103) — new-list dialog, switches to the result.
- `ReadingScreenViewModel.SelectList` (line ~1163) — sidebar list picker.
- `ReadingScreenViewModel.CreateNew` (line ~1155) — inline "New list," switches to it.
- `ReadingScreenViewModel.DeleteReadingList`'s post-delete fallback (line 779) — deleting the
  *active* list changes which list is now showing, same as a switch.

**Same-list mutation (should NOT replay entrance):** `AddSelectedIssues`, `ImportDroppedPathsAsync`,
`AddAllOfSeries`, `RemoveSelectedMembers`, `SetRoleForSelectedMembers`, `BulkMarkRead`,
`ToggleReadRow`, `LinkStoryEvent`/`UnlinkStoryEvent`, `Reorder`, `RemoveItem`, `AddIssue`, `UseArc`,
`RefreshArcList`, `ImportCbl`, `ImportCsv` — all reload the *same* list to reflect an edit; replaying
a full staggered fade on every "mark one row read" click would be exactly the kind of animate-on-an-
unrelated-trigger regression the shipped `EntranceAnimation` was designed to avoid for virtualization
recycle, just via method-reuse instead.

**Mechanism:** `LoadReadingList(int readingListId, bool triggerEntrance = false)` — a new defaulted
parameter, so all 15 mutation call sites need zero changes (default `false`, current behavior
preserved exactly); only the 6 switch call sites above pass `triggerEntrance: true`.

## 4. Large-N stagger-tail cap

Library/Home are virtualized, so a single container-preparation burst never realizes more than the
visible viewport (~20–40 containers) regardless of how many items the underlying collection has.
Books and Reading Lists are **not** virtualized — every item realizes in one burst, so an uncapped
`index * 24ms` delay gives a 200-book library or a long "everything I've read" list a stagger tail of
several seconds on every reload.

**Fix, in the shared control (benefits every consumer, not per-screen config):** extract the pure
delay math out of `Prepare` (currently inline: `index * PerItemDelayMs`, gated by
`MotionTokens.IsReducedMotion()`) into a small internal static function,
`ComputeDelayMs(int index, bool reducedMotion)`, that clamps `index` to a new
`MaxStaggerIndex = 20` const (alongside the existing `PerItemDelayMs = 24`, giving a 480ms max
stagger regardless of list length) before multiplying. `Prepare` calls it instead of computing
inline. Items beyond index 20 still fade in — just without a further-growing delay, so the tail is
bounded. Harmless for Library/Home: they never approach 20 realized containers in a single burst in
practice, so this clamp is a no-op for them today and simply removes latent risk for the future.
Separating the math from the `DispatcherTimer`/class-toggling side effects is also what makes it
unit-testable at all (§7) — the rest of `Prepare` stays exactly as untestable as it was.

## 5. What ships

1. **`EntranceAnimation.cs`** — add `MaxStaggerIndex` clamp (§4). No public API change.
2. **Books:** `controls:EntranceAnimation.Enabled="{Binding PlayEntranceAnimation}"` on both
   `ItemsControl`s (ungrouped `WrapPanel` + the per-group nested one) in `BooksScreen.axaml`; new
   `PlayEntranceAnimation` bool on `BooksScreenViewModel`, set `true` inside `Rebuild()`.
3. **Smart Lists:** `controls:EntranceAnimation.Enabled="{Binding PlayEntranceAnimation}"` on all
   three `VirtualizingWrapPanel`-backed `ItemsControl`s (issue/series/novel — one shared flag, since
   only one is ever visible, mirroring Library's one-flag/five-templates pattern); new
   `PlayEntranceAnimation` bool on `SmartScreenViewModel`, set `true` inside both
   `EnsureListLoaded()` and `RecomputeMatchCount()`.
4. **Reading Lists:** `controls:EntranceAnimation.Enabled="{Binding PlayEntranceAnimation}"` on the
   outer `ItemsControl` over `Groups` in `ReadingScreen.axaml` (the entrance staggers *rows*, since
   this screen has no card grid — same mechanism, different visual shape); new `PlayEntranceAnimation`
   bool on `ReadingScreenViewModel`; `LoadReadingList` gains the `triggerEntrance` parameter (§3),
   set `PlayEntranceAnimation = true` when it's `true`.

## 6. Explicitly out of scope

- Any change to `EntranceAnimation`'s public attached-property shape — the clamp is internal.
- Home carousels — already shipped, not touched here.
- Per-row entrance inside a Reading List *group* header itself (only the row list staggers, matching
  how Library's own grouped modes only stagger the tiles, not group headers).
- Re-auditing whether `LoadReadingList`'s 15 mutation call sites *should* be collapsed/reduced — out
  of scope for a motion-polish pass; noted as-is.

## 7. Testing

- `EntranceAnimation`-level: no test file exists for it today (verified — its `DispatcherTimer`/
  class-toggling behavior isn't unit-tested, presumably a headless-Avalonia constraint, matching
  this project's own established testing limits elsewhere). New `EntranceAnimationTests.cs` covering
  just the stagger-index clamp math (index 25 and index 20 produce the same delay; index 5 doesn't).
- `BooksScreenViewModelTests`: `PlayEntranceAnimation` flips true on `Rebuild()`/search/sort changes,
  same shape as `LibraryScreenViewModelTests`' existing `*_ResetsPlayEntranceAnimation` cases.
- `SmartScreenViewModelTests`: `PlayEntranceAnimation` flips true on `EnsureListLoaded()` and on a
  condition/group edit that runs `RecomputeMatchCount()`.
- `ReadingScreenViewModelTests`: table/theory covering a representative sample of the 6 switch sites
  (at least `SelectList`, `CreateNew`, `EnsureListLoaded`) asserting `PlayEntranceAnimation == true`,
  and a representative sample of mutation sites (at least `BulkMarkRead`, `Reorder`, `AddIssue`)
  asserting it stays `false`.

## 8. On-screen verification

Standing caveat, same as every other motion-polish item: no computer-use available this session. The
stagger visual itself, the large-N cap's effect on a real long list, and Reduced Motion's effect on
all three screens need a manual click-through pass before being marked verified.
