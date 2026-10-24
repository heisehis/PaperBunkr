# Metron Continuity Discovery and Needs Attention Bulk Actions — Plan

Implements [the design spec](2026-10-16-bulk-metron-continuity-discovery-design.md).

1. **Complete — Separate Metron lookup from persistence.** Added a reusable read-only per-series lookup and a library-wide scanner that produces typed suggestions with progress/cancellation and no analysis-time writes.
2. **Complete — Persist provider-scoped dismissals and accept safely.** Added a dedicated Metron dismissal entity/migration; bulk acceptance resolves by Metron ID first and commits memberships transactionally.
3. **Complete — Restore Check Metron under Suggestions & checks.** Added async scan, progress/cancellation, scan summary, selection, bulk accept/dismiss, retained individual row actions, and focused tests.
4. **Complete — Add Needs attention bulk Add.** Added selectable outside-series rows and Select all/Clear/Add selected with validation, transactional updates, and focused tests while retaining individual Add.
5. **Complete — Preserve per-continuity Metron browsing.** The existing universe catalog browser remains a distinct action from Check Metron and Needs attention; its catalog results only add local series when the Metron series ID matches exactly.
6. **Verified before browser restoration.** `dotnet test src/Paperbunkr.App.Tests --no-restore --filter "FullyQualifiedName~ContinuityPageViewModelTests|FullyQualifiedName~SuggestionsChecksViewModelTests"` passed 27/27. `dotnet test src/Paperbunkr.Data.Tests --no-restore --filter "FullyQualifiedName~ContinuityMetronSuggestionResolverTests"` passed 2/2. Re-run the App build and continuity tests after restoring the browser. Existing repository/package warnings remain. No matching roadmap item was identified for an update.
