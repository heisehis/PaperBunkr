# Reading Lists — Design Spec

*Date: 2026-08-06. Scope: wire the Reading Lists screen (`ReadingScreenViewModel`/`ReadingScreen.axaml`) and its sidebar to real, persisted data. Currently both are hardcoded sample content (`ReadingArcSample` in-memory data; sidebar entries hand-written in `MainWindow.axaml`).*

**Verification practice (per the standing CE-verification rule):** before designing, checked ComicRackCE's actual reading-list source (`_reference/ComicRackCE/ComicRack.Engine/Database/ComicReadingListContainer.cs`, `ComicReadingListItem.cs`, `ComicIdListItem.cs`) — CE's real "reading list" is a CBL-format ordered list of book match-keys (Series/Number/Volume/Year/Format/FileName), resolved against the live library at import time via `ComicIdListItem.CreateFromReadingList`; unmatched items either get a placeholder book created or get dropped, and afterward the list is just an ordered set of real book references, not a dangling text reference.

Also checked the **CBL Manager plugin** (`https://github.com/heisehis/CBLManager`, cloned to `_reference/CBLManager`), referenced by name in docs/onboarding.md §11 but not previously inspected in this environment. It's a substantial, real, multi-phase project (see its own `docs/action-plan.md`) covering local CBL import/export, an in-app list builder, plain-text export, **and** a full external story-arc lookup system (6 source adapters: ComicVine, Metron, and four scraped guide sites) with refresh/reconciliation and a live cover/synopsis panel. That external-lookup half is genuinely its own project, comparable in scope to the AniList/MyAnimeList integration already deferred from Smart Lists — **explicitly deferred here too**, to a separate future design pass. This spec covers the local-only half.

## 1. Scope

Covers:
- Reading lists as ordered collections of real `Issue` references (never dangling text), matching CE's actual post-import model.
- Manual list building: search/filter/multi-select from the library, reorder, save (CBL Manager's `ListBuilderForm`/`CreateReadingList`).
- CBL import/export, using the already-ported `ComicReadingListContainer`/`ComicReadingListItem` (`Paperbunkr.Engine/Database` — ported during the retarget spike, dormant/unused until now).
- CSV import — Paperbunkr-defined format (no CE precedent), symmetric with CBL's match-key fields.
- Export Reading List as Text (CBL Manager Phase 6) — numbered plain-text/Markdown list, copy/save, zero external dependency.
- Unmatched-item resolution: auto-create the `Series` (if needed) and a placeholder `Issue` (`FileIsMissing = true`, `IsPlaceholder = true`) — CBL Manager's `CreatePlaceholderBook` pattern, adapted to a real schema column instead of its custom-value-tag workaround (that workaround existed only because CE's plugin API had no free metadata slot on a book).
- Sidebar + screen UI wiring, replacing all hardcoded sample content.
- Sub-arc grouping within a list (`GroupLabel`, nullable, Paperbunkr-original — no CE or CBL Manager precedent, matches the existing wireframe).

Explicitly deferred (separate future pass, matching the CBL Manager source split):
- **External story-arc lookup** — search-by-name against ComicVine, Metron, or any of CBL Manager's four scraped sources (Comic Book Reading Orders, ReadingOrders.com, ComicArc, ReadThingsRight), auto-building a correctly-ordered list and matching it against the library. This is what the wireframe's "Auto-Build from Tracked Arc" button and the AniList/MyAnimeList buttons represent — all deferred together as one future "external reading-order sources" pass.
- **Refresh** (re-querying a source and reconciling placeholders) — meaningless without an external source to refresh against; ships alongside that pass.
- **Live cover/synopsis overview panel** — same dependency.
- **Post-to-dpaste.com sharing** — a real external service call; CBL Manager itself scoped this as a separate follow-up after plain-text export (which *is* in scope here).
- A dedicated "book collection" editor UI, per the same note in the Smart Lists spec — unrelated to this feature but worth remembering `Issue`'s book-collection fields still have no editor.

## 2. Schema

```csharp
public class ReadingList
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    // Forward-compat for the deferred external-source pass (mirrors CBL Manager's ArcLink
    // sidecar-file fields, but as real columns — Paperbunkr owns its schema, unlike a CE
    // plugin constrained to a JSON sidecar because ComicListItem had no metadata slot
    // reachable from the plugin API). All null until that pass exists.
    public string? Source { get; set; }
    public string? ArcId { get; set; }
    public string? ArcName { get; set; }
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }

    public List<ReadingListItem> Items { get; set; } = new();
}

public class ReadingListItem
{
    public int Id { get; set; }
    public int ReadingListId { get; set; }
    public ReadingList? ReadingList { get; set; }
    public int IssueId { get; set; }
    public Issue? Issue { get; set; }
    public string? GroupLabel { get; set; }   // sub-arc header; null renders ungrouped
    public int SortOrder { get; set; }
}
```

```csharp
// Issue.cs — one more addition:
public bool IsPlaceholder { get; set; }
```

`IsPlaceholder` distinguishes "auto-created to stand in for an unmatched import row" from "the user flagged their own real book `FileIsMissing` for an unrelated reason" (lost file, etc.) — CBL Manager needed exactly this distinction for its `RefreshArcList` reconciliation (reuse still-missing placeholders, delete ones a real copy has since replaced, but never touch a user's own missing-flagged book). Not load-bearing for anything in this pass's scope, but cheap to add now and directly reusable when the deferred Refresh pass lands.

## 3. Matcher

Two entry points sharing one lookup core (`Series.Name` case-insensitive + `Issue.Number` case-insensitive match, narrowed by `Volume`/`Year`/`Format` as tie-breakers when ambiguous — same shape as CE's own `ComicInfo.SeriesEquals`-based matching and CBL Manager's `ArcMatcher.FuzzyMatch`, confirmed via both sources rather than invented):
- `ReadingListMatcher.FindExisting(...) : Issue?` — read-only lookup, used by the manual "Add Issue" search (§7), which only ever offers issues already in the library.
- `ReadingListMatcher.ResolveOrCreatePlaceholder(...) : Issue` — used by CBL/CSV import; falls back to creating the `Series` (if needed) and a placeholder `Issue` per §2 when `FindExisting` comes back empty.

## 4. CBL import/export

Reuses `Paperbunkr.Engine.Database.ComicReadingListContainer`/`ComicReadingListItem` directly — real `.cbl` XML format, `XmlSerializer`-based, already ported and building cleanly, just unused until now. Import: `Deserialize` → resolve each item via the matcher → build a new `ReadingList`. Export: walk a `ReadingList`'s items → build a `ComicReadingListContainer` → `Serialize`.

## 5. CSV import

No CE or CBL Manager precedent (CBL Manager only ever did CBL). Paperbunkr-defined format, deliberately symmetric with CBL's fields for one shared matcher: header row `Series,Number,Volume,Year,Format` — only `Series,Number` required, rest optional/blank. Malformed rows are skipped and reported in a post-import summary, not fatal to the whole import.

## 6. Export Reading List as Text

Adopted from CBL Manager's Phase 6 (`ExportReadingListAsText`) essentially as-is: `# {ListName}`, issue count, then a numbered `{n}. {Series} #{Number} ({Year})` list — reads correctly as both plain text and Markdown with no special syntax, so there's no separate mode to pick. Copy-to-clipboard and save-to-file; no network call (dpaste.com posting stays deferred, §1).

## 7. UI wiring

- **Sidebar** (`MainWindow.axaml` Reading Lists section): binds to `ObservableCollection<ReadingListSummary>` (`Name`, `TotalCount`), same selectable-row pattern already built for Smart Lists' sidebar (`Button.sideItemButton`, named-element command binding). `+ New Reading List` creates a blank list.
- **`ReadingScreenViewModel.LoadReadingList(int)`/`EnsureListLoaded()`**: same convention as `SmartScreenViewModel`/`ReaderScreenViewModel`. Stats (`TotalIssues`/`OwnedIssues`/`MissingIssues`) computed from `Items`. Items rendered grouped by `GroupLabel` (a blank/default header for ungrouped items). Reorder via Up/Down buttons per row (no drag-and-drop — not requested, real added complexity for a first pass). Remove-item button. "Add Issue" control reuses the matcher's lookup half (search existing library, no placeholder creation from this path since the issue already exists).
- **Import .CBL / Import .CSV / Export .CBL / Export as Text** buttons wired to a new minimal `IFilePickerService` (Avalonia `TopLevel.StorageProvider.OpenFilePickerAsync`/`SaveFilePickerAsync`), injected into `ReadingScreenViewModel` the same way existing screens inject navigation callbacks (constructor delegate) — keeps the ViewModel free of any View/Window dependency, consistent with the rest of the codebase. First screen in the app needing a file picker.
- **AniList / MyAnimeList / "Auto-Build from Tracked Arc"** buttons stay visible but disabled, same treatment Smart Lists gave the deferred `AllProperties` search — the affordance exists, the wiring doesn't yet.

## 8. Testing

`Paperbunkr.Data.Tests`:
- Matcher resolution: exact series+number match, tie-break by volume/year/format when ambiguous, placeholder `Series`+`Issue` creation on no match (confirming `IsPlaceholder`/`FileIsMissing` both set).
- CBL round-trip: build a small `ComicReadingListContainer` via the real ported serializer, import it, confirm item order/count/placeholder creation — same "generate via the real code path, don't hand-write a fixture" precedent as `CeLibraryMigratorTests`.
- CSV parsing: valid rows, missing optional columns, malformed row handling (skipped + reported, not fatal).
- Export-as-Text: exact formatting against a small known list.
