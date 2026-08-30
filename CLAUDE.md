# Paperbunkr

ComicRack-inspired comic/manga library and reader, Avalonia/.NET 8. Full design doc:
[docs/onboarding.md](docs/onboarding.md). CE parity audit: [docs/ce-feature-inventory.md](docs/ce-feature-inventory.md).

## Roadmap status — check this first in a new session

- **Source of truth:** [docs/alpha-todo.md](docs/alpha-todo.md) — P0–P7 alpha checklist plus the
  unsequenced Beta backlog, with commit refs and rationale for every status.
- **Live dashboard (lighter view, same data):**
  https://claude.ai/code/artifact/0ca86894-977e-45e2-951b-476e1150a5ee
- **Kept in sync by:** a scheduled cloud routine (`paperbunkr-alpha-tracker-sync`, every 6h,
  read-only) that diffs `git log` against the dashboard's own embedded `HEAD` marker and
  republishes only verified status changes. It never edits `docs/alpha-todo.md` or commits
  anything — manage/inspect it at https://claude.ai/code/routines.
- **If you land roadmap-relevant work in a session:** update `docs/alpha-todo.md` by hand (status,
  commit ref, what you verified — not just what the commit message claims). The dashboard's own
  6-hourly check will pick up the underlying commits regardless, but the written doc is what a
  human actually reads for the "why."
- The doc drifted out of sync with the repo once already (see its "Live tracker" section) —
  don't assume either the doc or the dashboard is current without checking `git log` against the
  `HEAD` hash each one records.

## Standing rule

Before adding any field, default, or behavior, verify it against the original ComicRack CE
source/behavior (`_reference/ComicRackCE`) rather than assuming — this project is a from-scratch
rewrite aiming for CE parity plus deliberate deviations, not a guess at what CE probably did.

## UI/Avalonia foundation — mandatory across all phases

Load the `avalonia` skill (installed via https://github.com/linuxdevel/Avalonia-skills, symlinked
into `~/.claude/skills/avalonia`) for **every** UI/Avalonia-touching phase of work on Paperbunkr,
not just implementation:

- **Brainstorming/design** — when exploring a UI idea or option space, route through the relevant
  `avalonia`/`avalonia-pro-max` subskill (design-system, layout-patterns, components, motion,
  accessibility, themes) before proposing approaches, so trade-offs are grounded in real Avalonia
  constraints instead of guessed API shape.
- **Spec write-ups** (`docs/superpowers/specs/*-design.md`) — check the matching subskill while
  drafting so the spec doesn't bake in something Avalonia can't do cleanly (e.g. native sticky
  headers, per-property DynamicResource inside a `BoxShadows`/gradient string).
- **Planning** (`writing-plans`) — when a plan step touches XAML/styling/animation/testing/
  deployment, cite the subskill that governs it.
- **Implementation** — load the matching subskill before writing or reviewing the XAML/C#, and run
  `avalonia-pro-max/review-checklist` before calling UI work done (see the 2026-08-30 detail-screens
  polish pass for a worked example: it caught hardcoded hex colors that silently don't react to the
  app's own runtime skin system — a real defect, not a style nit).

The `avalonia-docs` MCP server (`https://docs-mcp.avaloniaui.net/mcp`, added 2026-08-30 via
`claude mcp add`) is available for live API lookups when a subskill doesn't cover something —
prefer it over guessing.

To update the skill later: `git -C ~/.local/share/avalonia-skills pull --ff-only` (no need to
re-run the curl installer).

**Explicitly excluded: `avalonia-layout-zafiro`.** A skill by this name may appear in
`~/.claude/skills` (symlinked from `~/.agents/skills`) — it teaches `Zafiro.Avalonia`-specific
patterns (`HeaderedContainer`, `EdgePanel`, its own `Interaction.Behaviors`/icon extension).
Paperbunkr has no dependency on `Zafiro.Avalonia` or ReactiveUI/DynamicData (it's on
CommunityToolkit.Mvvm) and a 2026-08-30 decision was to **not** adopt the Zafiro stack — do not
load or apply this skill's guidance here even though its name matches. If a future session
considers adopting Zafiro for real, that's a brainstorming-level architecture decision, not
something to slide in via skill auto-routing.

## Build gotcha: adding a new Avalonia View

Adding a brand-new `.axaml` file with a fresh `x:Class` (a View not previously compiled in this
project) can fail the first `dotnet build` with `Avalonia error AVLN2000: Unable to find type`.
Root cause (verified by binlog inspection): `CompileAvaloniaXaml` — the MSBuild target that weaves
compiled XAML IL into the assembly and builds the runtime `!AvaloniaResources` index — has no
`Inputs`/`Outputs` of its own. It only runs as a `TargetsTriggeredByCompilation` side effect of
`CoreCompile`. If `CoreCompile` succeeds (writing the `.dll`) but the immediately-following
`CompileAvaloniaXamlTask` then fails, that failure never touches the `.dll`'s timestamp. The next
plain `dotnet build` sees the `.dll` newer than every source file, skips `CoreCompile` entirely
(and therefore skips `CompileAvaloniaXaml` too), and reports **0 Errors** — but the XAML weave
never actually reran. The shipped assembly is the same un-woven artifact from the failed build,
with no valid `!AvaloniaResources` index for *any* view in the project, so the app crashes at
startup with `XamlLoadException: No precompiled XAML found for ...App` even though `App.axaml` was
never touched.

**Avoid it:** always add the code-behind `.cs` (even a minimal
`partial class X : UserControl { public X() => InitializeComponent(); }`) in the same step as the
new `.axaml`. `x:Class` types aren't stubbed out by a source generator in this Avalonia version —
without a matching compiled partial class, `CompileAvaloniaXamlTask` has nothing to bind to and
`AVLN2000` is not transient, it will recur on every genuinely fresh compile (confirmed via
`dotnet build -t:Rebuild`, which honestly re-fails instead of masking the error).

**If a build ever fails inside XAML compilation after `CoreCompile` already produced output,
don't just retry `dotnet build`** — a bare retry can silently report success while shipping a
never-woven assembly. Fix the real compile error first, then force `CoreCompile`'s own output to
be seen as stale before rebuilding:

```bash
rm src/Paperbunkr.App/obj/Debug/net8.0/Paperbunkr.App.dll src/Paperbunkr.App/obj/Debug/net8.0/Paperbunkr.App.pdb
dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj
```

`dotnet build -t:Rebuild` works too. Deleting only the Avalonia-specific cache under
`obj/.../Avalonia/` does **not** help — that's not what gates the skip. Treat "0 Errors" alone as
insufficient proof the weave ran; verify by launching the exe (or grepping a `-v:diag` log for
`CompileAvaloniaXaml` actually executing, not `"skipped... previously built successfully"`).
