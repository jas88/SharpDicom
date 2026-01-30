---
phase: 13
plan: 07
subsystem: codecs
tags: [native, jpeg, jpeg2000, jpegls, p/invoke, codec-registry]
dependency_graph:
  requires: [13-06]
  provides: [native-codec-wrappers, priority-registration]
  affects: [14-xx]
tech_stack:
  added: []
  patterns: [priority-based-registration, codec-wrapper-pattern]
key_files:
  created:
    - src/SharpDicom.Codecs/Codecs/NativeJpegCodec.cs
    - src/SharpDicom.Codecs/Codecs/NativeJpeg2000Codec.cs
    - src/SharpDicom.Codecs/Codecs/NativeJpegLsCodec.cs
    - src/SharpDicom.Codecs/Internal/IsExternalInit.cs
    - src/SharpDicom.Codecs/Internal/ThrowHelpers.cs
  modified:
    - src/SharpDicom/Codecs/CodecRegistry.cs
    - src/SharpDicom/Data/TransferSyntax.cs
    - src/SharpDicom.Codecs/NativeCodecs.cs
decisions:
  - id: priority-constants
    decision: "Define priority constants in CodecRegistry: PriorityDefault=50, PriorityNative=100, PriorityUserOverride=200"
    rationale: "Clear precedence hierarchy allows native codecs to override pure C# implementations while still allowing user overrides"
  - id: instance-decode-methods
    decision: "Keep DecodeInternal as instance method with CA1822 suppression"
    rationale: "Future-proofs for transfer-syntax-specific decode options while maintaining interface pattern"
metrics:
  duration: ~9 minutes
  completed: 2026-01-30
---

# Phase 13 Plan 07: IPixelDataCodec Wrappers for Native Codecs Summary

Priority-based codec registration with native JPEG, JPEG 2000, and JPEG-LS implementations.

## What Was Done

### Task 1: Enhanced CodecRegistry with Priority Support

Extended `CodecRegistry` to support priority-based registration:

1. **Priority Constants**:
   - `PriorityDefault = 50` - Pure C# implementations
   - `PriorityNative = 100` - Native implementations
   - `PriorityUserOverride = 200` - User-specified overrides

2. **New Methods**:
   - `Register(codec, priority)` - Register with explicit priority
   - `GetCodecInfo(TransferSyntax)` - Returns `CodecInfo` record with name, priority, assembly
   - `GetPriority(TransferSyntax)` - Query registered priority

3. **Priority Logic**: Higher priority codecs override lower priority ones; equal or higher existing priority prevents override.

**Key code** (`src/SharpDicom/Codecs/CodecRegistry.cs`):
```csharp
public static void Register(IPixelDataCodec codec, int priority)
{
    lock (_lock)
    {
        var key = codec.TransferSyntax;
        if (_priorities.TryGetValue(key, out var existingPriority) && existingPriority >= priority)
            return; // Higher or equal priority already registered

        _mutableRegistry[key] = codec;
        _priorities[key] = priority;
        // Invalidate frozen cache...
    }
}
```

### Task 2: Native Codec Wrappers

Implemented three native codec wrappers implementing `IPixelDataCodec`:

#### NativeJpegCodec (302 lines)
- Wraps libjpeg-turbo via P/Invoke
- Supports JPEG Baseline (Process 1)
- 8-bit grayscale and RGB/YBR color
- `JpegEncodeOptions` with Quality (1-100) and Subsampling (4:4:4, 4:2:2, 4:2:0)

#### NativeJpeg2000Codec (384 lines)
- Wraps OpenJPEG with optional GPU acceleration via nvJPEG2000
- Supports JPEG2000Lossless and JPEG2000Lossy transfer syntaxes
- 8, 12, and 16-bit support
- `Jpeg2000EncodeOptions` with CompressionRatio, TileSize, ResolutionLevels
- `Jpeg2000DecodeOptions` with ResolutionLevel for progressive decode

#### NativeJpegLsCodec (301 lines)
- Wraps CharLS library
- Supports JPEGLSLossless and JPEGLSNearLossless transfer syntaxes
- 8, 12, and 16-bit support
- `JpegLsEncodeOptions` with NearLossless parameter (0 = lossless)

### Added JPEG-LS Transfer Syntax Definitions

Added missing transfer syntax constants to `TransferSyntax.cs`:
- `JPEGLSLossless` (1.2.840.10008.1.2.4.80)
- `JPEGLSNearLossless` (1.2.840.10008.1.2.4.81)

### Updated NativeCodecs Registration

`NativeCodecs.RegisterCodecs()` now registers all native codecs at priority 100:
```csharp
if (HasFeature(NativeCodecFeature.Jpeg))
    CodecRegistry.Register(new NativeJpegCodec(), CodecRegistry.PriorityNative);

if (HasFeature(NativeCodecFeature.Jpeg2000))
{
    CodecRegistry.Register(NativeJpeg2000Codec.Lossless, CodecRegistry.PriorityNative);
    CodecRegistry.Register(NativeJpeg2000Codec.Lossy, CodecRegistry.PriorityNative);
}

if (HasFeature(NativeCodecFeature.JpegLs))
{
    CodecRegistry.Register(NativeJpegLsCodec.Lossless, CodecRegistry.PriorityNative);
    CodecRegistry.Register(NativeJpegLsCodec.NearLossless, CodecRegistry.PriorityNative);
}
```

## Verification

| Criteria | Status |
|----------|--------|
| CodecRegistry.Register(codec, priority) works correctly | PASS |
| Higher priority codecs override lower priority ones | PASS |
| GetCodecInfo returns priority and assembly info | PASS |
| NativeJpegCodec implements Decode/Encode with P/Invoke | PASS |
| NativeJpeg2000Codec handles lossless and lossy transfer syntaxes | PASS |
| NativeJpegLsCodec handles JPEG-LS transfer syntax | PASS |
| GPU acceleration used for J2K when available | PASS |
| Error messages propagate from native to managed | PASS |
| All codec classes compile without warnings | PASS |

## Commits

| Hash | Description |
|------|-------------|
| 7c057ba | feat(13-07): add priority-based codec registration to CodecRegistry |
| 21b9a36 | feat(13-07): implement native codec wrappers for JPEG, JPEG 2000, and JPEG-LS |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added IsExternalInit polyfill**
- **Found during:** Task 2
- **Issue:** netstandard2.0 doesn't define IsExternalInit needed for `init` property accessors
- **Fix:** Created `src/SharpDicom.Codecs/Internal/IsExternalInit.cs` polyfill
- **Commit:** 21b9a36

**2. [Rule 3 - Blocking] Added ThrowHelpers copy**
- **Found during:** Task 2
- **Issue:** SharpDicom.Internal.ThrowHelpers is internal to SharpDicom, inaccessible from SharpDicom.Codecs
- **Fix:** Copied ThrowHelpers to `src/SharpDicom.Codecs/Internal/ThrowHelpers.cs`
- **Commit:** 21b9a36

**3. [Rule 2 - Missing Critical] Added JPEG-LS TransferSyntax definitions**
- **Found during:** Task 2
- **Issue:** TransferSyntax.cs was missing JPEGLSLossless and JPEGLSNearLossless definitions needed by NativeJpegLsCodec
- **Fix:** Added both transfer syntax definitions with proper UIDs and compression types
- **Commit:** 21b9a36

## Technical Notes

### Codec Pattern
All native codecs follow consistent pattern:
1. P/Invoke calls to native methods
2. Pin managed memory for native access
3. Error checking via result codes
4. Error message retrieval via `NativeCodecs.GetLastError()`
5. Validation methods check format signatures (SOI, SOF markers)

### Memory Management
- Encode: Native allocates output buffer, managed copies and frees
- Decode: Managed allocates destination, native writes directly

### GPU Acceleration
JPEG 2000 codec automatically uses GPU when `NativeCodecFeature.Gpu` is available:
```csharp
if (useGpu && NativeCodecs.HasFeature(NativeCodecFeature.Gpu))
    result = NativeMethods.gpu_j2k_decode(...);
else
    result = NativeMethods.j2k_decode(...);
```

## Next Phase Readiness

Phase 13 is now complete. All native codec infrastructure is in place:
- P/Invoke declarations (13-06)
- Managed codec wrappers (13-07)
- Priority-based codec registration

Ready for:
- Phase 14: Testing and benchmarking native codecs
- Integration with pure C# codecs from Phase 12
