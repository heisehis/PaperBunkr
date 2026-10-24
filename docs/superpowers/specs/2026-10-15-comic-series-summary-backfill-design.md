# Comic series summary backfill

The existing scrape fills a blank series summary only while an issue is scraped. That leaves previously scraped comic series without a general synopsis. ComicRack CE's Summary is an issue field; Paperbunkr's series summary is a deliberate series-level addition and must not be synthesized from an issue plot.

For local Comic or Unknown series whose series summary is blank and not user-locked, use their persisted Metron and ComicVine series links to request the provider's series description. Try Metron first, then ComicVine when both links exist and the first source has no description or fails. Recheck the blank/unlocked condition immediately before saving, because a user may edit it during the request. Never use title-only matching or issue summaries for an unattended backfill. Count series without a usable link, unavailable credentials, missing descriptions and provider failures so the run reports its actual coverage.

Run the backfill as an enabled, low-priority scheduled network task on the next app launch and at a weekly interval. Only empty summaries are candidates, so successful rows naturally leave future runs. A provider with no description may be retried later in case its data changes. Existing summaries, manga-family series, and remote mirrors are untouched. If neither linked source has a description, the honest display remains “No summary available.”

Plan: implement an injectable Data-layer backfill service; add focused persistence and fallback tests; register the scheduled task with progress/result text; build and run the focused Data and scheduler tests. No schema or XAML change is needed.
