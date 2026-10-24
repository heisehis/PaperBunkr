# Publisher mark expansion and rating audit — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-22-publisher-mark-expansion-and-rating-audit-design.md*

## Step 1: Import and catalog the MangaBaka collection
**Files:** `src/Paperbunkr.App/Assets/Icons/Publishers/MangaBaka/` (new), `src/Paperbunkr.App/Assets/Marks/SOURCES.md` (edit)
**What:** Bring in all 121 source logos with MangaBaka publisher IDs and original filenames retained. Record each source URL and third-party-rights status. Preserve the 4 WebP originals and provide a runtime PNG rendition for each; use original SVG, PNG, and JPEG assets for runtime where supported.
**Depends on:** none
**Verify:** Confirm the imported inventory has 121 originals and the manifest maps every row to a readable source file and runtime asset.

## Step 2: Acquire and resolve 50 additional western publisher marks
**Files:** `src/Paperbunkr.App/Assets/Marks/Publishers/` (new assets), `src/Paperbunkr.App/Assets/Icons/Publishers/` (new raster assets), `src/Paperbunkr.App/Assets/Marks/publisher-aliases.tsv` (edit), `src/Paperbunkr.App/Assets/Marks/SOURCES.md` (edit)
**What:** Verify the roster against the actual alias resolver, all 751 ComicRack CE raster files, and the MangaBaka collection. Replace any covered candidates so the final 50 are distinct from both existing assets and the MangaBaka bundle. Obtain official or Commons logos, preserve supported source formats, add canonical rows and aliases, and document the per-logo source/license/attribution.
**Depends on:** Step 1
**Verify:** Reconcile the final 50 roster against both bundled sets and ensure each canonical name resolves to a real asset.

## Step 3: Add missing Australian rating marks and precise classification
**Files:** `src/Paperbunkr.App/Assets/Marks/AgeRatingsAustralia/` (new), `src/Paperbunkr.App/Assets/Marks/age-rating-aliases.tsv` (edit), `src/Paperbunkr.App/Assets/Marks/SOURCES.md` (edit), `src/Paperbunkr.App/Services/MarkResolver.cs` (edit)
**What:** Add the six ComicRack CE Australian PNG marks and route them through the existing raster mark rendering path. Map `G`, `PG`, `M`, `MA15+`, `R18+`, and `X18+` to their Australian marks while keeping `Mature 17+` mapped to ESRB Mature. Preserve all existing ComicInfo strings and ESRB assets.
**Depends on:** none
**Verify:** Inspect the alias rows and asset paths for all 14 ESRB/Australian labels; confirm `Mature 17+` and `M` remain distinct.

## Step 4: Integrate MangaBaka fallback resolution and review the bundle
**Files:** `src/Paperbunkr.App/Services/MarkResolver.cs` (edit), `src/Paperbunkr.App/Services/PublisherIconIndex.cs` (edit only if needed for the separate alias map), publisher asset manifests and source notes (edit)
**What:** Load the MangaBaka pack as a lower-priority bundled publisher source, after the existing user, historical CE, curated SVG, and CE raster sources but before generic letter/text fallback. Keep duplicate MangaBaka source originals bundled without changing the display precedence of existing marks. Compile the app and inspect the resulting asset inventory.
**Depends on:** Steps 1–3
**Verify:** Build the app and review exact file counts, resolver precedence paths, and rating/publisher asset references. Do not run the test suite unless requested.
