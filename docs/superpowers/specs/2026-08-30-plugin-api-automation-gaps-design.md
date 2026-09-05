# Plugin API Automation Gaps — Design

2026-08-30

## Background

Plugin API v2 (2026-08-24) ported CE's `ComicRack.Plugins.Automation` namespace
(`IApplication`/`IBrowser`/`IOpenBooksManager`) as real adapters
(`PaperbunkrApplication`/`PaperbunkrBrowser`/`PaperbunkrOpenBooksManager`), and v3 (2026-08-28)
added a metadata/rules/writer layer CE never had. Diffing the shipped interfaces against CE's
actual `MainForm.cs` implementations turned up three gaps: one the v2 spec's own mapping table
promised ("`AddNewBook`/`RemoveBook` → EF CRUD") but never actually shipped, one silently dropped
with no mention in either spec, and one shipped as a documented no-op whose stated blocker
(no Library selection API) is no longer true — Library multiselect landed the same week.

This closes all three. A fourth, larger gap — CE's plugins were IronPython scripts
(`PythonCommand`); Paperbunkr's are C# `.csx` scripts (`CSharpCommand`), so no existing CE plugin
script is directly portable — is explicitly out of scope here and will get its own spec once this
lands.

## Goal

Bring `IApplication`/`IBrowser` back in line with what CE's plugins could actually do, adapted to
Paperbunkr's real constraints (Issues always belong to a Series; Library's selection model exists
now and can be driven for real).

## Gap 1 — `AddNewBook`

CE's `MainForm.AddNewBook(bool showDialog)` creates a brand-new fileless `ComicBook`, optionally
showing the properties dialog first so the caller can fill in fields before it commits to the
database; declining the dialog aborts the add entirely.

Paperbunkr's `Issue` always has a `SeriesId` (CE's doesn't require one), so the signature adapts:

```csharp
// IApplication
int GetOrCreateSeriesId(string seriesName);
Issue? AddNewBook(int seriesId, bool showDialog);
```

- **`GetOrCreateSeriesId`** — case-insensitive match against `Series.Name`; creates a new `Series`
  row if nothing matches, returns its id either way. Without this a plugin has no way to target a
  series that doesn't exist yet, which would make `AddNewBook` useless for the "import a new
  series via script" case. `GetLibraryBooks()`'s existing `.Include(i => i.Series)` already lets a
  plugin discover *existing* series ids from books it already has; this closes the "brand new
  series" gap.
- **`AddNewBook(seriesId, showDialog: false)`** — creates a fileless `Issue` under `seriesId`
  (`AddedTime = DateTime.UtcNow`, no `FilePath`) and returns it immediately. Mirrors CE's
  fileless-placeholder support (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-
  entries-design.md) — Paperbunkr already treats a no-file `Issue` as a first-class, valid state.
- **`AddNewBook(seriesId, showDialog: true)`** — creates the same fileless `Issue`, then drives the
  exact flow the Library screen's own "Add Issue" panel already uses: sets
  `MainViewModel.IsIssuePropertiesOverlayOpen = true` and calls
  `IssueProperties.Load(newIssueId, deleteIfUnedited: true)`. That `deleteIfUnedited` flag already
  exists and already does exactly what CE's "declining the dialog aborts the add" needs — the
  overlay's existing `Cancel` command deletes the row it created if nothing was edited, no new
  delete-on-cancel logic to write. Returns the `Issue` regardless (caller can check
  `IsIssuePropertiesOverlayOpen` if it needs to know the dialog is still open) — a plugin awaiting
  a modal result isn't a pattern the rest of this API supports (`AskQuestion` is the one exception,
  genuinely modal/blocking by design), so `showDialog: true` is fire-and-forget from the plugin's
  perspective, same as `ShowComicInfo` already is.

`RemoveBook` already exists and needs no change.

## Gap 2 — Icon methods

Four CE methods resolve a comic's publisher/imprint/age-rating/format to an icon `Bitmap`. None
exist in Paperbunkr's port. Adding all four plus the un-ported `GetComicFields`:

```csharp
// IApplication
byte[]? GetComicPublisherIcon(Issue issue);
byte[]? GetComicImprintIcon(Issue issue);
byte[]? GetComicAgeRatingIcon(Issue issue);
byte[]? GetComicFormatIcon(Issue issue);
IDictionary<string, string> GetComicFields();
```

`byte[]?` PNG, not `Bitmap` — matches the existing convention `GetComicThumbnail`/`GetComicPage`
already established (a plugin script has no business holding an Avalonia `Bitmap` alive across the
process boundary a `.csx` script effectively is).

- **Publisher/AgeRating/Format** — resolve through the real `MarkResolver.Instance`
  (`ResolvePublisher`/`ResolveAgeRating`/`ResolveFormat`) to a `MarkSpec`, rasterize via the
  existing `SvgMarkRenderer.Render(spec.AssetPath, maxSize, tint)`, encode the result to PNG bytes
  (same `Bitmap.Save(stream, new PngBitmapEncoderOptions())` pattern `PaperbunkrApplication.GetComicPage`
  already uses). Null when the resolver has nothing to show, same as CE returning a blank/default
  icon in that case but adapted to this API's existing null-means-nothing convention.
- **Imprint** — mirrors CE's own fallback exactly (`PublisherIcons.GetImage(imprintKey) ??
  PublisherIcons.GetImage(publisher)`): resolve `Issue.Imprint` through `ResolvePublisher` first,
  fall back to `Issue.Publisher` if that resolves to nothing.
- **`GetComicFields`** — not comic-specific despite the name (CE's own signature takes no
  parameter) — it's a static field-name→label catalog a script UI would use to build a field
  picker. Projects the existing `IssueListFieldCatalog.SortFields` (already backs the Library's
  configurable Details columns) into `{ field.ToString(): descriptor.DisplayName }` — no new
  catalog, just exposing the one that exists.

## Gap 3 — `SelectComics`

Currently a documented no-op in `PaperbunkrBrowser`. Becomes real:

```csharp
public void SelectComics(IEnumerable<Issue> books)
```

Resolves each passed `Issue.Id` against `LibraryScreenViewModel.IssueList`'s currently-loaded
`IssueListRow`s (only issues actually present in whatever Library already has loaded get
selected — this does not clear an active search/filter to force everything into view, matching
CE's own behavior of only ever operating on the currently-visible list), clears
`LibraryScreenViewModel.Selection`, then `Selection.SelectAll(...)` with the resolved rows.
Switches `MainViewModel.CurrentScreen` to `"library"` first if not already there, so the selection
is immediately visible — a script driving the UI should show its work, not select something
invisibly on a screen nobody's looking at.

## Testing

- `PaperbunkrApplicationTests` (new or extended, mirrors the real-adapter test style already used
  for `PaperbunkrApplication`/`PaperbunkrBrowser` if such tests exist, otherwise a new file
  following `IssuePropertiesScreenViewModelTests`' fixture conventions):
  - `GetOrCreateSeriesId` — existing series (case-insensitive) returns its id without creating a
    duplicate; unknown name creates a new `Series` and returns its id.
  - `AddNewBook(seriesId, showDialog: false)` — creates a fileless `Issue` under that series,
    `FilePath` null, `AddedTime` set.
  - `AddNewBook(seriesId, showDialog: true)` — opens the Issue Properties overlay for the new
    issue; cancelling deletes it; saving keeps it.
  - Icon methods — a `MarkResolver`-resolvable publisher/format/age-rating returns non-null PNG
    bytes decodable back into a `Bitmap`; an unresolvable one returns null. Imprint falls back to
    Publisher when Imprint alone doesn't resolve.
  - `GetComicFields` — returned dictionary's keys match `IssueListSortField` enum names, values
    match `IssueListFieldCatalog.SortFields`' `DisplayName`s.
- `PaperbunkrBrowserTests` (same convention):
  - `SelectComics` — passing a set of `Issue`s present in Library's loaded data selects exactly
    those rows and switches `CurrentScreen` to `"library"`; an `Issue` not in the currently-loaded
    set is silently skipped, not an error.

## Out of scope

- Python/IronPython script compatibility (gap 4) — its own spec, next.
- Any change to `IComicDisplay`'s deliberate ~30→6 member scope-down (v2 spec §4) — that was a
  reasoned cut against CE's now-irrelevant GDI+ surface, not an oversight, and stays as-is.
- `IOpenBooksManager`'s dropped `inNewSlot` (v2 spec §4) — Paperbunkr is genuinely single-screen;
  nothing to restore.
