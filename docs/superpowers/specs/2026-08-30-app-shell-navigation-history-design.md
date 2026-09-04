# App Shell Navigation History — Back/Forward, Breadcrumbs, Deep-linking

**Builds on:** `docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md` (Phase 2
of the UI rework, shipped 2026-08-24), which explicitly left drill-down screen transitions and any
history/breadcrumb concept out of scope. This spec is that follow-on, scoped to the shell's
navigation *history* rather than its lateral rail motion.

**Related but explicitly split out:** comprehensive keyboard operability (context menus via
keyboard, opening flyout/dropdown menus, card-to-card movement on screens built after P5) is a
distinct subsystem — different screens, different files, little code overlap with this spec — and
gets its own design/plan/implementation cycle afterward, not folded in here.

## Background

`MainViewModel.CurrentScreen` is a single string driving the whole shell — no history, no
breadcrumbs, no back/forward, no deep-linking. The only "remember where I came from" logic that
exists today is two single-slot fields, each hand-rolled for one screen:

- `_screenBeforeReader` / `GoBackFromReader()` — remembers exactly one prior screen before Reader
  opens, since Library/Home/Detail/MangaDetail/Events can all open Reader directly.
- `_screenBeforeBookReader` / `GoBackFromBookReader()` — same idea for BookReader/PdfReader,
  remembering Books or BookDetail.

Neither supports more than one level, neither supports Forward, and neither generalizes to the
other four drill-down screens (Detail, MangaDetail, BookDetail) which have no "back" concept at
all beyond their own hardcoded `GoLibrary`/`GoBooks` callbacks.

**CE precedent, checked before assuming any shape (per this project's standing rule):** CE has
`IBrowseHistory` (`CanBrowsePrevious/Next`, `BrowsePrevious/Next`), implemented by
`ComicListLibraryBrowser` as a `LinkedList<IComicBookListProvider>` cursor. Confirmed by reading the
implementation: it's scoped narrowly to *which list/query/folder node was selected in the library
browser's own tree* — a within-one-screen cursor, not a whole-app screen-to-screen stack. CE has no
breadcrumb concept and no cross-screen history anywhere else in the codebase. A full app-wide
history/breadcrumb/back-forward/deep-link system is therefore a deliberate deviation beyond CE
parity, not a port of existing CE behavior — this spec follows CE's *shape* (a cursor over past
selections) but widens its *scope* to the whole shell.

## Scope

**In scope:**
1. A generalized navigation history (list + cursor) replacing both single-slot hacks, covering all
   six drill-down screens: Detail, MangaDetail, BookDetail, Reader, BookReader, PdfReader.
2. Back/Forward commands, driven by Backspace (back only) and a best-effort trackpad two-finger
   swipe (both directions) — no visible on-screen back/forward buttons.
3. A clickable breadcrumb trail, shown only on the six drill-down screens (not the seven lateral
   rail screens, which keep today's slide-transition system unchanged).
4. Restore-on-launch: the app reopens on the last screen/entity it had open.
5. CLI-argument deep-linking (`--open series:123` etc.) so an external process can launch/target the
   app at a specific entity.

**Explicitly out of scope:**
- Full keyboard operability (menus, context menus, card navigation) — separate follow-on spec.
- Persisting the *entire* back/forward stack across a restart — only the single last screen/entity
  is restored; the stack itself starts empty each launch.
- A registered OS-level `paperbunkr://` URI scheme (would need install-time registry association and
  single-instance activation handling) — CLI args only for this pass.
- Any change to the lateral rail's own slide-transition system (Phase 2, unchanged) — rail moves
  reset the history stack rather than pushing onto it.
- Visible back/forward buttons in chrome — keyboard/gesture + breadcrumb clicks only.

## Data model

New file `src/Paperbunkr.App/Services/NavigationHistoryService.cs`, deliberately decoupled from any
screen ViewModel — it knows nothing about `DetailScreenViewModel` etc., only these shapes:

```csharp
public enum NavigationEntryKind { Series, MangaSeries, Issue, Book, BookSeries }

public sealed record NavigationEntry(string ScreenKey, NavigationEntryKind Kind, int EntityId, string Label);

public sealed class NavigationHistoryService
{
    private readonly List<NavigationEntry> _entries = new();
    private int _cursor = -1;
    public string RootScreenKey { get; private set; } = "home";

    public void ResetRoot(string railScreenKey)
    {
        _entries.Clear();
        _cursor = -1;
        RootScreenKey = railScreenKey;
    }

    public void Push(NavigationEntry entry)
    {
        // Truncate any forward entries past the cursor (same as a browser: navigating from a
        // back-ed-up position discards the abandoned forward branch), then append and advance.
        if (_cursor < _entries.Count - 1)
        {
            _entries.RemoveRange(_cursor + 1, _entries.Count - _cursor - 1);
        }
        _entries.Add(entry);
        _cursor = _entries.Count - 1;
    }

    public bool CanGoBack => _cursor >= 0;
    public bool CanGoForward => _cursor < _entries.Count - 1;

    /// <summary>Moves the cursor back one step. Returns the entry now at the cursor, or null when
    /// the cursor has moved past index 0 to the root (caller navigates to <see cref="RootScreenKey"/>).</summary>
    public NavigationEntry? Back()
    {
        if (!CanGoBack) return null;
        _cursor--;
        return _cursor >= 0 ? _entries[_cursor] : null;
    }

    public NavigationEntry? Forward()
    {
        if (!CanGoForward) return null;
        _cursor++;
        return _entries[_cursor];
    }

    /// <summary>Jumps directly to a breadcrumb segment's index (clicking a mid-trail crumb), truncating
    /// anything past it exactly like a fresh <see cref="Push"/> would on the next navigation.</summary>
    public NavigationEntry? JumpTo(int index)
    {
        _cursor = index;
        return index >= 0 ? _entries[index] : null;
    }

    public IReadOnlyList<NavigationEntry> BreadcrumbTrail => _entries.Take(_cursor + 1).ToList();
}
```

`Back()` returning `null` at cursor `-1` means "go to `RootScreenKey`" — this single mechanism
replaces both `_screenBeforeReader` and `_screenBeforeBookReader`, which only ever supported exactly
this one level.

## Wiring into `MainViewModel`

Each drill-down entry point splits into a **core** navigate (no history side effect, reusable by
Back/Forward/restore-on-launch/CLI deep-link) and the existing public method becomes a thin wrapper
that also pushes:

```csharp
private void GoDetailForSeries(int seriesId)
{
    NavigateToDetailCore(seriesId);
    _history.Push(BuildDetailEntry(seriesId));
}

private void NavigateToDetailCore(int seriesId) => CurrentScreen = LoadDetailSeries(seriesId) ? "mangaDetail" : "detail";

private NavigationEntry BuildDetailEntry(int seriesId) =>
    new(CurrentScreen, CurrentScreen == "mangaDetail" ? NavigationEntryKind.MangaSeries : NavigationEntryKind.Series,
        seriesId, (MangaDetail.Series?.Name ?? Detail.Series?.Name) ?? "Unknown");
```

Same split applies to `GoReaderForIssue`/`GoReaderForIssueInReadingList`, `GoBookDetailForBook`/
`GoBookSeriesDetailForSeries`, and `GoBookReaderForBook`/the PDF branch. Every lateral `GoX()`
(`GoHome`, `GoLibrary`, `GoBooks`, `GoSmart`, `GoReading`, `GoReadingWithList`, `GoEvents`,
`GoPreferences`, `GoLibraryFoldersPreferences`) calls `_history.ResetRoot(screenKey)` instead of
nothing — this is the "rail moves don't push, but do become the new root" behavior.

New commands on `MainViewModel`:

```csharp
[RelayCommand(CanExecute = nameof(CanNavigateBack))]
private void NavigateBack() => TryLeaveCurrentEditor(() =>
{
    var entry = _history.Back();
    if (entry is null) GoToRailScreenQuiet(_history.RootScreenKey);
    else ReplayEntry(entry);
});

[RelayCommand(CanExecute = nameof(CanNavigateForward))]
private void NavigateForward() => TryLeaveCurrentEditor(() =>
{
    var entry = _history.Forward();
    if (entry is not null) ReplayEntry(entry);
});

public bool CanNavigateBack => _history.CanGoBack;
public bool CanNavigateForward => _history.CanGoForward;
```

`ReplayEntry` is a switch over `entry.ScreenKey` calling the matching `...Core` method
(`NavigateToDetailCore`, `NavigateToReaderCore`, etc.) with `entry.EntityId`. `GoToRailScreenQuiet`
sets `CurrentScreen` to a lateral screen without re-triggering that screen's own reload-on-navigate
logic redundantly (mirrors what `GoBackFromReader`'s per-case switch already did). Both wrap through
`TryLeaveCurrentEditor` — the same unsaved-editor guard rail nav already uses — since Back/Forward
can navigate away from an open Issue/Bulk Properties overlay exactly as easily as a rail click can.
`CanNavigateBack`/`CanNavigateForward` are refreshed via `OnPropertyChanged` calls at the same points
`_history` mutates (push/reset/back/forward), so the Backspace command's `CanExecute` stays accurate.

`_screenBeforeReader`, `RememberScreenBeforeReader`, `GoBackFromReader`, `_screenBeforeBookReader`,
and `GoBackFromBookReader` are all deleted — `NavigationHistoryService` fully subsumes them. Reader's
and BookReader's/PdfReader's own "back" callbacks (currently wired to `GoBackFromReader`/
`GoBackFromBookReader` in the constructor) get rewired to `NavigateBack` directly.

## UI — breadcrumb bar

New `src/Paperbunkr.App/Views/Breadcrumb.axaml` control, shown only when
`IsDetail || IsMangaDetail || IsBookDetail || IsReader || IsBookReader || IsPdfReader` — a thin bar
at the top of the content area, above the drill-down screen's own content. Segments:

```
[RootScreenKey label] › [entry 0 label] › [entry 1 label] › ... › [entry at cursor label]
```

The root label comes from a small static lookup (`"library"` → "Library", `"home"` → "Home", etc.,
mirroring the rail's own `railLabel` text already in `MainWindow.axaml`). Each segment after the
root is clickable and calls `NavigateToBreadcrumbIndex(int index)`, which calls
`_history.JumpTo(index)` and replays that entry directly — jumping several levels at once, not one
step at a time, same as clicking a mid-trail crumb in a browser truncates forward history past it.
`Detail`/`MangaDetail`/`BookDetail`/`Reader`/`BookReader`/`PdfReader`'s own headers are unaffected —
the breadcrumb bar is a new sibling element above them, not a replacement for any existing header
text.

## Input

- **Backspace** registered as a new `NavigateBack` entry in `KeyboardCommandRegistry`
  (`src/Paperbunkr.App/Models/KeyboardCommandRegistry.cs`), remappable via the existing Preferences
  → Keyboard Shortcuts surface, in the existing Navigation section. Confirmed no existing binding
  uses bare Backspace anywhere in this codebase, so no conflict. Guarded at the handler level to
  no-op while focus is inside any `TextBox`/search field (matching how this codebase already guards
  other global-feeling shortcuts against stealing input from text entry).
- **Forward** is intentionally keyboard-less per your call — reachable only via breadcrumb click or
  a successful trackpad swipe-forward.
- **Trackpad two-finger swipe** (both directions): flagged as an implementation-time risk, not a
  locked guarantee. Avalonia has no first-class desktop "swipe" gesture API (the existing
  `PinchGestureRecognizer` used by `PageCanvas` is zoom-only, touch-oriented). On Windows, a
  precision-touchpad two-finger horizontal swipe arrives as `PointerWheelChanged` with a horizontal
  `Delta.X` — the same signal ordinary horizontal scrolling produces — so implementation needs a
  deliberate heuristic (a single large horizontal delta arriving in a short window, distinct from
  the smaller accumulated deltas of deliberate horizontal scroll) to avoid false-triggering
  navigation during normal scroll/pan. This will be implemented as a best-effort threshold on the
  shell's root content area and documented as unverified-on-real-hardware, matching this project's
  existing honest posture on untestable desktop input (no unattended GUI automation available in
  this environment).

## Persistence & restore-on-launch

Two new nullable `AppSettings` fields (`src/Paperbunkr.Data/Entities/AppSettings.cs`), same
load-in-constructor/save-on-change pattern as `NavRailPinned`:

```csharp
public string? LastScreenKey { get; set; }
public int? LastScreenEntityId { get; set; }
```

Saved on every `CurrentScreen` change (not just app exit, so a crash or force-quit doesn't lose the
last position) — the same write frequency this codebase already tolerates for e.g. Library's saved
sort/filter state, so no throttling needed.

**Startup** (`App.axaml.cs`, right after `mainViewModel` is constructed and the DB is confirmed
ready — after `PaperbunkrDb.EnsureCreated()`, alongside the existing `offerFirstRunMigration` check):
1. Parse `desktop.Args` (`IClassicDesktopStyleApplicationLifetime.Args`) for `--open <kind>:<id>` —
   see CLI deep-linking below. If present, this wins outright.
2. Otherwise, if `AppSettings.LastScreenKey` is set, call the matching `...Core` navigate method with
   `LastScreenEntityId`.
3. Either path calls `_history.ResetRoot(...)` first with a sensible default root — `"library"` for
   Reader/BookReader/PdfReader-rooted opens (matching `_screenBeforeReader`'s existing default),
   `"home"` otherwise — so Back still lands somewhere coherent even though the stack itself starts
   empty each launch (confirmed: only the last screen is restored, not the full stack).
4. If the referenced entity no longer exists (deleted since last session), fall back to `GoHome()`
   and log via `DiagnosticsService.LogMilestone` — not a crash, matching `LibraryActiveCollectionId`'s
   existing "falls back to All Series if the collection was deleted" posture.

## CLI deep-linking

`paperbunkr.exe --open <kind>:<id>`, where `<kind>` is one of `series`, `issue`, `book`,
`collection` (collection routes through the existing `GoLibraryWithCollection`, not a drill-down
screen of its own). A small pure function,
`NavigationCliArgs.TryParseOpenArg(string[] args, out NavigationCliTarget? target)`
(`src/Paperbunkr.App/Services/NavigationCliArgs.cs`, new file), parses this independent of Avalonia
so it's unit-testable without touching `App.axaml.cs`. Unrecognized `<kind>` or a malformed argument
is ignored (falls through to restore-on-launch), not a startup failure.

## Error handling

- Deleted/missing entity at restore-on-launch or CLI-open time → fallback to Home, logged via
  `DiagnosticsService.LogMilestone`, not a crash (see above).
- `NavigationHistoryService.Back()`/`Forward()` are no-ops (return null, nothing replayed) when
  `CanGoBack`/`CanGoForward` is false — `MainViewModel`'s commands are additionally gated by
  `CanExecute`, so this is a defensive second layer, not the only guard.
- Back/Forward respect the same unsaved-editor discard-confirm flow as rail nav (`TryLeaveCurrentEditor`)
  — no silent data loss from a Backspace press or swipe while an editor overlay has unsaved changes.
- Malformed CLI `--open` argument → ignored, falls through to restore-on-launch, never a startup
  crash.

## Testing

- **`NavigationHistoryServiceTests`** (new, pure C#, no Avalonia) — the bulk of the real logic:
  push/back/forward, cursor-truncation-on-new-push-after-back, `JumpTo` truncation, `ResetRoot`
  clearing the stack and setting the new root, `BreadcrumbTrail` slicing, `CanGoBack`/`CanGoForward`
  at every boundary (empty stack, single entry, mid-stack, end-of-stack).
- **`NavigationCliArgsTests`** (new) — valid `series:123`/`issue:456`/`book:789`/`collection:12`
  parsing, missing `--open`, malformed id, unrecognized kind — all return the documented
  ignore-and-fall-through behavior rather than throwing.
- **`MainViewModelTests`** (extend) — `NavigateBackCommand`/`NavigateForwardCommand` `CanExecute`
  state through a push/back/forward sequence; lateral `GoX()` resets the stack; the unsaved-editor
  guard applies to Back/Forward same as it does to rail nav; restore-on-launch's deleted-entity
  fallback to Home against a scratch DB.
- Breadcrumb bar visuals/clickability, Backspace-while-focus-is-elsewhere-vs-in-a-textbox, and
  trackpad swipe: manual/on-screen verification only — stated honestly as unverified at design time,
  same standing caveat as every other desktop-input spec in this project (no unattended GUI
  automation available in this environment).

## Roadmap

Once landed, add a `docs/Paperbunkr-Roadmap.md` Beta-backlog entry and update
`docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md`'s own scope note (it
currently says drill-down transitions/history are "out of scope, deferred" — mark this spec as the
follow-on that picked that up). The comprehensive-keyboard-operability follow-on (context menus,
flyout menus, card navigation on newer screens) gets its own separate brainstorm/spec next, not
folded into this one.
