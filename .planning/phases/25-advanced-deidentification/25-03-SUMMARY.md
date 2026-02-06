---
phase: 25-advanced-deidentification
plan: 03
subsystem: deidentification
tags: [ocr, tesseract, pixel-data, burned-in-annotation, phi-detection, redaction]
dependency-graph:
  requires: [25-01, 25-02]
  provides: [ocr-scanner, ocr-pipeline-integration, ocr-scan-options, ocr-scan-result]
  affects: [25-04]
tech-stack:
  added: []
  patterns: [lazy-initialization, dual-threshold-confidence, allow-deny-filtering, codec-decompression]
key-files:
  created:
    - src/SharpDicom/Deidentification/OcrScannerOptions.cs
    - src/SharpDicom/Deidentification/OcrScanResult.cs
    - src/SharpDicom/Deidentification/OcrScanner.cs
  modified:
    - src/SharpDicom/Deidentification/DicomDeidentifierBuilder.cs
    - src/SharpDicom/Deidentification/DicomDeidentifier.cs
    - src/SharpDicom/Deidentification/DeidentificationResult.cs
decisions:
  - id: D25-03-01
    description: "Duplicate P/Invoke declarations in SharpDicom (DllImport) rather than referencing SharpDicom.Codecs to avoid circular project dependency"
    rationale: "SharpDicom.Codecs references SharpDicom, not vice versa. OcrScanner needs Tesseract P/Invoke but lives in the main project."
  - id: D25-03-02
    description: "Use DllImport uniformly (not LibraryImport) for the internal TessInterop nested class"
    rationale: "LibraryImport source generator has complications with nested private partial classes. DllImport works on all TFMs with no overhead for an internal class."
  - id: D25-03-03
    description: "OcrScanner lazy-created on first Deidentify() call rather than in Build()"
    rationale: "Avoids paying Tesseract init cost if the de-identifier is created but never used for images with pixel data."
metrics:
  duration: ~10 minutes
  completed: 2026-02-06
---

# Phase 25 Plan 03: OcrScanner for Burned-in PHI Detection Summary

OCR-based burned-in text detection using Tesseract, with dual-threshold confidence, allow/deny filtering, CodecRegistry decompression, and full DicomDeidentifier pipeline integration.

## What Was Built

### OcrScannerOptions and OcrScanResult (Task 1)

**OcrScanModality** flags enum categorises modalities by burned-in annotation risk (HighRisk, ModerateRisk, LowRisk, All), matching the risk categories already established in `BurnedInAnnotationDetector`.

**OcrScannerOptions** provides full configuration:
- Dual confidence thresholds: `ConfidenceThreshold` (0.6 for center) and `EdgeConfidenceThreshold` (0.4 for edges)
- `EdgeMarginPercent` (0.15) defines the edge zone where burned-in text most commonly appears
- `ScanModalities` controls which risk categories trigger scanning
- `DecompressForOcr` enables automatic decompression of compressed pixel data via CodecRegistry
- `Allowlist` with `DefaultNonPhiAllowlist` containing orientation markers, units, medical abbreviations, imaging labels, and directional terms
- `Denylist` for patterns that are always PHI (takes precedence over allowlist)

**OcrDetection** readonly record struct captures each detection (text, confidence, bounding box as RedactionRegion, frame index, edge region flag).

**OcrScanResult** provides all detections, filtered detections (PHI candidates), frame statistics, scan duration, warnings, and `ToRedactionRegions()` for direct PixelDataRedactor integration.

### OcrScanner (Task 2a)

**Pixel data preparation** handles all common formats:
- 8-bit grayscale: direct pass-through with MONOCHROME1 inversion
- 16-bit grayscale: Window Center/Width windowing or full-range normalisation, with MONOCHROME1 inversion
- RGB (3 samples/pixel, 8-bit): ITU-R BT.601 luminance conversion (0.299R + 0.587G + 0.114B)
- Other formats: `NotSupportedException` with descriptive message

**Compressed pixel data** handling via CodecRegistry:
- When `DecompressForOcr` is true: looks up Transfer Syntax, finds registered codec, decompresses all frames
- When false: returns empty result with warning

**Tesseract integration** via internal `TessInterop` nested class:
- DllImport P/Invoke to the same `sharpdicom_codecs` native library
- Fail-fast with `InvalidOperationException` when Tesseract not available
- `FindBundledTessdata()` searches `AppContext.BaseDirectory/tessdata` and `TESSDATA_PREFIX` env var
- Proper lifecycle: `tess_clear()` between frames, `tess_delete()` on dispose

**Allow/deny filtering** reduces false positives:
- Purely numeric strings filtered (measurements like "3.2")
- Denylist takes precedence over allowlist
- Case-insensitive matching

### Pipeline Integration (Task 2b)

**DicomDeidentifierBuilder.WithOcrScanner()** enables OCR scanning via fluent builder pattern.

**Pipeline order** in `DicomDeidentifier.Deidentify()`:
1. OCR scan + pixel redaction (needs original metadata)
2. ProcessDataset (primary PS3.15 de-identification)
3. Date shifting
4. UID reference walking
5. De-identification markers (always last)

**OcrScanner** is lazy-created on first `Deidentify()` call. If Tesseract is unavailable, a warning is added but de-identification continues without OCR.

**DeidentificationSummary** extended with `OcrFramesScanned`, `OcrDetectionsFound`, `OcrPhiCandidates`, `OcrRegionsRedacted`.

## Decisions Made

| ID | Decision | Rationale |
|----|----------|-----------|
| D25-03-01 | Duplicate P/Invoke declarations in SharpDicom rather than referencing SharpDicom.Codecs | Avoids circular project dependency; both call the same native library |
| D25-03-02 | Use DllImport uniformly (not LibraryImport) for TessInterop | LibraryImport source generator complications with nested private partial classes |
| D25-03-03 | Lazy-create OcrScanner on first Deidentify() call | Avoids paying Tesseract init cost when not processing images with pixel data |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] PixelDataInfo ambiguity**
- **Found during:** Task 2a build
- **Issue:** `PixelDataInfo` is defined in both `SharpDicom.Data` and `SharpDicom.Codecs` namespaces
- **Fix:** Added using aliases `DataPixelDataInfo` and `CodecPixelDataInfo` to disambiguate
- **Files modified:** `OcrScanner.cs`
- **Commit:** 8672366

**2. [Rule 3 - Blocking] TransferSyntax has no Name property**
- **Found during:** Task 2a build
- **Issue:** Plan referenced `ts.Name` but TransferSyntax struct has `UID` not `Name`
- **Fix:** Changed to `ts.UID` in warning message
- **Files modified:** `OcrScanner.cs`
- **Commit:** 8672366

**3. [Rule 1 - Bug] CA1861 inline array allocation warnings**
- **Found during:** Task 2a build
- **Issue:** `new[] { "warning message" }` inline arrays would be allocated on every call
- **Fix:** Extracted to `static readonly string[]` fields for common warning messages
- **Files modified:** `OcrScanner.cs`
- **Commit:** 8672366

## Verification

- Full solution builds with zero warnings and zero errors across all TFMs
- All 4225 tests pass (0 failures, 180 skipped as expected)
- Pipeline order verified: OCR -> ProcessDataset -> date shifting -> UID walking -> markers
- Backward compatibility confirmed: existing callers unaffected by optional OCR parameter

## Next Phase Readiness

Plan 25-04 (test suite) can now test:
- OcrScannerOptions defaults and validation
- OcrScanResult.ToRedactionRegions() conversion
- OcrScanner fail-fast when Tesseract unavailable
- Pipeline integration via builder.WithOcrScanner().Build()
- Allow/deny list filtering logic
- Pixel data preparation (8-bit, 16-bit windowing, RGB conversion, MONOCHROME1 inversion)
