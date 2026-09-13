# Cluster Library Manager — Scraper UI Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-design.md*

## Step 1: Host capability — persistent modal header
**Files:**
- `src/Paperbunkr.App/ViewModels/NativePluginModalHostViewModel.cs` (edit)
- `src/Paperbunkr.Plugins.Abstractions.Ui/INativePluginUiEnvironment.cs` (edit)
- `src/Paperbunkr.App/Plugins/PaperbunkrNativePluginEnvironment.cs` (edit)
- `src/Paperbunkr.App/Views/MainWindow.axaml` (edit, around line 945-958)
- `plugins/ClusterLibraryManager/ClusterLibraryManager.Tests/FakeNativePluginEnvironment.cs` (edit)

**What:** Add `HeaderContent` (`Control?`, `[ObservableProperty]`) to `NativePluginModalHostViewModel`.
Add `internal IDisposable BeginBatch(Control header)`: sets `HeaderContent`, sets a private
`_batchActive = true`; returns a disposable whose `Dispose()` sets `HeaderContent = null`,
`_batchActive = false`, and if `_current is null` also sets `IsOpen = false`. Change `Advance()`'s
final block so `IsOpen = false; HostedContent = null;` only run when `_batchActive` is false (still
dequeue from `_queue` unconditionally as today). Add `BeginModalBatch(Control header) -> IDisposable`
to `INativePluginUiEnvironment`, delegating in `PaperbunkrNativePluginEnvironment` to
`_modalHost.BeginBatch(header)`. Update `MainWindow.axaml`'s `OverlayShell` content `Grid`
(currently just the `ContentControl` + close button) to add a `ContentControl` bound to
`NativePluginModalHost.HeaderContent` above the existing one, only visible when non-null. Add a
matching throwing/no-op member to `FakeNativePluginEnvironment` for the test double.
**Depends on:** none
**Verify:** new unit tests in `src/Paperbunkr.App.Tests/` (find or create
`NativePluginModalHostViewModelTests.cs`) — `IsOpen` stays true and `HeaderContent` persists across
two sequential `ShowAsync` calls inside a batch; disposing an idle batch closes the shell; disposing
a batch with a pending queued modal does not close it.

## Step 2: Remote cover image loader (plugin-local)
**Files:** `plugins/ClusterLibraryManager/ComicVine/RemoteCoverImageLoader.cs` (new)
**What:** `internal sealed class RemoteCoverImageLoader` — `Task<Bitmap?> LoadAsync(string? url,
CancellationToken)`. Null/empty url returns null immediately (caller renders its own placeholder).
In-memory `ConcurrentDictionary<string, Task<Bitmap?>>` cache keyed by URL, same single-flight
pattern `AsyncCoverImage.cs` uses (`s_inflight`), decoding via a shared `static readonly HttpClient`
(same `TrackerHttpClients`-style static-readonly idiom the design doc §2 already establishes for
this plugin) and `new Bitmap(stream)` off the calling thread (caller is responsible for being
off-UI-thread or marshaling the result back, matching `AsyncCoverImage`'s own division of labor). A
failed fetch/decode caches a completed `null` result (don't retry a broken URL every rebind).
**Depends on:** none
**Verify:** new `plugins/ClusterLibraryManager/ClusterLibraryManager.Tests/RemoteCoverImageLoaderTests.cs`
against a fake `HttpMessageHandler` (same fixture style as `ComicVineServiceTests.cs`) — a real tiny
PNG byte fixture decodes to a non-null `Bitmap`; a 404/error response and a null/empty url both
resolve to null without throwing; two concurrent calls for the same URL issue one HTTP request.

## Step 3: `ConfirmIssueMatch` setting
**Files:**
- `plugins/ClusterLibraryManager/Settings/PluginSettings.cs` (edit)
- `plugins/ClusterLibraryManager/Settings/SettingsViewModel.cs` (edit)
- `plugins/ClusterLibraryManager/Settings/SettingsView.axaml` (edit)
- `plugins/ClusterLibraryManager/ClusterLibraryManager.Tests/SettingsViewModelTests.cs` (edit)

**What:** `PluginSettings.ConfirmIssueMatch` (bool, default `true`, doc comment referencing design
doc §3/CE's `confirm_issue_b`). `SettingsViewModel`: new `[ObservableProperty] bool
_confirmIssueMatch`, loaded in the constructor, written in `Save()`. `SettingsView.axaml`: new
`CheckBox` "Confirm the matched issue before applying (uncheck to auto-apply silently)" next to the
existing `AutoChooseTopMatch` checkbox in the "Apply behavior" section.
**Depends on:** none
**Verify:** extend `SettingsViewModelTests.cs` with a round-trip test (set `ConfirmIssueMatch =
false`, `Save()`, reload via a fresh `SettingsViewModel`, assert it stayed `false`) following
whatever round-trip pattern that file already uses for `AutoChooseTopMatch`.

## Step 4: Redesign the series-match dialog
**Files:**
- `plugins/ClusterLibraryManager/Dialogs/ComicVineMatchReviewDialogViewModel.cs` (edit)
- `plugins/ClusterLibraryManager/Dialogs/ComicVineMatchReviewDialogView.axaml` (edit)
- `plugins/ClusterLibraryManager/ClusterLibraryManager.Tests/DialogViewModelTests.cs` (edit)

**What:**
- `ComicVineMatchCandidateViewModel`: `ChooseCommand` renamed `SelectCommand`, no longer calls
  `_choose` directly — instead sets a `IsSelected` bool the dialog VM manages (single-selection: the
  VM's `Select(candidate)` clears every other candidate's `IsSelected`). Add `CoverImageUrl` passthrough
  and `IssueCountLabel` (`"{CountOfIssues} issues"` or empty when null).
- `ComicVineMatchReviewDialogViewModel`: new `[ObservableProperty] ComicVineMatchCandidateViewModel?
  _selectedCandidate`; `SetCandidates` splits `Candidates` (unchanged, still score-ordered) — the view
  renders index 0 under a "Best match" header and the rest under "Other results" via an `IValueConverter`
  or a computed `IsTopMatch` bool set at construction time (simplest: set `IsTopMatch` on element 0 of
  the already-sorted list in `SetCandidates`, `false` on the rest — no new sort). New `[RelayCommand]
  Confirm()` (enabled only when `SelectedCandidate is not null`) calls `_resolve(SelectedCandidate.Volume)`.
  `Skip()` unchanged. New `ShowIssuesCommand` — placeholder for Step 5, exposed as an `Action` the
  caller (dialog view's code-behind or the plugin wiring in Step 6) supplies; keep it a plain
  constructor-injected `Func<ComicVineVolumeSearchResult, Task>?` so this VM doesn't need to know
  about the issue dialog's own type.
- `ComicVineMatchReviewDialogView.axaml`: rebuilt per design doc §2 — toolbar (source `ComboBox` with
  one hardcoded "ComicVine" item, search `TextBox`+Search `Button`, sort `ComboBox` with one
  hardcoded "Best match" item), two-column body (candidate list with "Best match"/"Other results"
  section headers via `Classes.topMatch` styling or a grouped `ItemsControl`, cover-preview pane bound
  to `SelectedCandidate` rendering via a new `RemoteCoverImage` attached-property/control (Step 2's
  loader) with a placeholder), OK/Skip/Show-issues buttons. Width 640 (was 440).
**Depends on:** Step 2 (cover rendering), Step 6 (Show-issues wiring uses the issue dialog type, but
the VM itself only needs a delegate — view/plugin wiring for the real delegate lands in Step 6).
**Verify:** rewrite the existing `ComicVineMatchReviewDialog_choosing_a_candidate_resolves_with_that_volume`
test into two: selecting a candidate does NOT resolve, `Confirm()` after selecting does; keep the
score-ordering and skip/search tests, updating `ChooseCommand` references to `SelectCommand`+`Confirm()`.

## Step 5: Issue-match dialog (new)
**Files:**
- `plugins/ClusterLibraryManager/Dialogs/ComicVineIssueReviewDialogViewModel.cs` (new)
- `plugins/ClusterLibraryManager/Dialogs/ComicVineIssueReviewDialogView.axaml` (new)
- `plugins/ClusterLibraryManager/Dialogs/ComicVineIssueReviewDialogView.axaml.cs` (new — CLAUDE.md's
  AVLN2000 gotcha: this file must be added in the same step as the `.axaml`, not deferred)
- `plugins/ClusterLibraryManager/ClusterLibraryManager.Tests/DialogViewModelTests.cs` (edit)

**What:** Mirrors Step 4's list+cover-pane shape, no toolbar, over `ComicVineIssueSummary` rows.
Constructor: `(string bookLabel, IReadOnlyList<ComicVineIssueSummary> issues, ComicVineIssueSummary?
preSelected, bool readOnlyPeek, Action<ComicVineIssueSummary?>? resolve)`. `readOnlyPeek = true`
(the series dialog's "Show issues" link) shows only a `Close` button and `resolve` is never invoked
(pass a no-op). `readOnlyPeek = false` shows OK (resolves with `SelectedIssue`) / Skip (resolves
`null`) / Go Back (resolves a sentinel the caller distinguishes from Skip — simplest:
`Action<ComicVineIssueSummary?, bool goBack>` or a small `ComicVineIssueReviewResult` record with
`Issue`/`Skipped`/`WentBack`). Pre-selects `preSelected` in the candidate list the same way Step 4's
`SelectedCandidate` works (reuse the same select-single-item pattern, don't duplicate it — consider
extracting a tiny shared `SingleSelectionList<T>` helper if the duplication is more than a few lines).
**Depends on:** Step 2 (cover rendering), Step 4 (shared selection pattern to mirror/extract).
**Verify:** new tests alongside Step 4's in `DialogViewModelTests.cs` — pre-selected issue is marked
selected on construction; OK resolves with the selected issue; Skip resolves with "skipped"; Go Back
resolves with "went back"; peek mode's Close never invokes a real resolve action.

## Step 6: Orchestrator — progress callback + issue-confirm gate
**Files:**
- `plugins/ClusterLibraryManager/ComicVine/ComicVineScrapeOrchestrator.cs` (edit)
- `plugins/ClusterLibraryManager/ClusterLibraryManager.Tests/ComicVineScrapeOrchestratorTests.cs` (edit)

**What:** `ScrapeAsync` gains two new parameters (both nullable, both default via overload or named
optional args to avoid breaking every existing call site more than necessary):
`Action<int total, int index, Issue current>? onProgress` (invoked once per book at loop top, before
`SearchAndRank`) and a new interactive issue-review delegate,
`Func<string bookLabel, ComicVineVolumeSearchResult volume, IReadOnlyList<ComicVineIssueSummary>
issues, ComicVineIssueSummary? autoMatched, CancellationToken, Task<ComicVineIssueReviewResult>>?
interactiveIssueReview`. In `ApplyAsync`'s flow: `FindIssueDetailsAsync`'s existing number-matching
logic is refactored into two parts — `FindAutoMatchedIssueSummaryAsync(volumeId, bookNumber, ct) ->
ComicVineIssueSummary?` (returns the summary, not yet the full details) and a separate
`GetIssueDetailsAsync` call once the final chosen issue (auto or user-picked) is known. When
`isInteractive && _settings.ConfirmIssueMatch && interactiveIssueReview is not null`: fetch the full
issue list once (`SearchIssuesAsync` paged, same pagination as today), find the auto-match summary
from it, call `interactiveIssueReview`; a "went back" result re-invokes the series-level
`interactiveReview` for the same book (loop restructured slightly — extract the per-book body into a
local function so "go back" can re-enter it); "skip" proceeds with volume-level fields only (no
`ApplyIssueDetails` call); otherwise fetch `GetIssueDetailsAsync` for whichever issue was resolved
(auto or user-chosen) and apply as today. When `ConfirmIssueMatch` is false or non-interactive,
behavior is byte-for-byte what `FindIssueDetailsAsync` does today — no behavior change for existing
callers/tests that don't pass the new delegate.
**Depends on:** Step 5 (for `ComicVineIssueReviewResult`'s shape, defined in Step 5 — move it to
`ComicVineDtos.cs` or a new small file both the dialog and orchestrator reference, since the
orchestrator can't depend on `Dialogs/`).
**Verify:** extend `ComicVineScrapeOrchestratorTests.cs` — `onProgress` fires once per book with
correct `(total, index)` even when a book resolves through two dialog steps; `ConfirmIssueMatch =
true` with an interactive callback routes through it and applies whatever issue it returns;
`ConfirmIssueMatch = false` (or the callback null) reproduces every existing per-issue test in that
file unchanged (regression guard — these tests currently pass no such delegate at all, confirm they
still pass once the parameter exists with its default).

## Step 7: Batch-progress header view
**Files:**
- `plugins/ClusterLibraryManager/Dialogs/ScrapeBatchHeaderViewModel.cs` (new)
- `plugins/ClusterLibraryManager/Dialogs/ScrapeBatchHeaderView.axaml` (new)
- `plugins/ClusterLibraryManager/Dialogs/ScrapeBatchHeaderView.axaml.cs` (new)
- `plugins/ClusterLibraryManager/ClusterLibraryManager.Tests/DialogViewModelTests.cs` or a new
  `ScrapeBatchHeaderViewModelTests.cs` (edit/new)

**What:** `ScrapeBatchHeaderViewModel(int total, Action onCancel)` with `[ObservableProperty] int
_currentIndex`, `_currentBookLabel` (string), `_currentCoverBytes` (`byte[]?`, or decode to `Bitmap?`
directly here since `Avalonia.Media.Imaging` is available to the plugin), a computed
`ProgressLabel => $"Book {CurrentIndex} of {Total}"`, and `[RelayCommand] Cancel() => onCancel()`.
View: small horizontal strip — cover thumbnail (fixed ~48x72, decoded bitmap or placeholder),
label + progress bar (`Value = CurrentIndex, Maximum = Total`), Cancel button right-aligned.
**Depends on:** none (independent of Steps 4-6, can be built in parallel).
**Verify:** unit test — `ProgressLabel` reflects `CurrentIndex`/`Total`; `CancelCommand` invokes the
supplied `onCancel` exactly once.

## Step 8: Wire it all together in the plugin
**Files:** `plugins/ClusterLibraryManager/OrganizerScraperPlugin.cs` (edit)

**What:** `ScrapeSelectedBooksAsync`: when `isInteractive`, construct a `CancellationTokenSource`,
construct `ScrapeBatchHeaderViewModel(books.Count, cts.Cancel)`, call
`uiEnvironment.BeginModalBatch(new ScrapeBatchHeaderView { DataContext = headerVm })`, hold the
returned `IDisposable` in a `using`/`try-finally` around the `orchestrator.ScrapeAsync(...)` call
(passing `cts.Token` as the orchestrator's `cancellationToken` — currently it's not threaded from
here at all; check the current call site and wire it if missing), pass `onProgress: (total, index,
issue) => { headerVm.Total = total; headerVm.CurrentIndex = index; headerVm.CurrentBookLabel =
BookLabel(issue); headerVm.CurrentCoverBytes = environment.App.GetComicThumbnail(issue); }`, and pass
a new `ShowIssueReviewDialogAsync` method (mirroring the existing `ShowMatchReviewDialogAsync`
pattern) as `interactiveIssueReview`. Update `ShowMatchReviewDialogAsync`'s constructed
`ComicVineMatchReviewDialogViewModel` to also receive the "Show issues" delegate (opens
`ComicVineIssueReviewDialogView` in peek mode via `uiEnvironment.ShowModalAsync`, reusing
`environment.App`/`_comicVine`... actually the peek needs a `ComicVineService`/issue list fetch,
which the dialog VM doesn't have — pass a `Func<Task<IReadOnlyList<ComicVineIssueSummary>>>` closure
built in `OrganizerScraperPlugin` that calls the same `SearchIssuesAsync` pagination, or simpler:
thread the already-fetched issue list through if Step 6's flow fetches it before showing the series
dialog — **decide the simpler shape while implementing this step**, since it's the one place the
plan can't fully pin down without touching Step 6's exact refactor shape first).
**Depends on:** Steps 1, 4, 5, 6, 7.
**Verify:** extend `OrganizerScraperPluginTests.cs` (existing file, read its current shape before
editing) — a scrape run calls `BeginModalBatch` exactly once for the whole run, not once per book;
Cancel on the header actually stops the batch after the in-flight book.

## Step 9: Full-suite verification
**What:** `dotnet build` both `plugins/ClusterLibraryManager/ClusterLibraryManager.csproj` and its
test project; `dotnet test plugins/ClusterLibraryManager/ClusterLibraryManager.Tests/`; `dotnet
build src/Paperbunkr.App/Paperbunkr.App.csproj` per CLAUDE.md's AVLN2000 gotcha (delete
`obj/Debug/net10.0/*.dll`/`.pdb` first if any XAML compile step fails, to avoid the stale-assembly
trap); run `src/Paperbunkr.App.Tests` for the `NativePluginModalHostViewModel` tests from Step 1.
No on-screen/FlaUI verification planned this pass per the design doc §6, but a manual launch to
confirm the redesigned dialog actually renders (covers load, OK/Skip/Confirm work) is worth doing
given this is a UI-visible change — flag to the user as a recommended manual check even though it's
not automated.
**Depends on:** all prior steps.
