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
[ObservableProperty] private int? _readerMemoryLimitMb;

public bool IsReaderMemoryAuto  => ReaderMemoryLimitMb is not (512 or 1024);
public bool IsReaderMemory512   => ReaderMemoryLimitMb == 512;
public bool IsReaderMemory1024  => ReaderMemoryLimitMb == 1024;

[RelayCommand]
private void SetReaderMemoryLimit(string choice)   // "auto" | "512" | "1024"
{
    int? mb = choice switch { "512" => 512, "1024" => 1024, _ => null };
    if (ReaderMemoryLimitMb == mb) return;
    ReaderMemoryLimitMb = mb;
    PersistBehaviorSetting(s => s.ReaderMemoryLimitMb = mb);
    OnPropertyChanged(nameof(IsReaderMemoryAuto));
    OnPropertyChanged(nameof(IsReaderMemory512));
    OnPropertyChanged(nameof(IsReaderMemory1024));
}
```

- The `string` command parameter (not `int?`) keeps the XAML binding trivial —
  `CommandParameter="512"`.
- Load path (the settings-hydration method, ~line 668 alongside `ResetZoomOnPageChange` /
  `DefaultPageFitMode`): `ReaderMemoryLimitMb = settings.ReaderMemoryLimitMb;` then raise the
  three `OnPropertyChanged`.
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
  (`Styles/Primitives.axaml`) and drop the local copy from `LibrarySection.axaml` (behaviour
  identical — same selectors, same setters). Nothing about it needs 2-vs-3-segment handling; each
  button rounds itself.
- All brushes via `{DynamicResource}` / existing `Classes` — no hex literals (avalonia-pro-max
  design-system rule).

### Migration / tests

- **No migration** — column exists.
- `PreferencesScreenViewModelTests`: setting each choice persists the right `int?`
  (`null` / `512` / `1024`) and the three `Is…` bools reflect it; a fresh VM hydrates the
  selection from a stored value.
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
private int _awaitingPageIndex = -1;   // the page a cold-miss swap is waiting on; -1 = none
[ObservableProperty] private bool _isPageLoading;
```

Subscription — once, in `Load`, paged mode only, after `_decoder` is assigned and cast:

```csharp
if (_decoder is IReaderPageSource pipeline)
    pipeline.BackgroundDecodeCompleted += OnBackgroundPageDecoded;   // unsubscribe on reader teardown
```

`RefreshCurrentPage` paged branch (replacing the current unconditional `GetPage`):

```csharp
if (_decoder is IReaderPageSource pipeline)
{
    pipeline.SetViewportWidth(2560);
    pipeline.SetVirtualizationWindow(_currentPageIndex - 1, _currentPageIndex + 1); // enqueues target high-priority

    var cached = pipeline.TryGetCachedPage(_currentPageIndex);
    if (cached is not null)
    {
        _awaitingPageIndex = -1;
        IsPageLoading = false;
        CurrentPage = cached;
    }
    else
    {
        // keep the outgoing page on screen; don't blank CurrentPage
        _awaitingPageIndex = _currentPageIndex;
        IsPageLoading = true;
    }
}
else
{
    CurrentPage = _decoder.GetPage(_currentPageIndex);   // non-pipeline fallback (unchanged)
}
```

Handler:

```csharp
private void OnBackgroundPageDecoded(int idx)
{
    if (idx != _awaitingPageIndex) return;   // stale — user moved on
    Dispatcher.UIThread.Post(() =>
    {
        if (idx != _awaitingPageIndex || _decoder is not IReaderPageSource p) return;
        var bmp = p.TryGetCachedPage(idx);
        if (bmp is null) return;             // evicted again before we got here; a later turn re-requests
        _awaitingPageIndex = -1;
        IsPageLoading = false;
        CurrentPage = bmp;
        CurrentPageSecondary = TryDecodePairedPage(idx, bmp.PixelSize);
    });
}
```

- **Error path:** the current `catch` around `GetPage` moves — a cold miss no longer throws here;
  a genuine decode failure surfaces via the pipeline's own logging + the page simply never
  arriving. Keep a fallback: if `IsPageLoading` is still true after a timeout (~5 s), set
  `ErrorMessage = "Couldn't decode page N."` and clear the flag. (Timer armed on miss, disarmed on
  swap.)
- **Double-page secondary:** stays synchronous via `TryDecodePairedPage` — the paired page is
  adjacent and almost always already cached or in the high-priority window. If it too is a cold
  miss, `TryDecodePairedPage` returns null (existing contract) and the spread shows solo until the
  next refresh. Acceptable; noted.
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

- `ReaderScreenViewModelTests`: with a fake `IReaderPageSource` whose `TryGetCachedPage` returns
  null then (after raising `BackgroundDecodeCompleted`) a bitmap — `IsPageLoading` goes
  true→false, `CurrentPage` swaps, the outgoing page stays visible in between.
- Stale guard: two jumps in a row; the first page's late `BackgroundDecodeCompleted` is ignored.
- Warm path unchanged: `TryGetCachedPage` returns a bitmap → `IsPageLoading` never set,
  synchronous assignment.

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
    IsPageInputActive = true;   // view focuses + selects the TextBox off this
}

[RelayCommand]
private void CommitPageInput()
{
    IsPageInputActive = false;
    if (!int.TryParse(PageInputText?.Trim(), out int n)) return;   // invalid → no-op
    n = Math.Clamp(n, 1, PageCount);
    int index = n - 1;
    if (IsContinuousMode) ScrollToPageRequested?.Invoke(index);
    else GoToPage(index);
}

[RelayCommand]
private void CancelPageInput() => IsPageInputActive = false;
```

- Routing is exactly `SelectThumbnail`'s split (paged → `GoToPage`, continuous →
  `ScrollToPageRequested`). Double-page: `GoToPage` lands on the typed page and its existing
  pairing logic re-pairs.
- `GoToPage` already no-ops when `index == _currentPageIndex`, so re-entering the current number
  is harmless.

### Keyboard — `Models/KeyboardCommandRegistry`

New entry in the `NavigationGroup` list:

```csharp
new(ReaderGoToPage, NavigationGroup, "Go to page…", new KeyGesture(Key.G), ConflictContext.Always),
```

- `G` is unbound in the reader registry today (`D1`–`D5` are fit modes; digit-accumulation is
  therefore not an option — this is why the trigger is an explicit key/click, not typing digits
  into the canvas).
- `ReaderGoToPage` is a new command-id constant alongside the other `Reader*` ids.
- Defaults live in code; `KeyBinding` persists only user overrides — **no migration, no seeding.**
- Dispatch: wherever reader registry commands are routed to VM actions (the
  `KeyBindingService`-driven switch in `PageCanvas.OnKeyDown` or `ReaderScreen.axaml.cs`), map
  `ReaderGoToPage` → `BeginPageInputCommand`. It must fire in every reading mode and whether or
  not the canvas has focus — same dispatch reach as `ReaderToggleFullscreen`.

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
  - React to `IsPageInputActive` becoming true (property-changed subscription or an
    `AttachedToVisualTree`/`IsVisibleProperty` observer on `PageJumpBox`) → `PageJumpBox.Focus();
    PageJumpBox.SelectAll();`.
  - `PageJumpBox.KeyDown`: `Enter` → `CommitPageInputCommand`; `Escape` → `CancelPageInputCommand`;
    mark handled so the reader's global key handling doesn't also see them.
  - `PageJumpBox.LostFocus` → `CancelPageInputCommand` (blur = cancel; blur-commit surprises when
    the user clicks away).
- `PartLabel` (split-page part indicator) sits after this in the same cluster — unchanged; it
  only shows when zoomed, orthogonal to page input.

### Tests

- `ReaderScreenViewModelTests`: `CommitPageInput` — `"5"` → page 5; `"9999"` → clamped to
  `PageCount`; `"0"` / `"-3"` → clamped to 1; `"abc"` / `""` → no navigation, input closes;
  continuous mode raises `ScrollToPageRequested` not `GoToPage`; `BeginPageInput` pre-fills the
  current number; `CancelPageInput` closes without navigating.
- `KeyboardCommandRegistryTests` (or equivalent): `ReaderGoToPage` present, default `G`, no
  gesture conflict in `ConflictContext.Always`.
- `KeyBindingServiceTests`: an override on `ReaderGoToPage` round-trips through persistence.

---

## Files touched

| File | Change |
|---|---|
| `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` | Item 1 VM: backing prop, 3 bools, `SetReaderMemoryLimitCommand`, load-path hydration |
| `src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml` | Item 1 view: "Performance" group |
| `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` | Item 2 narrow fix (state, subscription, `RefreshCurrentPage` paged branch, handler, timeout); Item 3 VM (3 props/commands) |
| `src/Paperbunkr.App/Views/ReaderScreen.axaml` | Item 2 loading spinner; Item 3 page-label button + `pageJump` TextBox |
| `src/Paperbunkr.App/Views/ReaderScreen.axaml.cs` | Item 3 focus/keydown/lostfocus wiring; `ReaderGoToPage` dispatch |
| `src/Paperbunkr.App/Views/ReaderScreen.axaml` (`<UserControl.Styles>`) | Item 3: `pageJump` + `pageLabelButton` inline styles |
| `src/Paperbunkr.App/Models/KeyboardCommandRegistry.cs` | Item 3: `ReaderGoToPage` id + entry |
| `src/Paperbunkr.App/Styles/Primitives.axaml` | Item 1: promote `segTab` / `segTab.active` here (shared) |
| `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` | Item 1: drop the now-shared local `segTab` style pair |
| `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` | Item 1 tests |
| `src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` | Item 2 + Item 3 tests |
| `src/Paperbunkr.App.Tests/KeyboardCommandRegistryTests.cs` / `KeyBindingServiceTests.cs` | Item 3 registry + roundtrip tests |
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
- **`ReaderGoToPage` dispatch reach** — needs to fire regardless of canvas focus. If the reader's
  registry dispatch is canvas-focus-gated today, that's a one-line addition to the always-on
  handler (same place `F`/fullscreen is handled), not a redesign.
- **On-screen verification** — no computer-use for this project. The user verifies the freeze is
  gone on a large jump, the segmented control persists, and the inline editor focuses/commits/
  cancels. Automated tests cover the VM logic; they can't prove the UI-thread stall is actually
  gone.
