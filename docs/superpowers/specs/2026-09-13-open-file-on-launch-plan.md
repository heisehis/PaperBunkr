# Open-a-comic-on-launch — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-13-open-file-on-launch-design.md*

## Step 1: Pure bare-file-path detector
**Files:** [src/Paperbunkr.App/Services/NavigationCliArgs.cs](../../../src/Paperbunkr.App/Services/NavigationCliArgs.cs) (edit)

**What:** Add `public static bool TryParseFilePathArg(string[] args, out string? path)` alongside
`TryParseOpenArg`. Rules, mirroring `TryParseOpenArg`'s "malformed input never throws, just returns
false" posture:
- Only recognizes the shape "exactly one bare argument, not `--open` and not its value". Concretely:
  if `args.Length != 1` or `args[0] == "--open"`, return false. (A `--open kind:id` invocation is
  always 2 args, so the length check alone already rejects it; the explicit `"--open"` check is
  belt-and-suspenders for a malformed single-arg `--open` with no value.)
- `File.Exists(args[0])` must be true.
- `Providers.Readers.GetFileExtensions()` (same convention as
  [LibraryFolderScanner.cs:81](../../../src/Paperbunkr.App/Services/LibraryFolderScanner.cs) and
  [DragImportService.cs:70](../../../src/Paperbunkr.App/Services/DragImportService.cs) — **not**
  `ProviderFactory.GetSourceProviderType`) must contain `Path.GetExtension(args[0])`,
  case-insensitive.
- On success, `path = args[0]` (or a normalized `Path.GetFullPath(args[0])` — check what
  `LibraryFolderScanner`/`DragImportService` store in `Issue.FilePath` and match it, so the later
  `FilePath == path` DB lookup in Step 3 actually hits; if those services store the raw dropped path
  as-is, do the same here rather than normalizing).

**Depends on:** none
**Verify:** Step 4's new unit tests.

## Step 2: Wire the new branch into startup
**Files:** [src/Paperbunkr.App/App.axaml.cs](../../../src/Paperbunkr.App/App.axaml.cs) (edit, around
line 247)

**What:** Add a branch checked alongside the existing `--open` check:

```csharp
if (NavigationCliArgs.TryParseFilePathArg(desktop.Args ?? Array.Empty<string>(), out var filePath) && filePath is not null)
{
    mainViewModel.OpenFilePath(filePath);
}
else if (NavigationCliArgs.TryParseOpenArg(desktop.Args ?? Array.Empty<string>(), out var deepLinkTarget) && deepLinkTarget is not null)
{
    mainViewModel.OpenDeepLink(deepLinkTarget);
}
else
{
    mainViewModel.RestoreLastScreen();
}
```

The two parses are mutually exclusive by shape (spec §3), so the `if`/`else if` order is safe either
way — file-path branch first per the design doc's stated diff-size reasoning.

**Depends on:** Step 1
**Verify:** Not unit-tested (spec §5 — this file's startup sequence has no existing test coverage,
matched deliberately). Covered by manual on-screen pass (§6 caveat, still outstanding).

## Step 3: `MainViewModel.OpenFilePath`
**Files:** [src/Paperbunkr.App/ViewModels/MainViewModel.cs](../../../src/Paperbunkr.App/ViewModels/MainViewModel.cs)
(edit, new method placed next to `OpenDeepLink` at line 2145)

**What:**

```csharp
/// <summary>Bare file-path CLI launch (docs/superpowers/specs/2026-09-13-open-file-on-launch-
/// design.md) - called once from App.axaml.cs at startup when NavigationCliArgs.TryParseFilePathArg
/// found a supported comic file on the command line (the shape Windows' own file-association launch
/// produces). Mirrors CE's Storage.FindItemByFile short-circuit: a path already in the library just
/// opens; a new one is always imported first (deliberate deviation from CE's AddToLibraryOnOpen
/// default of "false" - see design doc §1/§4, no transient reading mode exists to fall back to).</summary>
public void OpenFilePath(string path)
{
    using var context = PaperbunkrDb.CreateContext();
    var existing = context.Issues.FirstOrDefault(i => i.FilePath == path);
    if (existing is not null)
    {
        _navigationHistory.ResetRoot("library");
        GoReaderForIssue(existing.Id);
        return;
    }

    try
    {
        var result = new LibraryFolderScanner().ImportNewFilesAsync(
            new[] { path }, new Progress<(int Done, int Total)>(), CancellationToken.None)
            .GetAwaiter().GetResult();

        if (result.AddedIssueIds.Count == 1)
        {
            _navigationHistory.ResetRoot("library");
            GoReaderForIssue(result.AddedIssueIds[0]);
            return;
        }
    }
    catch
    {
        // Import failed (corrupt archive, I/O error) - fall through to the toast + restore below,
        // same "don't crash startup over one bad file" posture as NavigationCliArgs' own parsing.
    }

    RestoreLastScreen();
    ShowToast("Couldn't open file", "That file couldn't be added to your library.");
}
```

Check exact `ShowToast` copy conventions used elsewhere in this file (e.g. the "Nothing to undo" /
"Undone" pairs around line 1292) before finalizing wording — match tone, don't invent a new voice.

Confirm `LibraryFolderScanner`, `PaperbunkrDb`, `System.Threading`, `System.Threading.Tasks` are
already `using`'d in this file (they should be, given `RestoreLastScreen`/other async work already
here) — add any missing usings.

**Depends on:** Step 1 (none directly, but logically follows it in the branch flow)
**Verify:** Step 4's new unit tests.

## Step 4: Tests
**Files:**
- [src/Paperbunkr.App.Tests/NavigationCliArgsTests.cs](../../../src/Paperbunkr.App.Tests/NavigationCliArgsTests.cs) (edit)
- [src/Paperbunkr.App.Tests/MainViewModelTests.cs](../../../src/Paperbunkr.App.Tests/MainViewModelTests.cs) (edit)

**What — NavigationCliArgsTests.cs**, mirroring the existing `TryParseOpenArg_*` naming/shape:
- `TryParseFilePathArg_ExistingSupportedFile_Succeeds` — use `CbzFixture.Create` (precedent:
  [CbzFixture.cs](../../../src/Paperbunkr.App.Tests/CbzFixture.cs), already used by
  `DragImportServiceTests`) to write a real temp `.cbz`, assert `true` + `path` equals it. Clean up
  the temp file/dir in a `finally` or test-class `IDisposable`, matching `DragImportServiceTests`'
  temp-root pattern.
- `TryParseFilePathArg_OpenFlagWithKindId_Fails` — `new[] { "--open", "issue:42" }` must return
  `false` (2-arg shape already excludes it, but assert it explicitly since it's the exact ambiguity
  the spec calls out in §5).
- `TryParseFilePathArg_NonexistentPath_Fails` — a path that doesn't exist on disk.
- `TryParseFilePathArg_UnsupportedExtension_Fails` — a real existing temp file with e.g. `.txt`.
- `TryParseFilePathArg_EmptyArgs_Fails`.

**What — MainViewModelTests.cs**, mirroring the existing `OpenDeepLink_*` tests
(lines ~567–610) and their `SeedSeriesWithIssue` helper:
- Add a small seed helper (or inline in each test) that creates an `Issue` with a specific
  `FilePath` set — `SeedSeriesWithIssue` currently leaves `FilePath` null, so either extend it with
  an optional `string? filePath = null` parameter or add a sibling
  `SeedSeriesWithIssueAtPath(string seriesName, string filePath)`. Prefer extending the existing
  helper with an optional param — smaller diff, no duplicate seeding logic.
- `OpenFilePath_AlreadyInLibrary_OpensExistingIssue_WithoutImporting` — seed an issue with
  `FilePath = <some temp path string>` (file need not actually exist on disk for this branch, since
  `OpenFilePath` only queries the DB by path here — no scanner call happens), call
  `vm.OpenFilePath(thatPath)`, assert `vm.IsReader`. To prove no import ran, assert
  `context.Issues.Count()` is unchanged (still 1) after the call.
- `OpenFilePath_NewSupportedFile_ImportsAndOpens` — use `CbzFixture.Create` to write a real `.cbz`
  to a temp dir (same pattern as `DragImportServiceTests`), call `vm.OpenFilePath(thatPath)`, assert
  `vm.IsReader` and that a new `Issue` with that `FilePath` now exists in the DB.
- `OpenFilePath_UnsupportedOrMissingFile_FallsBackWithoutCrashing` — call
  `vm.OpenFilePath(@"C:\nonexistent\file.cbz")` (or an existing `.txt`), assert it doesn't throw and
  `vm.IsHome` (or whatever `RestoreLastScreen`'s no-prior-session default resolves to — check
  `RestoreLastScreen_NoPriorSession_DefaultsToHome` at line ~613 for the exact expected screen).

Follow the existing `[Collection(nameof(AvaloniaTestCollection))]` / `IDisposable` /
`PaperbunkrDbContext.DatabasePathOverride` temp-DB fixture setup already at the top of
`MainViewModelTests` — no new fixture machinery needed.

**Depends on:** Steps 1 and 3
**Verify:** `dotnet test` on `Paperbunkr.App.Tests`, targeted to the new test names (full-suite runs
are known-flaky headless per existing project memory — run filtered, not the whole assembly).

## Not covered here (per design doc §4/§6)
- No `AppSettings` field, no Preferences UI toggle.
- No manual on-screen double-click verification this session (no computer-use available) — flag as
  outstanding for a later manual pass, same as the design doc's own §6 caveat.
