# Comic reader — Image quality (slice C) — Implementation Plan
*Implements: `2026-09-26-comic-reader-image-quality-design.md`.*

**Status 2026-09-26:** all steps built and unit tested (see the spec's "Implementation notes" for deviations); **on-screen checks by the user outstanding**. Uncommitted, shared working tree (slices A/B/D/F/G and other
sessions): edit narrowly, never revert another hunk. Test runs: check for a running Paperbunkr first (it locks the normal output), build the tests to a scratch folder (`-o`) and run that dll, redirect output to a log. The migration's
`Up()` is read before it is kept. Avalonia UI steps run the `avalonia-pro-max` review checklist.

1. **#25 measurement** (`ScreentoneDownscaleTests`), then the fix it points at (`ReaderPageVisualHandler.SamplingFor` on the leased-canvas draws).
2. **Data:** `AppSettings` (3 columns), `Issue` (2 override columns), `PageCropMode` + `PageCropOverride` + table, migration `AddImageQualitySettings`, Data tests.
3. **Pure processing:** `ImageAdjustmentMath` (levels matrix, `NeedsLevels`, sharpen kernel, `IsIdentity` with sharpen), `PageLumaGrid`, `PageLevelsAnalyzer`, `PageCropDetector`, `PageImageProcessor`. Tests: `PageImageProcessingTests`.
4. **Pipeline:** `PageId.Variant`, `IReaderPageProcessing`, `SetProcessing`/`DetectCrop`, processing before the downscale in the display and detail tiers. Tests in the same file.
5. **Render and view model:** sharpen filter in the handler (`AdjustmentVisualData.Sharpen`, canvas `Sharpen`, `ProcessingVersion`), `ReaderScreenViewModel.ImageQuality.cs` (settings layering, overrides, reset, session crop switch, per-page crop, Show crop), profile fields, palette, page menu, drawer controls. Tests: `ReaderImageQualityTests`.
6. **Preferences, docs, wiki, memory,** review checklist.

## Checklist
- [x] 1 #25  - [x] 2 Data  - [x] 3 Pure processing  - [x] 4 Pipeline  - [x] 5 Render and view model  - [x] 6 Preferences and docs
