---
phase: 14-de-identification
plan: 05
subsystem: deidentification
status: complete
completed: 2026-02-02
duration: ~18min
tags: [pixel-cleaning, batch-processing, burned-in-phi, overlay-planes]
tech-stack:
  patterns: [async-await, iasyncenumerable, parallel-foreach]
dependency-graph:
  requires: ["14-03", "14-04"]
  provides: ["PixelDataCleaner", "StudyDeidentifier"]
  affects: ["14-06", "14-07"]
key-files:
  created:
    - src/SharpDicom/Deidentification/PixelCleaner/PixelDataCleaner.cs
    - src/SharpDicom/Deidentification/StudyDeidentifier.cs
  modified:
    - src/SharpDicom/Deidentification/DicomDeidentifier.cs
    - src/SharpDicom/Deidentification/PixelCleaner/TesseractPhiDetector.cs
decisions:
  - id: d1
    title: "DicomBinaryElement for cleaned pixel data"
    context: "Need to replace pixel data with cleaned bytes"
    decision: "Use DicomBinaryElement with same VR as original DicomPixelDataElement"
    rationale: "Maintains VR consistency while allowing raw byte replacement"
  - id: d2
    title: "Async ApplyAsync with pixel cleaning"
    context: "PixelDataCleaner.CleanAsync is async for lazy-loaded pixel data"
    decision: "ApplyAsync becomes truly async, not sync-returning ValueTask"
    rationale: "Enables proper async pattern for pixel data loading"
  - id: d3
    title: "ContainsIgnoreCase for SafeModalities"
    context: "Need to check if modality is in safe list"
    decision: "Custom helper instead of LINQ for netstandard2.0 compatibility"
    rationale: "Avoids StringComparison issues in Contains for older frameworks"
metrics:
  tasks-completed: 3
  commits: 3
  files-created: 2
  files-modified: 2
  tests-passing: 1663
  tests-skipped: 26
---

# Phase 14 Plan 05: Pixel Cleaning Integration Summary

Pixel cleaning integration into DicomDeidentifier with StudyDeidentifier for batch processing

## One-liner

PixelDataCleaner for region replacement, DicomDeidentifier pixel cleaning integration, and StudyDeidentifier for multi-file batch processing with shared context

## Completed Tasks

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | PixelDataCleaner for region replacement | de02c4c | PixelDataCleaner.cs, TesseractPhiDetector.cs |
| 2 | Integrate pixel cleaning into DicomDeidentifier | 28e6b7a | DicomDeidentifier.cs |
| 3 | StudyDeidentifier for batch processing | 9f7d492 | StudyDeidentifier.cs |

## Key Implementations

### PixelDataCleaner (Task 1)

Span-based pixel region cleaning for burned-in PHI:

```csharp
// Clean regions in-place
PixelDataCleaner.Clean(
    pixelData: imageBytes,
    width: 512, height: 512,
    bitsAllocated: 16,
    samplesPerPixel: 1,
    regions: detectedRegions,
    replacement: PixelReplacementValue.Black);

// Async dataset-level cleaning
await PixelDataCleaner.CleanAsync(dataset, detector, options, ct);
```

Features:
- **8-bit and 16-bit support**: Handles both common pixel depths
- **Multiple replacement modes**: Black, White, AverageOfRegion
- **Bounds clamping**: Safely handles regions extending beyond image
- **DicomPixelDataElement integration**: Uses proper lazy-loading API

### DicomDeidentifier Integration (Task 2)

Updated de-identification engine with pixel data processing:

```csharp
var options = new DeidentificationOptions
{
    PixelCleaning = new PixelCleaningOptions
    {
        Enabled = true,
        ReplacementValue = PixelReplacementValue.Black,
        ProcessOverlayPlanes = true,
        WarnHighRiskModalities = true,
        SafeModalities = new[] { "CT", "MR" }
    }
};

var deidentifier = new DicomDeidentifier(options);
deidentifier.HighRiskModalityDetected += (modality, dataset) =>
    Console.WriteLine($"Warning: High-risk modality {modality}");

await deidentifier.ApplyAsync(dataset, customDetector, ct);
```

**Key Links Verified:**
- `PixelDataCleaner.CleanAsync` called from DicomDeidentifier line 140
- `OverlayPlaneProcessor.RemoveAllOverlays` called when enabled
- `HighRiskModalityDetected` event raised for US, SC, XA, ES, RF

**Processing Order:**
1. Attribute-level de-identification (ApplyCore)
2. High-risk modality warning
3. Pixel data cleaning (if enabled or BurnedInAnnotation=YES)
4. Overlay plane removal (if enabled)

### StudyDeidentifier (Task 3)

Multi-file batch processor with shared context:

```csharp
await using var deidentifier = new StudyDeidentifier(options);

// Process directory with progress
await foreach (var result in deidentifier.ProcessDirectoryAsync(inputDir, outputDir))
{
    if (!result.Success)
        Console.WriteLine($"Failed: {result.Input}: {result.Error?.Message}");
}

// Parallel processing for throughput
await deidentifier.ProcessParallelAsync(files, maxDegreeOfParallelism: 4, progress);

// Save context for resumption
await deidentifier.SaveContextAsync("context.json");

// Resume from saved context
var resumed = await StudyDeidentifier.LoadAsync("context.json", options);
```

**Key Links Verified:**
- `_deidentifier.ApplyAsync` called from StudyDeidentifier line 145

Features:
- **Shared context**: Consistent UID/date mappings across all files
- **ProcessFileAsync**: Single file processing
- **ProcessDirectoryAsync**: IAsyncEnumerable with relative path preservation
- **ProcessParallelAsync**: Parallel.ForEachAsync on net6.0+
- **SaveContextAsync/LoadAsync**: Resumable batch operations
- **Warning event**: Aggregates high-risk modality notifications

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed CA1822 analyzer warning in TesseractPhiDetector**
- **Found during:** Task 1
- **Issue:** IsOcrAvailable property didn't access instance data on netstandard2.0
- **Fix:** Changed to reference _fallback field (_fallback == null evaluates to false)
- **Files modified:** TesseractPhiDetector.cs
- **Commit:** de02c4c

**2. [Rule 3 - Blocking] Fixed CA1310 string.EndsWith warning**
- **Found during:** Task 3
- **Issue:** EndsWith without StringComparison on netstandard2.0
- **Fix:** Added StringComparison.Ordinal to EndsWith call
- **Files modified:** StudyDeidentifier.cs
- **Commit:** 9f7d492

## Verification Results

```
Build: Succeeded (all TFMs: netstandard2.0, net8.0, net9.0, net10.0)
Tests: 1663 passed, 0 failed, 26 skipped (DCMTK interop)
```

Key pattern verification:
- `PixelDataCleaner.CleanAsync` in DicomDeidentifier: confirmed
- `_deidentifier.ApplyAsync` in StudyDeidentifier: confirmed

## Next Phase Readiness

**Blockers:** None

**Ready for:**
- 14-06: DicomFile.Anonymize extension
- 14-07: Integration tests for de-identification

**Context provides:**
- PixelDataCleaner for burned-in PHI removal
- StudyDeidentifier for batch processing
- HighRiskModalityDetected event for warnings
- Custom detector support via ApplyAsync overload
- Overlay plane removal via OverlayPlaneProcessor
