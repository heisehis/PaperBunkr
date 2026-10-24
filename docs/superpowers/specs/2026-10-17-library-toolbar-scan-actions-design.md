# Library toolbar scan actions

**Date:** 2026-10-17  
**Status:** Approved for implementation by user on 2026-10-17  
**Project:** Paperbunkr  
**Feature area:** Library and Books

## Summary

Add direct toolbar access to the existing manual folder scans. The Library toolbar gains a **Scan library** action for comic folders, and the Books toolbar gains **Scan books** for EPUB/PDF folders. Keep the Library's existing **Resync series from file** action as a distinct command. Both scan actions use the existing scan implementations and Activity Center jobs, disable while their corresponding folder operation is active, and reload the relevant screen after the scan completes.

## Current behavior

- Comic folder scanning is `PreferencesScreenViewModel.ScanNowCommand`, which calls `LibraryFolderScanner.ScanAllAsync`, creates an `ActivityJobKind.LibraryScan` job, reports file and cover progress, honors Activity Center cancellation, and applies configured missing-file handling and post-scan verification.
- Novel-folder scanning is `PreferencesScreenViewModel.ScanBooksNowCommand`, which calls `BookFolderScanService.ScanAllAsync`, creates an `ActivityJobKind.BookScan` job, generates covers for added books, and honors Activity Center cancellation.
- Preferences owns the busy flags and job state for both scan types. Toolbar view models currently do not depend on Preferences.
- Library's `ArrowClockwise` toolbar button runs `ResyncSeriesFromFileCommand`; its tooltip says it resyncs each issue's series from embedded metadata. It is not a folder scan.
- Books has no manual reload action. Its toolbar row is replaced by selection actions when items are selected.
- ComicRack CE exposes a full scan as its own `Application.ScanFolders` command, routed to `StartFullScan`; the browser `RefreshInformation` action is separate.

## Goals

1. Make manual comic and novel scans reachable without opening Preferences.
2. Preserve the existing scan semantics, Activity Center progress/cancellation, scheduler reporting, cover generation, and missing-file policy.
3. Keep resync distinct from scanning.
4. Keep the displayed Library or Books data current after a scan finishes.
5. Keep Scan books accessible when the Books selection toolbar is shown.

## Non-goals

- Changing scan discovery rules, folder configuration, scheduled scans, or live folder watching.
- Adding a general-purpose screen reload action to Books.
- Combining comic and novel scans into one job or changing scan order.
- Changing the existing resync action or CE item-refresh behavior.

## Approved decisions

- Add a separate **Scan library** toolbar button; retain **Resync series from file**.
- The Library action scans only configured comic folders.
- Add **Scan books** to the Books toolbar; it scans only configured EPUB/PDF folders.
- Both buttons are disabled while the corresponding folder scan/maintenance operation is running.
- Both screens reload after their scan completes, including a scan that finds no new files.
- Scan books remains visible in both normal and selection toolbar states.
- Books gets no separate reload button.

## Design

### Command ownership and flow

Keep scan orchestration in `PreferencesScreenViewModel`, the current owner of scanner dependencies, activity jobs, cancellation, scan busy flags, scheduler reporting, and scan completion behavior. Make the existing scan operations callable by the Library and Books toolbar view models, and keep the existing Preferences buttons wired to those same operations. Do not copy the scan sequence into either screen view model.

When invoked from a toolbar, the scan operation additionally signals completion so the caller can reload its screen. The callback must run after the scan's `finally` cleanup, regardless of success, cancellation, or failure. A successful or no-change scan reloads data; a failed or cancelled scan may reload harmlessly to reflect any partial changes already persisted. Existing completion feedback remains in Activity Center.

The Library and Books screens share the same Preferences view-model instance used by the shell. `MainViewModel` supplies the scan operation and a reload callback to the screen view models. Library reload follows its existing `ReloadAfterChange`/background-load policy; Books uses `LoadFromDatabase` after completion.

### Toolbar behavior

- Add a scan icon beside the existing Library maintenance controls. Use a tooltip and `AutomationProperties.Name` that clearly say **Scan library**. Keep the existing resync icon and its label unchanged.
- Add a **Scan books** icon/button in the Books toolbar. Keep it visible while selection actions replace the normal controls. Use a tooltip/name and stable AutomationId.
- Bind each action's enabled state to the existing comic/book operation busy state, so maintenance actions that share that busy-state contract also prevent starting a scan.
- Show the existing Activity Center job for active progress and cancellation; no second progress surface or duplicate toast is introduced.

### Error and cancellation behavior

Retain the existing scan handling: cancellation is recorded as cancellation without escaping the command, failures are recorded on the Activity Center job and scheduler, and operation flags are reset in `finally`. The toolbar command must not swallow unexpected failures beyond the current command's handling. Completion reload is guaranteed via `finally`, and cannot prevent busy-state cleanup.

## Verification

- View-model tests prove the Library toolbar action invokes the comic scan operation and reloads after completion, including no-change and failure/cancellation completion paths where practical.
- Books view-model tests prove the book scan action invokes the book scan operation and reloads after completion.
- View/XAML tests verify toolbar command bindings, busy-state enabled bindings, automation names/IDs, and that the Books scan action is not hidden by selection mode.
- Existing Preferences scan tests remain green to prove its buttons still use the same command path.
- Inspect Avalonia styling/accessibility against the existing browse-button and Books toolbar patterns; retain token-based theme brushes and meaningful accessible names.

## Relevant implementation surfaces

- `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs`
- `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs`
- `src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs`
- `src/Paperbunkr.App/ViewModels/MainViewModel.cs`
- `src/Paperbunkr.App/Views/LibraryToolbar.axaml`
- `src/Paperbunkr.App/Views/BooksScreen.axaml`
- Corresponding App view-model and toolbar tests

## Self-review

- **Scope:** One focused feature across the two existing scan entry points and their toolbar bindings.
- **Consistency:** Scan and resync stay separate, consistent with CE and current Paperbunkr tooltips.
- **Failure behavior:** Existing Activity Center and scheduler handling remains centralized; screen reload is in a completion path.
- **Accessibility/theme:** New icon actions require explicit names, stable automation IDs, tooltips, and existing dynamic theme tokens.
