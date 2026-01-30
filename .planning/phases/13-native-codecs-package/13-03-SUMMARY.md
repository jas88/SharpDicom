---
phase: 13-native-codecs-package
plan: 03
subsystem: native-codecs
tags: [jpeg2000, openjpeg, native, c, codec]
dependency-graph:
  requires: [13-01]
  provides: [j2k_wrapper, openjpeg-vendoring]
  affects: [13-04, 13-05]
tech-stack:
  added: [openjpeg-2.5.x]
  patterns: [memory-stream-callbacks, feature-flag-compilation]
key-files:
  created:
    - native/src/j2k_wrapper.h
    - native/src/j2k_wrapper.c
    - native/vendor/openjpeg/.gitkeep
    - native/vendor/README.md
  modified:
    - native/build.zig
    - native/src/sharpdicom_codecs.c
    - .github/workflows/native-build.yml
decisions:
  - id: j2k-format-detection
    choice: Auto-detect J2K vs JP2 from magic bytes
    rationale: Simplifies caller API, handles both formats transparently
  - id: memory-stream-pattern
    choice: Custom memory stream callbacks for OpenJPEG
    rationale: Avoid file I/O, work directly with in-memory buffers
  - id: stub-compilation
    choice: Compile stub when SHARPDICOM_HAS_OPENJPEG not defined
    rationale: Allows building without vendor source, returns UNSUPPORTED error
  - id: resolution-levels
    choice: cp_reduce parameter for thumbnail generation
    rationale: Efficient partial decode at 1/2, 1/4, etc. resolution
metrics:
  duration: 5m
  completed: 2026-01-30
---

# Phase 13 Plan 03: JPEG 2000 Wrapper Summary

OpenJPEG wrapper providing JPEG 2000 encode/decode with resolution levels, ROI decode, and tiled encoding.

## One-liner

OpenJPEG wrapper with resolution-level decode for thumbnails, ROI decode for large images, and lossless/lossy encoding with configurable tiles.

## What Was Built

### Task 1: OpenJPEG Vendoring Setup

Created infrastructure for CI-downloaded OpenJPEG source:

1. **native/vendor/openjpeg/.gitkeep** - Placeholder for CI downloads
2. **native/vendor/README.md** - Documented OpenJPEG 2.5.x dependency (BSD-2-Clause license)
3. **.github/workflows/native-build.yml** - Added OpenJPEG download steps for Linux, Windows, macOS

### Task 2: JPEG 2000 Wrapper Implementation

Created full wrapper API with advanced features:

**Header (j2k_wrapper.h - 254 lines):**
- `J2kFormat` enum: J2K_FORMAT_J2K (raw codestream), J2K_FORMAT_JP2 (file format)
- `J2kColorSpace` enum: GRAY, RGB, YCC, SYCC
- `J2kImageInfo` struct: width, height, components, bits, signed, resolutions, tiles
- `J2kEncodeParams` struct: lossless, compression_ratio, quality, num_resolutions, tiles, progression_order
- `J2kDecodeOptions` struct: reduce (resolution level), max_quality_layers
- API functions: j2k_get_info, j2k_decode, j2k_decode_region, j2k_encode, j2k_free, j2k_version

**Implementation (j2k_wrapper.c - 1045 lines):**
- Memory stream callbacks for opj_stream (read, write, skip, seek)
- Error/warning/info message handlers routing to thread-local storage
- Format auto-detection from magic bytes (J2K SOC marker vs JP2 signature)
- `j2k_get_info()`: Header-only decode for metadata extraction
- `j2k_decode()`: Full decode with resolution level support (cp_reduce)
- `j2k_decode_region()`: ROI decode via opj_set_decode_area()
- `j2k_encode()`: Lossless (5/3 DWT) and lossy (9/7 DWT) encoding with configurable tiles, quality layers, progression order
- Stub implementations returning SHARPDICOM_ERR_UNSUPPORTED when OpenJPEG not available

**Build Integration (build.zig):**
- Added detectVendorLibrary() for checking openjpeg-src presence
- Added addOpenJpegSources() to compile 22 OpenJPEG core files
- Conditional compilation with SHARPDICOM_HAS_OPENJPEG flag
- OpenJPEG-specific flags (-DOPJ_STATIC, -DUSE_JPIP=0, suppressed warnings)

**Core Updates (sharpdicom_codecs.c):**
- Added set_error_fmt() for printf-style error formatting
- SHARPDICOM_WITH_J2K flag enables SHARPDICOM_HAS_J2K feature bit

## Key API Features

### Resolution Level Decode (Thumbnails)
```c
J2kDecodeOptions options = { .reduce = 2 }; // 1/4 resolution
j2k_decode(data, len, output, output_len, &options, &w, &h, &c);
// w and h will be original dimensions / 4
```

### ROI Decode (Large Images)
```c
// Decode only a 512x512 region starting at (100, 100)
j2k_decode_region(data, len, output, output_len,
                  100, 100, 612, 612, NULL, &w, &h, &c);
```

### Lossless Encode
```c
J2kEncodeParams params = { .lossless = 1, .format = J2K_FORMAT_J2K };
j2k_encode(pixels, len, width, height, 1, 16, 0, &params,
           output, output_len, &compressed_size);
```

### Lossy Encode with Compression Ratio
```c
J2kEncodeParams params = {
    .lossless = 0,
    .compression_ratio = 10.0f, // 10:1 compression
    .num_resolutions = 5,
    .tile_width = 512,
    .tile_height = 512
};
j2k_encode(pixels, len, w, h, 3, 8, 0, &params, output, outlen, &size);
```

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 69ac0b7 | chore | Set up OpenJPEG vendoring infrastructure |
| 2f5c6b3 | feat | Implement JPEG 2000 wrapper with OpenJPEG |

## Verification Checklist

- [x] native/vendor/openjpeg/.gitkeep exists
- [x] native/vendor/README.md documents OpenJPEG 2.5.x dependency
- [x] native/src/j2k_wrapper.h declares all J2K functions (254 lines, min 40)
- [x] native/src/j2k_wrapper.c implements OpenJPEG API usage (1045 lines, min 200)
- [x] Resolution level decode (thumbnail) implemented via cp_reduce
- [x] ROI decode (partial image) implemented via opj_set_decode_area
- [x] Lossless and lossy encode work (5/3 vs 9/7 DWT)
- [x] sharpdicom_features() reports J2K capability when SHARPDICOM_WITH_J2K defined
- [x] build.zig includes j2k_wrapper.c and links OpenJPEG sources

## Deviations from Plan

None - plan executed exactly as written.

## Technical Notes

### OpenJPEG Source Files

The build includes these 22 OpenJPEG core files:
- bio.c, cio.c, dwt.c, event.c, function_list.c, ht_dec.c
- image.c, invert.c, j2k.c, jp2.c, mct.c, mqc.c
- openjpeg.c, opj_clock.c, opj_malloc.c, pi.c
- sparse_array.c, t1.c, t2.c, tcd.c, tgt.c, thread.c

### Format Detection

J2K raw codestream starts with SOC marker (0xFF 0x4F).
JP2 file format starts with 12-byte signature including 'jP  ' box.

### Memory Management

All decoding writes to caller-provided buffers (no internal allocation returned).
j2k_free() is reserved for future streaming/handle-based API.

## Next Phase Readiness

Ready for:
- Plan 13-04: CharLS (JPEG-LS) wrapper
- Plan 13-05: NuGet packaging integration

---

*Completed: 2026-01-30 | Duration: 5 minutes | Commits: 2*
