# Reader Backlog Batch A — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-10-reader-backlog-batch-a-design.md*

Branch `claude/reader-backlog-batch-a` (off `master`). Steps are grouped by item; within an item
they're ordered by dependency. Items 2 and 3 both touch `ReaderScreenViewModel.cs`,
`ReaderScreen.axaml(.cs)` and `PageCanvas.cs` — do Item 3 first so Item 2's `_coldMissTimeout`
spinner slots into a Navigate cluster the editor swap already restructured.

Surveyed shapes this plan relies on:
- `KeyboardCommandRegistry` — `KeyboardCommandDescriptor(Id, Group, Label, KeyGesture, Context)`
  records in a collection-expression list; `Reader*` id consts; `NavigationGroup`.
- Remappable-shortcut pattern: registry entry → `_xxxKey` `[ObservableProperty] IReadOnlyList<KeyGesture>`
  on the VM (default mirrors registry) → `Load()` sets it from `_keyBindings.GetKeys(context, id)` →
  `XxxGestureProperty`/`XxxCommandProperty` `StyledProperty` on `PageCanvas` → bound in
  `ReaderScreen.axaml` → `OnKeyDown` Always-block: `if (AnyMatches(XxxGesture, e)) { if (TryExecute(XxxCommand)) e.Handled = true; return; }`.
- `ReaderScreenViewModel`: `_decoder` (`IPageImageDecoder?`) opened only in `Load()` via
  `ReaderImagePipeline.TryOpen(path, appSettings.ReaderMemoryLimitMb)`; `_decoder?.Dispose()` at the
  top of every `Load()` (`:856`); `GoBack()` does **not** dispose it. `RefreshCurrentPage()` paged
  branch does `pipeline.SetViewportWidth(2560)` + `SetVirtualizationWindow` then synchronous
  `_decoder.GetPage(idx)` in a try/catch, then `CurrentPageSecondary = TryDecodePairedPage(idx, CurrentPage.PixelSize)`.
  `internal` test hooks already exist (`FlushPendingPositionSave`, `OnAutoScrollTick`, …) via
  `InternalsVisibleTo("Paperbunkr.App.Tests")`.
- `IReaderPageSource : IPageImageDecoder` — `TryGetCachedPage(int)→Bitmap?`, `event Action<int> BackgroundDecodeCompleted`,
  `SetVirtualizationWindow`, `SetViewportWidth`.
- `SpreadLayoutMath.IsPairEligible(PixelSize, PixelSize)`; `DoublePagePairingActive` (private bool prop).
- Preferences → Reader is `Views/Preferences/ReaderSection.axaml`: `<StackPanel Tag="reader.xxx">`
  groups, each a `settingsGroupCaption` `TextBlock` + `pref:SettingsRow Icon Title Description` rows
  whose `SettingsRow.SettingsContent` is any control. VM persists one field via
  `PersistBehaviorSetting(Action<AppSettings>)`; hydrates in the `_suppressBehaviorApply` block
  (~`:657`).
- `segTab` / `segTab.active` styles live **locally** in `LibrarySection.axaml` (`:19–30`).
  `Primitives.axaml` is `StyleInclude`d by `App.axaml:230` and already holds `Button.primary` etc.

---

## Step 1 — Promote `segTab` to a shared style
**Files:** `src/Paperbunkr.App/Styles/Primitives.axaml` (edit), `src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit)
**What:** Move the `Button.segTab` + `Button.segTab.active` `<Style>` pair verbatim (same setters,
same `{DynamicResource}` keys) from `LibrarySection.axaml`'s `<UserControl.Styles>` into
`Primitives.axaml` (near `Button.pbChip`). Delete them from `LibrarySection.axaml`; leave its other
local styles. Keep the explanatory comment, reworded for the shared location.
**Depends on:** none
**Verify:** `dotnet build`; existing `LibrarySection` folder-management tabs still compile. Visual
check is on the on-screen list.

## Step 2 — `ReaderMemoryLimitMb` Preferences control
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml` (edit),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:**
- VM: `[ObservableProperty]` `int? _readerMemoryLimitMb` with three `[NotifyPropertyChangedFor]`
  (`IsReaderMemoryAuto`/`IsReaderMemory512`/`IsReaderMemory1024`); the three computed bools
  (`IsReaderMemoryAuto => ReaderMemoryLimitMb is null`); `[RelayCommand] void SetReaderMemoryLimit(string choice)`
  → maps `"512"`/`"1024"`/else-null, early-returns if unchanged, sets the prop, `PersistBehaviorSetting(s => s.ReaderMemoryLimitMb = mb)`.
  Hydrate `ReaderMemoryLimitMb = settings.ReaderMemoryLimitMb;` in the `_suppressBehaviorApply` block.
- View: new `<StackPanel Tag="reader.performance">` at the end of the outer `StackPanel` —
  `settingsGroupCaption` "PERFORMANCE" + one `pref:SettingsRow Icon="Memory" Title="Reader memory limit"`
  `Description="Auto uses up to a quarter of system memory (128–512 MB). Takes effect the next time you open a book."`
  whose `SettingsContent` is a horizontal `StackPanel Spacing="4"` of three `Button Classes="segTab"`
  (`Content` Auto / 512 MB / 1024 MB, `Classes.active="{Binding IsReaderMemoryAuto}"` etc.,
  `Command="{Binding SetReaderMemoryLimitCommand}" CommandParameter="auto|512|1024"`).
- Tests: each choice persists the right `int?` and flips the bools; a fresh VM hydrates from a
  stored value; a stored `768` → all three bools false; re-selecting Auto writes `null`.
**Depends on:** Step 1 (`segTab` must be app-global).
**Verify:** `dotnet test --filter FullyQualifiedName~PreferencesScreenViewModelTests`.

## Step 3 — Register the `ReaderGoToPage` command
**Files:** `src/Paperbunkr.App/Models/KeyboardCommandRegistry.cs` (edit),
`src/Paperbunkr.App.Tests/KeyBindingServiceTests.cs` (edit)
**What:** Add `public const string ReaderGoToPage = "Reader.GoToPage";` and, in the `Commands` list
(after `ReaderNextBookmark`, in `NavigationGroup`),
`new(ReaderGoToPage, NavigationGroup, "Go to page…", new KeyGesture(Key.G), ConflictContext.Always)`.
Test: `ReaderGoToPage` present with default `G`; no other `Always`/`NavigationGroup` command uses
plain `G` (assert against the registry). `KeyBindingServiceTests:157` (`Commands.Count`) stays green
automatically.
**Depends on:** none
**Verify:** `dotnet test --filter FullyQualifiedName~KeyBindingServiceTests`.

## Step 4 — Item 3 ViewModel
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit)
**What:**
- `[ObservableProperty] bool _isPageInputActive;` `[ObservableProperty] string _pageInputText = "";`
- `[ObservableProperty] IReadOnlyList<KeyGesture> _goToPageKey = [new(Key.G)];` alongside the other
  `_xxxKey` fields; add `GoToPageKey = _keyBindings.GetKeys(context, KeyboardCommandRegistry.ReaderGoToPage);`
  in `Load()` with the rest (~`:947`).
- `[RelayCommand] void BeginPageInput()` — guard `_decoder is null || PageCount <= 0`; set
  `PageInputText = (_currentPageIndex + 1).ToString()`, `IsPageInputActive = true`.
- `[RelayCommand] void CommitPageInput()` — `IsPageInputActive = false`; trim; empty or
  `!All(char.IsDigit)` → return; `int target = long.TryParse(s, out var v) ? (int)Math.Clamp(v, 1, PageCount) : PageCount;`
  `NavigateToPageIndex(target - 1);`
- `[RelayCommand] void CancelPageInput() => IsPageInputActive = false;`
- New `private void NavigateToPageIndex(int index)` = `if (IsContinuousMode) ScrollToPageRequested?.Invoke(index); else GoToPage(index);`
  Refactor `SelectThumbnail` to call it (`NavigateToPageIndex(Thumbnails.IndexOf(thumbnail))`).
- Tests: `CommitPageInput` boundary table (incl. 20-digit overflow → `PageCount-1`, `"12a"` → no-nav);
  `BeginPageInput` prefill + guard; `CancelPageInput`; continuous vs paged routing (spy
  `ScrollToPageRequested`); existing thumbnail tests stay green.
**Depends on:** Step 3 (`ReaderGoToPage` const).
**Verify:** `dotnet test --filter FullyQualifiedName~ReaderScreenViewModelTests`.

## Step 5 — Item 3 PageCanvas wiring + gesture binding
**Files:** `src/Paperbunkr.App/Views/PageCanvas.cs` (edit), `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit)
**What:**
- `PageCanvas`: `GoToPageGestureProperty` (`StyledProperty<IReadOnlyList<KeyGesture>>`, default
  `[new KeyGesture(Key.G)]`) + `GoToPageCommandProperty` (`StyledProperty<ICommand?>`) + CLR
  wrappers, mirroring `FullscreenToggle*`. Add `PageInputActiveProperty` (`StyledProperty<bool>`).
- `OnKeyDown`: first line after `base.OnKeyDown(e)` → `if (PageInputActive) return;`. In the
  Always-context block (after the rotate entries) add the `GoToPageGesture` match → `TryExecute(GoToPageCommand)`.
- `ReaderScreen.axaml` `<views:PageCanvas>`: add `GoToPageGesture="{Binding GoToPageKey}"`,
  `GoToPageCommand="{Binding BeginPageInputCommand}"`, `PageInputActive="{Binding IsPageInputActive}"`.
**Depends on:** Step 4 (`GoToPageKey`, `BeginPageInputCommand`, `IsPageInputActive`).
**Verify:** `dotnet build`.

## Step 6 — Item 3 inline editor (view + code-behind)
**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit),
`src/Paperbunkr.App/Views/ReaderScreen.axaml.cs` (edit)
**What:**
- In `<UserControl.Styles>` (with `chromeCluster`/`floatingPanel`): `Button.pageLabelButton`
  (transparent, borderless, `Padding="0"`, `Cursor="Hand"`) and `TextBox.pageJump` (compact
  padding, monospace, `Width≈52`, metrics matching the label).
- Navigate cluster (~`:347`): wrap the `PageLabel` `TextBlock` — replace with a `<Panel>` holding
  a `Button Classes="pageLabelButton"` (`IsVisible="{Binding !IsPageInputActive}"`,
  `Command="{Binding BeginPageInputCommand}"`, `ToolTip.Tip="Go to page (G)"`,
  `AutomationProperties.Name="Go to page"`, child = the existing `TextBlock`) and a
  `TextBox x:Name="PageJumpBox" Classes="pageJump"` (`IsVisible="{Binding IsPageInputActive}"`,
  `Text="{Binding PageInputText, Mode=TwoWay}"`, `MaxLength="7"`, `AutomationProperties.Name="Go to page"`).
  Leave the `PartLabel` `TextBlock` after it untouched.
- `ReaderScreen.axaml.cs`:
  - In `OnViewModelPropertyChanged`, handle `nameof(ReaderScreenViewModel.IsPageInputActive)` →
    when true, `Dispatcher.UIThread.Post(() => { PageJumpBox.Focus(); PageJumpBox.SelectAll(); })`.
  - `PageJumpBox.KeyDown` (wire in XAML or ctor): `Enter`→`CommitPageInputCommand`,
    `Escape`→`CancelPageInputCommand`, `Up`/`Down`/`PageUp`/`PageDown`/`Space` → `e.Handled = true`; all set `e.Handled`.
  - `PageJumpBox.AddHandler(InputElement.TextInputEvent, …, RoutingStrategies.Tunnel)` — `e.Handled = true`
    if `e.Text` is not all digits.
  - `PageJumpBox.AddHandler(DataObject.PastingEvent, …)` — read text, strip non-digits; if changed,
    cancel and insert the digit string at the caret (or mutate the `DataPackage` if the running
    Avalonia exposes it — confirm API, fall back to cancel+insert).
  - `PageJumpBox.LostFocus` → `CancelPageInputCommand`.
**Depends on:** Step 4.
**Verify:** `dotnet build`; on-screen list covers behaviour.

## Step 7 — Item 2 async-swap-on-cold-miss
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit),
`src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit),
`src/Paperbunkr.App.Tests/Fakes/FakeReaderPageSource.cs` (new)
**What:**
- Test seam: `internal Func<string, int?, IPageImageDecoder?>? DecoderFactoryForTest;` — `Load()`
  uses `(DecoderFactoryForTest ?? Services.Reader.ReaderImagePipeline.TryOpen)(issue.FilePath, appSettings.ReaderMemoryLimitMb)`.
- State: `int _awaitingPageIndex = -1; int _awaitingSecondaryPageIndex = -1;`
  `[ObservableProperty] bool _isPageLoading;` `IDisposable? _coldMissTimeout;`
- `private void DetachDecoderEvents()` — if `_decoder is IReaderPageSource p`,
  `p.BackgroundDecodeCompleted -= OnBackgroundPageDecoded`; `_coldMissTimeout?.Dispose()`;
  `_coldMissTimeout = null`; `_awaitingPageIndex = _awaitingSecondaryPageIndex = -1`;
  `IsPageLoading = false`. Call it at the top of `Load()` **before** `_decoder?.Dispose()`, and in
  `GoBack()` before `_goBack()`. After the new `_decoder` is created in `Load()`, if it
  `is IReaderPageSource np` → `np.BackgroundDecodeCompleted += OnBackgroundPageDecoded`.
- Rewrite the `RefreshCurrentPage()` paged branch — **but scope the async path to large jumps
  only** (see note below): `GoToPage` computes `largeJump = Math.Abs(new − old) > 3` and passes it
  as `RefreshCurrentPage(allowAsyncColdMiss:)`. Pipeline branch peeks `TryGetCachedPage`; if it's
  a miss **and** `allowAsyncColdMiss`, keep the outgoing `CurrentPage`, set `_awaitingPageIndex`,
  `IsPageLoading = true`, `CurrentPageSecondary = null`, arm `_coldMissTimeout`; otherwise take the
  synchronous path (`cached ?? _decoder.GetPage(...)` in a try/catch, then `TryDecodePairedPage`)
  exactly as before. Non-`IReaderPageSource` fallback unchanged. `ResolveSecondaryPage` (async
  peek+await for the pair) runs only from `ApplyDecoded`'s primary branch.
  **Why scoped:** a first pass made *every* page turn async, which regressed deterministic
  double-page pairing on ordinary turns (3 pre-existing tests: `NextPage_StepsByTwo_WhenCurrentlyPaired`,
  `ToggleDoublePageModeCommand…RePairsImmediately`, `CurrentPageSecondary_PairsAdjacentPortraitPages`) —
  a real pipeline often hasn't decoded the adjacent page the instant you turn to it. Adjacent
  turns stay synchronous (prefetch nearly always made them a hit; a rare cold ±1/±2 decode is
  ~50 ms, the pre-existing behaviour); only thumbnail-click / type-to-jump distances go async.
- `internal void OnColdMissTimeoutElapsed(int waitingFor)` (extracted like `OnAutoScrollTick`, so
  headless tests can invoke it): if `_awaitingPageIndex == waitingFor` → clear await, `IsPageLoading = false`,
  `ErrorMessage = $"Couldn't decode page {waitingFor + 1}."`.
- `private void ResolveSecondaryPage(IReaderPageSource p, int primaryIdx, PixelSize primarySize)` and
  `OnBackgroundPageDecoded(int idx)` / `ApplyDecoded(int idx)` exactly as in the spec (both awaits;
  `Dispatcher.UIThread.CheckAccess()` fast-path; `ApplyDecoded` re-checks each await, disposes the
  timer on primary swap, calls `ResolveSecondaryPage`).
- `FakeReaderPageSource` test double: implements `IReaderPageSource`; a settable dict of
  `int→Bitmap?` for `TryGetCachedPage`; `RaiseBackgroundDecodeCompleted(int)`; records
  `SetVirtualizationWindow` calls; no-ops the rest.
- Tests (per the spec's Item 2 list): cold-miss→swap (outgoing stays visible mid-wait); warm path
  (no `IsPageLoading`, no timer); stale guard A→B→A; timeout via `OnColdMissTimeoutElapsed`;
  subscription lifecycle (2nd `Load` / `GoBack` detach); double-page cold miss × primary/secondary/both;
  secondary stale.
**Depends on:** Step 6 (Navigate cluster restructured for the spinner in Step 8) is not a hard dep —
Step 7 is VM-only; do it after 6 to keep the file edits sequential.
**Verify:** `dotnet test --filter FullyQualifiedName~ReaderScreenViewModelTests`.

## Step 8 — Item 2 loading affordance
**Files:** `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit)
**What:** In the Navigate cluster, after the page-label `Panel`, a small indicator
`IsVisible="{Binding IsPageLoading}"` — try `BusyIndicator` sized to ~14px; if it doesn't sit
cleanly, a 12px `fi:SymbolIcon` with an `Opacity`/`RenderTransform` rotate driven by a style, or a
static `…` `TextBlock`. Reduced-motion respected.
**Depends on:** Step 7 (`IsPageLoading`).
**Verify:** `dotnet build`; on-screen list.

## Step 9 — Docs
**Files:** `docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md` (edit — new §17),
`docs/ce-feature-inventory.md` (edit — line ~138 + §239 gap list),
`docs/Paperbunkr-Roadmap.md` (edit — reader section), `docs/alpha-todo.md` (edit — manual session note)
**What:** §17 "Post-batch-A decisions": async-paged-swap-on-cold-miss shipped (narrow); full
`PageCanvas` restructure and N decode workers formally deferred with rationale; explicit
`IReaderPageSource` queue-cancellation "not needed / not added" note with the dequeue-staleness
evidence. `ce-feature-inventory.md`: correct "type-to-jump-to-page genuinely not built" to note CE
has no numeric go-to-page and Paperbunkr's is a deliberate deviation (done via `ReaderGoToPage`/`G`);
drop it from the §239 reader gap list. Roadmap + alpha-todo: short notes, P0–P7 unchanged.
**Depends on:** Steps 2, 4–8 done (so the notes describe what actually landed).
**Verify:** read-through.

## Step 10 — Full verification
**What:** `rm src/Paperbunkr.App/obj/Debug/net10.0/Paperbunkr.App.dll src/Paperbunkr.App/obj/Debug/net10.0/Paperbunkr.App.pdb`
then `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` (per CLAUDE.md's XAML-weave gotcha — a
new `TextBox` name + style additions touch XAML). Then targeted suites:
`dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~ReaderScreenViewModelTests|FullyQualifiedName~PreferencesScreenViewModelTests|FullyQualifiedName~KeyBindingServiceTests"`.
Launch the exe to confirm it starts (XAML weave actually ran). Full `Paperbunkr.App.Tests` run if
time permits (note the known whole-suite headless flake). **On-screen verification stays the
user's** — list it in the hand-off: memory-limit persistence + Library tabs unbroken; jump-into-a-
spread has no freeze; `G` opens the editor, digits only (typed + pasted), caret stays on mid-string
edit, Enter commits / Esc / blur cancels, no page-turn fires while open.
**Depends on:** all prior steps.
