# Versioning Convention

*Date: 2026-09-10.*

## Problem

Paperbunkr's release *mechanism* is built and CI-enforced (tag-triggered
`.github/workflows/release.yml`, `<Version>` in `Paperbunkr.App.csproj` as the source of truth,
`ReleaseVersion.cs` for in-app display, a required `CHANGELOG.md` section per release). What was
never written down is the *semantic* rule behind the numbers:

- Nothing says when a release bumps the minor vs. the patch. In practice every feature release so
  far bumped the minor (`0.1.0` → `0.2.0` → `0.3.0`) and `0.1.1-alpha` was the lone patch, but
  that's convention-by-accident, not a stated rule.
- `-beta` looks permanent. There's no recorded criterion for what flips Paperbunkr to `1.0.0`, or
  whether an `-rc` phase happens first.
- Post-1.0, the ambiguous case for a desktop app with a local SQLite DB — does a non-reversible EF
  migration count as a "breaking" (major) change? — has no answer.
- CI and `ReleaseVersion.DisplayString` both **hardcode** the string `-beta`, so the pipeline as
  written cannot cut a non-beta release at all.

This doc records the convention. It is deliberately a **process/deviation doc**, not a CE-parity
one: CE versioned by git commit against a rolling `nightly` tag
(`_reference/ComicRackCE/.../GithubAPI.cs`) with no SemVer and no changelog. Paperbunkr already
deviated to SemVer-shaped tags + a hand-authored changelog in the
[2026-09-01 auto-update design](2026-09-01-auto-update-and-changelog-design.md); this just fills in
the rules that doc left implicit.

Decided with the user 2026-09-10 (six questions, all recommendations accepted).

## Approaches considered

1. **Strict SemVer from now on** — treat `0.x` as if the "anything may break in a minor" escape
   hatch didn't exist, and reason carefully about major/minor/patch for every pre-1.0 release.
   Rejected: pointless ceremony for a solo pre-1.0 project with one consumer of the version number
   (the updater's "is there something newer" check, which only compares ordering).
2. **CalVer** (`2026.9.0`) — matches how every spec file is already named and sidesteps the
   "when is 1.0" question entirely. Rejected: the updater, the changelog, and three tags of history
   are all SemVer-shaped already; switching now is churn with no payoff, and CalVer hides the
   alpha→beta→stable maturity signal that `-beta` currently carries for free.
3. **Keep commit-hash identity like CE** — rejected already by the auto-update design; noted here
   only because `BuildInstaller.ps1` still falls back to it for un-tagged local builds (which is
   fine and stays).

**Chosen: pragmatic pre-1.0 SemVer** — the scheme that's already in the repo, with the gaps
written down (below). No code changes except Q5 (build metadata) and the deferred Q6 list.

## The convention

### Shape

`MAJOR.MINOR.PATCH[-suffix][+buildmetadata]`

- **Source of truth:** `<Version>` in `src/Paperbunkr.App/Paperbunkr.App.csproj`, four-part
  (`0.3.0.0`). The fourth (revision) segment is **always `0` and carries no meaning** — kept only
  because it's the .NET `AssemblyName.Version` norm and `ReleaseVersion.cs` / the CI derivation
  already tolerate it (Q3). Do not start encoding anything in it.
- **Release/tag string:** CI drops the revision segment and appends the suffix → `0.3.0-beta`.
  Git tag is that, `v`-prefixed: `v0.3.0-beta`. CI fails the build if the pushed tag doesn't match
  the string derived from the csproj — that mismatch guard is the only drift protection and must
  stay.
- **Changelog heading:** `## [0.3.0-beta] - YYYY-MM-DD`, required or CI fails.

### Pre-1.0 number semantics (Q1)

While MAJOR is `0`:

| Segment | Bump when |
|---|---|
| **MINOR** (`0.X.0`) | The normal release cadence: any release that adds user-facing features. This is the default — nearly every release is a minor. |
| **PATCH** (`0.x.Y`) | An out-of-band fix to an **already-released** version that ships **no new features** — a hotfix. `0.1.1-alpha` was the model case. |
| **MAJOR** | Stays `0` until the 1.0 criteria below are met. Never bumped for a pre-1.0 reason. |

"User-facing feature" is judged the same way the changelog already is: if it earns an **Added** or
a substantive **Changed** entry, it's a minor. A release that is *only* **Fixed** entries is a
patch.

### Suffix lifecycle and the road to 1.0 (Q2)

```
0.1.x-alpha   →   0.x.y-beta   →   1.0.0   →   1.x.y / 2.0.0 ...
   (done)          (we are here)    (target)      (post-1.0 SemVer)
```

- **No `-rc` phase.** Solo project, no external release-candidate testers to justify the extra
  step. `-beta` goes straight to `1.0.0`.
- **`1.0.0` criteria — both must hold:**
  1. The CE-parity audit ([docs/ce-feature-inventory.md](../../ce-feature-inventory.md)) has **no
     open P0 or P1 gaps**.
  2. The unsequenced **Beta backlog** in [docs/alpha-todo.md](../../alpha-todo.md) is drained (or
     every remaining item has been explicitly reclassified as post-1.0).
- `1.0.0` ships as a **plain version, no suffix**. It is still a single release stream — `1.0.0`
  is not a "stable channel" split from beta, it's just the point where the suffix goes away.
- Releases are still **not** marked GitHub pre-release (unchanged from the auto-update design's
  Scope) — `prerelease: false` stays, because `UpdateService` resolves the appcast via the "latest
  non-prerelease release" URL.

### Post-1.0 "breaking" definition (Q4)

Once MAJOR is `1`+, standard SemVer applies. For this app specifically:

- A **non-reversible EF Core migration is NOT a breaking change.** Schema migration is the
  expected upgrade path; the app has automatic backups (`%APPDATA%\Paperbunkr\backups\`) and the
  standing no-op-`Down()` rule for `AppSettings`/`Issues` columns. A migration bumps minor or
  patch like any other change.
- **MAJOR is reserved for:**
  - Removing or materially changing a user-facing capability people depend on.
  - A breaking change to the **plugin API contract** (`IMetadataGraph` / `IRulesEngine` /
    `IMetadataWriter` and friends) — anything that makes an existing v3-era plugin stop loading or
    misbehave.
  - Dropping or restructuring settings/library data **with no automatic migration path**.
- Everything else is MINOR (features) or PATCH (fixes), per the pre-1.0 table — that table's
  minor/patch split carries over unchanged; only the MAJOR=0 freeze lifts.

### Build metadata (Q5) — the one net-new code change — **implemented 2026-09-10**

An informational build-metadata suffix so About / logs / bug reports can identify the exact build,
without touching the release contract:

- **csproj `_PbStampBuildMetadata` target** — sets `InformationalVersion` to `<Version>+<short git
  hash>` (`0.3.0.0+3d9b7c7`), or `<Version>+dev` off a non-git build; disables the SDK's own
  `IncludeSourceRevisionInInformationalVersion` append so it can't double-stamp. Runs
  `BeforeTargets="GetAssemblyVersion;GenerateAssemblyInfo"`; a new commit changes the hash and
  triggers a one-file assembly-info regen, stable between commits (no build churn).
- **`ReleaseVersion.BuildMetadata`** (`"3d9b7c7"` / `"dev"` / null) and
  **`ReleaseVersion.DisplayStringWithBuild`** (`"0.3.0-beta+3d9b7c7"`). Never in the git tag, the
  installer filename, or `ReleaseVersion.IsNewerThan` — `TryParseHeading` already strips anything
  after `+`.
- **About section** — `PreferencesScreenViewModel.CurrentVersion` now returns
  `ReleaseVersion.DisplayString` (`0.3.0-beta`) instead of the raw four-part `0.3.0.0`; a faint
  `build 3d9b7c7` line sits under it (`BuildLabel`, hidden when null). **Latent-bug fix:** the
  changelog accordion's `VersionEqualsCurrentConverter` is an exact string compare against the
  `## [x.y.z-beta]` heading — while `CurrentVersion` was `0.3.0.0` it never matched, so the
  "Current" badge and initial-expand were dead. They work now.
- **`DiagnosticsService`** startup/crash log block gets a `Version : 0.3.0-beta+3d9b7c7` line
  (the raw `Assembly : 0.3.0.0` line stays).
- Tests: `ReleaseVersionTests` (+4 cases), new `AboutSectionConvertersTests`. 24/24 green.

### Deferred to 1.0.0 release prep (Q6) — record now, do later

The pipeline hardcodes `-beta` in two spots. When cutting `1.0.0`:

1. `.github/workflows/release.yml` — the tag trigger (`v*-beta`, line ~10) and the derivation
   that literally appends `-beta` (line ~45). Generalize: read an optional `<VersionSuffix>` from
   the csproj (empty = stable), trigger on `v[0-9]+.[0-9]+.[0-9]+*`.
2. `src/Paperbunkr.App/Services/ReleaseVersion.cs` — `DisplayString` hardcodes
   `$"{...}-beta"`. Same fix: derive the suffix from a single constant (or drop it when empty).
3. ~~`installer/Installer.iss:37` — `MyAppVersionNumeric "0.3.0.0"` hand-maintained, CI never
   overrode it.~~ **Fixed 2026-09-10** (not deferred — every installer build is a new version, so
   the drift was continuous): `BuildInstaller.ps1` now derives the numeric `x.y.z.w` from its
   `-Version` argument (leading numeric run, padded to four parts) and passes
   `/DMyAppVersionNumeric`; the literal in `Installer.iss` is now an `#ifndef` fallback for
   compiling the `.iss` without the build script.

Do **not** do these now — a half-generalized pipeline that still only has `-beta` releases to test
against is more risk than value. One focused change at 1.0 prep.

## How to cut a release (the checklist this doc formalizes)

1. Decide minor vs. patch per the table above.
2. Edit `<Version>` in `src/Paperbunkr.App/Paperbunkr.App.csproj` (`x.y.z.0`).
3. Add `## [x.y.z-beta] - <today>` to `CHANGELOG.md` with Added / Changed / Fixed sections.
4. Update `docs/alpha-todo.md` if the release closes roadmap items (the doc's own "update by
   hand" rule).
5. Commit. Tag `vx.y.z-beta`. Push the tag.
6. CI verifies tag↔csproj match, extracts the changelog section, builds the installer, signs the
   appcast, publishes the GitHub Release.
7. Manually verify: Release appears with the right notes + both assets; a prior-version build
   offers and applies the update.

## Testing

- `ReleaseVersion` already has `ReleaseVersionTests.cs`. Add cases for the `+buildmetadata` form:
  `TryParseHeading("0.3.0-beta+9cc0b62")` parses to `0.3.0`; `IsNewerThan` ignores the metadata.
- The `InformationalVersion` wiring is verified by asserting `ReleaseVersion` / the About VM
  surface the `+`-suffixed string in a build that has one, and degrade cleanly when it's absent.
- The Q6 items are explicitly **not** tested now — they're a future change with their own plan.
- Everything else here is documentation; there is no other code to test.

## Self-review

- **YAGNI check:** the only code this doc authorizes is Q5 (build metadata, ~1 csproj property +
  2 display sites + test cases). Q1–Q4 are pure written rules. Q6 is a deferred list. Passes.
- **Does it bake in anything the stack can't do?** No — `InformationalVersion` + SourceLink is
  standard .NET; `TryParseHeading` already handles `+`.
- **Ambiguity left:** "user-facing feature" still needs a judgment call per release, but it's
  anchored to the changelog-entry test, which is the same call already being made.
- **Resolved at review:** the `Installer.iss` `MyAppVersionNumeric` drift (Q6 item 3) was pulled
  out of "deferred" and fixed the same day — see the struck-through item above.
