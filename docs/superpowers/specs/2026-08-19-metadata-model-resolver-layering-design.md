# Metadata Model: Resolver Layering + Architecture Boundary Test

**Date:** 2026-08-19
**Status:** Approved, implemented

## Context

Fourth item from the same architecture-review roadmap as the three adjacent specs. The review's
original wording was "formalize the existing `Paperbunkr.Data/Metadata` resolvers as an explicit
application-service seam (interfaces + DI registration), independent of any project split." Read
literally that means introducing `IContinuityResolver`/`IMediaRelationResolver`/etc. plus a DI
container - which this app has never had (every ViewModel wires its own `PaperbunkrDb.CreateContext`
factory, with a `Func<PaperbunkrDbContext>` test seam where needed, no service collection anywhere).

## What was actually done, and what was deliberately not

**Not done: interfaces + DI.** This app's ~10 resolvers (`ContinuityResolver`,
`MediaRelationResolver`, `EventMembershipResolver`, `RecommendationResolver`, `HomeFeedResolver`,
`SeriesReassignmentResolver`, `ExternalMetadataResolver`, `MetadataLinkResolver`, `TitleMatchScorer`)
are already a consistent, flat, testable application layer: `public static class XResolver { public
static Y Method(PaperbunkrDbContext context, ...) }`. None of them have more than one real
implementation, none are swapped at runtime, and every one is already independently unit-testable by
constructing a real `PaperbunkrDbContext` against a temp SQLite file - the actual reason interfaces
exist (substitutability, mocking) doesn't apply here. Converting ~10 classes and every call site to
`IXResolver` + constructor injection + a hand-rolled service locator (since there's still no DI
container to register with) would be real churn for zero behavioral benefit - the same premature-
abstraction pattern this review flagged against the *source* document's own five-project Clean
Architecture proposal. Consistency with the rest of this session's own recommendations mattered more
than following the review's literal wording here.

**Done: named the convention.** `Paperbunkr.Data/Metadata` *is* this codebase's application layer -
this doc makes that explicit for future resolvers (`MetadataLinkResolver`, added this same session,
already follows it) rather than leaving it as an unstated pattern. The convention:
- Static class, `PaperbunkrDbContext` as an explicit parameter (never a field/captured context -
  keeps lifetime obvious at the call site, matches the `using var context = ...` idiom everywhere).
- No `Paperbunkr.App`/Avalonia reference, ever.
- Unit-tested against a real temp-file SQLite `PaperbunkrDbContext`, not an in-memory provider (this
  codebase's established rationale: catches real SQLite-specific behavior, e.g. the `HasSentinel`
  enum-default gotcha and `last_insert_rowid()` connection-affinity flake documented in earlier
  metadata-model phases).

**Done: the architecture boundary test.** `ArchitectureBoundaryTests`
(`Paperbunkr.Data.Tests/ArchitectureBoundaryTests.cs`) - the review's own genuinely cheap, genuinely
adoptable idea, applied to this project's real 4-project structure (`App`/`Common`/`Data`/`Engine`)
instead of the hypothetical `Domain`/`Application`/`Infrastructure` split. Reflects on
`Assembly.GetReferencedAssemblies()` for each project's own known type rather than adding a NuGet
architecture-testing library - overkill for asserting three known-good facts about four projects:

- `Data` never references `App`.
- `Engine` never references `App` or `Data`.
- `Common` references nothing else in this solution.

This fails the build the moment a boundary is crossed, rather than the drift being discovered months
later - the actual value of the review's recommendation, delivered without adopting the project
structure it was originally paired with.

## Testing

`ArchitectureBoundaryTests`: 3 tests, one per boundary above. All pass against the current
dependency graph (`App -> {Common, Engine, Data}`, `Data -> Engine -> Common -> nothing`).
