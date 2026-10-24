# Comic and Manga Detail List Layouts — Implementation Plan

*Implements: [2026-10-18-detail-screen-list-layouts-design.md](2026-10-18-detail-screen-list-layouts-design.md)*

**Implementation status:** Source changes are complete. No build or tests were run in this session; compiled XAML and live UI behavior remain unverified.

## Step 1: Add tolerant, detail-specific layout state

**Files:** `src/Paperbunkr.Data/Entities/WorkspaceScreen.cs`; new `src/Paperbunkr.App/Models/DetailListLayoutState.cs`.

**What:** Append `ComicDetail` and `MangaDetail` screen values after the current values. `ListLayout` stores screen names as strings (max 16 characters), so the new names require no database migration. Document that these values namespace list layouts only and do not create Workspace rows. Add separate state records and field enums for Comic Poster/List/Card and Manga Chapters, plus tolerant JSON normalization for unknown fields, malformed JSON, empty visible-column sets, and bounded widths. Keep existing Library/Books `ListLayoutState` serialization unchanged.

**Depends on:** none.

**Verify:** Inspect enum names and serializers against the existing stored string conventions; no migration is expected because the existing `ListLayout.Screen` and `ListLayoutAssignment.Screen` columns already store enum names.

## Step 2: Support a shared default in the existing layout manager

**Files:** `src/Paperbunkr.App/ViewModels/ListLayoutsViewModel.cs`; `src/Paperbunkr.App/Views/EditLayoutsOverlay.axaml`; `src/Paperbunkr.App/Services/ListLayoutService.cs` only if a narrow default-write helper is needed.

**What:** Extend the list-layout host contract so detail hosts can declare a single type-wide default and supply their built-in reset state. For that mode, capture/apply/track against the `*` assignment only, make Reset restore the built-in detail state, and omit “All lists” in Edit Layouts while labeling named-layout application as setting the type default. Preserve Library/Books per-list behavior. Defer menu closure before collection or popup changes.

**Depends on:** Step 1.

**Verify:** Review each branch to ensure Library/Books retain their current `SelectionKey` and reset/set-on-all behavior; detail hosts never write series-keyed assignments.

## Step 3: Add the detail List Options editor

**Files:** new `src/Paperbunkr.App/ViewModels/DetailListOptionsViewModel.cs`; new `src/Paperbunkr.App/Views/DetailListOptionsOverlay.axaml` and `.axaml.cs`; `src/Paperbunkr.App/ViewModels/MainViewModel.cs`; `src/Paperbunkr.App/Views/MainWindow.axaml`.

**What:** Add a draft/apply/cancel editor with Poster, List, and Card field selections for Comic; ordered visible columns and width values for Comic List and Manga Chapters; and Manga sort field/direction. Use accessible labels and keyboard Move Up/Move Down controls. Wire a separate overlay through `MainViewModel` so the existing Library/Books options editor remains stable. Add its open state to modal-close handling.

**Depends on:** Step 1.

**Verify:** Review compiled-binding paths, automation names, resource tokens, and focus order. Do not add screen-specific hotkey handlers.

## Step 4: Apply layouts to Comic and Manga detail views

**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs`; `src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs`; `src/Paperbunkr.App/Models/IssueCardSample.cs`; `src/Paperbunkr.App/Models/ChapterRowSample.cs`; `src/Paperbunkr.App/Views/DetailTabs.axaml`; `src/Paperbunkr.App/Views/DetailTabs.axaml.cs`; `src/Paperbunkr.App/Views/MangaDetailScreen.axaml`; `src/Paperbunkr.App/Views/MangaDetailScreen.axaml.cs`; `src/Paperbunkr.App/ViewModels/MainViewModel.cs`.

**What:** Make the Comic detail tabs and Manga detail view hosts for their respective screen keys. Initialize the saved type default on first use, reapply it when another series is loaded, and track view-mode/field/sort changes. Render configured Comic fields in all three modes while retaining covers, keyboard focus, selection, and reader/property actions. Render Manga chapter columns from the normalized column order and widths, keep volume grouping, and leave the active chapter filter untouched by layout application. Add List Layout controls to Issues, Specials, and Chapters; Issues and Specials share the same mode and layout. Connect menu rows to the layout manager and options/editor overlays.

**Depends on:** Steps 1–3.

**Verify:** Inspect all three view paths for unchanged selection, Enter/double-click reader activation, Specials parity, chapter grouping, and no layout writes during transient filter changes. Check that popup row actions defer close/removal.

## Verification constraints

The project instructions normally call for focused tests and a UI review checklist. In this run, no tests will be added or run because the active developer instruction prohibits doing so unless the user explicitly asks for testing or verification. The existing likely follow-up test surfaces are `ListLayoutServiceTests`, `DetailTabsViewModelTests`, `MangaDetailScreenViewModelTests`, and `ListLayoutViewTests`. Implementation review will be source/diff review only; no build or on-screen verification will be claimed.
