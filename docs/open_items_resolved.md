# Paperbunkr — Open Items (§15) Resolved

## 1. Author/artist credit-collapsing convention — resolved, no real dilemma existed
Checked MangaDex and AniList directly: both expose author and artist as **distinguishable relationship types**, not a single blob. So the common case has no collapsing decision to make at all — map author→`Issue.Writer`, artist→`Issue.Penciller` directly, same as western-comics credits already do. The convention only needs to cover the fallback case: when a source provides a single undifferentiated creator credit (no author/artist distinction), populate **both** `Writer` and `Penciller` with that name, rather than leaving one blank or inventing a merged pseudo-field. Simple, implementable, no schema change needed.

## 2. MangaUpdates type enum + MangaDex rate limits — resolved via research

**MangaUpdates' actual taxonomy is broader than assumed.** Beyond Manga/Manhwa/Manhua, it also classifies: light novel, manga anthology, Indonesian comics, webtoon (as its own category, distinct from manhwa!), original English-language (OEL) manga, and manfra (French manga-style). This looked like it might force expanding the `ContentType` enum — it doesn't, once you notice `webtoon` is a *format/reading-convention* signal, not a content-origin one, and that distinction is exactly what `ReadingMode` (§6) already exists to capture separately from `ContentType`. **Decision: keep `ContentType` at the 5 values already in the schema for v1.** Route anything MangaUpdates classifies outside that set (light novel, anthology, manfra, etc.) into `Genre`/`Tags` as descriptive metadata rather than growing the core enum indefinitely — the enum was designed to drive reading-mode defaults and library filtering, and these edge categories don't need to drive either.

**MangaDex rate limits, confirmed from their own docs:** ~5 requests/second per IP, global, documented as a conservative floor ("your effective allowed rate may be higher, but don't rely on that"). A stricter 40 requests/minute applies specifically to the `at-home/server` endpoint — irrelevant here since that's the chapter-image-fetching endpoint, explicitly out of scope per §9. MangaDex's own guidance is explicit that request-pattern matters as much as raw rate: "if 500 requests could be one, do that" — abusive-pattern throttling is separate from and stricter than the documented numeric limit. **For a bulk "scrape my whole library" pass:** throttle client-side to a real margin under the documented floor (target ~2-3 req/s, not 5), and design around MangaDex's search/list endpoints for batch-style lookups where possible rather than one call per series as the default pattern.

## 3. Decode-queue threading primitive — resolved: `System.Threading.Channels`

Checked current benchmarks and guidance rather than assuming: Channels consistently outperforms TPL Dataflow in producer-consumer throughput tests (roughly 5.6ms vs. 7.7ms in one representative benchmark), and — more relevant than raw speed — `Channel<T>.Reader.ReadAsync(CancellationToken)` gives native, first-class cancellation support, which is exactly what the reader canvas's "cancel in-flight decodes for pages that scrolled back out of the window" requirement needs. Dataflow is the right tool when you need multi-stage pipelining with built-in blocks; this is a single producer-consumer decode queue, which is precisely the case Channels was purpose-built for.

**Neither has built-in priority ordering** — that needs a small layer on top regardless of which primitive wins. Given the reader canvas only really needs a few priority tiers (current page, near-window pages, far/background pages) rather than arbitrary priority values, the simplest implementation is **multiple bounded channels, one per tier**, with the consumer loop preferring to drain higher tiers first — simpler than a general `PriorityQueue<T,TPriority>` layered on a single channel, and easier to reason about for this specific, bounded use case.

## 4. Double-page spread heuristics — designed

Auto-pair adjacent pages when: (a) both pages are portrait-oriented (not already a wide "spread" image scanned as one file), (b) the active `ReadingMode` is `LeftToRight` or `RightToLeft` — pairing is meaningless in the continuous modes, so this logic doesn't run there at all, (c) neither page is the issue's designated cover (covers conventionally display solo, matching precedent from Mihon and CDisplayEx). This should be a **user-facing override, not a silent automatic-only behavior** — store it as a display preference (per-series, with a per-issue escape hatch for genuine exceptions), not a data-model field, since it's a rendering choice rather than metadata about the work itself. Lives in the reader canvas's layout model (§8), not the schema.

## 5. CE `Config.xml` migration scoping — resolved via direct source inspection

Checked `EngineConfiguration.cs` directly (691 lines, 65 settings) rather than assuming. It splits cleanly into four buckets:

- **Genuinely portable as literal values** — `ComicCaptionFormat`, `ComicExportFileNameFormat`, `IgnoredArticles`, `OfValues`, `IsRecentInDays`, `IsReadCompletionPercentage`, `IsNotReadCompletionPercentage`, `GhostscriptExecutable`, `DjVuLibreInstall`, `TempPath`, `ThumbnailQuality`, `CacheThumbnailPages`, `JpegXLEncoderEffort`, `UseLegacyZipConfiguration`, `IgnoreEmbeddedComicBookXml`, `DisableNTFS` — text formatting, thresholds, and format-provider settings that carry over directly since the underlying providers are being ported largely as-is (§3–4).
- **Conceptually relevant, but needs new values, not literal migration** — `EnableParallelQueries`, `MaximumQueueThreads`, `MaximumUpdateThreads`, `OperationTimeout`, `ServerProviderCacheSize`. These map onto Paperbunkr's own decode-queue/EF Core threading model (§8, §5), but the old values were tuned for a GDI+/WinForms pipeline that no longer exists — fresh defaults appropriate to SkiaSharp/Avalonia's actual threading behavior, not a carried-over number.
- **Out of scope entirely** — every `Sync*`/`WifiSync*` setting, `ExtraWifiDeviceAddresses`, `FreeDeviceMemoryMB`, `ParallelConversions`. These all belong to CE's mobile WiFi-sync feature, which isn't part of Paperbunkr's scope at all (§1: desktop-first, local-first). Not "retire the setting" — the whole feature area doesn't exist here.
- **Retired, superseded by the new rendering architecture** — `PageBow*` (page-curl visual effect), `ThumbnailPageBow`, `PageShadow*`, `MirroredPageTurnAnimation`, `AnimationDuration`, `BlendDuration`, `SoftwareFilterDelay`/`MinScale`, `AeroFullScreenWorkaround` (literally a Windows Vista/7-era compatibility setting), `HtmlInfoContextMenu`, `EnableHtmlScriptErrors`, `NavigationPanelWidth`, `ListCoverAlpha`, `RatingStarsBelowThumbnails`, `SearchBrowserCaseSensitive`. These are artifacts of the old GDI+/WinForms rendering pipeline and layout — the reader canvas spec (§8) and new UI have their own settings surface, with nothing meaningful to carry over.

Net effect: `Config.xml` migration is much smaller in practice than "migrate the settings file" implied — roughly a quarter of the fields carry over as values, another chunk carries over as *concepts* with new defaults, and half the file belongs either to a feature that doesn't exist in Paperbunkr or a rendering pipeline that's been replaced outright.

## 6. Category nesting — closed, no change
Confirmed as flat (matching Mihon's precedent), only worth revisiting if a real use case demands it. Nothing further to resolve here — noting closure rather than leaving it dangling.

## 7. `theme.json` schema — designed

```json
{
  "name": "Windows 11",
  "author": "...",
  "version": "1.0",
  "colors": {
    "accent": "#0078D4",
    "background": "#FFFFFF",
    "backgroundElevated": "#F3F3F3",
    "foreground": "#1A1A1A",
    "foregroundMuted": "#6B6B6B",
    "border": "#E0E0E0",
    "success": "#107C10",
    "warning": "#FFB900",
    "error": "#D13438"
  },
  "typography": {
    "fontFamily": "Segoe UI Variable"
  },
  "layout": {
    "cornerRadius": 8,
    "spacingUnit": 4
  },
  "icons": {
    "manifest": ["toolbar/*.png", "sidebar/*.png", "badges/*.png", "tabs/*.png"]
  }
}
```

This resolves the "does v1 cover anything beyond color" question left open in §13: **yes, corner radius and a base spacing unit are in scope**, alongside color and font family — all of these apply the same way (parsed in code, set directly on `Application.Current.Resources`, consumed via `DynamicResource`), so including them costs nothing extra and doesn't cross into the structural/`ControlTemplate` territory that stays explicitly out of scope. Icons stay a manifest of relative paths within the extracted skin folder, matching the existing `.crpck` extraction model (§13) unchanged.

## 8. Retarget spike — still not runnable here
No change from §4/§13's original note: this container has no .NET SDK and no network access to install one. This remains the one item on the list that's a genuine Claude Code task, not something resolvable through research or design reasoning. Everything else on this list is now closed or designed; this is the actual next action.
