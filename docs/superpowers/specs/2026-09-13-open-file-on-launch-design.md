# Open-a-comic-on-launch (shell file association)

**Date:** 2026-09-13
**Status:** Approved, ready for planning
**Scope:** Beta backlog (Preferences: Behavior / CE-parity toggle remainder), not Alpha P0–P7.

## 1. Background

[`Paperbunkr-Roadmap.md`](../../Paperbunkr-Roadmap.md)'s "Preferences: Behavior / CE-parity toggle
remainder" section named `AddToLibraryOnOpen` ("Opened Files are added to the Library") as blocked
on a real prerequisite: Paperbunkr has no shell-open-a-loose-file path at all. Verified against
`_reference/ComicRackCE`:

- `ComicRack.Engine/ComicBookFactory.cs` / `NavigatorManager.cs`: opening a file checks
  `Storage.FindItemByFile` first (already-in-library files just open normally); a file *not* already
  in the library either permanently adds it (`CreateBookOption.AddToStorage`, toggle ON) or opens it
  as an in-memory-only `TemporaryBooks` entry that never touches the database (`AddToTemporary`,
  toggle OFF).
- `Config/Settings.cs:1250`: **CE's own default for this toggle is `false`** — most CE users
  experience the *transient, never-persisted* path by default, not permanent-add.

Genuine parity would mean building a real non-persisted reading mode. Paperbunkr's Reader is
Issue-row-based throughout — position tracking, bookmarks, and metadata write-back gating all key
off `Issue.Id` — so a true "read without an Issue row" would touch every one of those features for
that session. Decided in brainstorming: **out of scope**, disproportionate to what the roadmap
itself flagged as a small gap-filler. This spec builds the "always import, then open" MVP only, and
the `AddToLibraryOnOpen` **toggle itself is dropped** — with no transient mode to gate, an OFF
setting has no real behavior left to control (same reasoning already applied to two sibling CE
toggles in this roadmap section: build only if a user asks for it).

**Not a new prerequisite:** file-association registration is already a real, shipped feature
(Preferences → Advanced's `FileAssociationService`, plus the installer's per-format `[Tasks]`) —
`WindowsShellFileAssociation`/`ShellRegister.RegisterFileOpen` already wires the Windows command line
as `"...\Paperbunkr.exe" "%1"`. Double-clicking an associated file already launches Paperbunkr with
that path as a bare argument; the gap is purely that `App.axaml.cs`'s startup silently ignores it.

## 2. Current state (verified)

- `App.axaml.cs`'s startup: `NavigationCliArgs.TryParseOpenArg(desktop.Args, ...)` only recognizes
  `--open <kind>:<id>`; anything else (a bare file path) falls through to
  `MainViewModel.RestoreLastScreen()`, silently ignored.
- `MainViewModel.OpenDeepLink(NavigationCliTarget)` dispatches by kind (`series`/`issue`/`book`/
  `collection`) to each screen's own `Go*` method, e.g. `GoReaderForIssue(int issueId)` for `issue`.
- `LibraryFolderScanner.ImportNewFilesAsync(IReadOnlyCollection<string> files, IProgress<...>, CancellationToken)`
  already exists and is reused by drag-and-drop import: dedupes on `Issue.FilePath`, filters to
  supported extensions, returns a `LibraryFolderScanResult` with the added `Issue` ids. This is the
  exact mechanism the "always import" path needs — no new import logic required.
- Paperbunkr is **explicitly not single-instance** (`Program.cs`'s own comment: "NOT single-instance
  enforcement - the app still allows multiple windows"). A second file-open while Paperbunkr is
  already running launches a second process against the same database — an existing, accepted
  behavior this feature doesn't change or need to solve.

## 3. What ships

`App.axaml.cs`'s startup sequence gains one more branch, checked before the existing `--open`
parse (a bare file path and `--open <kind>:<id>` are mutually exclusive CLI shapes, so order doesn't
matter for correctness, but checking the more specific file-path case first keeps the diff smallest
at the actual `desktop.Args` inspection):

1. Look for a single argument that's an existing file path with a supported comic/reader extension
   (`Providers.Readers.GetSourceProviderType(path) is not null`, the same check
   `ComicBookFactory.Create` uses in CE and `LibraryFolderScanner` already gates imports on).
2. If found: open a `PaperbunkrDbContext`, check `context.Issues.FirstOrDefault(i => i.FilePath == path)`.
   - **Already in the library:** open that Issue directly (mirrors CE's `Storage.FindItemByFile`
     short-circuit) — reuses the exact `OpenDeepLink`'s `"issue"` case
     (`_navigationHistory.ResetRoot("library"); GoReaderForIssue(issue.Id);`).
   - **Not in the library:** call `ImportNewFilesAsync` with the single file. If it returns one added
     issue id, open it the same way. If it returns zero (shouldn't happen given the extension check
     above already passed, but the scanner has its own independent supported-extension list as a
     second gate) or throws, fall back to `RestoreLastScreen()` and surface a toast
     (`_showToast`/whatever this app's existing "something went wrong" pattern is) rather than a
     silent no-op.
3. If no valid file-path argument is found, behavior is completely unchanged (existing `--open`
   check, then `RestoreLastScreen()`).

New method: `MainViewModel.OpenFilePath(string path)` (or similar; the plan phase names it exactly),
containing steps 2's dispatch — kept separate from `OpenDeepLink` since it needs a DB round-trip
`OpenDeepLink` doesn't, not shoehorned into that method's simpler kind-switch shape.

## 4. Explicitly out of scope

- The `AddToLibraryOnOpen` toggle itself — no new `AppSettings` field, no new Preferences UI. Opening
  an external file always imports it into the library; this is a deliberate, disclosed deviation
  from CE's literal default (which is "don't add"), not an oversight.
- A genuine transient/non-persisted reading mode — real CE parity, a separate and substantially
  larger sub-project if ever wanted (would need to thread "no Issue row" through position tracking,
  bookmarks, and write-back gating).
- Single-instance enforcement / forwarding a second launch's file-open into an already-running
  window — Paperbunkr already allows multiple windows/processes by design; unrelated to this feature.
- Handling a file path passed via `--open` (e.g. `--open file:C:\...`) — the existing `--open
  <kind>:<id>` convention stays exactly as-is; this feature only handles a *bare* path argument (the
  literal shape Windows' own file-association command line produces).

## 5. Testing

- `NavigationCliArgs`-level: if the bare-path detection logic is factored out as its own pure
  function (recommended, mirrors `TryParseOpenArg`'s own "pure string handling, no Avalonia
  dependency" shape), a few unit cases: a valid existing path with a supported extension is
  recognized; a `--open kind:id` arg is not mistaken for a file path; a nonexistent path or
  unsupported extension is rejected.
- `MainViewModelTests`: `OpenFilePath` with an already-in-library path opens that Issue without
  calling import; with a new path, imports it and opens the resulting Issue; with an unsupported/
  nonexistent path, falls back to `RestoreLastScreen` behavior and doesn't crash.
- App-startup wiring itself (the `desktop.Args` branch in `App.axaml.cs`) isn't unit-tested, matching
  this codebase's own established convention for that file's startup sequence.

## 6. On-screen verification

Standing caveat: no computer-use available this session. Actually double-clicking an associated
comic file (both when Paperbunkr is closed and when it's already running) needs a manual pass before
being marked verified.
