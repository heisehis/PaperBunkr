# Library toolbar scan actions — Implementation Plan

*Implements: [2026-10-17-library-toolbar-scan-actions-design.md](2026-10-17-library-toolbar-scan-actions-design.md)*

## Step 1: Expose the existing scan operations

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs`, `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs`  
**What:** Add completion-aware methods around the existing comic and book scan commands while retaining their existing Preferences bindings. Expose read-only busy state for toolbar bindings. Run completion callbacks after operation cleanup so screens can reload consistently.  
**Depends on:** none  
**Verify:** Existing scan tests; add assertions for completion callbacks and busy-state reset.

## Step 2: Wire the Library scan action

**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs`, `src/Paperbunkr.App/ViewModels/MainViewModel.cs`, `src/Paperbunkr.App/Views/LibraryToolbar.axaml`, `src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs`  
**What:** Supply the existing comic scan operation and busy state to Library. Add an accessible Scan library toolbar button beside the maintenance controls; preserve Resync series from file. Reload Library after scan completion with its existing background policy.  
**Depends on:** Step 1  
**Verify:** Focused VM test for invocation/reload; XAML check for command, enabled binding, tooltip, automation name and ID.

## Step 3: Wire the Books scan action

**Files:** `src/Paperbunkr.App/ViewModels/BooksScreenViewModel.cs`, `src/Paperbunkr.App/ViewModels/MainViewModel.cs`, `src/Paperbunkr.App/Views/BooksScreen.axaml`, `src/Paperbunkr.App.Tests/BooksScreenViewModelTests.cs`  
**What:** Supply the existing book scan operation and busy state to Books. Add Scan books to both toolbar rows so it remains visible with a selection; reload Books after scan completion. Do not add a separate reload action.  
**Depends on:** Step 1  
**Verify:** Focused VM test for invocation/reload and XAML assertions for both toolbar states and automation metadata.

## Step 4: Verify the integrated UI and roadmap

**Files:** `docs/paperbunkr-todo.md` (manual status note)  
**What:** Run the touched test classes and the fast App test set if time allows; compile the app and confirm Avalonia XAML weaving occurred. Review the toolbar layout and interaction using the Avalonia review checklist; update the roadmap note with what actually passed.  
**Depends on:** Steps 2–3  
**Verify:** `dotnet test` targeted classes and `dotnet test src/Paperbunkr.App.Tests --filter "Speed!=Slow"`; forced rebuild when needed per repository XAML build guidance. Record any on-screen checks that could not be completed.
