# Phase 25: Advanced De-identification - Research

**Researched:** 2026-02-06
**Domain:** OCR-based burned-in PHI detection (Tesseract native), DICOM UID reference walking
**Confidence:** MEDIUM (Tesseract C API well-documented; cross-compilation risk flagged; DICOM references use VR-based traversal approach)

## Summary

This phase adds two major capabilities to SharpDicom's de-identification pipeline: (1) OCR-based detection and redaction of burned-in PHI in pixel data using Tesseract via native P/Invoke, and (2) comprehensive UID reference walking to update all referenced SOP Instance UIDs across sequences in RT Plan, Presentation State, Structured Report, Key Object Selection, and other referencing SOP classes.

The Tesseract integration follows the established Phase 13 native codec pattern (Zig cross-compilation, RID-specific NuGet packages, P/Invoke with LibraryImport on NET7+). The critical challenge is Tesseract's hard dependency on Leptonica with at minimum libpng and zlib -- Tesseract internally serializes image data as PNG during recognition, meaning these dependencies cannot be stripped. Cross-compilation to macOS from Linux via Zig is a known risk that may require native macOS CI runners.

The UID reference walking problem is best solved through a generic VR-based approach: recursively traverse all sequences and remap every element with VR=UI against the existing UidRemapper. This handles all known referencing patterns (including future/unknown ones) without maintaining a brittle hardcoded tag list.

**Primary recommendation:** Implement OcrScanner as a standalone class using Tesseract C API via P/Invoke in SharpDicom.Codecs, and implement UidReferenceWalker as a generic recursive VR=UI traversal in the core Deidentification namespace.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Tesseract OCR | 5.5.2 | Text detection and recognition in images | Only mature open-source OCR engine with C API |
| Leptonica | >= 1.74.0 | Image handling library (Tesseract dependency) | Required by Tesseract for internal image processing |
| libpng | latest stable | PNG serialization (Leptonica dependency) | Required: Tesseract crashes without PNG support |
| zlib | latest stable | Compression (libpng dependency) | Required by libpng |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Zig toolchain | 0.13+ | Cross-compilation of native binaries | Build time only, same as Phase 13 |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Tesseract native P/Invoke | Tesseract.NET wrapper (NuGet) | Tesseract.NET wraps the same native library but adds a managed dependency; P/Invoke stays consistent with Phase 13 pattern and keeps control over cross-compilation |
| Tesseract C API | Tesseract C++ API | C API (capi.h) is stable and ABI-compatible; C++ API has name mangling and version coupling issues |
| VR-based UID traversal | Tag-list-based traversal | VR-based handles unknown/future tags automatically; tag-list would miss new referencing sequences |

### No Additional NuGet Dependencies

All OCR code goes in the existing SharpDicom.Codecs native package. The UidReferenceWalker uses only existing SharpDicom APIs. No new NuGet package references needed.

## Architecture Patterns

### Recommended Project Structure

```
src/
├── SharpDicom/
│   └── Deidentification/
│       ├── OcrScannerOptions.cs         # OCR configuration (thresholds, modalities, allow/deny)
│       ├── OcrScanResult.cs             # Detection results (text, bbox, confidence per region)
│       ├── OcrScanner.cs                # Main OCR scanning class (delegates to native)
│       ├── UidReferenceWalker.cs        # Generic VR=UI recursive sequence traversal
│       └── ... (existing files)
├── SharpDicom.Codecs/
│   └── Interop/
│       ├── TesseractNativeMethods.cs    # P/Invoke declarations for Tesseract C API
│       ├── TesseractHandle.cs           # SafeHandle for TessBaseAPI lifecycle
│       └── NativeMethods.cs             # (existing - add feature flag for Tesseract)
└── native/
    ├── build.zig                        # (extend with Tesseract/Leptonica targets)
    ├── vendor/
    │   ├── tesseract/                   # Tesseract source (downloaded in CI)
    │   └── leptonica/                   # Leptonica source (downloaded in CI)
    └── src/
        └── tesseract_wrapper.c          # Thin C wrapper around Tesseract C API
```

### Pattern 1: OcrScanner as Standalone + Pipeline Integration

**What:** OcrScanner is a standalone class that takes raw pixel data and returns detected text regions. It integrates into the DicomDeidentifier pipeline via DicomDeidentifierBuilder.WithOcrScanner().

**When to use:** Always. The standalone API allows direct use without the full de-identification pipeline.

**Example:**
```csharp
// Standalone usage
var options = new OcrScannerOptions
{
    ConfidenceThreshold = 0.6f,
    EdgeConfidenceThreshold = 0.4f,
    ScanModalities = OcrScanModality.HighRisk | OcrScanModality.ModerateRisk,
    Allowlist = OcrScannerOptions.DefaultNonPhiAllowlist
};

using var scanner = new OcrScanner(options);
var result = scanner.ScanDataset(dataset);

foreach (var detection in result.Detections)
{
    Console.WriteLine($"Text: {detection.Text}, Confidence: {detection.Confidence:P}, " +
                      $"Bounds: {detection.BoundingBox}, Frame: {detection.FrameIndex}");
}

// Pipeline integration
var deidentifier = new DicomDeidentifierBuilder()
    .WithBasicProfile()
    .WithOcrScanner(options)
    .Build();
```

### Pattern 2: UidReferenceWalker as Generic VR=UI Traversal

**What:** Recursively walks all sequences to unlimited depth, remapping every element with VR=UI that is not a standard DICOM UID. This is the same approach already used by UidRemapper.RemapDataset() but factored out as a named, configurable component.

**When to use:** After primary de-identification, as a post-processing step to ensure all cross-references use remapped UIDs.

**Example:**
```csharp
// The walker reuses the existing UidRemapper from the de-identification context
var walker = new UidReferenceWalker(uidRemapper);
var remapResult = walker.RemapAllReferences(dataset);

// Result includes count of remapped UIDs and specific tags affected
Console.WriteLine($"Remapped {remapResult.UidsRemapped} UIDs across {remapResult.SequenceItemsTraversed} sequence items");
```

### Pattern 3: Native Tesseract Integration Following Phase 13 Pattern

**What:** Tesseract C API exposed via P/Invoke in SharpDicom.Codecs, with LibraryImport on NET7+ and DllImport fallback for netstandard2.0. A thin C wrapper (`tesseract_wrapper.c`) reduces the number of P/Invoke calls by bundling init+recognize+iterate into fewer calls.

**When to use:** All Tesseract interactions go through native methods. The C wrapper manages Tesseract/Leptonica memory internally.

### Anti-Patterns to Avoid

- **Tag-list-based UID remapping:** Maintaining a hardcoded list of "all tags that can contain referenced UIDs" is fragile and incomplete. Use VR=UI traversal instead.
- **Calling Tesseract per-word:** Initialize once, set image per frame, iterate results. Do not create/destroy TessBaseAPI per frame.
- **Pixel data copy for OCR:** Use GetFrameSpan() to get a ReadOnlySpan pointing into the existing buffer. Only copy when conversion is needed (e.g., 16-bit to 8-bit for Tesseract).
- **Silently skipping OCR when native library is unavailable:** Fail fast with a clear exception, as specified in CONTEXT.md decisions.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| OCR text detection | Custom pattern matching | Tesseract 5.5.2 C API | Decades of training data, LSTM engine, handles variable fonts |
| Image format handling for Tesseract | Custom image serialization | Leptonica (bundled) | Tesseract requires Leptonica internally for PIX management |
| UID generation | Custom UID algorithm | Existing UidRemapper + UidGenerator | Already handles standard UID preservation and consistent mapping |
| Pixel data extraction | Manual byte math | Existing PixelDataInfo.FromDataset + DicomPixelDataElement.GetFrameSpan | Already handles dimensions, frame sizes, multi-frame |
| Risk classification | New modality risk analysis | Existing BurnedInAnnotationDetector | Already has HighRisk/ModerateRisk/Low categorization |
| Allow/deny text filtering | Complex NLP | Simple HashSet<string> contains check | Burned-in non-PHI text (units, markers) is a small fixed vocabulary |

**Key insight:** Most infrastructure already exists from Phase 14 (PixelDataRedactor, BurnedInAnnotationDetector, UidRemapper, RedactionRegion). Phase 25 adds OCR as a detection mechanism feeding into the existing redaction pipeline, and makes UID remapping comprehensive across sequences.

## Common Pitfalls

### Pitfall 1: Tesseract Crashes Without PNG/zlib in Leptonica

**What goes wrong:** Tesseract internally calls `pixWriteMemPng()` during recognition to serialize intermediate images. Without libpng linked into Leptonica, this causes a segfault -- not a graceful error.
**Why it happens:** Leptonica's `pixWriteMem*` functions are compile-time conditional. Without PNG support, the function pointer is null.
**How to avoid:** Always build Leptonica with libpng and zlib. This is non-negotiable. The minimal dependency chain is: Tesseract -> Leptonica -> libpng -> zlib.
**Warning signs:** Segfault during TessBaseAPIRecognize(), not during TessBaseAPISetImage().

### Pitfall 2: Cross-Compilation to macOS from Linux

**What goes wrong:** Zig cross-compilation from Linux to macOS may fail for Tesseract/Leptonica due to missing macOS SDK headers or framework dependencies.
**Why it happens:** Tesseract is a C++ library (despite the C API wrapper), and cross-compiling C++ to macOS requires Apple SDK headers. Leptonica uses some POSIX APIs that differ between Linux and macOS.
**How to avoid:** Plan for macOS builds using native CI runners (macOS GitHub Actions). Windows and Linux can likely be cross-compiled from Linux. Test this early.
**Warning signs:** Linker errors referencing Apple frameworks or missing system headers during Zig build.

### Pitfall 3: 16-bit DICOM Images with Tesseract

**What goes wrong:** Tesseract only supports 8-bit grayscale and 24/32-bit color images. DICOM images are commonly 12-bit or 16-bit.
**Why it happens:** TessBaseAPISetImage accepts bytes_per_pixel of 1, 3, or 4. There is no 2-byte grayscale mode.
**How to avoid:** Window/level the 16-bit data to 8-bit before OCR. Use the DICOM Window Center/Width tags or a reasonable default (full range mapping). This is a lossy conversion but sufficient for text detection.
**Warning signs:** Garbled OCR results, zero detections, or crashes when passing 16-bit data directly.

### Pitfall 4: False Positives from Anatomy Labels and Orientation Markers

**What goes wrong:** OCR detects "L", "R", "P", "A", "S", "I" (orientation markers) and measurement text ("3.2 cm", "HR: 72 bpm") as potential PHI.
**Why it happens:** These are legitimate non-PHI text burned into images.
**How to avoid:** Implement an allowlist of common non-PHI patterns. Default list should include: single-letter orientation markers (L, R, P, A, S, I, H, F), measurement units (cm, mm, Hz, bpm, ml, mg), common medical abbreviations (HR, BP, SpO2, ECG), and numeric-only strings.
**Warning signs:** High detection count on CT/MR images that shouldn't have PHI burned in.

### Pitfall 5: Tesseract Memory Leaks

**What goes wrong:** Each call to TessBaseAPIRecognize() and TessResultIteratorGetUTF8Text() allocates memory that must be explicitly freed.
**Why it happens:** C API returns raw pointers. Managed GC doesn't know about native allocations.
**How to avoid:** Use SafeHandle for TessBaseAPI lifecycle. Call TessDeleteText() for every UTF8 string returned. Call TessBaseAPIClear() between frames (not Delete, which destroys the instance). Wrap iterator results in try/finally.
**Warning signs:** Gradual memory growth during multi-frame processing.

### Pitfall 6: MONOCHROME1 Inversion for OCR

**What goes wrong:** MONOCHROME1 images have inverted polarity (white=minimum, black=maximum). Text appears as dark-on-light or light-on-dark depending on modality. Tesseract expects dark text on light background.
**Why it happens:** PhotometricInterpretation controls pixel value meaning. OCR accuracy degrades significantly with inverted polarity.
**How to avoid:** Check PhotometricInterpretation. For MONOCHROME1, invert pixel values before OCR (255 - value for 8-bit). For MONOCHROME2, use as-is. For RGB/YBR, convert to grayscale first.
**Warning signs:** Zero OCR detections on images that visually contain text.

### Pitfall 7: Circular or Self-Referencing UIDs

**What goes wrong:** A UID reference walker that doesn't track visited items could infinite-loop on circular references in malformed DICOM data.
**Why it happens:** While DICOM standard doesn't permit circular references, real-world data can contain them.
**How to avoid:** The VR=UI traversal approach is inherently safe -- it walks the dataset tree structure (sequences contain items contain elements), which is acyclic by definition. No visited-set needed.
**Warning signs:** Stack overflow during sequence traversal (would indicate a bug in the sequence structure itself).

## Code Examples

### Tesseract C API P/Invoke Declarations (NET7+)

```csharp
// Source: https://github.com/tesseract-ocr/tesseract/blob/main/include/tesseract/capi.h
internal static unsafe partial class TesseractNativeMethods
{
    internal const string LibraryName = "sharpdicom_codecs"; // Same library, new functions

#if NET7_0_OR_GREATER
    [LibraryImport(LibraryName, EntryPoint = "tess_create")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr tess_create();

    [LibraryImport(LibraryName, EntryPoint = "tess_delete")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void tess_delete(IntPtr handle);

    [LibraryImport(LibraryName, EntryPoint = "tess_init",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int tess_init(IntPtr handle, string datapath, string language);

    [LibraryImport(LibraryName, EntryPoint = "tess_set_image")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void tess_set_image(
        IntPtr handle, byte* imagedata,
        int width, int height,
        int bytes_per_pixel, int bytes_per_line);

    [LibraryImport(LibraryName, EntryPoint = "tess_set_page_seg_mode")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void tess_set_page_seg_mode(IntPtr handle, int mode);

    [LibraryImport(LibraryName, EntryPoint = "tess_recognize")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int tess_recognize(IntPtr handle);

    // Bundled: iterate results and return array of detections
    [LibraryImport(LibraryName, EntryPoint = "tess_get_detections")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int tess_get_detections(
        IntPtr handle,
        TessDetection* results, int maxResults,
        out int actualCount);

    [LibraryImport(LibraryName, EntryPoint = "tess_free_text")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void tess_free_text(IntPtr text);

    [LibraryImport(LibraryName, EntryPoint = "tess_clear")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void tess_clear(IntPtr handle);
#else
    // DllImport equivalents for netstandard2.0
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr tess_create();
    // ... etc
#endif
}

[StructLayout(LayoutKind.Sequential)]
internal struct TessDetection
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
    public float Confidence;
    public IntPtr Text; // UTF-8 null-terminated, must be freed
}
```

### Native C Wrapper (tesseract_wrapper.c)

```c
// Thin wrapper to reduce P/Invoke round-trips
#include <tesseract/capi.h>
#include <string.h>

typedef struct {
    int left, top, right, bottom;
    float confidence;
    char* text; // owned by this struct, caller must free via tess_free_text
} TessDetectionResult;

TessBaseAPI* tess_create(void) {
    return TessBaseAPICreate();
}

void tess_delete(TessBaseAPI* handle) {
    TessBaseAPIDelete(handle);
}

int tess_init(TessBaseAPI* handle, const char* datapath, const char* language) {
    return TessBaseAPIInit3(handle, datapath, language);
}

void tess_set_image(TessBaseAPI* handle, const unsigned char* data,
                    int width, int height, int bpp, int bpl) {
    TessBaseAPISetImage(handle, data, width, height, bpp, bpl);
}

void tess_set_page_seg_mode(TessBaseAPI* handle, int mode) {
    TessBaseAPISetPageSegMode(handle, (TessPageSegMode)mode);
}

int tess_recognize(TessBaseAPI* handle) {
    return TessBaseAPIRecognize(handle, NULL);
}

// Bundled iteration: returns all word-level detections in one call
int tess_get_detections(TessBaseAPI* handle,
                        TessDetectionResult* results, int max_results,
                        int* actual_count) {
    TessResultIterator* ri = TessBaseAPIGetIterator(handle);
    if (!ri) { *actual_count = 0; return 0; }

    int count = 0;
    TessPageIteratorLevel level = RIL_WORD;

    do {
        if (count >= max_results) break;

        float conf = TessResultIteratorConfidence(ri, level);
        char* text = TessResultIteratorGetUTF8Text(ri, level);
        if (!text) continue;

        int left, top, right, bottom;
        if (TessPageIteratorBoundingBox((TessPageIterator*)ri, level,
                                        &left, &top, &right, &bottom)) {
            results[count].left = left;
            results[count].top = top;
            results[count].right = right;
            results[count].bottom = bottom;
            results[count].confidence = conf;
            results[count].text = text; // caller must free
            count++;
        } else {
            TessDeleteText(text);
        }
    } while (TessResultIteratorNext(ri, level));

    TessPageIteratorDelete((TessPageIterator*)ri);
    *actual_count = count;
    return 0;
}

void tess_free_text(char* text) {
    TessDeleteText(text);
}

void tess_clear(TessBaseAPI* handle) {
    TessBaseAPIClear(handle);
}
```

### Pixel Data Preparation for OCR

```csharp
// Source: Existing SharpDicom APIs (PixelDataInfo, DicomPixelDataElement)
private static byte[] PrepareFrameForOcr(DicomPixelDataElement pixelData, int frameIndex)
{
    var info = pixelData.Info;
    var frameSpan = pixelData.GetFrameSpan(frameIndex);

    // Handle different bit depths
    int bitsAllocated = info.BitsAllocated ?? 8;
    int samplesPerPixel = info.SamplesPerPixel ?? 1;

    if (bitsAllocated <= 8 && samplesPerPixel == 1)
    {
        // 8-bit grayscale: check photometric interpretation
        var phot = info.PhotometricInterpretation;
        if (phot == "MONOCHROME1")
        {
            // Invert: MONOCHROME1 has white=0
            var inverted = new byte[frameSpan.Length];
            for (int i = 0; i < frameSpan.Length; i++)
                inverted[i] = (byte)(255 - frameSpan[i]);
            return inverted;
        }
        return frameSpan.ToArray(); // MONOCHROME2: use as-is
    }

    if (bitsAllocated == 16 && samplesPerPixel == 1)
    {
        // 16-bit to 8-bit windowing
        var pixels16 = MemoryMarshal.Cast<byte, ushort>(frameSpan);
        var result = new byte[pixels16.Length];
        // Simple min-max normalization (or use Window Center/Width if available)
        ushort min = ushort.MaxValue, max = 0;
        for (int i = 0; i < pixels16.Length; i++)
        {
            if (pixels16[i] < min) min = pixels16[i];
            if (pixels16[i] > max) max = pixels16[i];
        }
        float range = max - min;
        if (range < 1) range = 1;
        for (int i = 0; i < pixels16.Length; i++)
            result[i] = (byte)((pixels16[i] - min) * 255f / range);

        // Handle MONOCHROME1 inversion
        if (info.PhotometricInterpretation == "MONOCHROME1")
            for (int i = 0; i < result.Length; i++)
                result[i] = (byte)(255 - result[i]);

        return result;
    }

    if (samplesPerPixel == 3)
    {
        // RGB to grayscale for OCR (luminance: 0.299R + 0.587G + 0.114B)
        int pixelCount = frameSpan.Length / 3;
        var result = new byte[pixelCount];
        for (int i = 0; i < pixelCount; i++)
        {
            int offset = i * 3;
            result[i] = (byte)(0.299f * frameSpan[offset] +
                               0.587f * frameSpan[offset + 1] +
                               0.114f * frameSpan[offset + 2]);
        }
        return result;
    }

    throw new NotSupportedException(
        $"Unsupported pixel format for OCR: {bitsAllocated} bits, {samplesPerPixel} samples");
}
```

### UidReferenceWalker Implementation Pattern

```csharp
// Source: Based on existing UidRemapper.RemapDatasetInternal() pattern
public sealed class UidReferenceWalker
{
    private readonly UidRemapper _remapper;

    public UidReferenceWalker(UidRemapper remapper)
    {
        _remapper = remapper ?? throw new ArgumentNullException(nameof(remapper));
    }

    public UidRemapResult RemapAllReferences(DicomDataset dataset, string? context = null)
    {
        var result = new UidRemapResult();
        WalkDataset(dataset, context, result);
        return result;
    }

    private void WalkDataset(DicomDataset dataset, string? context, UidRemapResult result)
    {
        var tagsToProcess = new List<DicomTag>();
        foreach (var element in dataset)
            tagsToProcess.Add(element.Tag);

        foreach (var tag in tagsToProcess)
        {
            var element = dataset[tag];
            if (element == null) continue;

            // Recurse into sequences (unlimited depth)
            if (element is DicomSequence seq)
            {
                foreach (var item in seq.Items)
                {
                    WalkDataset(item, context, result);
                    result.SequenceItemsTraversed++;
                }
                continue;
            }

            // Remap ALL VR=UI elements (not just known tags)
            if (element.VR == DicomVR.UI && element is DicomStringElement strElem)
            {
                var originalUid = strElem.GetString(DicomEncoding.Default);
                if (string.IsNullOrWhiteSpace(originalUid)) continue;

                var trimmedUid = originalUid!.Trim();
                if (_remapper.IsStandardUid(trimmedUid)) continue;

                var newUid = _remapper.Remap(trimmedUid, context);
                if (newUid != trimmedUid)
                {
                    var bytes = Encoding.ASCII.GetBytes(newUid);
                    dataset.Add(new DicomStringElement(tag, DicomVR.UI, bytes));
                    result.UidsRemapped++;
                    result.RemappedTags.Add(tag);
                }
            }
        }
    }
}
```

## Tesseract C API Reference

### Minimal Function Set Required

| Function | Purpose | Notes |
|----------|---------|-------|
| `TessBaseAPICreate()` | Create API instance | Returns handle, must be freed with Delete |
| `TessBaseAPIInit3(handle, datapath, lang)` | Initialize with language data | Returns 0 on success, -1 on failure |
| `TessBaseAPISetPageSegMode(handle, mode)` | Set page segmentation mode | Use PSM_SPARSE_TEXT (11) for medical overlays |
| `TessBaseAPISetVariable(handle, name, value)` | Set engine variables | Useful for `tessedit_char_whitelist` |
| `TessBaseAPISetImage(handle, data, w, h, bpp, bpl)` | Set raw pixel buffer | 8-bit grayscale: bpp=1, bpl=width |
| `TessBaseAPIRecognize(handle, NULL)` | Run recognition | Returns 0 on success |
| `TessBaseAPIGetIterator(handle)` | Get result iterator | For word-level iteration |
| `TessResultIteratorGetUTF8Text(iter, level)` | Get recognized text | Must free with TessDeleteText |
| `TessResultIteratorConfidence(iter, level)` | Get confidence 0-100 | Float, per-word |
| `TessPageIteratorBoundingBox(iter, level, ...)` | Get bounding rectangle | Returns left, top, right, bottom |
| `TessResultIteratorNext(iter, level)` | Advance iterator | Returns BOOL |
| `TessBaseAPIClear(handle)` | Clear results, keep init | Use between frames |
| `TessBaseAPIDelete(handle)` | Destroy instance | Final cleanup |
| `TessDeleteText(text)` | Free returned string | Must be called for every GetUTF8Text result |

### Page Segmentation Modes

| PSM | Value | Description | Use for Medical |
|-----|-------|-------------|-----------------|
| PSM_AUTO | 3 | Fully automatic | General purpose, may miss sparse text |
| PSM_SINGLE_BLOCK | 6 | Assume single text block | When scanning known text regions |
| PSM_SPARSE_TEXT | 11 | Find as much text as possible, no order | **Best for burned-in overlays** -- text scattered across image |
| PSM_SPARSE_TEXT_OSD | 12 | Sparse text with OSD | Use if orientation detection needed |

**Recommendation:** Use PSM_SPARSE_TEXT (11) as the default for medical image scanning. Burned-in annotations are typically scattered across the image (corners, edges, status bars) rather than forming coherent text blocks.

### Image Requirements

- **Supported:** 8-bit grayscale (bpp=1), 24-bit RGB (bpp=3), 32-bit RGBA (bpp=4)
- **Not supported:** 16-bit grayscale (must window/level to 8-bit first)
- **Text orientation:** Tesseract expects dark text on light background. Invert MONOCHROME1 images.
- **Resolution:** Tesseract works best at 300 DPI. Medical images are typically higher resolution, which is fine.
- **Minimum text height:** Approximately 10 pixels for reliable detection.

## DICOM UID Reference Architecture

### Tag (0008,1155) -- Referenced SOP Instance UID

This is the primary tag for SOP Instance references. It appears within many different sequences across all referencing SOP classes. Rather than enumerating all containing sequences (which changes with each DICOM standard update), the correct approach is VR-based traversal.

### Key UID Tags to Remap

| Tag | Name | VR | Must Remap |
|-----|------|----|------------|
| (0008,0018) | SOPInstanceUID | UI | Yes (already done in Phase 14) |
| (0008,1155) | ReferencedSOPInstanceUID | UI | Yes -- inside any reference sequence |
| (0020,000D) | StudyInstanceUID | UI | Yes |
| (0020,000E) | SeriesInstanceUID | UI | Yes |
| (0020,0052) | FrameOfReferenceUID | UI | Yes |
| (0020,0200) | SynchronizationFrameOfReferenceUID | UI | Yes |
| (3006,0024) | ReferencedFrameOfReferenceUID | UI | Yes (RT-specific) |
| (0008,1150) | ReferencedSOPClassUID | UI | **No** -- this is a standard DICOM UID |
| (0002,0002) | MediaStorageSOPClassUID | UI | **No** -- standard UID |
| (0002,0010) | TransferSyntaxUID | UI | **No** -- standard UID |

### Known Referencing Sequences (Non-Exhaustive)

| Sequence Tag | Sequence Name | Found In |
|--------------|---------------|----------|
| (0008,1115) | ReferencedSeriesSequence | Many IODs |
| (0008,1199) | ReferencedSOPSequence | SR, KOS, many others |
| (0008,1140) | ReferencedImageSequence | Most image IODs |
| (0008,2112) | SourceImageSequence | Many IODs |
| (0008,114A) | ReferencedInstanceSequence | SR, KOS |
| (300C,0006) | ReferencedBeamSequence | RT Plan |
| (300C,0002) | ReferencedRTPlanSequence | RT Dose |
| (3006,0010) | ReferencedFrameOfReferenceSequence | RT Structure Set |
| (0008,9237) | ReferencedPresentationStateSequence | Various |
| (0040,A375) | CurrentRequestedProcedureEvidenceSequence | SR |
| (0040,A385) | PertinentOtherEvidenceSequence | SR |
| (0088,0200) | IconImageSequence | Various |
| (0008,1250) | RelatedSeriesSequence | Various |

**Critical design decision:** Do NOT maintain this list in code. Instead, walk ALL sequences recursively and remap ALL VR=UI elements. The UidRemapper.IsStandardUid() check (prefix "1.2.840.10008.") correctly preserves Transfer Syntax UIDs, SOP Class UIDs, etc. This approach is future-proof and handles private sequences too.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Tag-list UID remapping | VR-based recursive remapping | Best practice | Handles all current and future referencing patterns |
| Tesseract 4.x LSTM | Tesseract 5.5.2 LSTM | Dec 2024 | Better accuracy, stable C API, CMake build |
| DllImport only | LibraryImport (NET7+) + DllImport fallback | .NET 7 | Source-generated marshalling, better perf |

**Deprecated/outdated:**
- Tesseract 3.x legacy engine: No LSTM support, much lower accuracy
- Tesseract 4.0.0 C API: Missing some functions present in 5.x (use 5.5.2)

## Open Questions

1. **macOS Zig cross-compilation feasibility**
   - What we know: Zig can cross-compile C to macOS, but C++ (which Tesseract is) has additional complexity. The CONTEXT.md flags this as a known risk.
   - What's unclear: Whether the Tesseract C++ library compiles cleanly with Zig's C++ support targeting macOS, or whether native macOS runners are required.
   - Recommendation: Attempt Zig cross-compilation first; fall back to native macOS CI runners if it fails. Budget time for this investigation.

2. **Tesseract trained data bundling strategy**
   - What we know: English data (eng.traineddata) is ~12MB. CONTEXT.md says bundle English, users add others.
   - What's unclear: Whether to embed in the NuGet package as content, distribute as a separate NuGet package, or require users to set a TESSDATA_PREFIX path.
   - Recommendation: Bundle eng.traineddata as embedded content in SharpDicom.Codecs NuGet package. Provide OcrScannerOptions.TessdataPath for custom data.

3. **Compressed pixel data handling for OCR**
   - What we know: CONTEXT.md specifies "decompress and store lossless (safe default)" or "re-compress with same algorithm (caller opts in)."
   - What's unclear: How exactly to trigger decompression -- does existing CodecRegistry handle this transparently?
   - Recommendation: Use existing codec infrastructure to decompress. OcrScanner should call codec decompression if pixel data is encapsulated, before scanning.

4. **Thin C wrapper vs direct Tesseract C API P/Invoke**
   - What we know: Either approach works. The thin wrapper reduces P/Invoke round-trips and manages C memory internally.
   - What's unclear: Whether to add Tesseract functions to the existing sharpdicom_codecs native library (simpler deployment) or create a separate native binary.
   - Recommendation: Add to existing sharpdicom_codecs library with a SHARPDICOM_WITH_TESSERACT compile flag, following the existing pattern for JPEG/J2K/JLS.

## Sources

### Primary (HIGH confidence)

- Tesseract C API (capi.h): [GitHub source](https://github.com/tesseract-ocr/tesseract/blob/main/include/tesseract/capi.h) -- verified function signatures
- Tesseract 5.5.2 release: [GitHub releases](https://github.com/tesseract-ocr/tesseract/releases) -- current stable version (Dec 2024)
- Tesseract API examples: [tessdoc](https://tesseract-ocr.github.io/tessdoc/APIExample.html) -- official documentation
- Existing SharpDicom codebase: DicomDeidentifier, UidRemapper, PixelDataRedactor, BurnedInAnnotationDetector, NativeMethods -- read directly from source
- DICOM Standard PS3.3 Reference Macros: [NEMA](https://dicom.nema.org/medical/dicom/current/output/chtml/part03/sect_10.3.html) -- official tag definitions

### Secondary (MEDIUM confidence)

- Tesseract minimal dependencies (issue #2333): [GitHub issue](https://github.com/tesseract-ocr/tesseract/issues/2333) -- confirmed libpng+zlib required
- Tesseract PSM modes: [PyImageSearch](https://pyimagesearch.com/2021/11/15/tesseract-page-segmentation-modes-psms-explained-how-to-improve-your-ocr-accuracy/) -- PSM 11 for sparse text
- Tesseract compilation guide: [tessdoc](https://tesseract-ocr.github.io/tessdoc/Compiling.html) -- build requirements
- DICOM Standard Browser (Innolitics): [Referenced SOP Instance UID](https://dicom.innolitics.com/ciods/rt-plan/general-reference/00082112/00081155)
- Zig cross-compilation: [Zig NEWS](https://zig.news/kristoff/cross-compile-a-c-c-project-with-zig-3599)

### Tertiary (LOW confidence)

- macOS Zig cross-compilation of C++ libraries: Not verified -- flagged as risk in CONTEXT.md
- Tesseract accuracy on medical image overlays: No published benchmarks found -- will need empirical testing

## Metadata

**Confidence breakdown:**
- Standard stack: MEDIUM -- Tesseract C API is well-documented, but Zig cross-compilation adds uncertainty
- Architecture: HIGH -- patterns follow established Phase 13/14 codebase patterns exactly
- Pitfalls: HIGH -- well-documented issues (PNG dependency crash, 16-bit conversion, MONOCHROME1 inversion)
- DICOM references: HIGH -- VR-based traversal is standard approach, verified against DICOM standard
- Cross-compilation: LOW -- macOS cross-compilation is a known risk with no verified solution

**Research date:** 2026-02-06
**Valid until:** 2026-03-06 (30 days -- Tesseract is stable, DICOM standard changes infrequently)
