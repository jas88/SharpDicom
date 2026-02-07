# Phase 27: Extended Codec Support - Context

**Gathered:** 2026-02-06
**Status:** Ready for planning

<domain>
## Phase Boundary

Add 12-bit/16-bit JPEG encoding/decoding (both managed and native) and video DICOM encoding (MPEG2, H.264, HEVC with audio) to the SharpDicom codec infrastructure. Phase 13's decode-only FFmpeg wrappers become full encode+decode. The existing Zig cross-compilation, NuGet packaging, and codec registry infrastructure are extended, not replaced.

</domain>

<decisions>
## Implementation Decisions

### 12-bit JPEG build strategy
- Single fat native library with symbol prefixes (`jpeg8_`/`jpeg12_`) — NOT separate libraries
- Build libjpeg-turbo from source via Zig twice (8-bit and 12-bit), link both into one native library
- Descriptive symbol prefixes: `jpeg8_*` and `jpeg12_*` (not short jp8/jp12)
- Both lossy (Process 2,4) and lossless (Process 14) modes for 12-bit
- Extend precision support up to 16-bit for lossless JPEG

### 12-bit JPEG managed codec
- Both managed (pure C#) and native codecs get 12-bit support
- Separate `JpegExtendedCodec` class for 12-bit lossy DCT (not merged into JpegBaselineCodec)
- Managed lossless codec extended to handle up to 16-bit precision
- Explicit codec classes: `NativeJpeg8Codec` and `NativeJpeg12Codec` registered separately in codec registry

### 12-bit transfer syntax handling
- Register for standard 1.2.840.10008.1.2.4.51 (JPEG Extended Process 2,4)
- Also leniently handle 12-bit data found in 1.2.840.10008.1.2.4.50 (Process 1) for non-conformant systems

### 12-bit test data
- Synthetic 12/16-bit pixel data generated programmatically for codec correctness
- Also include any available NEMA/DICOM WG4 reference files for 12-bit transfer syntaxes

### Video encoding scope
- All three formats: MPEG2, H.264, HEVC encoding
- Full audio support: AAC (compressed) and PCM (uncompressed), caller chooses
- Both color and grayscale video (MONOCHROME1/2 and YCbCr/RGB)
- Both streaming (IAsyncEnumerable<Frame>) and batch encoding modes
- IProgress<T> for encoding progress reporting (frames encoded, percentage, ETA)
- GPU acceleration when available (NVENC, VideoToolbox, VAAPI) with CPU fallback
- High-level convenience API (VideoEncoder.EncodeFromDicom) plus low-level granular API

### Video input sources
- Raw pixel frames (ReadOnlySpan<byte>) with dimensions/format
- Existing multi-frame DicomFile (re-encode from any compressed format via codec registry)
- Image file sequences (PNG/BMP/TIFF) via built-in stb_image decoding
- Color space: accept both RGB and YCbCr, caller specifies which via flag; encoder converts as needed
- Frame rate: auto-detect from DICOM tags (FrameTime 0018,1063 / CineRate 0018,0040), require explicit if missing

### Video quality presets
- Named presets: Diagnostic (high quality), Review (balanced), Archive (smaller)
- Plus escape hatch for raw FFmpeg parameters (CRF, bitrate, etc.)

### Video DICOM file structure
- All applicable SOP classes: Video Endoscopic, Microscopic, Photographic, SC True Color, XA/XRF, US Multi-frame
- Both builder pattern (fluent API) and template dataset for metadata
- Auto-generate 2.25.{uuid} UIDs if not provided
- Single encapsulated fragment (one video bitstream), not per-GOP fragmentation

### Distribution & dependencies
- SharpDicom is GPL — bundling GPL FFmpeg/x264/x265 is fine
- All native additions (FFmpeg encoding, stb_image, 12-bit libjpeg-turbo) go into existing SharpDicom.Codecs.Native package
- stb_image compiled directly into the sharpdicom_native library (single-header, ~50KB)
- FFmpeg built from source via Zig (consistent with Phase 13 cross-compilation pattern)

### Claude's Discretion
- Exact FFmpeg configure flags for minimal encoding build
- stb_image format support scope (which image formats beyond PNG/BMP/TIFF)
- GPU encoder detection and fallback mechanism details
- Frame rate derivation logic when multiple DICOM tags conflict
- Exact medical imaging quality preset CRF/bitrate values

</decisions>

<specifics>
## Specific Ideas

- Symbol prefix pattern: `jpeg8_compress`, `jpeg8_decompress`, `jpeg12_compress`, `jpeg12_decompress`
- Re-encoding pipeline: decompress existing frames via codec registry, feed raw pixels to video encoder
- VideoFileBuilder should follow the same fluent pattern as DicomDeidentifierBuilder
- stb_image is already proven in game dev for PNG/BMP/TGA/JPEG loading — lightweight and battle-tested

</specifics>

<deferred>
## Deferred Ideas

- None — discussion stayed within phase scope

</deferred>

---

*Phase: 27-extended-codec-support*
*Context gathered: 2026-02-06*
