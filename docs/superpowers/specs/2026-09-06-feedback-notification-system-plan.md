# Feedback & notification system — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-06-feedback-notification-system-design.md*

**Ground truth corrections found while surveying the codebase** (the design doc's prose was
slightly off in a few places — this plan uses the real shapes):

- MainWindow.axaml has **17** backdrop-`Border`-wrapped overlays following the identical pattern
  (not 15) — the 15 the design doc named, plus `Welcome` and `UpdateAvailable`, which use the same
  shape. `IsDiscardConfirmOpen` and `IsTourOfferOpen` are small inline confirm cards (different
  pattern, no wrapped view) and `WelcomeTourOverlay` deliberately draws its own scrim-with-cutout —
  none of these three migrate to `OverlayShell`.
- Every one of the 17 already has a dedicated `CloseXOverlayCommand` (or, for `Welcome`/
  `UpdateAvailable`, binds straight to the child VM's own `SkipCommand`/`NotNowCommand`) — **no
  command extraction is needed anywhere**, contrary to the design doc's hedge.
- `IssuePropertiesOverlay`'s "unsaved-changes check" is its own screen VM's `CancelCommand`, not
  `MainViewModel.TryLeaveCurrentEditor` (that guards rail-nav away from the whole editor, a
  separate path). `OverlayShell.CloseCommand` for those three (Issue/BulkIssue/BulkSeries
  Properties) binds straight to the existing `CloseXOverlayCommand`, which already forwards to it.
- `MainWindow.axaml`'s `countBadge` (4 sites) is the Smart-List sidebar match-count, not a
  separate "search result count" — this plan scopes `CountPill` to those 4 sites; no other
  distinct search-count badge was found.
- `MainViewModelTests.cs` does **not** currently test `ShowToast`/`Undo`/`Redo` (despite the design
  doc implying it does) — its relevant existing coverage is the overlay-close/discard-confirm
  tests, which double as `OverlayShell.CloseCommand` regression coverage. The toast-audit
  regression coverage for the "already-correct" sites actually lives in
  `LibraryScreenViewModelTests.cs` (`...AndToasts` test names).

## Step 1: OverlayShell control
**Files:** `src/Paperbunkr.App/Controls/OverlayShell.cs` (new), `src/Paperbunkr.App/Styles/Overlays.axaml` (new), `src/Paperbunkr.App/App.axaml` (edit — add the new Styles file to the merge list, same place `Marks.axaml` is included)
**What:** `ContentControl` subclass mirroring `BrandMark`'s pattern (`StyledProperty` via `AvaloniaProperty.Register`, not `DirectProperty` — nothing here is computed): `IsOpen` (bool, drives the control's own `IsVisible` via a class handler on `IsOpenProperty.Changed`), `CloseCommand` (`ICommand?`), `AllowDismiss` (bool, default true). Template (in `Overlays.axaml`, keyed `x:Key="{x:Type controls:OverlayShell}"` `TargetType="controls:OverlayShell"`, exactly like `Marks.axaml` keys `BrandMark`) renders the `#B0000000` scrim `Border` (named `PART_Scrim`), a centered `Grid` hosting `ContentPresenter` for the child content plus the standard corner "X" `Button` (`Classes="rail"`, `DismissCircle` icon — copy the exact button markup from any current overlay block, e.g. `MainWindow.axaml:887-892`) bound to `CloseCommand` via `TemplateBinding`. `OnApplyTemplate` finds `PART_Scrim` and wires its `PointerPressed` to invoke `CloseCommand` when `AllowDismiss` is true (never a raw `IsOpen = false`).
**Escape is deliberately NOT handled here.** `MainViewModel.Escape()` (`MainViewModel.cs:2105-2191`) already centrally checks each overlay's `IsXOverlayOpen` in sequence and invokes its close command, wired via `MainWindow.axaml`'s `Window.KeyBindings` — its own doc comment explicitly records a real bug from having two uncoordinated Escape handlers fight over precedence. `OverlayShell` adding its own `KeyDown`/Escape handling would reintroduce that exact bug. Migrating the 17 overlays onto `OverlayShell` does not touch `Escape()` at all — it keeps working unchanged, since it operates on the same `IsXOverlayOpen`/close-command pairs regardless of what XAML wraps them.
**Depends on:** none
**Verify:** new `OverlayShellTests.cs` — `CloseCommand` invoked (not `IsOpen` flipped directly) on scrim `PointerPressed`; nothing invoked when `AllowDismiss = false`. Existing `Escape_MigrationOverlayOpen_ClosesIt` (Step 7) is the regression check that centralized Escape handling still works after migration.

## Step 2: ConfirmDialog + IDialogService
**Files:** `src/Paperbunkr.App/Services/IDialogService.cs` (new), `src/Paperbunkr.App/Services/DialogService.cs` (new), `src/Paperbunkr.App/ViewModels/ConfirmDialogViewModel.cs` (new), `src/Paperbunkr.App/Views/ConfirmDialogView.axaml`+`.axaml.cs` (new), `src/Paperbunkr.App/Views/MainWindow.axaml` (edit — mount one `OverlayShell` wrapping `ConfirmDialogView`, bound to `ConfirmDialogViewModel`, alongside the other 17), `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — expose the `ConfirmDialogViewModel` instance + construct `DialogService`)
**What:**
```csharp
public interface IDialogService
{
    Task<int> ShowAsync(ConfirmDialogRequest request); // 0 = primary, 1 = secondary, -1 = dismissed
    Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
        string cancelLabel = "Cancel", bool isDestructive = false);
}
public sealed record ConfirmDialogRequest(string Message, string? Title = null,
    IReadOnlyList<string>? Items = null, string PrimaryLabel = "Confirm",
    string? SecondaryLabel = "Cancel", bool IsDestructive = false);
```
`DialogService.ShowAsync` sets the shared `ConfirmDialogViewModel`'s bindable properties (`Title`,
`Message`, `Items`, `PrimaryLabel`, `SecondaryLabel`, `IsDestructive`), opens its `OverlayShell`
(`IsOpen = true`), and awaits a `TaskCompletionSource<int>` that the primary/secondary/dismiss
commands complete. `ConfirmAsync` wraps `ShowAsync`, mapping `0 => true`, else `false`.
`ConfirmDialogView.axaml`: title + message `TextBlock`s, a `ScrollViewer`+`ItemsControl` for
`Items` (only visible when non-null/non-empty, `MaxHeight` so long lists scroll), a button row —
primary button styled with the destructive/error brush when `IsDestructive`.
**Depends on:** Step 1
**Verify:** new `DialogServiceTests.cs` — `ShowAsync` resolves 0/1/-1 correctly for primary/
secondary/dismiss; `ConfirmAsync`'s bool mapping; `Items` rendering only when provided.

## Step 3: PluginQuestionDialog absorption
**Files:** `src/Paperbunkr.App/Views/PluginQuestionDialog.axaml`+`.axaml.cs` (delete), `src/Paperbunkr.App/Plugins/PaperbunkrApplication.cs` (edit `AskQuestion`, ~line 152)
**What:** `IApplication.AskQuestion(string question, string buttonText, string optionText)` keeps
its exact synchronous `int`-returning signature (versioned plugin contract — untouched). Its
implementation changes from `PluginQuestionDialog.ShowModal(...)` to pumping a `DispatcherFrame`
while an async call to the new `IDialogService.ShowAsync` completes — same technique
`CrashReportWindow.ShowModal` already uses, just around a `Task` instead of a `Window.Closed`
event:
```csharp
public int AskQuestion(string question, string buttonText, string optionText)
{
    int answer = -1;
    var frame = new DispatcherFrame();
    _ = RunAsync();
    async Task RunAsync()
    {
        answer = await _dialogService.ShowAsync(new ConfirmDialogRequest(question,
            PrimaryLabel: buttonText,
            SecondaryLabel: string.IsNullOrEmpty(optionText) ? null : optionText));
        frame.Continue = false;
    }
    Dispatcher.UIThread.PushFrame(frame);
    if (answer == 0 && PluginInvocationContext.Current is { RequiresWriteConfirmation: true } ctx)
    {
        ctx.WritesConfirmed = true;
    }
    return answer;
}
```
`PaperbunkrApplication` needs an `IDialogService` reference (constructor injection).
**Depends on:** Step 2
**Verify:** `PluginApiV3Tests.cs`'s `ConfirmWrites_Gate_...` tests must keep passing unchanged
(they exercise a `FakeConfirmingApp : IApplication` stub, not the real pump, so they're unaffected
by construction — confirms the plugin contract itself didn't move). Add one new manual/GUI check:
trigger a real plugin `AskQuestion` call and confirm the in-page dialog blocks correctly and
returns control to the plugin script — a `DispatcherFrame` pump around a `Task` is exactly the
kind of thing that can deadlock if done wrong, and that's not mechanically provable by a headless
unit test.

## Step 4: BusyIndicator control
**Files:** `src/Paperbunkr.App/Controls/BusyIndicator.cs` (new), `src/Paperbunkr.App/Styles/Indicators.axaml` (new), `App.axaml` (edit — add include)
**What:** `TemplatedControl` with one `StyledProperty<ActivityJob?> Job`. Template reads
`Job.Fraction`/`Job.IsIndeterminate` via `{Binding Job.Fraction, RelativeSource={RelativeSource TemplatedParent}}`
(no `DirectProperty` needed — nothing is computed on the control itself, it just forwards to a
`ProgressBar`, `Height="3"`, `CornerRadius="2"`, matching `ActivityTemplates.axaml:44-45`'s current
look exactly).
**Depends on:** none
**Verify:** new `BusyIndicatorTests.cs` — template's `ProgressBar.Value`/`IsIndeterminate` reflect
the bound `Job`.

## Step 5: StatusBadge control
**Files:** `src/Paperbunkr.App/Controls/StatusBadge.cs` (new), `src/Paperbunkr.App/Styles/Badges.axaml` (new), `App.axaml` (edit — add include)
**What:** `TemplatedControl` with `StyledProperty<StatusBadgeVariant> Variant` (new enum: `Read`,
`InProgress`, `New`) in `src/Paperbunkr.App/Models/StatusBadgeVariant.cs`. Template is a
`Panel` with three `fi:SymbolIcon`/`Border` children, each `IsVisible` bound to `Variant` via a
converter (mirror `BrandMark`'s `IsImage`/`IsGlyph`/`IsChip` `DirectProperty` pattern — add
`IsRead`/`IsInProgress`/`IsNew` computed `DirectProperty`s driven off `Variant` in a private
`Rebuild()`, same as `BrandMark.Rebuild()`): `Read` → `CheckmarkCircle` icon,
`PbSuccessBrush` (exact markup from `DetailTabs.axaml:114-118`); `InProgress` → `CircleHalfFill`
icon, `PbAccentBrush` (from `DetailTabs.axaml:136-137` — this becomes the *only* in-progress
treatment; the poster-tile accent bar is dropped); `New` → the `newBadge` pill markup from
`MangaDetailScreen.axaml:105-110,224-226` ("NEW" text, `PbAccentBrush` background).
**Depends on:** none
**Verify:** new `StatusBadgeTests.cs` — each `Variant` shows the right icon/color and hides the
other two.

## Step 6: CountPill & TagPill shared styles
**Files:** `src/Paperbunkr.App/Styles/Badges.axaml` (edit — add alongside `StatusBadge`'s
`ControlTheme`, same file since they're the same design-doc section)
**What:** Two `Style` blocks (not controls): `Style Selector="Border.countPill"` (rounded pill,
`CornerRadius="999"`, padding, background — parameterizable via `Classes.destructive` for the
red variant seen on `StatusBar`'s alert count) and `Style Selector="Border.tagPill"`/
`"Button.tagPill"` (rounded chip, muted border+background, matching the existing
`Button.toolbarPill` selector precedent in `LibraryToolbar.axaml:18`). These are pure XAML style
resources — no new `.cs` file.
**Depends on:** none
**Verify:** no new unit test (pure styling); covered by the rollout steps' visual verification (13–14).

## Step 7: Migrate 3 overlays onto OverlayShell (smoke test)
**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (edit — `Migration`, `CollectionProperties`,
`IssueProperties` blocks, lines ~883, ~914, ~1057)
**What:** Replace each `<Border IsVisible="{Binding IsXOverlayOpen}" Background="#B0000000"><Grid>...<Button Command="{Binding CloseXOverlayCommand}">...</Button></Grid></Border>`
with `<controls:OverlayShell IsOpen="{Binding IsXOverlayOpen}" CloseCommand="{Binding CloseXOverlayCommand}"><views:XOverlay DataContext="{Binding X}" /></controls:OverlayShell>`.
`IssueProperties` is the one to prove the close-command-delegation nuance works correctly (its
`CloseIssuePropertiesOverlayCommand` already forwards to `IssueProperties.CancelCommand` — no
change needed there, `OverlayShell` just needs to invoke the same command scrim-click/Escape
already do today via the X button).
**Depends on:** Step 1
**Verify:** Existing `MainViewModelTests.cs` overlay tests (`Escape_MigrationOverlayOpen_ClosesIt`,
`Escape_IssuePropertiesOverlayOpen_ClosesOverlay_LeavingUnderlyingScreenAlone`,
`GoLibrary_WithDirtyIssueProperties_PromptsInsteadOfNavigating`) must keep passing — these are
the direct regression check that `OverlayShell`'s Escape handling didn't bypass the unsaved-check.
Manual GUI check: open each of the 3, confirm scrim-click/Escape/X-button all close correctly,
and that Issue Properties still prompts on unsaved changes.

**Correction found during Step 8 implementation:** `OverlayShell` needed two properties not in the
original Step 1 spec, discovered once the remaining 14 overlays' real markup was in front of me:
`ContentHorizontalAlignment`/`ContentVerticalAlignment` (default Center/Center) — `QuickOpenOverlay`
was the one exception, using `VerticalAlignment="Stretch"` for its results list, which the fixed
template would have broken; and `ShowCloseButton` (default true) — `QuickOpenOverlay` also had no
corner close button at all before this control existed (closes via Escape/selecting a result).
Both are now real `StyledProperty`s on `OverlayShell`, not scope creep — every other overlay uses
the defaults unchanged.

## Step 8: Migrate remaining 14 overlays onto OverlayShell
**Files:** `src/Paperbunkr.App/Views/MainWindow.axaml` (edit — `ReadingListProperties`,
`NewReadingListDialog`, `WorkspaceName`, `QuickOpen`, `NewEventDialog`, `BookProperties`,
`BulkBookProperties`, `BookSeriesProperties`, `QuickRate`, `DesignShowcase`,
`BulkIssueProperties`, `BulkSeriesProperties`, `Welcome`, `UpdateAvailable` — remaining lines from
the 899–1158 range)
**What:** Same mechanical replacement as Step 7, for the rest. `Welcome`'s `CloseCommand` binds to
`Welcome.SkipCommand` and `UpdateAvailable`'s to `Update.NotNowCommand` (not a `MainViewModel`
wrapper — confirmed these are the two exceptions to the `CloseXOverlayCommand` naming pattern).
`QuickOpen` has no visible close button today (dismisses itself) — still wrap it, `AllowDismiss`
stays true so Escape/scrim-click still work, just no extra X button was needed before and none is
added now beyond what `OverlayShell` provides by default.
**Depends on:** Step 7 (only start this once the 3-overlay smoke test is verified — this is
where a big-bang `MainWindow.axaml` change would be risky; doing it in two passes catches a
systemic problem after touching 3 files instead of 17)
**Verify:** `OpenWelcomeOverlay_...`/`CloseWelcomeOverlay_...` tests in `MainViewModelTests.cs`
keep passing. Manual GUI spot-check on a handful (Welcome, QuickRate, BulkIssueProperties) since
not all 14 have dedicated automated overlay-close tests today.

## Step 9: Shared toast template
**Files:** `src/Paperbunkr.App/Models/ToastRequest.cs` (new — `ToastRequest`, `ToastAction`,
`ToastSeverity` records/enum), `src/Paperbunkr.App/Views/PbToastView.axaml`+`.axaml.cs` (new),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — `ShowToast`, `ToastRequested` event
shape, `ShowMinimizeToTrayNotice`, `UndoCommand`/`RedoCommand`'s toast calls),
`src/Paperbunkr.App/Views/MainWindow.axaml.cs` (edit — `OnDataContextChanged`'s
`WindowNotificationManager` wiring)
**What:**
```csharp
public sealed record ToastRequest(string Title, string? Message = null,
    ToastSeverity Severity = ToastSeverity.Info, IReadOnlyList<ToastAction>? Actions = null);
public sealed record ToastAction(string Label, ICommand Command);
public enum ToastSeverity { Info, Success, Warning, Error }
```
`ToastRequested` becomes `event Action<ToastRequest>?`; `ShowToast(string title, string message)`
becomes a thin wrapper: `ToastRequested?.Invoke(new ToastRequest(title, message))` (default
`Severity.Info` — today's code hardcodes `NotificationType.Success` for literally everything, so
this is a real, intentional behavior correction, not a refactor-preserving no-op; call sites that
want `Success` explicitly, like `ActivityService`'s completion toasts, pass it in Step 10).
`PbToastView` renders a `FluentIcons.Avalonia SymbolIcon` (mapped from `Severity` — `CheckmarkCircle`
for `Success`, etc.) + left-border accent color + `Title`/`Message`/optional `Actions` button row.
`MainWindow.axaml.cs`: `viewModel.ToastRequested += request => _notificationManager.Show(new PbToastView { DataContext = request }, MapNotificationType(request.Severity), expiration: request.Actions is null ? default : TimeSpan.Zero);`
— toasts with `Actions` stay until dismissed (matching today's update-ready behavior), others use
the default auto-dismiss. **Verify empirically** (not assumed) whether passing a custom
`PbToastView` alongside a non-null `NotificationType` double-renders an icon (Avalonia's
`NotificationCard` may add its own chrome even for custom content) — if so, pass
`NotificationType.Information` uniformly and let `PbToastView`'s own icon be the only one.
**Depends on:** none (independent of Steps 1–8)
**Verify:** new `ToastRequestTests.cs` — `ShowToast` produces the right `ToastRequest`. Existing
`LibraryScreenViewModelTests.cs` toast tests (`MarkSelectionRead_..._AndToasts`,
`AddSelectionToReadingList_..._ToastsWithoutInserting`, etc.) must keep passing — they assert a
toast *fired*, not its exact shape, so they're the regression check that the event signature
change didn't break existing callers.

## Step 10: Activity Center completion-toast severity
**Files:** `src/Paperbunkr.App/Services/ActivityService.cs` (edit — `CompletionToastRequested`
declaration + invocation, ~lines 63/243), `src/Paperbunkr.App/Services/IActivityService.cs` (edit
— event signature), `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — the
`Activity.CompletionToastRequested += (title, message) => ShowToast(title, message);` wiring at
line 110)
**What:** `CompletionToastRequested` becomes `event Action<ToastRequest>?`; `ActivityService`
constructs `new ToastRequest(title, summary, status == ActivityJobStatus.Succeeded ? ToastSeverity.Success : ToastSeverity.Error)`
instead of passing two bare strings.
**Depends on:** Step 9
**Verify:** `ActivityServiceTests.cs`'s `CompletionToast_RaisedWhenPanelClosed_SuppressedWhenOpen`
must keep passing, extended to assert the `Severity` matches the job's outcome.

## Step 11: Update-ready toast folded into PbToastView
**Files:** `src/Paperbunkr.App/ViewModels/UpdateReadyToastViewModel.cs` (delete),
`src/Paperbunkr.App/Views/UpdateReadyToastView.axaml`+`.axaml.cs` (delete),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — `UpdateReadyToastRequested`/
`UpdateReadyToastCloseRequested` events + `DownloadUpdateAsync`, ~line 1772),
`src/Paperbunkr.App/Views/MainWindow.axaml.cs` (edit — replace the `_updateReadyToasts`
dictionary + its two event handlers, ~lines 418-454)
**What:** `MainViewModel` builds `new ToastRequest("Update ready", "Version {X} will install on restart", ToastSeverity.Info, new[] { new ToastAction("Restart", restartCommand), new ToastAction("Later", laterCommand), new ToastAction("What's New", whatsNewCommand) })`
and raises it through the *same* `ToastRequested` event Step 9 introduced — `UpdateReadyToastRequested`
as a separate event is removed entirely. The 3 commands close over the same
`UpdateService`/`AppCastItem`/`downloadPath` state `UpdateReadyToastViewModel`'s constructor used
to take. `MainWindow.axaml.cs`'s per-VM `_updateReadyToasts: Dictionary<UpdateReadyToastViewModel, Control>`
generalizes to `_persistentToasts: Dictionary<ToastRequest, Control>`, populated whenever a shown
`ToastRequest.Actions is not null`, so any future actionable toast (not just update-ready) can be
closed by identity the same way.
**Depends on:** Step 9
**Verify:** Manual GUI check — trigger an update-ready toast (or a test double), confirm Restart/
Later/What's New all behave identically to today and the toast doesn't auto-dismiss mid-decision.
No existing automated test covers this flow today (confirmed during survey), so this is
manual-only, consistent with the design doc's own testing section.

## Step 12: BusyIndicator rollout
**Files:** `src/Paperbunkr.App/Views/ActivityTemplates.axaml` (edit, lines 44-45)
**What:** Replace `<ProgressBar Minimum="0" Maximum="1" Value="{Binding Fraction}" Height="3" CornerRadius="2" IsIndeterminate="{Binding IsIndeterminate}" IsVisible="{Binding IsRunning}" />`
with `<controls:BusyIndicator Job="{Binding}" IsVisible="{Binding IsRunning}" />`. Nothing else in
the row template changes.
**Depends on:** Step 4
**Verify:** Existing `ActivityServiceTests.cs` job-lifecycle tests unaffected (they don't touch
XAML). Manual GUI check: open the Activity Center peek/drawer while a job runs, confirm the bar
still looks identical to today.

## Step 13: StatusBadge rollout
**Files:** `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit — 3 templates: poster ~104-119, list
~128-141, card ~157-171), `src/Paperbunkr.App/Views/MangaDetailScreen.axaml` (edit — `newBadge` usage ~224-226; the
`newBadge` `Style` def at ~105-110 is removed once nothing references it), `src/Paperbunkr.App/Models/IssueCardSample.cs` (edit — expose a single `StatusBadgeVariant?` computed
property replacing the separate `IsRead`/`TileGlyph` consumers need, or keep `IsRead`/`IsInProgress`
and let the XAML pick `Variant` via a converter — whichever keeps `Models/IssueTileGlyph.cs`'s
existing enum meaningful without duplicating it; reconcile at implementation time by reading
`IssueTileGlyph`'s full definition first)
**What:** Each read-checkmark/in-progress-icon/NEW-pill usage becomes
`<controls:StatusBadge Variant="Read" IsVisible="{Binding IsRead}" />` (etc.) in place of the raw
`fi:SymbolIcon`/`Border` markup. The poster tile's separate accent-bar in-progress treatment
(`DetailTabs.axaml:112-113`) is deleted outright — `InProgress` now only ever renders via the icon
variant, consistent across poster/list/card.

**Correction found during implementation:** `BookDetailScreen.axaml:95-100`'s checkmark is not a
status badge — it's the icon on an always-visible "toggle finished" action Button (the label text,
not the icon, is what changes with state via `FinishedToggleLabel`). Converting a static button
icon to a conditionally-visible `StatusBadge` doesn't fit; left untouched, out of scope for this
step.
**Depends on:** Step 5
**Verify:** `MangaDetailScreenViewModelTests.cs`'s `ChapterRow_IsNew_OnlyWhenUnreadAndReleasedWithinTwoWeeks`
and `ChapterRow_ShowReadGlyph_TrueOnlyWhenFullyReadNotInProgress` must keep passing unchanged —
they test the underlying boolean logic, not the XAML, so they're the regression guard that
`StatusBadge`'s `Variant` selection didn't change *when* a badge shows, only *how*. Manual GUI
check across all 3 `DetailTabs` view modes plus `BookDetailScreen`/`MangaDetailScreen`.

## Step 14: CountPill rollout
**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit — 3 sites: dot overlay ~246,423;
pill+count ~313,501,617-682; plain text ~536), `src/Paperbunkr.App/Views/StatusBar.axaml` (edit —
alert-count pill ~89-93; the separate pulsing dot ~80-85 is untouched),
`src/Paperbunkr.App/Views/MainWindow.axaml` (edit — 4 Smart-List match-count sites, ~444,464,530,564)
**What:** Every one of these becomes `<Border Classes="countPill"><TextBlock Text="{Binding ...Count}" /></Border>`
using Step 6's shared style, replacing each site's bespoke `Background`/`CornerRadius`/`Padding`
combination. `LibraryScreen`'s three divergent unread treatments (dot-with-bullet-character,
pill-with-bullet-character, plain-text-count) collapse to one consistent
`countPill` showing the actual `UnreadCount` number in all three places (today's dot/pill variants
show a bullet character instead of the count — this is a real, intentional visual improvement:
they've been treated as pure presence indicators; unifying them means users can see the count in
list/table rows too, not just the poster-grid tile).
**Correction found during implementation:** the "3 divergent treatments" were actually 2 families —
per-issue boolean unread dots (panorama tile + row, bullet character, no count) and per-series
numeric `UnreadCount` pills (panorama/row/poster-grid). Both families now share `countPill`'s
chrome regardless (a rounded pill is a rounded pill whether it holds a bullet or a number), so the
unification still lands. The table-view row's `UnreadCount` column
(`LibraryScreen.axaml:528`) was left as plain text, not wrapped in `countPill` — every other column
in that row (`IssueCount`, etc.) is plain aligned text too, and wrapping only one column in badge
chrome would look inconsistent with its own row rather than more consistent app-wide.
**Depends on:** Step 6
**Verify:** `LibraryScreenViewModelTests.cs`'s existing unread-related tests
(`FilterUnreadOnly_...`, `MarkIssueRead_...`) are unaffected (they test the underlying
`UnreadCount`/`IsRead` data, not XAML). Manual GUI check across Library's panorama/row/poster-grid
view modes, the status bar, and the 4 Smart List sidebar sections.

## Step 15: TagPill rollout
**Files:** `src/Paperbunkr.App/Views/ReadingScreen.axaml` (edit — done: local `tagPillButton`/
`tagPillText` styles removed, button switched to the shared `tagPill` class)
**What:** `ReadingScreen`'s tag chips now use Step 6's shared `Button.tagPill`/`TextBlock.tagPillText`
styles instead of a locally-duplicated near-identical style block. `Badges.axaml`'s `tagPill`
tokens were adjusted to `CornerRadius="999"` (a true capsule) + `Padding="10,4"` to match
`ReadingScreen`'s pre-existing shipped look exactly, rather than inventing a new shape.

**Correction 2026-09-07, after user review of the running app:** the capsule shape (`CornerRadius
999`) below was wrong — the user's actual preference is a squircle, matching what `bandChip` (and,
it turns out, what the user wants for `tagPill` too) looked like before this pass. Both
`Badges.axaml`'s `tagPill` and `bandChip` below now use `CornerRadius="{DynamicResource PbRadius}"`
again. The "match `ReadingScreen`'s pre-existing capsule" reasoning that originally justified 999
was based on stale markup, not current user preference — corrected once seen live.

**`DetailBand.axaml`'s `bandChip` — shape-only unification, resolved on the second pass.** It's a
materially richer component than `ReadingScreen`'s tag pill: 10 style rules including weight-driven
visual hierarchy (`Classes.core`/`.defining`/`.recurrent`/`.incidental` - docs/superpowers/specs/
2026-08-23-weighted-categorized-tags-design.md), hover/focus states, brush/box-shadow transitions,
and a glow-ring on the non-interactive `Border.bandChip` variant - none of that is touched.
What *was* a real, fixable inconsistency: its base shape used `CornerRadius="{DynamicResource
PbRadius}"` (a rounded rect) while every other pill/chip in the app (including `tagPill` itself)
converged on a true capsule. Changed only `CornerRadius` (→ `999`) and `Padding` (→ `10,4`, matching
`tagPill`'s) on both `Border.bandChip` and `Button.bandChip` - every color, hover state, transition,
and the glow ring are untouched. This is the shape-level fix the original correction was pointing
at; it does not attempt the deeper "should weighted tags share the exact same style class as plain
tags" question, which remains a legitimate follow-up if ever wanted.
**Depends on:** Step 6
**Verify:** No behavioral test changes (interaction logic untouched, weight-class assertions if any
exist are about color/class, not shape). Manual GUI check on `ReadingScreen`'s and `DetailBand`'s
tag/chip rendering and click-to-search behavior.

## Step 16: Reclassify — drag-and-drop import → Job-tracked
**Files:** `src/Paperbunkr.App/ViewModels/ReadingScreenViewModel.cs` (edit, ~376),
`src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit, ~2245), and whatever shared
import path they call into (read `DragImportService` in full at implementation time — its exact
current signature wasn't part of this survey)
**What:** Wrap the import operation in `IActivityService.StartJob(ActivityJobKind.Import, "Importing files", ...)`,
report progress via the returned handle as files are processed, and let the job settle
(`Succeed`/`Fail`) instead of calling `_showToast(...)` directly at the end — completion then
follows the normal `ActivityToastPolicy` path from Step 10, consistent with every other tracked
operation.
**Depends on:** Step 10 (needs the `ToastRequest`-shaped `CompletionToastRequested` in place)
**Verify:** Whatever existing drag-and-drop import tests exist (find via
`grep -r "drag.*import\|DragImport" src/Paperbunkr.App.Tests/` at implementation time) must keep
asserting a completion signal — updated to assert a settled `ActivityJob` instead of a direct
toast call, since the toast is now a side effect of the job policy rather than the primary
outcome.

## Step 17: Reclassify — write-back batch → Job-tracked — DONE (resolved on the second pass)
**Files:** `src/Paperbunkr.App/Services/MetadataWriteBackQueue.cs` (edit),
`src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — pass `Activity` through),
`src/Paperbunkr.App.Tests/MetadataWriteBackQueueTests.cs` (edit)
**What:** The original deferral assumed the job had to start *before* the write loop (for
progress reporting), which is what created the 3-way toast-policy conflict. But
`MetadataFileWriteBackService.WriteAsync` exposes no progress callback either (same as the
drag-and-drop imports), so there's no live progress to lose. Moving job creation to *after*
`ReportBatch` already knows the outcome sidesteps the conflict entirely: the `singleSkipToast`
path and the true-no-op early return are both untouched (still a direct `_showToast` call and no
toast at all, respectively, exactly as before); only the general-summary path (~212) constructs
`_activity.StartJob(ActivityJobKind.SyncMetadata, "Writing metadata to files")` and immediately
calls `job.Fail(message)`/`job.Succeed(message)` — an instant-settling job (no live progress bar,
same as the drag-import jobs), whose completion toast now follows `ActivityToastPolicy.Always`
(the default) instead of a direct call. `IActivityService` threaded through both
`MetadataWriteBackQueue` constructors as an optional trailing param (default `new ActivityService()`),
same pattern as `ReadingScreenViewModel`/`LibraryScreenViewModel`.
**Depends on:** Step 10
**Verify:** `MetadataWriteBackQueueTests.cs`'s `Drain_CoalescesByIssueId` and
`Drain_MixedBatch_SummaryReportsWroteAndSkipped` updated to assert a settled `ActivityJob` in
`activity.RecentJobs` instead of a captured toast tuple; the two Job-tracking tests pass their own
`ActivityService(a => a(), _ => { })` (synchronous dispatch) rather than the parameterless
constructor, matching the dispatch-timing lesson from the Step 16 regression pass.

## Step 18: Reclassify — plugin scan issues → Alert — CORRECTED TO "NO CHANGE" DURING IMPLEMENTATION
**Files:** none
**What:** Reading the actual code changed the call: `PluginCommandRowViewModel.cs`'s manual "Run"
button (~line 92, `_host.ShowToast(Name, "Found {count} issue(s)...")`) fires from a
user-initiated, foreground button click — the user is looking directly at the row they just
clicked. `PluginScanAlertService.cs:69` (the actual precedent cited for this reclassification) is
a fundamentally different trigger: an *automatic* background re-check that runs after a scan/
import completes, with no guarantee the user is watching. The taxonomy (design doc §1) explicitly
turns on exactly that distinction ("would losing this because the user wasn't looking be bad").
Two superficially similar "plugin found N results" messages are not the same case once the
trigger context is accounted for — this was a shallow-read misclassification made during
brainstorming, before the actual code was in front of me. **Left as Toast, unchanged.**
**Depends on:** n/a
**Verify:** n/a — no code changed.

## Step 19: Reclassify — folder-watch new comics → Alert
**Files:** `src/Paperbunkr.App/Services/LiveFolderWatchService.cs` (edit, ~268)
**What:** Change the `_showToast(...)` call to `RaiseAlert`, matching its own sibling events in
`MainViewModel.cs:180,203` (missing/reconnected files) which already use `RaiseAlert` for the same
watcher.
**Depends on:** none
**Verify:** Locate and update whatever test covers `LiveFolderWatchService`'s new-comics detection
path to assert an alert instead of a toast.

**Regression pass results (Steps 1-16, 18-19 combined):** 447/447 targeted tests green after
fixing 3 real bugs the pass caught:
- `OverlayShell`'s `IsOpenProperty.Changed` handler only fires on an actual transition, so a
  freshly constructed shell (`IsOpen` defaults to `false`) kept Avalonia's own `Visual.IsVisible`
  default of `true` — every overlay would have rendered open on first paint. Fixed with a
  constructor that syncs `IsVisible = IsOpen` once at construction.
- The two Job-tracked drag-and-drop import tests used `new ActivityService()` (parameterless),
  whose default dispatch posts through `Dispatcher.UIThread` rather than running synchronously
  outside a real UI-thread context — `RecentJobs` was still empty when the test asserted right
  after `await`. Fixed by matching the established test convention
  (`new ActivityService(a => a(), _ => { })`, synchronous dispatch) already used elsewhere in this
  codebase's tests.
- `ToastRequestTests` used `new MainViewModel()` with no database isolation, which hit the real
  per-user `%APPDATA%\Paperbunkr\paperbunkr.db` — which turned out to be genuinely corrupted
  ("database disk image is malformed"), unrelated to this work. Fixed the test itself by adding
  the same `PaperbunkrDbContext.DatabasePathOverride` temp-SQLite-file isolation
  `MainViewModelTests` already uses. **The real database corruption is a separate, pre-existing
  environment issue this session surfaced but did not cause or fix** — flagged to the user
  separately; `%APPDATA%\Paperbunkr\backups\` has auto-backups per existing project convention.

## Step 20: Full regression pass
**Files:** none (verification only)
**What:** Run the full `Paperbunkr.App.Tests` and `Paperbunkr.Data.Tests` suites (targeted
`--filter` subsets per this project's known headless-flake convention, not the whole suite at
once). Then a manual GUI pass per the design doc's own testing section: click through all 17
migrated overlays, both `ConfirmDialog` shapes (plugin `AskQuestion` path and — once it exists —
the Library Health path), every `StatusBadge`/`CountPill`/`TagPill` surface, and the update-ready
toast flow.
**Depends on:** Steps 1–19
**Verify:** All targeted test suites green; manual GUI checklist above completed and reported
honestly (per this project's convention, don't claim GUI-verified without actually having clicked
through it).

---

**Sequencing note:** Steps 1, 4, 5, 6, and 9 are independent "new file" steps — no ordering
constraint between them. Steps 18 and 19 are independent of the entire toast-template branch
(9–11) since they're a direct `ShowToast`→`RaiseAlert` swap using an API that already exists
today. Steps 7 and 8 are deliberately sequential (smoke-test 3 before touching the other 14) per
the design doc's own risk-staging recommendation for `MainWindow.axaml`.
