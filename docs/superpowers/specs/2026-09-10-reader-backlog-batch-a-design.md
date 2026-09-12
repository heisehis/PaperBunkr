# Reader Backlog — Batch A — design

*Date: 2026-09-10. Status: draft for review.*

## Scope

Three items from the reader backlog, grouped because each is small, low-branching, and mostly
settled by existing specs:

1. **Reader memory-limit control** — wire a Preferences → Reader UI control for the
   already-shipped `AppSettings.ReaderMemoryLimitMb` setting (pipeline design §5.2 left the column
   + migration shipped but the control deferred).
2. **Async-swap-on-cold-miss + decision record** — an honest write-up of whether the deferred
   "full async-paged swap-on-`PageReady`" and "1-reader/N-decoder thread split" (alpha-todo
   2026-09-09 note) are needed, plus the one narrow fix that write-up concludes is warranted.
3. **Type-to-jump-to-page** — let the reader jump to an arbitrary page by typing its number.
   Comic reader only.

Not in this batch: PDF-reader type-to-jump; the full viewport-aware async-paged `PageCanvas`
restructure; N parallel decode workers; anything in Batches B–D of the reader backlog.

## CE-parity note

Verified against `_reference/ComicRackCE` (subagent sweep, 2026-09-10):

- **CE has no numeric go-to-page of any kind** — no dialog, no field, no status-bar input, no
  digit-key seek. CE reader page navigation is first/prev/next/last, prev/next bookmark, the
  `pageSlider` scrubber, and thumbnail-strip clicks. CE binds digit keys `D1`–`D0` to zoom/layout
  modes. So Item 3 is a **deliberate deviation**, in the spirit of "more than CE," not a parity
  gap. `docs/ce-feature-inventory.md:138` currently frames "type-to-jump-to-page genuinely not
  built" as if CE has it — that line is corrected as part of this work.
- **CE's memory model is two caches** — an in-memory page cache (`Settings.MemoryPageCacheCount`,
  20–100 pages, default 25) and an on-disk page cache (`Settings.PageCacheSizeMB`, default 500
  MB). Paperbunkr's single adaptive **byte budget** (`ReaderMemoryBudget`, pipeline design §5) is
  a deliberate deviation already established in that spec. CE does not constrain the control
  shape, and this work adds no on-disk page cache.

---

## Item 1 — Reader memory-limit control

### Current state

- `AppSettings.ReaderMemoryLimitMb` — nullable `int`, `null` = Auto. Column + migration
  `20260909221449_AddReaderMemoryLimitMb` (no-op `Down()`) already shipped in PR #68.
- `ReaderMemoryBudget.Resolve(int? userLimitMb)` already consumes it: `userLimitMb is int mb &&
  mb > 0 ? mb * Mib : clamp(25% RAM, 128 MiB, 512 MiB)`.
- `ReaderScreenViewModel.Load` already passes `appSettings.ReaderMemoryLimitMb` to
  `ReaderImagePipeline.TryOpen`.
- The budget is an **immutable per-session snapshot** — a changed limit takes effect on the next
  reader open, not live (pipeline design §16).

The only missing piece is the Preferences control.

### ViewModel — `PreferencesScreenViewModel`

Mirror the existing segmented-tab idiom (`IsComicFoldersMaintenanceTabActive` + a toggle command):

```csharp
// backing store — the persisted value, null = Auto
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(IsReaderMemoryAuto))]
[NotifyPropertyChangedFor(nameof(IsReaderMemory512))]
[NotifyPropertyChangedFor(nameof(IsReaderMemory1024))]
private int? _readerMemoryLimitMb;

// Auto == "no explicit override" == null. A non-standard stored value (e.g. 768 set by
// hand or a future build) lights up NO segment — an honest "custom" state — rather than
// being mislabelled Auto.
public bool IsReaderMemoryAuto  => ReaderMemoryLimitMb is null;
public bool IsReaderMemory512   => ReaderMemoryLimitMb == 512;
public bool IsReaderMemory1024  => ReaderMemoryLimitMb == 1024;

[RelayCommand]
private void SetReaderMemoryLimit(string choice)   // "auto" | "512" | "1024"
{
    int? mb = choice switch { "512" => 512, "1024" => 1024, _ => null };
    if (ReaderMemoryLimitMb == mb) return;
    ReaderMemoryLimitMb = mb;                              // generated setter raises the 3 NotifyPropertyChangedFor
    PersistBehaviorSetting(s => s.ReaderMemoryLimitMb = mb);
}
```

- The `string` command parameter (not `int?`) keeps the XAML binding trivial —
  `CommandParameter="512"`. `[NotifyPropertyChangedFor]` on the field means the three `Is…`
  bools re-raise automatically whenever the property changes — no manual `OnPropertyChanged`
  in the command, and the load path gets the notifications for free too.
- Load path (the settings-hydration method, ~line 668 alongside `ResetZoomOnPageChange` /
  `DefaultPageFitMode`): `ReaderMemoryLimitMb = settings.ReaderMemoryLimitMb;`.
- `PersistBehaviorSetting` is the class's existing single-field write helper. No new event —
  `ReaderDisplaySettingsChanged` is deliberately **not** raised (nothing live-applies this).

### View — `Views/Preferences/ReaderSection.axaml`

New group at the **bottom** of the section, after the background/margins group:

```xml
<Border Classes="groupBox">
  <StackPanel>
    <Border Classes="groupHeader">
      <TextBlock Text="Performance" />
    </Border>
    <Grid ColumnDefinitions="Auto,*" Margin="0,8">
      <TextBlock Grid.Column="0" Text="Reader memory limit" VerticalAlignment="Center" />
      <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="0" HorizontalAlignment="Right">
        <Button Classes="segTab" Content="Auto"    Classes.active="{Binding IsReaderMemoryAuto}"
                Command="{Binding SetReaderMemoryLimitCommand}" CommandParameter="auto" />
        <Button Classes="segTab" Content="512 MB"  Classes.active="{Binding IsReaderMemory512}"
                Command="{Binding SetReaderMemoryLimitCommand}" CommandParameter="512" />
        <Button Classes="segTab" Content="1024 MB" Classes.active="{Binding IsReaderMemory1024}"
                Command="{Binding SetReaderMemoryLimitCommand}" CommandParameter="1024" />
      </StackPanel>
    </Grid>
    <TextBlock Classes="pbTextFaint" TextWrapping="Wrap"
               Text="Auto uses up to a quarter of system memory (128–512 MB). Takes effect the next time you open a book." />
  </StackPanel>
</Border>
```

- `segTab` currently lives as a **local style pair inside `LibrarySection.axaml`** (lines 19–26)
  — a row of independently-rounded pill buttons (`Padding="12,5"`, `CornerRadius="6"`,
  `Background="Transparent"`; `.active` → `PbSurface3Brush` fill + `PbTextBrush` + SemiBold), not
  a joined 3-segment control. Its own comment calls it a stopgap "rather than a new shared
  control." This batch is its second consumer, so **promote the pair to a shared styles file**
  (`Styles/Primitives.axaml`) and drop the local copy from `LibrarySection.axaml`. Behaviour is
  identical — same selectors, same setters, same `{DynamicResource}` keys (`PbTextFaintBrush` /
  `PbSurface3Brush` / `PbTextBrush`) carried over verbatim so theme switching still works.
  Nothing about it needs 2-vs-3-segment handling; each button rounds itself.
- All brushes via `{DynamicResource}` / existing `Classes` — no hex literals (avalonia-pro-max
  design-system rule).

### Migration / tests

- **No migration** — column exists.
- `PreferencesScreenViewModelTests`: each command choice persists the right `int?`
  (`null` / `512` / `1024`) and the three `Is…` bools track it; a fresh VM hydrates the selection
  from a stored value; a stored **non-standard** value (e.g. `768`) → all three bools false
  (no segment shown), and re-selecting `Auto` writes `null`.
- No `ReaderMemoryBudget` change — `Resolve` already handles every value this control can produce.

---

## Item 2 — Async-swap-on-cold-miss + decision record

### The investigation (written into pipeline design doc as new §17)

**What shipped (PR #68 + rev-3):** one background decode consumer loop draining a high- then
low-priority channel; an adaptive prefetch fringe; `TryGetCachedPage` peek + `BackgroundDecodeCompleted`
event on `IReaderPageSource`. Continuous and PDF-continuous render fully async off `Decoder`.

**What still blocks the UI thread:** `ReaderScreenViewModel.RefreshCurrentPage`'s **paged**
branch calls `_decoder.GetPage(_currentPageIndex)` **synchronously**. On a warm cache (the common
forward page-turn, thanks to prefetch) this returns instantly. On a **cold miss** it decodes on
the UI thread — a multi-frame stall for a large scan. Cold misses happen on:

1. First page shown after opening a book (already covered by a loading state elsewhere — the
   reader screen isn't interactive yet).
2. A backward turn past the small backward fringe (`-1`, `-2`).
3. **Any large jump** — thumbnail-rail click today, and **type-to-jump (Item 3) makes this a
   first-class, frequent action.** A jump to page 300 of 400 is a guaranteed cold miss.

**Two deferred designs from the 2026-09-09 note:**

| Deferred item | Decision | Rationale |
|---|---|---|
| **Full async-paged swap-on-`PageReady`** (viewport-aware display tier + zoom-triggered detail re-decode, the full `PageCanvas` restructure) | **Stays deferred.** | The shipped detail tier (`GetDetailPage`, rev-3 budget-accounted) already covers zoom sharpness. The restructure's remaining benefit is viewport-width-aware display decode; the interim 2560px cap is good enough on any real monitor to ~1.5× zoom. Large scope, low marginal value. |
| **1-reader / N-decoder thread split** (§8.3: `N = clamp(ProcessorCount−1, 1, 4)` Skia workers) | **Formally accepted as not-needed.** | The single loop already decodes off the UI thread. Archive reads are serialized regardless (`_readerLock` + the single 7z COM executor thread, rev-3 #1), so parallel Skia decode only helps a multi-page *cold* burst, and the `RawBytesCache` makes re-reads cheap. Revisit **only** if a benchmark shows decode (not read) as the bottleneck. |
| **Explicit queue cancellation on `IReaderPageSource`** (`FlushQueue()` / a `CancellationToken` on `SetVirtualizationWindow`) | **Not needed; not added.** | Stale queue entries are already dropped at dequeue in O(1) (`ProcessQueuedPage` window-membership check, `ReaderImagePipeline.cs:739`/`:762`); `_window`/`_enqueued` are re-bounded every call (`:600–614`); the fringe pass is debounced 30 ms. A 50-jump burst does ~3 real decodes, not 150. Widening the interface is out of Batch A scope and would revisit a shipped design. Revisit only if on-screen testing shows a real burst stall. |

**What this batch does build:** the **narrow async swap on cold miss** — the smallest change that
removes the UI-thread stall on jumps and backward-beyond-fringe, using the seam exactly as
`IReaderPageSource` documents it (*"caller draws a gap and waits for `BackgroundDecodeCompleted`"*).

### The narrow fix — `ReaderScreenViewModel` only

State:

```csharp
private int _awaitingPageIndex = -1;           // primary page a cold-miss swap waits on; -1 = none
private int _awaitingSecondaryPageIndex = -1;   // spread's paired page, when it too missed; -1 = none
[ObservableProperty] private bool _isPageLoading;
private IDisposable? _coldMissTimeout;  // ~5 s "still loading" fallback (primary only); disarmed on swap / new request / teardown
```

> **Why a secondary await is needed, not "keep the pair synchronous".** `TryDecodePairedPage`
> (`ReaderScreenViewModel.cs:1914`) calls `_decoder.GetPage(pageIndex + 1)` **synchronously** — on
> a cold miss that blocks the UI thread, the exact stall Item 2 removes for the primary. A jump
> into a spread (`SetVirtualizationWindow` enqueues `primary-1 … primary+1` and the single loop
> clears them in order) routinely has the primary ready a beat before its pair. So the pipeline
> branch must peek the pair too, and wait for its completion — otherwise the fix just moves the
> freeze from the primary decode to the pair decode.

**Subscription lifecycle.** `Load()` already does `_decoder?.Dispose(); _decoder = null;` near its
top (`ReaderScreenViewModel.cs:856`) to drop the previous issue's decoder, and `GoBack()`
(`:2553`) is the "leaving the reader" exit. Both are the churn points for `_decoder`.

- **Before** `_decoder?.Dispose()` in `Load` **and** in `GoBack`: if the outgoing
  `_decoder is IReaderPageSource oldPipe`, `oldPipe.BackgroundDecodeCompleted -= OnBackgroundPageDecoded`.
  Also `_coldMissTimeout?.Dispose()`, `_awaitingPageIndex = -1`, `_awaitingSecondaryPageIndex = -1`,
  `IsPageLoading = false`.
- **After** the new `_decoder` is created and cast (paged mode), `newPipe.BackgroundDecodeCompleted
  += OnBackgroundPageDecoded`.

Factor the detach into a small `DetachDecoderEvents()` helper so `Load` and `GoBack` can't
drift. The VM instance itself is never destroyed (rail-nav toggles `IsVisible`), so there is no
`Dispose()` on the VM to hook — the decoder-churn points are the complete set.

`RefreshCurrentPage` paged branch (replacing the current unconditional `GetPage`):

```csharp
if (_decoder is IReaderPageSource pipeline)
{
    pipeline.SetViewportWidth(2560);
    pipeline.SetVirtualizationWindow(_currentPageIndex - 1, _currentPageIndex + 1); // enqueues target high-priority

    // always cancel a prior wait first — a new page request supersedes it
    _coldMissTimeout?.Dispose();
    _coldMissTimeout = null;
    _awaitingSecondaryPageIndex = -1;

    var cached = pipeline.TryGetCachedPage(_currentPageIndex);
    if (cached is not null)
    {
        _awaitingPageIndex = -1;
        IsPageLoading = false;
        CurrentPage = cached;
        ResolveSecondaryPage(pipeline, _currentPageIndex, cached.PixelSize);
    }
    else
    {
        // keep the outgoing page on screen; don't blank CurrentPage
        _awaitingPageIndex = _currentPageIndex;
        IsPageLoading = true;
        CurrentPageSecondary = null;   // pair resolves after the primary lands (needs its PixelSize)
        int waitingFor = _currentPageIndex;
        _coldMissTimeout = DispatcherTimer.RunOnce(() =>
        {
            if (_awaitingPageIndex != waitingFor) return;   // already resolved
            _awaitingPageIndex = -1;
            IsPageLoading = false;
            ErrorMessage = $"Couldn't decode page {waitingFor + 1}.";
        }, TimeSpan.FromSeconds(5));
    }
}
else
{
    CurrentPage = _decoder.GetPage(_currentPageIndex);              // non-pipeline fallback (unchanged)
    CurrentPageSecondary = TryDecodePairedPage(_currentPageIndex, CurrentPage.PixelSize);
}
```

Secondary-page peek (pipeline branch only — the non-pipeline fallback keeps `TryDecodePairedPage`):

```csharp
private void ResolveSecondaryPage(IReaderPageSource pipeline, int primaryIdx, PixelSize primarySize)
{
    if (!DoublePagePairingActive || primaryIdx == 0 || primaryIdx + 1 >= _decoder!.PageCount)
    {
        _awaitingSecondaryPageIndex = -1;
        CurrentPageSecondary = null;
        return;
    }
    var sec = pipeline.TryGetCachedPage(primaryIdx + 1);
    if (sec is not null)
    {
        _awaitingSecondaryPageIndex = -1;
        CurrentPageSecondary = SpreadLayoutMath.IsPairEligible(primarySize, sec.PixelSize) ? sec : null;
    }
    else
    {
        _awaitingSecondaryPageIndex = primaryIdx + 1;   // wait for it; spread shows solo meanwhile
        CurrentPageSecondary = null;
    }
}
```

Handler — one event, both awaits. `BackgroundDecodeCompleted` fires on a decode thread; the
check here is a cheap early-out and the **authoritative** re-check is inside `ApplyDecoded` on the
UI thread (the awaits are only ever written on the UI thread, so a stale read here just means one
extra no-op `Post`):

```csharp
private void OnBackgroundPageDecoded(int idx)
{
    if (idx != _awaitingPageIndex && idx != _awaitingSecondaryPageIndex) return;   // stale — see "stale guard"
    if (Dispatcher.UIThread.CheckAccess()) ApplyDecoded(idx);
    else Dispatcher.UIThread.Post(() => ApplyDecoded(idx));
}

private void ApplyDecoded(int idx)
{
    if (_decoder is not IReaderPageSource p) return;

    if (idx == _awaitingPageIndex)
    {
        var bmp = p.TryGetCachedPage(idx);
        if (bmp is null) return;              // evicted again before we got here; a later turn re-requests
        _awaitingPageIndex = -1;
        _coldMissTimeout?.Dispose();
        _coldMissTimeout = null;
        IsPageLoading = false;
        CurrentPage = bmp;
        ResolveSecondaryPage(p, idx, bmp.PixelSize);   // may set _awaitingSecondaryPageIndex
    }
    else if (idx == _awaitingSecondaryPageIndex)
    {
        var sec = p.TryGetCachedPage(idx);
        if (sec is null) return;
        _awaitingSecondaryPageIndex = -1;
        CurrentPageSecondary = CurrentPage is not null
            && SpreadLayoutMath.IsPairEligible(CurrentPage.PixelSize, sec.PixelSize) ? sec : null;
    }
}
```

- **Stale guard — why index awaits alone are sufficient (no sequence counter).** For a given
  issue and the fixed paged viewport width (2560), page index → decoded bitmap is deterministic:
  `TryGetCachedPage(N)` can only ever return *page N*, never stale content. So the identity that
  matters is "is N still wanted," which `_awaitingPageIndex` / `_awaitingSecondaryPageIndex`
  capture exactly. Rapid A→B→A: the B request is abandoned when the awaits move back to A; a late
  `BackgroundDecodeCompleted(A)` from the *first* A visit still delivers the correct page A. A
  monotonic request-id would only make us *wait longer* for a byte-identical re-decode. The
  `_coldMissTimeout`'s own `waitingFor` capture covers the timer.
- **Error path:** the current `catch` around `GetPage` stays only on the non-pipeline fallback
  branch. On the pipeline branch a primary cold miss no longer throws — a genuine decode failure
  surfaces as the `_coldMissTimeout` firing (page never arrived) → `ErrorMessage`. The timeout is
  disarmed on: a successful primary swap (`ApplyDecoded`), any new page request (top of the
  pipeline branch above), and decoder teardown (`DetachDecoderEvents`).
- **Secondary never blocks and never errors.** A stuck paired-page decode just leaves the spread
  showing solo (`_awaitingSecondaryPageIndex` stays set); it self-heals on that page's completion
  or on the next turn. No timeout, no error — it's cosmetic, and forcing an error for "the second
  half of a spread is 200 ms late" would be worse than the brief solo frame. The non-pipeline
  fallback keeps `TryDecodePairedPage` (synchronous) since that decoder has no async path anyway.
- **`ResetZoomOnPageChange`, `PageRotationOverrideDegrees`, `LastPageRead`, `TrackSessionProgress`,
  thumbnail selection, `PageLabel`** — all already updated in `GoToPage` *before* `RefreshCurrentPage`,
  so they're correct immediately regardless of when the bitmap lands. No change.
- **Continuous mode:** `RefreshCurrentPage` early-returns for continuous (unchanged); continuous
  never used `CurrentPage`.
- **Rapid-jump queue pressure — no new mechanism needed, and none added to `IReaderPageSource`.**
  This change calls `SetVirtualizationWindow` on a page change exactly as the code does today; it
  removes the synchronous `GetPage`, so a jump does *less* UI-thread work than before, not more.
  The shipped pipeline already bounds the cost of a burst (verified against
  `ReaderImagePipeline.cs`, 2026-09-10):
  - `SetVirtualizationWindow` is O(window size), not O(queue size): it clamps indices, rebuilds
    `_window` / prunes `_enqueued` to `[min−BackFringe(2) … max+MaxForwardFringe(6)]` on **every**
    call (`:600–614`), and enqueues at most the 3 on-screen indices high-priority.
  - The single consumer loop drops stale entries at dequeue in O(1) — `ProcessQueuedPage` returns
    before any decode when `!_window.Contains(pageIndex)` (`:739`) and again post-decode (`:762`).
  - `TryEnqueue` dedups against `_enqueued` + the cache (`:693`).
  - The heavier fringe recompute is debounced 30 ms (`_fringeTimer`).
  So 50 thumbnail-rail jumps in 3 s ≈ 150 enqueues, of which ~147 are dequeued-and-skipped
  (a lock + two set ops each, sub-µs) and only the final resting window's ~3 actually decode.
  There is no unbounded growth and no UI-thread stall. If on-screen testing ever shows one, it is
  a pipeline-level follow-up (a real `CancellationToken` / queue-drain on `IReaderPageSource`),
  explicitly **out of scope for Batch A** — this batch does not widen that interface. The §17
  decision record states this.

### The loading affordance — `Views/ReaderScreen.axaml`

A small spinner beside `PageLabel` in the top-left Navigate cluster, `IsVisible="{Binding
IsPageLoading}"`. Reuse `BusyIndicator` if it sizes down cleanly into the cluster; otherwise a
12px indeterminate `fi:SymbolIcon` with a rotate animation, or a static `…` glyph. Motion:
`Opacity`/`RenderTransform` only, respects reduced-motion (avalonia-pro-max motion rule).

### Tests

- **Cold miss → swap:** fake `IReaderPageSource` whose `TryGetCachedPage` returns null, then a
  bitmap after `BackgroundDecodeCompleted` is raised — `IsPageLoading` true→false, `CurrentPage`
  swaps, and the *outgoing* bitmap is still `CurrentPage` in between (not null).
- **Warm path unchanged:** `TryGetCachedPage` returns a bitmap immediately → `IsPageLoading` never
  set true, synchronous assignment, no timer armed.
- **Stale guard:** jump A→B→A; the first-A-visit's late `BackgroundDecodeCompleted(A)` after the
  user is back on A still applies (correct page); a late `BackgroundDecodeCompleted(B)` after the
  user left B is ignored (`CurrentPage` unchanged).
- **Timeout:** cold miss, no completion raised — after the 5 s fallback (use a fake/virtual
  timer or the headless dispatcher's time control) `IsPageLoading` clears and `ErrorMessage` is
  set; a completion arriving *after* the timeout is a no-op (`_awaitingPageIndex` already -1).
- **Subscription lifecycle:** a second `Load()` (new decoder) and a `GoBack()` each detach the
  handler from the previous `IReaderPageSource` — raising `BackgroundDecodeCompleted` on the
  *old* fake pipe afterwards does nothing. (Assert via a spy `IReaderPageSource` that counts
  live handlers, or that a post-teardown event doesn't touch `CurrentPage`.)
- **Double-page cold miss — primary warm, pair cold:** spread mode, `TryGetCachedPage(primary)`
  hits, `TryGetCachedPage(primary+1)` misses → `CurrentPage` set, `CurrentPageSecondary` null,
  `_awaitingSecondaryPageIndex == primary+1`; raise `BackgroundDecodeCompleted(primary+1)` with
  an eligible-sized bitmap → `CurrentPageSecondary` populated, await cleared. Also: pair completes
  with an *ineligible* size (landscape) → `CurrentPageSecondary` stays null, await cleared (solo
  is correct).
- **Double-page cold miss — both cold:** neither peek hits → `IsPageLoading` true, both awaits
  set; `BackgroundDecodeCompleted(primary)` → primary swaps, `ResolveSecondaryPage` re-peeks
  (still cold) → `_awaitingSecondaryPageIndex` still set; then `BackgroundDecodeCompleted(primary+1)`
  → spread completes.
- **Secondary stale:** jump into spread A (both cold), then jump away before the pair lands —
  a late `BackgroundDecodeCompleted(A+1)` is ignored (`_awaitingSecondaryPageIndex` moved).

---

## Item 3 — Type-to-jump-to-page (comic reader)

### ViewModel — `ReaderScreenViewModel`

```csharp
[ObservableProperty] private bool _isPageInputActive;
[ObservableProperty] private string _pageInputText = string.Empty;

[RelayCommand]
private void BeginPageInput()
{
    if (_decoder is null || PageCount <= 0) return;
    PageInputText = (_currentPageIndex + 1).ToString();
    IsPageInputActive = true;   // view defers focus + select-all off this
}

[RelayCommand]
private void CommitPageInput()
{
    IsPageInputActive = false;
    var s = (PageInputText ?? string.Empty).Trim();
    if (s.Length == 0 || !s.All(char.IsDigit)) return;            // empty / non-numeric → no-op
    // Graceful overflow: a digit string too big even for long (only reachable if the view's
    // MaxLength is somehow bypassed) means "way past the end" → clamp to the last page.
    int target = long.TryParse(s, out long v) ? (int)Math.Clamp(v, 1, PageCount) : PageCount;
    NavigateToPageIndex(target - 1);                              // 1-based entry → 0-based index
}

[RelayCommand]
private void CancelPageInput() => IsPageInputActive = false;
```

- **No VM-side text coercion.** Round 2 put digit-stripping in an `OnPageInputTextChanged` partial
  for testability; that's reverted — mutating a two-way-bound `TextBox.Text` from the VM while the
  box is focused re-clamps `CaretIndex` and snaps the caret to the end on a mid-string edit.
  Filtering moves to the view (below), where the character is rejected *before* insertion so the
  bound text never needs correcting. The VM keeps the part that actually matters for correctness —
  the commit-time parse / clamp / route — and that stays fully headless-testable.

**Shared navigation helper.** Extract the mode split that `SelectThumbnail` currently inlines
into one private method, and call it from both:

```csharp
private void NavigateToPageIndex(int index)   // index is 0-based, already clamped by the caller
{
    if (IsContinuousMode) ScrollToPageRequested?.Invoke(index);
    else GoToPage(index);
}
```

- `SelectThumbnail` becomes `NavigateToPageIndex(Thumbnails.IndexOf(thumbnail))` — behaviour
  unchanged, one code path.
- **Double-page / spread mode:** `GoToPage(N-1)` is *exactly* what clicking page N's thumbnail
  does today. `GoToPage`'s existing `DoublePagePairingActive` logic then positions the spread so
  page N is visible (paired with N−1 or N+1 per the existing rules). "Jump to page N" therefore
  means "show me page N," landing on whatever spread contains it — no separate spread-index math,
  and consistent with the thumbnail rail.
- `GoToPage` already no-ops when `index == _currentPageIndex`, so re-entering the current number
  is harmless.

### Keyboard — remappable, via the existing `PageCanvas` gesture-property pattern

Every remappable reader shortcut is: a `KeyboardCommandRegistry` entry → a `…Key`
`IReadOnlyList<KeyGesture>` property on `ReaderScreenViewModel` (resolved through
`KeyBindingService`) → a `StyledProperty<IReadOnlyList<KeyGesture>>` + a `StyledProperty<ICommand>`
on `PageCanvas`, bound in `ReaderScreen.axaml` → matched in `PageCanvas.OnKeyDown` via
`AnyMatches(…Gesture, e)` + `TryExecute(…Command)`. `ReaderGoToPage` follows that pattern exactly:

1. `KeyboardCommandRegistry`: new `ReaderGoToPage` id constant + entry in the `NavigationGroup`
   list — `new(ReaderGoToPage, NavigationGroup, "Go to page…", new KeyGesture(Key.G),
   ConflictContext.Always)`. `G` is unbound in the reader registry today (`D1`–`D5` are fit
   modes, so digit-accumulation is not an option — hence an explicit trigger key). Defaults live
   in code; `KeyBinding` persists only overrides — **no migration, no seeding.**
2. `ReaderScreenViewModel`: new `GoToPageKey` property, resolved the same way the other `…Key`
   properties are.
3. `PageCanvas`: `GoToPageGestureProperty` + `GoToPageCommandProperty`; in `OnKeyDown`'s
   Always-context block (alongside rotate/zoom/fullscreen) —
   `if (AnyMatches(GoToPageGesture, e)) { if (TryExecute(GoToPageCommand)) e.Handled = true; return; }`.
4. `ReaderScreen.axaml`: `GoToPageGesture="{Binding GoToPageKey}"`
   `GoToPageCommand="{Binding BeginPageInputCommand}"`.

Reach is identical to every other reader shortcut: fires while the canvas has focus (the normal
reading state), in every reading mode. Once focus has moved into a chrome control the user clicks
the page label instead.

### View — `Views/ReaderScreen.axaml` (Navigate cluster, ~line 347)

The `PageLabel` `TextBlock` and an inline editor share one slot:

```xml
<Panel>
  <Button Classes="pageLabelButton" IsVisible="{Binding !IsPageInputActive}"
          Command="{Binding BeginPageInputCommand}" Cursor="Hand"
          ToolTip.Tip="Go to page (G)" AutomationProperties.Name="Go to page">
    <TextBlock Text="{Binding PageLabel}" FontFamily="Consolas,monospace" FontSize="12"
               Foreground="{DynamicResource PbTextMutedBrush}" />
  </Button>
  <TextBox x:Name="PageJumpBox" Classes="pageJump" IsVisible="{Binding IsPageInputActive}"
           Text="{Binding PageInputText, Mode=TwoWay}" MaxLength="7"
           FontFamily="Consolas,monospace" FontSize="12" Width="52"
           AutomationProperties.Name="Go to page" />
</Panel>
```

- `MaxLength="7"` structurally caps the input below `int.MaxValue` (no comic has 10 M pages), so
  the commit-time parse can never overflow through the normal path; the `long.TryParse` fallback
  in `CommitPageInput` is only for a theoretically-bypassed MaxLength.

- `pageLabelButton` — a near-invisible button style (transparent bg, no border, `Padding="0"`),
  same treatment as the existing `GoBackCommand` button in this cluster.
- `pageJump` — minimal `TextBox` style: compact padding, monospace, matches the label's metrics
  so the cluster doesn't jump width. Both new styles go **inline in `ReaderScreen.axaml`'s own
  `<UserControl.Styles>`**, where `chromeCluster` / `clusterIcon` / `floatingPanel` already live
  — the reader chrome styles are not in a shared `Styles/*.axaml` file.
- Code-behind (`ReaderScreen.axaml.cs`):
  - React to `IsPageInputActive` becoming true (via the existing `OnViewModelPropertyChanged`
    handler) → `Dispatcher.UIThread.Post(() => { PageJumpBox.Focus(); PageJumpBox.SelectAll(); })`.
    Deferred, **matching every other post-visibility-change `Focus()` call in this file**
    (`OnScrollToPageRequested`, the two `PageCanvasControl.Focus()` sites) — the `IsVisible`
    binding and the layout pass that realises the `TextBox` haven't run yet on the tick the
    property changes, so a synchronous `Focus()` no-ops. The `PageInputActive` guard below covers
    the one-tick gap where the canvas still holds focus.
  - `PageJumpBox.KeyDown`: `Enter` → `CommitPageInputCommand`, `Escape` → `CancelPageInputCommand`
    (both `e.Handled = true`). Also `e.Handled = true` for `Up` / `Down` / `PageUp` / `PageDown` /
    **`Space`** — a single-line `TextBox` does nothing useful with them, and marking them handled
    stops any bubble to reader nav. (`Space` isn't bound to page-turn in this reader today and the
    `PageInputActive` guard already blocks the path — see below — but it's free insurance against a
    future binding.) `Left` / `Right` / `Home` / `End` are left alone — caret movement.
  - `PageJumpBox` typing filter — handle `TextInput` on the control and set `e.Handled = true`
    for any non-digit, so a rejected keystroke never even flickers into the field and the bound
    `Text` never needs correcting (no caret snap).
  - `PageJumpBox` paste sanitisation — `AddHandler(DataObject.PastingEvent, …)`: pull the text
    payload, keep only `char.IsDigit` chars, and if the result differs from the original either
    replace the `DataPackage` text or cancel the paste and insert the digit-only string manually
    at the caret. Covers Ctrl+V and context-menu paste. Drag-drop of text is handled the same way
    via `DragDrop.DropEvent` if it proves reachable on this control (verify during implementation;
    a page-number field is an unlikely drop target).
  - `PageJumpBox.LostFocus` → `CancelPageInputCommand` (blur = cancel; blur-commit surprises when
    the user clicks away).
- **Belt-and-suspenders shortcut suppression:** `PageCanvas` gains a `bool` `PageInputActiveProperty`
  bound `{Binding IsPageInputActive}`; `OnKeyDown` returns immediately (before it matches any
  gesture) when it's set. This is the *real* guard against any key reaching reader nav while the
  input is open — the chrome `TextBox` is a sibling of `PageCanvas` in the visual tree, not a
  descendant, so its `KeyDown` never routes through `PageCanvas.OnKeyDown` anyway; the guard
  covers only the one-dispatcher-tick gap between the property flip and the deferred `Focus()`.
- `PartLabel` (split-page part indicator) sits after this in the same cluster — unchanged; it
  only shows when zoomed, orthogonal to page input.

### Tests

- `ReaderScreenViewModelTests` — `CommitPageInput`: `"5"` → index 4; `"9999"` → `PageCount-1`;
  `"1"` / `"0"` → index 0; `"  7  "` → index 6 (`Trim` + `long.TryParse` handle surrounding
  space); `""` → no navigation; `"99999999999999999999"` (20 digits, overflows `long`) → clamps
  to `PageCount-1`, does *not* silently abort; `"12a"` → no navigation (defensive `All(IsDigit)`
  check — the view normally prevents this reaching commit); `IsPageInputActive` is false after
  every commit; continuous mode routes to `ScrollToPageRequested` (spy the event) not `GoToPage`;
  `BeginPageInput` sets `PageInputText` to the current 1-based number and `IsPageInputActive`
  true; `BeginPageInput` no-ops with no decoder / `PageCount == 0`; `CancelPageInput` closes
  without navigating.
- `NavigateToPageIndex` shared helper: `SelectThumbnail` still routes correctly after the
  refactor (existing thumbnail tests must stay green — assert, don't just assume).
- `KeyboardCommandRegistryTests`: `ReaderGoToPage` present in `NavigationGroup`, default `G`,
  `ConflictContext.Always`, no gesture collision with any other `Always` command.
- `KeyBindingServiceTests`: an override on `ReaderGoToPage` round-trips through persistence;
  `GoToPageKey` on the VM reflects the override.
- Not headless-testable → on the on-screen verification list: the `TextInput` digit filter, the
  `DataObject.Pasting` sanitiser, `MaxLength`, `Up/Down/PageUp/PageDown/Space` suppression,
  deferred focus + select-all, caret stays put on a mid-string edit, and the
  `PageCanvas.PageInputActive` guard.

---

## Files touched

| File | Change |
|---|---|
| `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` | Item 1 VM: backing prop, 3 bools, `SetReaderMemoryLimitCommand`, load-path hydration |
| `src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml` | Item 1 view: "Performance" group |
| `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` | Item 2 (`_awaitingPageIndex` / `_awaitingSecondaryPageIndex` / `_coldMissTimeout` state, `DetachDecoderEvents` helper, subscribe/unsubscribe in `Load`+`GoBack`, `RefreshCurrentPage` paged branch, `ResolveSecondaryPage`, `OnBackgroundPageDecoded`/`ApplyDecoded`); Item 3 VM (`IsPageInputActive` / `PageInputText` props, 3 commands, `CommitPageInput` with `long.TryParse`+clamp, `NavigateToPageIndex` helper reused by `SelectThumbnail`, `GoToPageKey`) — **no VM text coercion** |
| `src/Paperbunkr.App/Views/ReaderScreen.axaml` | Item 2 loading spinner; Item 3 page-label button + `pageJump` `TextBox` (`MaxLength="7"`) + `GoToPageGesture`/`GoToPageCommand`/`PageInputActive` bindings on `PageCanvas`; both new inline styles in `<UserControl.Styles>` |
| `src/Paperbunkr.App/Views/ReaderScreen.axaml.cs` | Item 3 deferred focus+select-all on activation; `PageJumpBox` KeyDown (Enter/Esc/Up/Down/PageUp/PageDown/Space), `TextInput` digit-filter, `DataObject.PastingEvent` sanitiser, LostFocus wiring |
| `src/Paperbunkr.App/Views/PageCanvas.cs` | Item 3: `GoToPageGesture`/`GoToPageCommand` styled properties + `OnKeyDown` Always-block match; `PageInputActive` styled property + early-return guard |
| `src/Paperbunkr.App/Models/KeyboardCommandRegistry.cs` | Item 3: `ReaderGoToPage` id + entry |
| `src/Paperbunkr.App/Styles/Primitives.axaml` | Item 1: promote `segTab` / `segTab.active` here (shared) |
| `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` | Item 1: drop the now-shared local `segTab` style pair |
| `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` | Item 1 tests |
| `src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` | Item 2 (cold-miss swap, warm path, stale guard, timeout, subscription lifecycle, double-page cold miss × primary/secondary/both, secondary stale) + Item 3 (`CommitPageInput` boundaries incl. `long`-overflow → clamp, mode routing, `NavigateToPageIndex` refactor safety) tests |
| `src/Paperbunkr.App.Tests/KeyboardCommandRegistryTests.cs` / `KeyBindingServiceTests.cs` | Item 3 registry entry + override roundtrip |
| `docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md` | new §17 (Item 2 decision record) |
| `docs/ce-feature-inventory.md` | line 138: correct the CE-has-it framing; note Item 3 shipped as a deviation |
| `docs/Paperbunkr-Roadmap.md` | reader section: mark these items |
| `docs/alpha-todo.md` | manual session note |

## Risks / open points

- **`segTab` promotion** touches `LibrarySection.axaml` (removing the local copy). Low risk —
  identical selectors/setters move to `Primitives.axaml`, which `App.axaml` already merges — but
  it means Item 1 lightly touches a file outside the reader. Verify `Primitives.axaml` is in the
  merged style set and the `Library` folder-management tabs still render after the move.
- **`BusyIndicator` fit** in the Navigate cluster is unverified — the design allows a plain glyph
  fallback, so this isn't blocking.
- **`ReaderGoToPage` dispatch reach** — confirmed: reader shortcuts are matched in
  `PageCanvas.OnKeyDown` via per-command `StyledProperty<IReadOnlyList<KeyGesture>>` bound from
  `ReaderScreen.axaml`. `ReaderGoToPage` uses that same pattern; its reach (canvas-focused, any
  mode) matches every other reader shortcut. Not a redesign.
- **Cold-miss timeout value (5 s)** is a guess. It only governs how long a genuinely stuck decode
  shows the spinner before an error — generous is fine. Tune from feel if it ever matters.
- **`DataObject.PastingEvent` API shape** — confirm the current Avalonia (net10 / this project's
  pinned version) event name and `DataPackage` mutation surface during implementation; if the
  clean interception isn't available, fall back to cancel-and-reinsert-digits at the caret.
- **Drag-drop into `PageJumpBox`** — likely not a reachable target for text drops; verify and add
  a `DragDrop.DropEvent` filter only if it is.
- **On-screen verification** — no computer-use for this project. The user verifies: the freeze is
  gone on a large jump, incl. jumping into a spread (Item 2); the segmented control persists
  across app restart and the Library folder tabs still render after the `segTab` move (Item 1);
  the inline editor focuses on `G`, accepts only digits (typed *and* pasted), keeps the caret
  put on a mid-string edit, commits on Enter, cancels on Esc/blur, and no page-turn/fit-mode
  fires while it's open (Item 3). Automated tests cover VM logic only.
