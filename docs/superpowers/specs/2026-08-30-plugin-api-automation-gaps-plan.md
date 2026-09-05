# Plugin API Automation Gaps — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-30-plugin-api-automation-gaps-design.md*

Working in worktree `C:\Users\DeeDee\PaperBunkr-plugin-automation`, branch `plugin-api-gap-closure`
off `master`.

Survey confirmed: `PaperbunkrApplication`/`PaperbunkrBrowser` have exactly one construction call
site each (`PluginHostService.Initialize`); `MainViewModel` already has a `GoNewIssuePropertiesForPlaceholder(int issueId, int seriesId, bool deleteIfUnedited)` doing the exact
overlay-open-with-delete-on-cancel flow gap 1 needs, and an existing `OpenReaderForPlugin`/
`IsIssueOpenInReaderForPlugin` naming convention for plugin-facing public wrappers around private
navigation methods - gap 1's wrapper follows that same convention. `LibraryScreenViewModel.Selection`
is `TileSelectionController<IssueListRow>` with `Clear`/`SelectAll` already public. `MarkSpec.Kind`
matters for gap 2: only `SvgAsset`/`Flag` have a bundled image (`AssetPath`) to rasterize -
`LetterMark`/`Glyph`/`Text`/`None` don't, so those must also return null, not just `None`.

## Step 1: Extend `IApplication`

**Files:** `src/Paperbunkr.Plugins/Automation/IApplication.cs` (edit)

**What:** Add to the interface:
```csharp
int GetOrCreateSeriesId(string seriesName);
Issue? AddNewBook(int seriesId, bool showDialog);
byte[]? GetComicPublisherIcon(Issue issue);
byte[]? GetComicImprintIcon(Issue issue);
byte[]? GetComicAgeRatingIcon(Issue issue);
byte[]? GetComicFormatIcon(Issue issue);
IDictionary<string, string> GetComicFields();
```
Update the type's XML doc comment to reference this spec alongside the existing v2 spec reference.

**Depends on:** none.

**Verify:** `dotnet build` - interface-only change, nothing implements it yet so this alone won't
compile clean until Step 2 lands; fine to build together with Step 2 in one pass.

## Step 2: Implement the new `IApplication` members

**Files:** `src/Paperbunkr.App/Plugins/PaperbunkrApplication.cs` (edit)

**What:**
- Constructor changes from parameterless to `PaperbunkrApplication(MainViewModel main)`, storing
  `_main` (mirrors `PaperbunkrBrowser`'s existing constructor shape exactly).
- `GetOrCreateSeriesId(string seriesName)`: case-insensitive `context.Series.FirstOrDefault(s =>
  s.Name.ToLower() == seriesName.ToLower())` (SQLite `ToLower()` is the codebase's existing
  case-insensitive-match idiom - grep an existing case-insensitive series lookup to confirm exact
  phrasing before writing this); creates+saves a new `Series { Name = seriesName }` if none found;
  returns the id either way.
- `AddNewBook(int seriesId, bool showDialog)`: creates `new Issue { SeriesId = seriesId, AddedTime
  = DateTime.UtcNow }`, saves, then if `showDialog` calls `_main.OpenIssuePropertiesForPlugin(issue.Id,
  seriesId, deleteIfUnedited: true)` (new wrapper - see Step 3). Returns the created `Issue`
  (re-fetched with the same `.Include`s `GetBook` uses, so a plugin gets the same shape back) in
  both cases.
- Icon methods: resolve via `MarkResolver.Instance.ResolvePublisher(issue.Publisher)` /
  `ResolveAgeRating(issue.AgeRating)` / `ResolveFormat(issue.Format)`; for each, if
  `spec.Kind is MarkKind.SvgAsset or MarkKind.Flag` and `spec.AssetPath is { } path`, rasterize via
  `SvgMarkRenderer.Render(path, maxSize: 200, tint: null)` (200px matches `BrandMark`'s own default
  sizing scale - confirm against `BrandMark.cs` before hardcoding), encode to PNG bytes via the
  same `MemoryStream` + `Bitmap.Save(stream, new PngBitmapEncoderOptions())` pattern
  `GetComicPage` already uses; otherwise return null. Imprint: try
  `MarkResolver.Instance.ResolvePublisher(issue.Imprint)` first, fall back to
  `ResolvePublisher(issue.Publisher)` if the first resolves to `MarkKind.None`/`Text` (no real
  icon).
- `GetComicFields()`: `IssueListFieldCatalog.SortFields.ToDictionary(kv => kv.Key.ToString(), kv =>
  kv.Value.DisplayName)`.

**Depends on:** Step 1.

**Verify:** `dotnet build` succeeds (this is where Step 1's interface actually gets satisfied).

## Step 3: Plugin-facing wrapper + call-site update on `MainViewModel`/`PluginHostService`

**Files:**
- `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
- `src/Paperbunkr.App/Plugins/PluginHostService.cs` (edit)

**What:**
- `MainViewModel`: add `public void OpenIssuePropertiesForPlugin(int issueId, int seriesId, bool
  deleteIfUnedited) => GoNewIssuePropertiesForPlaceholder(issueId, seriesId, deleteIfUnedited);`
  right next to the existing `OpenReaderForPlugin`/`IsIssueOpenInReaderForPlugin` pair (~line 911),
  same naming convention.
- `PluginHostService.Initialize`: change `App = new PaperbunkrApplication(),` to
  `App = new PaperbunkrApplication(main),` (the `main` parameter is already in scope there).

**Depends on:** Step 2 (PaperbunkrApplication's new constructor shape).

**Verify:** `dotnet build`.

## Step 4: `SelectComics` for real

**Files:** `src/Paperbunkr.App/Plugins/PaperbunkrBrowser.cs` (edit)

**What:** Replace the no-op body:
```csharp
public void SelectComics(IEnumerable<Issue> books)
{
    var targetIds = books.Select(b => b.Id).ToHashSet();
    var matchingRows = _main.Library.IssueList.Rows.Where(r => targetIds.Contains(r.Id)).ToList();
    _main.Library.Selection.Clear();
    _main.Library.Selection.SelectAll(matchingRows);
    _main.GoLibraryCommand.Execute(null);
}
```
Update the class's XML doc comment - it currently says `SelectComics` is a documented no-op
because "the Library grid doesn't yet expose a selection API a plugin can drive"; that sentence is
now false and must go.

**Depends on:** none (independent of Steps 1-3 - can be done in parallel/either order).

**Verify:** `dotnet build`.

## Step 5: Tests

**Files:**
- `src/Paperbunkr.App.Tests/PaperbunkrApplicationTests.cs` (new)
- `src/Paperbunkr.App.Tests/PaperbunkrBrowserTests.cs` (new)

**What:** Both follow `PluginApiV3Tests.cs`'s exact fixture (`PaperbunkrDbContext.DatabasePathOverride`
redirected to a temp SQLite file in the constructor, `[Collection(nameof(AvaloniaTestCollection))]`,
plain `AddSeries`/`AddIssue` helpers using `PaperbunkrDb.CreateContext()`). Both need a real
`MainViewModel` instance (same DB override applies to it transparently, same as `MainViewModelTests`).

`PaperbunkrApplicationTests`:
- `GetOrCreateSeriesId_ExistingSeries_CaseInsensitive_ReturnsExistingId_NoDuplicate`
- `GetOrCreateSeriesId_UnknownName_CreatesNewSeries`
- `AddNewBook_NoDialog_CreatesFilelessIssueUnderSeries`
- `AddNewBook_ShowDialog_OpensOverlay_CancellingDeletesIt` — assert
  `vm.IsIssuePropertiesOverlayOpen` is true after the call, then `vm.IssueProperties.CancelCommand.Execute(null)`,
  then assert the issue is gone from the DB (mirrors `IssuePropertiesScreenViewModel`'s own
  `deleteIfUnedited` test coverage if any exists - check for one to mirror before writing this one
  from scratch).
- `AddNewBook_ShowDialog_SavingKeepsIt` — same setup, but edit a field and
  `SaveCommand.Execute(null)` instead of cancelling; issue persists.
- Icon methods: seed an `Issue` with a `Publisher`/`Format`/`AgeRating` known to resolve to a real
  `SvgAsset`/`Flag` mark (find one via `MarkResolverTests` if it exists, to reuse a known-good
  fixture value rather than guessing an alias) - assert non-null PNG bytes decodable via `new
  Bitmap(new MemoryStream(bytes))`. A field value that resolves to `LetterMark`/`None` returns
  null. Imprint-falls-back-to-Publisher case with an Issue whose `Imprint` doesn't resolve but
  `Publisher` does.
- `GetComicFields_KeysMatchEnumNames_ValuesMatchCatalogDisplayNames`.

`PaperbunkrBrowserTests`:
- `SelectComics_IssuesPresentInLoadedLibraryData_SelectsExactlyThose_AndNavigatesToLibrary` -
  navigate to Library first (so `IssueList.Rows` is populated), then call `SelectComics` with a
  subset, assert `Selection.SelectedIds` matches exactly and `CurrentScreen == "library"`.
- `SelectComics_IssueNotInLoadedData_IsSilentlySkipped_NotAnError`.

**Depends on:** Steps 1-4 (exercises the finished surface).

**Verify:** `dotnet test` filtered to these two new files, then the full `Paperbunkr.App.Tests`
suite for regressions (same pattern the cover-thumbnail-content-verification work just used -
expect 1-2 pre-existing flaky failures under full-suite parallel load, confirm any failure passes
in isolation before treating it as a real regression).
