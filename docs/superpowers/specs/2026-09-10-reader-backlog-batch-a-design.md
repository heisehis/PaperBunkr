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

// Single sanitisation chokepoint — covers typing, paste, drag-drop, IME. Strip to digits;
// re-assign only when it actually changed (the generated setter's equality check stops recursion).
partial void OnPageInputTextChanged(string value)
{
    var digits = new string(value.Where(char.IsDigit).ToArray());
    if (digits != value) PageInputText = digits;
}

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
    if (!int.TryParse(PageInputText?.Trim(), out int n)) return;   // invalid → no-op
    n = Math.Clamp(n, 1, PageCount);
    NavigateToPageIndex(n - 1);                                    // 1-based entry → 0-based index
}

[RelayCommand]
private void CancelPageInput() => IsPageInputActive = false;
```

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
           Text="{Binding PageInputText, Mode=TwoWay}"
           FontFamily="Consolas,monospace" FontSize="12" Width="52"
           AutomationProperties.Name="Go to page" />
</Panel>
```

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
    (both `e.Handled = true`). Also `e.Handled = true` for `Up` / `Down` / `PageUp` / `PageDown`
    (a single-line `TextBox` does nothing useful with them and they must not bubble to reader
    nav). `Left` / `Right` are left alone — caret movement.
  - `PageJumpBox` typing filter — handle `TextInput` on the control and set `e.Handled = true`
    for any non-digit, so a rejected keystroke never even flickers into the field. This is the
    fast-path nicety; **paste / drag-drop / IME are caught by the VM's `OnPageInputTextChanged`
    coercion** (above), and the commit-time `int.TryParse` + `Clamp` is the final backstop.
  - `PageJumpBox.LostFocus` → `CancelPageInputCommand` (blur = cancel; blur-commit surprises when
    the user clicks away).
- **Belt-and-suspenders shortcut suppression:** `PageCanvas` gains a `bool` `PageInputActiveProperty`
  bound `{Binding IsPageInputActive}`; `OnKeyDown` returns immediately (before it matches any
  gesture) when it's set. Covers the one-dispatcher-tick gap between the property flipping and the
  deferred `Focus()` landing.
- `PartLabel` (split-page part indicator) sits after this in the same cluster — unchanged; it
  only shows when zoomed, orthogonal to page input.

### Tests

- `ReaderScreenViewModelTests` — `CommitPageInput` (note `PageInputText` is already digits-only by
  the time commit runs, thanks to the coercion): `"5"` → index 4; `"9999"` → `PageCount-1`;
  `"0"` → index 0; `""` → no navigation, `IsPageInputActive` false; continuous mode routes to
  `ScrollToPageRequested` (spy the event) not `GoToPage`; `BeginPageInput` sets `PageInputText`
  to the current 1-based number and `IsPageInputActive` true; `BeginPageInput` no-ops with no
  decoder / `PageCount == 0`; `CancelPageInput` closes without navigating.
- **Input sanitisation (VM, headless-testable):** `PageInputText = "3a4"` coerces to `"34"`;
  `"  12 "` → `"12"`; `"abc"` → `""`; `"5"` unchanged (no re-assignment / no notification storm).
  This is the paste / drag-drop / IME guard — it lives in the VM precisely so it's testable.
- `NavigateToPageIndex` shared helper: `SelectThumbnail` still routes correctly after the
  refactor (existing thumbnail tests must stay green — assert, don't just assume).
- `KeyboardCommandRegistryTests`: `ReaderGoToPage` present in `NavigationGroup`, default `G`,
  `ConflictContext.Always`, no gesture collision with any other `Always` command.
- `KeyBindingServiceTests`: an override on `ReaderGoToPage` round-trips through persistence;
  `GoToPageKey` on the VM reflects the override.
- The code-behind typing filter, `Up/Down/PageUp/PageDown` suppression, deferred focus, and the
  `PageCanvas.PageInputActive` guard are not headless-testable — on the on-screen verification
  list, not xUnit.

---

## Files touched

| File | Change |
|---|---|
| `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` | Item 1 VM: backing prop, 3 bools, `SetReaderMemoryLimitCommand`, load-path hydration |
| `src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml` | Item 1 view: "Performance" group |
| `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` | Item 2 (`_awaitingPageIndex` / `_awaitingSecondaryPageIndex` / `_coldMissTimeout` state, `DetachDecoderEvents` helper, subscribe/unsubscribe in `Load`+`GoBack`, `RefreshCurrentPage` paged branch, `ResolveSecondaryPage`, `OnBackgroundPageDecoded`/`ApplyDecoded`); Item 3 VM (props, `OnPageInputTextChanged` coercion, 3 commands, `NavigateToPageIndex` helper reused by `SelectThumbnail`, `GoToPageKey`) |
| `src/Paperbunkr.App/Views/ReaderScreen.axaml` | Item 2 loading spinner; Item 3 page-label button + `pageJump` TextBox + `GoToPageGesture`/`GoToPageCommand`/`PageInputActive` bindings on `PageCanvas`; both new inline styles in `<UserControl.Styles>` |
| `src/Paperbunkr.App/Views/ReaderScreen.axaml.cs` | Item 3 deferred focus+select-all on activation, `PageJumpBox` KeyDown (Enter/Esc/Up/Down/PageUp/PageDown) / TextInput digit-filter / LostFocus wiring |
| `src/Paperbunkr.App/Views/PageCanvas.cs` | Item 3: `GoToPageGesture`/`GoToPageCommand` styled properties + `OnKeyDown` Always-block match; `PageInputActive` styled property + early-return guard |
| `src/Paperbunkr.App/Models/KeyboardCommandRegistry.cs` | Item 3: `ReaderGoToPage` id + entry |
| `src/Paperbunkr.App/Styles/Primitives.axaml` | Item 1: promote `segTab` / `segTab.active` here (shared) |
| `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` | Item 1: drop the now-shared local `segTab` style pair |
| `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` | Item 1 tests |
| `src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` | Item 2 (cold-miss swap, warm path, stale guard, timeout, subscription lifecycle, double-page cold miss × primary/secondary/both, secondary stale) + Item 3 (`CommitPageInput` boundaries, mode routing, `PageInputText` coercion, `NavigateToPageIndex` refactor safety) tests |
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
- **On-screen verification** — no computer-use for this project. The user verifies: the freeze is
  gone on a large jump (Item 2); the segmented control persists across app restart and the Library
  folder tabs still render after the `segTab` move (Item 1); the inline editor focuses on `G`,
  accepts only digits, commits on Enter, cancels on Esc/blur, and no page-turn/fit-mode fires
  while it's open (Item 3). Automated tests cover VM logic only — they can't prove the UI-thread
  stall is gone or that key-suppression works.
