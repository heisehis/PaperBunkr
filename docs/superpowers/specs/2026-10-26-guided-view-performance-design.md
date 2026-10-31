# Guided View Detection Performance

Status: implemented in the working tree, 2026-10-26. Paperbunkr-original behavior: ComicRack CE has no panel detection or guided panel view to match.

## Problem

Cold panel analysis currently takes about 250–340 ms per regular page on the measured CPU, with tall strips around 150 ms. Guided View already requests detection in the background. However, smart double-click calls `EnsurePanels` synchronously when the current page is not cached; “Show detected panels” and “Report bad panel detection” also reach that synchronous path. This can freeze the reader during an interaction. A background Guided View request and a synchronous request can also analyze the same page twice because the cache is populated only after work finishes.

The goal is reader responsiveness. This delivery records cold latency and keeps inference off the UI thread; it does not promise a hardware-independent cold-time target or change panel accuracy.

## Approved design

### Detection work

- Keep the existing eight-entry in-memory cache keyed by issue, page, and reading direction.
- Route all panel requests through one background coordinator. Requests for the same key share one in-flight result; no caller starts a duplicate analysis while that result is pending.
- Run at most one panel inference at a time. If the page changes, discard queued work for pages that are no longer current. Let an inference already inside ONNX finish; it cannot be reliably interrupted. Cache and display its result only if the same issue/page/direction is still current.
- Continue requesting the visible page when Guided View is on. Do not prefetch neighboring pages as part of this delivery.
- Keep analysis and UI state changes off the wrong thread: image analysis and report-file writing run in background work; bound properties, toast requests, and canvas changes return to the UI dispatcher.
- Measure elapsed cold analysis time at the shared request boundary in the reader's existing performance overlay (p50/p99/sample count). Do not add a persistent performance log or change the detector/model in this pass.

### Smart double-click

- A cold-page smart double-click records the click position and page identity, requests shared analysis, and is considered handled so the normal immediate 200% fallback does not race ahead.
- When analysis finishes, apply the existing panel-under-pointer behavior only if the same page is still current and the user has not changed the relevant view state in the meantime.
- If the detection is unconfident, apply the existing plain zoom-at-pointer fallback. If the page or view has changed while waiting, discard the pending interaction without affecting the new state.
- Cached results keep the current immediate behavior. Guided View’s panel/whole-page toggle and its existing restore behavior do not change.

### Tuning actions

- “Show detected panels” requests the shared asynchronous result. It gives brief pending feedback, then shows the existing numbered overlay only if the requested page is still visible. The existing three-second overlay lifetime remains unchanged.
- “Report bad panel detection” owns a stable snapshot of the requested page and completes its report even if the reader navigates away. If it joins a request that is still queued, the worker uses the owned snapshot. Detection, PNG/JSON writing, and success/error feedback happen asynchronously. The report contains the same page, issue/page identity, reading direction, model-availability flag, confidence, and panel rectangles as today.
- If report capture itself fails, show the existing failure feedback and do not create a partial report.

## Scope boundaries

- No changes to panel detection accuracy, ONNX model/session configuration, image scaling quality, panel ordering, guided navigation, cache capacity, reading modes, or detector persistence.
- No neighboring-page prefetch, GPU provider, persistent detections, or general reader decode/cache redesign.
- Existing whole-page fallback remains the behavior for detector failure or an unconfident result.
- No ComicRack CE behavior is inferred; CE has no guided panel detection.

## Acceptance checks

- No cold detection path invoked by smart double-click, “Show detected panels,” or “Report bad panel detection” performs inference on the UI thread.
- Duplicate requests for the same issue/page/direction share one task; at most one detector runs at once; stale queued pages are skipped.
- Smart double-click honors its original click after a confident result, uses the existing plain zoom when detection is unconfident, and never changes a different page’s view.
- “Show detected panels” overlays only the page for which it was requested. A report completes with its original page after navigation.
- Existing in-memory cache behavior and Guided View navigation semantics remain intact.
- Build the main `Paperbunkr.App` Debug configuration. Native, screen-reader, and visual review remain separate follow-up checks.

## Implementation notes

The current canvas expects a synchronous panel result for smart double-click. The implementation must therefore preserve the pending click as canvas interaction state and consume it when the bound panel result arrives; merely changing `EnsurePanels` to fire-and-forget is incorrect. Existing Avalonia input remains in the canvas’s established pointer handling. View-model work and notifications follow the project’s MVVM/dispatcher patterns.
