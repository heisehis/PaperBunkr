# Remaining provider metadata paths

Continuation authorized by the user's request to finish the listed omissions, using the existing checkout and existing overwrite policy.

AniList requests and normalizes partial publication start/end dates, popularity, MAL identity, synonyms, score and tag categories. Unknown date components remain unknown. MangaBaka preserves source identifiers even when Paperbunkr has no adapter for that source; known identities continue through the existing identity conflict rules.

Provider-reported facts appear with attribution in Details, through a typed projection of the latest normalized snapshot for each currently linked identity. This is an explicit extension of the original audit-only snapshot design: these rows are source reports, not canonical editable fields. No migration or chapter-date overwrite is needed. CE's ComicInfo Year/Month/Day describe an individual book, not a series publication interval.

ComicVine series descriptions flow through search results and exact-id results into blank series summaries, respecting the Summary scrape toggle and locks. Existing text is preserved. Metron uses the same apply helper. No extra ComicVine request is necessary.

Implementation plan: repair request contracts; extend normalized fields; connect the typed Details projection; carry series facts through comic search; audit Wikidata/GCD/pull-list/tracker consumers; fix confirmed integration defects; run targeted request, mapping, persistence and presentation tests. UI binding follows avalonia-mvvm; no new input handlers or visual controls.

Verification and remaining capabilities are recorded in docs/provider-utilization-audit.md.
