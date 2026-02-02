---
phase: 14-de-identification
plan: 07
subsystem: deidentification
tags: [ocr, tesseract, phi-detection, pixel-data]

# Dependency Graph
requires:
  - 14-04  # IBurnedInPhiDetector interface
provides:
  - TesseractPhiDetector OCR-based detection
  - Graceful fallback to heuristics
  - Multi-format image handling
affects:
  - 14-06  # Pixel cleaning can use OCR for better detection

# Tech Tracking
tech-stack:
  added:
    - TesseractOCR 5.5.1 (conditional, net6.0+ only)
  patterns:
    - "Conditional compilation for optional dependencies"
    - "Graceful fallback when native libraries unavailable"
    - "Image format conversion (16-bit, RGB to grayscale)"

# File Tracking
key-files:
  created:
    - src/SharpDicom/Deidentification/PixelCleaner/TesseractPhiDetector.cs
    - tests/SharpDicom.Tests/Deidentification/TesseractPhiDetectorTests.cs
  modified:
    - src/SharpDicom/SharpDicom.csproj
    - Directory.Packages.props

# Decisions
decisions:
  - id: 14-07-001
    choice: "Conditional compilation with TESSERACT_AVAILABLE"
    why: "TesseractOCR has native dependencies not available on netstandard2.0"
  - id: 14-07-002
    choice: "Graceful fallback to HeuristicPhiDetector"
    why: "System must work without Tesseract installed (optional dependency)"
  - id: 14-07-003
    choice: "0.6 default confidence threshold"
    why: "Balance between detecting text and avoiding false positives"
  - id: 14-07-004
    choice: "Combine OCR and heuristic regions"
    why: "OCR provides precision, heuristics provide coverage for undetected areas"

# Metrics
duration: 16m
completed: 2026-02-02
test-results:
  total: 1689
  passed: 1663
  failed: 0
  skipped: 26
---

# Phase 14 Plan 07: TesseractOCR Integration Summary

OCR-based PHI detection using Tesseract, with graceful fallback to heuristics when Tesseract is unavailable.

## What Was Built

### TesseractPhiDetector

OCR-based detector implementing IBurnedInPhiDetector:

```csharp
public sealed class TesseractPhiDetector : IBurnedInPhiDetector, IDisposable
{
    // On net6.0+ with Tesseract available:
    // - Uses TesseractOCR engine for text detection
    // - Returns word-level bounding boxes with confidence scores
    // - Merges overlapping OCR regions
    // - Combines with heuristic regions for comprehensive coverage

    // On netstandard2.0 or when Tesseract unavailable:
    // - Falls back to HeuristicPhiDetector
    // - No OCR performed

    public bool IsOcrAvailable { get; }  // Check if OCR is operational
}
```

### Conditional Compilation

TesseractOCR only available on modern .NET:

```xml
<!-- In SharpDicom.csproj -->
<PropertyGroup Condition="'$(TargetFramework)' != 'netstandard2.0'">
  <DefineConstants>$(DefineConstants);TESSERACT_AVAILABLE</DefineConstants>
</PropertyGroup>

<ItemGroup Condition="'$(TargetFramework)' != 'netstandard2.0'">
  <PackageReference Include="TesseractOCR" />
</ItemGroup>
```

### Image Format Handling

Converts various DICOM pixel formats to 8-bit grayscale for OCR:

| Input Format | Conversion |
|-------------|------------|
| 8-bit grayscale | Direct copy |
| 16-bit grayscale | High byte extraction |
| 8-bit RGB | ITU-R BT.601 luma (0.299R + 0.587G + 0.114B) |

### Graceful Fallback

Detection strategy when Tesseract unavailable:

1. Constructor catches initialization errors silently
2. `IsOcrAvailable` returns `false`
3. `DetectAsync` delegates to `HeuristicPhiDetector`
4. Returns modality-specific regions without OCR

### Region Combination

When OCR is available, combines detection sources:

1. Run OCR to get word-level bounding boxes
2. Filter by confidence threshold (default 0.6)
3. Merge overlapping OCR regions
4. Get heuristic regions for modality
5. Add non-overlapping heuristic regions
6. Return combined result

## Test Coverage

13 unit tests covering:

- Constructor with invalid/null paths
- Fallback behavior verification
- Image format conversion (8-bit, 16-bit, RGB)
- High-risk modality detection
- Cancellation token handling
- Interface implementation checks
- Dispose pattern (multiple calls)
- Explicit integration test for real Tesseract

## Tesseract Requirements

For OCR to function, users need:

1. **tessdata folder** with trained data files
2. **eng.traineddata** at minimum (from tesseract-ocr/tessdata or tessdata_best)
3. **TESSDATA_PREFIX** environment variable or explicit path to constructor

Without these, system automatically falls back to heuristic detection.

## Verification Results

1. **Build**: All targets (netstandard2.0, net8.0, net9.0, net10.0) compile successfully
2. **Tests**: 1689 total (1663 passed, 26 skipped)
3. **Interface**: TesseractPhiDetector properly implements IBurnedInPhiDetector
4. **Fallback**: Works correctly when Tesseract not installed

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] CA1822 analyzer warning on netstandard2.0**
- **Found during:** Task 2 build
- **Issue:** `IsOcrAvailable` property always returns false on netstandard2.0, triggering CA1822
- **Fix:** Linter auto-fixed by referencing `_fallback` field in expression
- **Files modified:** TesseractPhiDetector.cs

**2. [Rule 2 - Missing Critical] TesseractOCR API differences**
- **Found during:** Task 2 implementation
- **Issue:** Plan used hypothetical API (`TesseractEngine`, `Pix.LoadFromMemory`, `GetIterator`)
- **Fix:** Used actual TesseractOCR API (`Engine`, `Image.LoadFromMemory`, `page.Layout`)
- **Files modified:** TesseractPhiDetector.cs

**3. [Rule 2 - Missing Critical] eng.traineddata existence check**
- **Found during:** Task 2 implementation
- **Issue:** Constructor would fail if tessdata folder exists but eng.traineddata missing
- **Fix:** Added explicit check for eng.traineddata file before creating Engine
- **Files modified:** TesseractPhiDetector.cs

## Next Phase Readiness

**Dependencies Met:**
- TesseractPhiDetector available for pixel cleaning in 14-06
- Graceful degradation ensures system works without external dependencies
- Tests verify both OCR and fallback paths

**Usage Example:**
```csharp
// With Tesseract (best accuracy)
using var detector = new TesseractPhiDetector("/path/to/tessdata");

// Without Tesseract (heuristic fallback)
using var detector = new TesseractPhiDetector();  // Uses fallback

var result = await detector.DetectAsync(
    pixelData, width, height, 8, 1, "US");

foreach (var region in result.Regions)
{
    // region.Source is "OCR" or "Heuristic"
    Console.WriteLine($"{region.Source}: ({region.X},{region.Y}) {region.Width}x{region.Height}");
}
```

## Commits

| Hash | Description |
|------|-------------|
| f957da9 | feat(14-07): add TesseractPhiDetector for OCR-based PHI detection |
| 82a072f | test(14-07): add TesseractPhiDetector tests |
