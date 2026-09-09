# Reader decode / cache / prefetch pipeline — Phase 1 Implementation Plan
*Implements: docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-pipeline-design.md (§13 Phase 1)*

**Branch / worktree:** `claude/reader-pipeline` at `.claude/worktrees/reader-pipeline` (off
`master` + the two spec commits). Isolated from the concurrent `claude/library-health` work.

**Framework note:** solution is net10 (CLAUDE.md still says net8 — stale). `obj/Debug/net10.0/…`.

**Phase 1 goal:** every mode decodes off the UI thread; container (archive + PDF) opened once per
session; one adaptive byte budget; adaptive prefetch; one pipeline behind `IReaderPageSource`;
benchmark + perf-regression harness. Phases 2–4 are out of scope for this plan.

---

## Status (2026-09-09, branch `claude/reader-pipeline`) — all four phases landed

**Phase 1 done.** Steps 1–8 all complete: `IComicAccessorSession` for every archive engine
(7z with solid forward-mark, SharpZipLib, SharpCompress, tar) + held-open PDFium `PdfDocument`;
`ReaderImagePipeline`/`IReaderPageSource` (one bg loop, three byte-bounded `Cache<K,T>` tiers,
adaptive prefetch); `AppSettings.ReaderMemoryLimitMb` + no-op-`Down()` migration (**Preferences
control still not wired** — Auto works without it); all three readers rewired, the 3 one-shot
decoders moved to `PageDecodeCore.DecodeSinglePage`, `PageImageDecoder`/`PageDecodeService`
deleted; `Paperbunkr.Benchmarks` (BenchmarkDotNet).

**Phase 2 done.** 2560px paged display cap; zoom **detail tier** (`ActivePageIndex` +
`PageCanvas` settle-timer, strictly additive); **cross-issue raw-bytes retention** (process-wide
`SharedRawCache`). Full async-paged swap-on-`PageReady` not done — detail tier + prefetch cover it.

**Phase 3: contained slice done + full design doc.** `PageDecodeCore.TryDecodeScaled` (SKCodec
scaled decode, no full-native intermediate) is the primary continuous decode path. Per-band
progressive decode remains `2026-09-09-reader-webtoon-strip-band-decode-design.md`'s own pass.

**Phase 4 done.** `SkiaBitmapConverter` output cached per-frame for continuous + paged;
WebP/HEIF/etc. route through the engine's `ConvertToJpeg`; dead decoders removed; `ReaderPerfStats`
+ `ReaderFrameStats` overlay (Ctrl+Shift+P).

**~1000 reader/cover/plugin/data tests green across targeted runs; full solution builds clean.**
**GUI-UNVERIFIED — a manual or FlaUI pass on the running reader is the one gate before merge**
(no computer-use for this project).

<details><summary>original step table</summary>

| Step | State |
|---|---|
| 1 — `IComicAccessorSession` + 7z session | done — 7z solid forward-mark |
| 2 — non-default engine sessions | done — zip / sharpcompress / tar |
| 3 — `IPdfDocumentSession` | done — held-open PDFium |
| 4 — cache value types | done |
| 5 — `ReaderImagePipeline` + `IReaderPageSource` | done — one bg loop, no window pinning |
| 6 — `ReaderMemoryLimitMb` + migration | done — Preferences UI control not wired |
| 7 — rewire consumers | done — old decoders deleted |
| 8 — `Paperbunkr.Benchmarks` + perf asserts | done |

</details>

---

## Step 1 — Engine: `IComicAccessorSession` + `SevenZipEngine` session
**Files:**
- `src/Paperbunkr.Engine/IO/Provider/Readers/IComicAccessorSession.cs` (new)
- `src/Paperbunkr.Engine/IO/Provider/Readers/IComicAccessor.cs` (edit — add `bool SupportsSession { get; }` + `IComicAccessorSession? OpenSession(string source)`, default-implemented to `false`/`null` on the interface so existing accessors need no change)
- `src/Paperbunkr.Engine/IO/Provider/Readers/Archive/SevenZipEngine.cs` (edit — `SupportsSession => true`; `OpenSession` returns a `SevenZipAccessorSession` holding the `IDisposable`+`IInArchive` from the existing private `OpenArchive`; forward-mark range extract per §4.2, `GetFileData(IInArchive,int)` logic reused/made accessible)
- `src/Paperbunkr.Engine/IO/Provider/Readers/Archive/SevenZipAccessorSession.cs` (new, or nested in SevenZipEngine)

**What:** The default engine for `.cbz`/`.cbr`/`.cb7`. Session opens 7z.dll `IInArchive` once; `ReadPageBytes(i)` extracts via `archive.Extract([i], …)` (7z.dll handles solid positioning). Keep a high-water mark; a forward request extracts the contiguous `[mark+1..i]` range in one call and the session raises each entry's bytes through a callback so the pipeline can cache them.
**Depends on:** none
**Verify:** new `SevenZipAccessorSessionTests` in `Paperbunkr.Engine.Tests` (check it exists; else `Paperbunkr.App.Tests`) — open a synthetic `.cbz`/`.cb7`, read all pages sequentially, assert one `IInArchive` open (inject a counter) and bytes match `ReadByteImage`.

## Step 2 — Engine: session impls for the non-default engines + tar
**Files:**
- `.../Archive/ZipSharpZipEngine.cs` (edit + `ZipSharpAccessorSession` — held `ZipFile`, `GetInputStream(index)`)
- `.../Archive/SharpCompressEngine.cs` (edit + `SharpCompressAccessorSession` — held `IArchive` for non-solid; forward `IReader`/`RarReader` for solid)
- `.../Archive/TarSharpZipEngine.cs` (edit — session via SharpCompress `TarArchive` on a retained seekable stream, replacing the forward-only `TarInputStream` for the session path only)

**What:** Cover the `CbzUses`/`CbrUses`/`Cb7Uses` non-default settings and `.cbt`. Same `IComicAccessorSession` contract.
**Depends on:** Step 1 (interface)
**Verify:** parametrised engine tests over each configured engine.

## Step 3 — Engine: `IPdfDocumentSession` + `PdfiumReaderEngine` session
**Files:**
- `src/Paperbunkr.Engine/IO/Provider/Readers/Pdf/IPdfDocumentSession.cs` (new)
- `.../Pdf/PdfiumReaderEngine.cs` (edit — `OpenSession` holds one `PdfDocument`; `RenderPageBytes(index, targetLongEdgePx)` renders by index; `CalculateSize` honours the target hint)
- `.../Pdf/PdfiumAccessorSession.cs` (new)

**What:** Held `FPDF_DOCUMENT` for the session; per-page render only, no re-parse. All `FPDF_*` calls documented as caller-serialised.
**Depends on:** none (parallel to 1–2)
**Verify:** engine test with a synthetic 1-page PDF fixture (check for an existing PDF fixture helper; `PdfPageReaderScreen` tests may have one).

## Step 4 — App: cache value types + `Cache<K,T>` adoption
**Files:**
- `src/Paperbunkr.App/Services/Reader/PageId.cs` (new — `readonly record struct PageId(string Container, long ContainerStamp, int Index, PageTier Tier)`)
- `src/Paperbunkr.App/Services/Reader/ReaderBitmap.cs` (new — wraps `Avalonia.Media.Imaging.Bitmap`, `: IDataSize`, `DataSize => W*H*4`, `IDisposable`)
- `src/Paperbunkr.App/Services/Reader/RawPageBytes.cs` (new — `: IDataSize`, `DataSize => Bytes.Length`)
- `src/Paperbunkr.App/Services/Reader/ReaderPageHandle.cs` (new — wraps `IItemLock<ReaderBitmap>`)

**What:** The typed payloads for `Cache<PageId,T>` (from `Paperbunkr.Common`, already referenced). `IDataSize` is `cYo.Common.ComponentModel.IDataSize` (`int DataSize`).
**Depends on:** none
**Verify:** `ReaderCacheValueTests` — `DataSize` math; `Cache<K,T>` eviction drops+disposes past `SizeCapacity`; `MinimalTimeInCache` behaviour understood (spec §5.4 flags a unit check — verify `Machine.Ticks` semantics here and set `MinimalTimeInCache` explicitly).

## Step 5 — App: `ReaderImagePipeline` + `IReaderPageSource`
**Files:**
- `src/Paperbunkr.App/Services/Reader/IReaderPageSource.cs` (new — the §3.1 interface)
- `src/Paperbunkr.App/Services/Reader/ReaderImagePipeline.cs` (new — orchestrator)
- `src/Paperbunkr.App/Services/Reader/IReaderByteSource.cs` + `ArchiveByteSource.cs` / `PdfByteSource.cs` / `ProviderByteSource.cs` (new — wrap the engine sessions from Steps 1–3; `ProviderByteSource` is the fallback over the existing `ImageProvider` for exotic formats and for `SupportsSession == false`)
- `src/Paperbunkr.App/Services/Reader/RawBytesCache.cs`, `DisplayDecodeWorkers.cs`, `PrefetchCoordinator.cs` (new — or folded into `ReaderImagePipeline` if small)
- `src/Paperbunkr.App/Services/PageDecodeCore.cs` (edit — reuse its raw-bytes→`Bitmap` decode + downsample; the GDI/PNG fallback stays for now, Phase 4 trims it)

**What:** byte source → `RawBytesCache` (Cache) → N decode workers draining hi/lo `Channel<DecodeRequest>` → `DisplayCache` (Cache, byte-bounded) + `ThumbnailCache` (Cache). `SetActiveWindow` → coordinator computes enqueue/evict + adaptive radius (§8). `TryGet` non-blocking; `PageReady` event. Window-staleness cancellation (port `PageDecodeService`'s `_window` snapshot pattern). 1 reader thread / `clamp(ProcessorCount−1,1,4)` decoders.
**Depends on:** Steps 1–4
**Verify:** `ReaderImagePipelineTests` (`[Collection(AvaloniaTestCollection)]`, `CbzFixture`):
- decode never on the calling/UI thread (dispatcher-affinity probe)
- session opened once for a full sequential read (injected counter)
- `DisplayCache` bytes ≤ `SizeCapacity` + 1 in-flight under a scripted 200-page walk at 64 MiB budget
- LRU: after walking away, resident set == window ∪ fringe
- `TryGet` returns null then non-null after `PageReady`

## Step 6 — App: `ReaderMemoryBudget` + setting + Preferences control
**Files:**
- `src/Paperbunkr.App/Services/Reader/ReaderMemoryBudget.cs` (new — `clamp(0.25*GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, 128MiB, 512MiB)`, override from setting, split per §5.3)
- `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit — `int? ReaderMemoryLimitMb { get; set; }`)
- `src/Paperbunkr.Data/Migrations/*_AddReaderMemoryLimitMb.cs` (new via `dotnet ef migrations add` — **hand-verify `Down()` is a no-op `DropColumn`… actually per the standing rule (memory: migration-rollback orphan-column bug) a new AppSettings column needs `Down()` that does NOT drop → confirm the exact required shape against the last AppSettings-column migration**)
- `src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml` + `.axaml.cs` (edit — add the Auto/512/1024 control)
- `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit — bind it)

**What:** Auto default + 3-value override; one shared budget pushed to the caches' `SizeCapacity`.
**Depends on:** Step 4 (caches consume it); Step 5 (pipeline owns it)
**Avalonia:** load `avalonia` router → `avalonia-pro-max` preferences/forms subskill before touching `ReaderSection.axaml`; run `review-checklist` after (hardcoded-hex / skin-reactivity check).
**Verify:** migration up→down→up test (per the project's migration-test antipattern note — assert against a fresh DB, not up-down-up on one); `AppSettings` round-trip test; Prefs VM test.

## Step 7 — App: rewire the three consumers
**Files:**
- `src/Paperbunkr.App/Services/IPageImageDecoder.cs` (edit — either replace with `IReaderPageSource` or keep as a thin adapter over it; decide during impl based on blast radius)
- `src/Paperbunkr.App/Views/PageCanvas.cs` (edit — `Decoder` property type; continuous path `SetVirtualizationWindow`→`SetActiveWindow`; `TryGetCachedPage`→`TryGet`; `BackgroundDecodeCompleted`→`PageReady`)
- `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit — `_decoder` type; `Load` picks `ReaderImagePipeline` for both modes; `RefreshCurrentPage` async — `SetActiveWindow` + `TryGet` + swap on `PageReady`; `LastPageRead` write already on a background path — confirm; double-page lookahead via `TryGet`)
- `src/Paperbunkr.App/ViewModels/PdfPageReaderScreenViewModel.cs` (edit — same, PDF byte source)
- delete `PageImageDecoder.cs` / `PageDecodeService.cs` once nothing references them (or keep one pass as adapters, remove in a follow-up commit)

**What:** One pipeline behind the existing `PageCanvas.Decoder` seam. Paged turn becomes non-blocking (old page stays until `PageReady`, light spinner past ~120 ms).
**Depends on:** Steps 5–6
**Avalonia:** `PageCanvas` is a custom `Control` + `CompositionCustomVisualHandler` — check `avalonia-pro-max` rendering/components subskill for the composition-visual message contract before editing.
**Verify:** migrate `PageImageDecoderTests` + `PageDecodeServiceTests` onto the new seam, keep green; full targeted `--filter` reader test run (never the whole suite — memory: headless flake); **manual GUI smoke is the only real proof for the turn-to-turn feel — flag for the user / a FlaUI UiTest, per the no-computer-use rule.**

## Step 8 — Benchmarks + perf-regression asserts
**Files:**
- `src/Paperbunkr.Benchmarks/Paperbunkr.Benchmarks.csproj` (new — net10, `<OutputType>Exe</OutputType>`, BenchmarkDotNet pkg, refs App)
- `src/Paperbunkr.Benchmarks/Program.cs` + `ReaderPipelineBenchmarks.cs` (new — `[GlobalSetup]` procedural archives per §12.1: manga 1600×2560×180, comic 2048×3072×24, webtoon 800×12000×20 `.cbz`, solid `.cb7` ×60; one tiny checked-in solid `.cbr` under `tests/fixtures/` for the RAR forward-mark correctness test only)
- `Paperbunkr.sln` (edit — add the project)
- `src/Paperbunkr.App.Tests/Reader/PipelinePerfRegressionTests.cs` (new — the §12.2 asserts consolidated)
- `src/Paperbunkr.App.Tests/…` throttled-mode hook (`--reader-throttle` / env var → 1 worker + injected read latency)

**What:** `[MemoryDiagnoser]` on cold-open / sequential-flip / random-seek / webtoon-scroll-sim; cache-hit ratio; record the first run as the committed baseline in the plan or a `benchmarks/BASELINE.md`.
**Depends on:** Step 5 (pipeline)
**Verify:** `dotnet run -c Release --project src/Paperbunkr.Benchmarks` completes and emits a summary; perf tests pass in the targeted filter.

---

## Ordering & parallelism

```
1 ─┐
2 ─┼─→ 5 ─┬─→ 6 ─→ 7
3 ─┘      └─→ 8
4 ────────┘
```
Steps 1–4 are independent. 5 is the keystone. 6/7/8 follow 5 (6 before 7). Land each step as its
own commit on `claude/reader-pipeline`; `docs/alpha-todo.md` gets a Beta-backlog entry when Phase 1
merges (not per-step).

## Risks / watch-items
- **7z.dll `Extract` range semantics** — confirm a multi-index `Extract` call streams each entry to
  its own callback sink (Step 1 spike first).
- **`Cache<K,T>.MinimalTimeInCache` unit** — spec §5.4; resolve in Step 4 before building on it.
- **New `.axaml` build gotcha** (CLAUDE.md) — Step 6 adds no new `x:Class`, only edits
  `ReaderSection.axaml`; safe, but if a new View appears, add its `.cs` same commit.
- **Shared working tree** — worktree isolates us; do **not** run the app or `dotnet ef database
  update` from here (memory: worktree shares the user DB). `migrations add` is code-only, safe.
- **Whole-suite test flake** (memory) — always `--filter`, never a bare `dotnet test`.
