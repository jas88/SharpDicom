---
phase: 14-de-identification
plan: 04
subsystem: deidentification
tags: [pixel-data, phi-detection, overlay-planes, modality]

# Dependency Graph
requires:
  - 14-02  # Core de-identification types
provides:
  - IBurnedInPhiDetector interface
  - PhiRegion and PhiDetectionResult types
  - HeuristicPhiDetector implementation
  - BurnedInPhiRegions modality definitions
  - OverlayPlaneProcessor for 60xx groups
  - HighRiskModalities classification
affects:
  - 14-06  # Pixel cleaning implementation

# Tech Tracking
tech-stack:
  added: []
  patterns:
    - "Heuristic region detection by modality"
    - "Overlay plane (60xx group) processing"
    - "Confidence-scored PHI regions"

# File Tracking
key-files:
  created:
    - src/SharpDicom/Deidentification/PixelCleaner/IBurnedInPhiDetector.cs
    - src/SharpDicom/Deidentification/PixelCleaner/BurnedInPhiRegions.cs
    - src/SharpDicom/Deidentification/PixelCleaner/HeuristicPhiDetector.cs
    - src/SharpDicom/Deidentification/PixelCleaner/OverlayPlaneProcessor.cs
  modified: []

# Decisions
decisions:
  - id: 14-04-001
    choice: "Modality-specific region templates"
    why: "Different equipment vendors place burned-in text in predictable locations"
  - id: 14-04-002
    choice: "70% confidence for heuristic regions"
    why: "Indicates uncertainty without OCR verification"
  - id: 14-04-003
    choice: "Relative coordinates with negative offsets"
    why: "Support anchoring regions to any edge of variable-size images"

# Metrics
duration: 15m
completed: 2026-02-02
test-results:
  total: 1675
  passed: 1650
  failed: 0
  skipped: 25
---

# Phase 14 Plan 04: Burned-in PHI Detection Summary

PHI region detection infrastructure with modality-specific heuristics and overlay plane processing for DICOM de-identification.

## What Was Built

### IBurnedInPhiDetector Interface

Core abstraction for PHI detection in pixel data:

```csharp
public interface IBurnedInPhiDetector
{
    ValueTask<PhiDetectionResult> DetectAsync(
        ReadOnlyMemory<byte> pixelData,
        int width, int height,
        int bitsAllocated, int samplesPerPixel,
        string? modality,
        CancellationToken ct = default);
}
```

Supporting types:
- `PhiRegion`: Record struct with X, Y, Width, Height, Confidence, Source
- `PhiDetectionResult`: Regions list, modality info, BurnedInAnnotation status
- `HighRiskModalities`: Static class with US, SC, XA, ES, RF classification

### HeuristicPhiDetector

Region-based detector without OCR:
- Returns modality-specific PHI-prone regions
- No actual text recognition (fast, deterministic)
- 70% confidence score indicating heuristic detection
- Suitable for conservative masking approaches

### BurnedInPhiRegions

Predefined region templates by modality:

| Modality | Regions |
|----------|---------|
| US (Ultrasound) | Top/bottom banners, all four corners |
| SC (Secondary Capture) | Same as US (worst-case assumption) |
| XA/RF (Angio/Fluoro) | Top/bottom banners, top-left corner |
| CT/MR/DX/CR/MG | Corner regions only (typically minimal burned-in) |
| ES (Endoscopy) | Same as US (similar equipment patterns) |

Region template format:
- Positive coordinates: offset from top-left
- Negative coordinates: offset from bottom-right
- -1 dimension: full width or height

### OverlayPlaneProcessor

Handles DICOM overlay planes (60xx groups):

```csharp
// Detection
GetOverlayGroups(dataset)    // Enumerate all present overlays
HasOverlayPlanes(dataset)    // Quick presence check

// Removal
RemoveAllOverlays(dataset)   // Remove all 60xx elements
RemoveOverlay(dataset, group) // Remove specific overlay

// Modification
ClearOverlayData(dataset)    // Zero out data, keep metadata

// Info
GetOverlayInfo(dataset, group) // Get dimensions, type, label
```

Overlay planes can contain text annotations separate from pixel data and must be processed for complete PHI removal.

## Verification Results

1. **Build**: All targets (netstandard2.0, net8.0, net9.0, net10.0) compile successfully
2. **Tests**: 1650 passed, 0 failed, 25 skipped
3. **High-risk modalities**: US, SC, XA, ES, RF correctly flagged

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] ValueTask.FromResult compatibility**
- **Found during:** Task 2
- **Issue:** `ValueTask.FromResult` not available in netstandard2.0
- **Fix:** Added conditional compilation for netstandard2.0
- **Files modified:** HeuristicPhiDetector.cs

**2. [Rule 3 - Blocking] IReadOnlySet compatibility**
- **Found during:** Task 1
- **Issue:** `IReadOnlySet<T>` not available in netstandard2.0
- **Fix:** Use `HashSet<T>` directly on netstandard2.0
- **Files modified:** IBurnedInPhiDetector.cs

## Next Phase Readiness

**Dependencies Met:**
- IBurnedInPhiDetector interface ready for 14-06 (pixel cleaning)
- OverlayPlaneProcessor ready for integration with DicomDeidentifier
- HeuristicPhiDetector provides baseline detection without external dependencies

**Future Enhancements (not in scope):**
- OCR-based detector implementation
- Machine learning PHI detection
- Vendor-specific region profiles

## Commits

| Hash | Description |
|------|-------------|
| 0050a09 | feat(14-04): add IBurnedInPhiDetector interface and PhiRegion types |
| bf8cf65 | feat(14-04): add OverlayPlaneProcessor for 60xx group handling |

Note: BurnedInPhiRegions.cs and HeuristicPhiDetector.cs were committed alongside 14-03 DateShifter due to linter auto-fixes affecting multiple files.
