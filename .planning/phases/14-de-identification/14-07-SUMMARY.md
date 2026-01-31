---
phase: 14
plan: 07
subsystem: deidentification
tags: [pixel-data, redaction, burned-in-annotation, clean-pixel-data]

dependency-graph:
  requires: [14-05]
  provides: [PixelDataRedactor, RedactionRegion, BurnedInAnnotationDetector]
  affects: [14-08]

tech-stack:
  added: []
  patterns: [region-based-redaction, modality-risk-assessment]

key-files:
  created:
    - src/SharpDicom/Deidentification/PixelDataRedactor.cs
    - src/SharpDicom/Deidentification/RedactionRegion.cs
    - src/SharpDicom/Deidentification/RedactionOptions.cs
    - src/SharpDicom/Deidentification/BurnedInAnnotationDetector.cs
    - tests/SharpDicom.Tests/Deidentification/PixelDataRedactorTests.cs
  modified:
    - src/SharpDicom/Data/DicomTag.WellKnown.cs

decisions:
  - id: static-redactor-api
    description: Made RedactRegions a static method since no instance state needed
    rationale: Simpler API, no object lifecycle management required

metrics:
  duration: ~15 minutes
  completed: 2026-01-29
---

# Phase 14 Plan 07: Pixel Data Redaction Summary

**One-liner:** Region-based pixel data redaction for burned-in annotation removal with modality-aware risk detection.

## What Was Built

### RedactionRegion (Task 1)
Struct defining rectangular regions for redaction:
- X, Y coordinates (0-based, top-left origin)
- Width and Height in pixels
- Optional Frame index for frame-specific redaction
- Factory methods: TopBar, BottomBar, LeftBar, RightBar, FromCorners
- IEquatable implementation for value comparison

### RedactionOptions (Task 1)
Configuration class for redaction operations:
- Regions list with IReadOnlyList interface
- FillValue (uint) for 8/16/32-bit pixels, RGB color encoding
- UpdateBurnedInAnnotationTag toggle
- SkipCompressed flag for encapsulated pixel data handling
- Presets: UltrasoundDefault, SecondaryCapture, Endoscopy, FullImage

### PixelDataRedactor (Task 2)
Static class implementing Clean Pixel Data Option:
- `RedactRegions(DicomDataset, RedactionOptions)` entry point
- Supports 8-bit, 16-bit, 32-bit pixel depths
- Handles grayscale (1 sample) and RGB (3 samples) images
- Multi-frame support with per-frame or all-frame redaction
- Automatic region clamping to image bounds
- Updates BurnedInAnnotation (0028,0301) tag to "NO"
- Returns RedactionResult with statistics and warnings

### BurnedInAnnotationDetector (Task 3)
Static class for modality risk assessment:
- High risk: US, ES, SC, XC, GM, SM, OP, OPT, ECG, HD
- Moderate risk: XA, RF, MG, DX, CR, PX, IO
- Checks BurnedInAnnotation tag value
- Analyzes ImageType for SECONDARY/CAPTURE indicators
- Case-insensitive modality matching
- SuggestRedactionOptions() provides modality-appropriate presets

### Well-Known Tags Added
- DicomTag.Modality (0008,0060)
- DicomTag.ImageType (0008,0008)
- DicomTag.BurnedInAnnotation (0028,0301)

## Technical Details

### Pixel Fill Algorithm
```csharp
// 8-bit grayscale: direct byte assignment
// 8-bit RGB: R from bits 16-23, G from 8-15, B from 0-7
// 16-bit: little-endian write of lower 16 bits
// 32-bit: little-endian write of full uint
```

### Multi-Frame Handling
- Frame size calculated from PixelDataInfo.FrameSize
- Each frame processed sequentially
- Region.Frame filters to specific frame (null = all frames)
- FramesModified count tracks actual changes

### netstandard2.0 Compatibility
- Custom HashCode polyfill for RedactionRegion
- ContainsIgnoreCase helper for string matching
- Conditional ThrowIfNull patterns

## Tests Added

55 new tests in PixelDataRedactorTests.cs:
- Single/multiple region redaction
- 8-bit, 16-bit, 32-bit pixel depth handling
- RGB color fill verification
- Little-endian byte order verification
- Multi-frame with all-frames and frame-specific regions
- Bounds clamping (negative coords, exceeding dimensions)
- BurnedInAnnotation tag update verification
- BurnedInAnnotationDetector risk level detection
- Modality-based risk assessment
- ImageType-based detection (SECONDARY, CAPTURE)

## Files Created/Modified

| File | Change |
|------|--------|
| RedactionRegion.cs | New - region specification struct |
| RedactionOptions.cs | New - redaction configuration |
| PixelDataRedactor.cs | New - redaction implementation |
| BurnedInAnnotationDetector.cs | New - risk detection |
| PixelDataRedactorTests.cs | New - comprehensive tests |
| DicomTag.WellKnown.cs | Added Modality, ImageType, BurnedInAnnotation |

## Commits

| Hash | Message |
|------|---------|
| 9ac592f | feat(14-07): add redaction region types for pixel data redaction |
| 6bd4390 | feat(14-07): add PixelDataRedactor for burned-in annotation removal |
| a3507ce | test(14-08): add de-identification integration tests (includes BurnedInAnnotationDetector) |

## Deviations from Plan

None - plan executed exactly as written.

## Next Phase Readiness

Ready for 14-08 (Integration and Documentation):
- PixelDataRedactor API complete and tested
- BurnedInAnnotationDetector provides risk assessment
- Clean Pixel Data Option can be integrated into DicomDeidentifier
- Modality-aware default redaction regions available
