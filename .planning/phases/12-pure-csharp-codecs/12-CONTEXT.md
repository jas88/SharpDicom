# Phase 12: Pure C# Codecs - Context

**Gathered:** 2026-01-28
**Status:** Ready for planning

<domain>
## Phase Boundary

Implement JPEG and JPEG 2000 codecs in pure C# for maximum portability and AOT compatibility. Covers JPEG Baseline (Process 1), JPEG Lossless (Process 14 SV1), JPEG 2000 Lossless/Lossy, with photometric interpretation handling and multi-frame support. All codecs must register via existing IPixelDataCodec interface.

</domain>

<decisions>
## Implementation Decisions

### Codec Prioritization
- JPEG Baseline (8-bit lossy) first - most common in medical imaging
- JPEG Lossless (Process 14 SV1) second - critical for diagnostic quality
- JPEG 2000 Lossless third - growing adoption
- JPEG 2000 Lossy fourth - should-have priority
- JPEG-LS and JPEG XR as future phases if needed

### Error Handling Architecture

#### Exception Hierarchy
- `DicomCodecException` as base class for all codec errors
- Specific derived types: `JpegDecodeException`, `Jpeg2000DecodeException`, `JpegLsDecodeException`
- Severity levels: Fatal (unrecoverable), Error (operation failed), Warning (degraded result), Info (diagnostic)
- Full context capture: tag, frame number, byte offset, transfer syntax, photometric interpretation

#### Error Behavior Modes
- Configurable strictness: Strict (fail on first error), Lenient (recover where possible), Permissive (best effort)
- Per-operation error suppression available
- Different defaults for encode (stricter) vs decode (more lenient)
- Streaming errors to callbacks rather than collecting large error lists

#### Error Logging & Diagnostics
- Automatic logging integration via configurable sink
- Callbacks for real-time error notification
- Both ISerializable support and DTO conversion for error objects
- Rate counters and circuit breaker pattern for degraded mode
- AsyncLocal isolation for error configuration per-operation

#### Recovery & State
- Memory pressure: degrade gracefully first, fail if still insufficient
- Platform validation at startup + runtime normalization
- Vendor-specific exceptions for known issues, generic for unknown
- Error equality via full comparison + IsSameError convenience method
- Configurable error deduplication
- Minimal state capture by default + full capture on demand

#### Concurrency & Tracing
- Try-timeout pattern for concurrency issues
- Activity-based correlation + fallback for pre-.NET 6
- Hash-based fingerprinting + pattern matching for error classification
- Configurable hot path checking
- No error history tracking (external responsibility)
- No error sampling (all errors reported)
- Dynamic mitigation hints based on error patterns

#### Testing & Documentation
- Conditional test mode + runtime injection for error simulation
- Auto-generated documentation from exception types + manual overrides
- Category + severity based priority classification
- Static factory methods + seams for testability
- Full AOT support (no reflection in error handling)

#### Specific Error Scenarios
- SIMD errors: pre-check capability only (no runtime fallback)
- Buffer safety: bounds checking + context on overflow
- Recursion: iterative rewrite for stack safety
- Text encoding: use replacement characters except in strict mode
- Long paths: auto-prefix with \\?\ on Windows
- DICOM-specific: DICOM rules for culture/decimal parsing
- Date/time parsing: configurable strictness
- Y2K38: use 64-bit timestamps always
- LUT errors: error on issues (not silent fallback)
- Frame numbering: error in strict mode, warn and use actual frame count otherwise
- Overlay data: separate channel handling
- Fragment ordering: reorder to correct sequence
- Offset table mismatch: error always

### Photometric Conversion

#### Supported Interpretations
- Decode: MONOCHROME1, MONOCHROME2, RGB, YBR_FULL, YBR_FULL_422, YBR_PARTIAL_420, PALETTE COLOR
- Rare types supported: HSV, ARGB, CMYK (with conversion to RGB)
- YBR_RCT and YBR_ICT: distinct handling (lossless vs lossy transforms)

#### Conversion Behavior
- YCbCr conversion method: auto-detect from transfer syntax and photometric
- Auto-convert to standard format: configurable (default off for lossless, on for display)
- MONOCHROME1 handling: configurable (invert on decode vs preserve)
- JPEG 2000 ICT/RCT: auto-detect from codestream

#### Palette/LUT Handling
- Palette COLOR: both indexed output and expanded RGB output available
- Segmented LUT: both linear interpolation and discrete approaches
- Supplemental LUT: full support
- LUT optimization: on-demand calculation
- LUT caching: yes (not result caching)
- LUT order: configurable (DICOM standard vs file order)
- Palette index validation: configurable

#### Chroma & Subsampling
- Chroma upsampling: configurable algorithm (nearest, bilinear, etc.)
- Subsampling on encode: full control (4:4:4, 4:2:2, 4:2:0)

#### Output Formats
- Supported: BGRA32, RGBA32, Gray8, Gray16, native format passthrough
- Bit depth conversion: via windowing (W/L or VOI LUT)
- Precision for intermediate calculations: configurable (default higher precision)
- Signed output: preserve signed pixel representation
- Float pixel handling: full float pipeline support
- Mixed bit depth frames: configurable handling

#### Color Space & ICC
- ICC profile handling: configurable (honor, ignore, convert)
- Output color spaces: multiple targets (sRGB, Display P3, Adobe RGB, device profile)
- Output ICC embedding: optional
- Gamma correction: configurable
- Rendering intent: configurable (perceptual, relative colorimetric, etc.)
- Black point compensation: configurable
- White point: configurable (D50, D65, custom)
- Chromatic adaptation: configurable (Bradford, Von Kries, etc.)

#### Advanced Color Science
- Lab color space: full support
- XYZ color space: full exposure
- Spectral data: full support for advanced workflows
- Color temperature: full support
- Delta E metrics: full support (CIE76, CIE94, CIEDE2000)
- Perceptual uniformity: full analysis tools
- 3D LUT: full support (import/export)
- Color matching/correction: full support
- False color mapping: full support (for visualization)
- CT window presets: full preset library

#### Processing Options
- SIMD: always use where available (Vector128/256)
- Parallel conversion: configurable per-frame parallelism
- Progress reporting: none (too fine-grained)
- In-place conversion: option available
- Memory layout: both row-major and column-major
- Stride: configurable
- Component reorder: both RGB↔BGR directions
- Byte swap: integrated with conversion

#### Quality & Validation
- Quality metrics: PSNR and SSIM available
- Color validation: reference image comparison tests
- DICOM conformance testing: included
- Roundtrip: bit-exact for lossless paths
- Test coverage target: 100%

#### HDR & Tone Mapping
- HDR support: full (including PQ, HLG curves)
- HDR tone mapping: full support (Reinhard, ACES, filmic, custom)
- Gamut clipping: configurable (clip, compress, map)
- Gamut validation: full check capability

#### Image Processing Extensions
- Histogram equalization: advanced (CLAHE support)
- Auto white balance: multiple algorithms
- Skin tone protection: full support
- Denoising: full support
- Edge-aware processing: full suite (bilateral, guided, domain transform)
- Color segmentation: full support
- Morphological operations: full
- Feature extraction: full color descriptors
- Dithering on quantization: configurable

#### Frame Optimization
- Frame-to-frame optimization: full (exploit temporal coherence)
- Color drift prevention: implemented

#### API Design
- Both integrated (in codec) and standalone conversion API
- API naming: both DICOM terminology and graphics terminology
- Extensibility: override capability for custom conversions
- Subpixel rendering: aware (for display optimization)
- Color blindness simulation: full support
- Bidirectional conversion: encode and decode paths
- Batch processing: single-image focus (not batch utilities)

#### Vendor & Format Specifics
- Vendor photometric handling: configurable (raw, best guess, error)
- GSPS (Grayscale Softcopy Presentation State): optional support
- JPEG markers precedence: configurable (APP0 vs DICOM metadata)
- Quality limits: medical imaging defaults (conservative lossy)
- Auto-detect missing photometric: configurable

#### Numeric & Platform
- Clamping: saturating arithmetic
- Intermediate precision: higher than output
- Profiling: none built-in (use external tools)

#### Documentation
- Both API reference and conceptual documentation
- Code examples included
- Full color theory reference
- Profile formats: full support (ICC, DNG, 3DL, cube, CLF)
- Display profile matching: full
- Soft proofing: full support

#### Testing
- IQ (Image Quality) testing: full suite
- Per-channel processing: both locked and independent modes
- Quantization algorithms: multiple (median cut, octree, k-means)
- Channel operations: full support
- Adjustment suite: full (brightness, contrast, saturation, hue, etc.)
- Tone curves: full control

### Multi-frame Handling

#### Frame Decode Strategy
- Sequential with cache: reuse DCT tables, Huffman trees, J2K tiles between frames
- Exploit inter-frame redundancy for better decode performance

#### Frame Access Patterns
- Both modes: sequential default with optional random access API
- Random access via offset table when available

#### Partial Failure Handling
- Configurable: strictness setting determines behavior
- Strict mode: fail entire decode on any frame error
- Lenient mode: return successful frames with error list for failed

#### Parallelism
- Configurable: MaxDegreeOfParallelism option
- Default: single-threaded (caller controls parallelism)

#### Memory Management
- Both options: caller-provided buffers and codec-allocated from ArrayPool
- Overloads for each pattern

#### Progress Reporting
- Configurable: optional IProgress<T> with frame count/total

#### Cancellation
- Immediate abort: check within frame decode
- May leave partial state on cancellation

#### Frame Metadata
- Full extraction: include codec-specific metadata (J2K tile info, JPEG markers)

#### Iteration API
- Both patterns: IEnumerable<Frame> and IAsyncEnumerable<Frame>

#### Frame Caching
- No caching: each access re-decodes frame (caller manages caching)

#### Range Decode
- Index list: DecodeFrames(int[] indices) for arbitrary frame subsets

#### Encode API
- Both APIs: bulk all-at-once and streaming AddFrame() patterns

#### Temporal Compression
- Motion estimation: exploit temporal redundancy for better compression
- Applicable to video-like sequences (cine, fluoroscopy)

#### Frame Timing
- Full timing API: FrameTimeVector, playback rate, temporal position

#### Validation
- Full validation: verify dimensions, bit depth, photometric consistent across frames
- Verify actual frame count matches NumberOfFrames tag

#### Functional Groups
- Full support: merge shared + per-frame functional groups for each frame's context

#### Per-frame Photometric
- Auto-detect: infer from codec markers if metadata missing

#### Frame Extraction
- Extract single: ExtractFrame(index) returns standalone DICOM dataset

#### Frame Concatenation
- Append frames: AppendFrame() adds to existing multi-frame

#### Thumbnails/Previews
- Full preview API: DecodePreview(targetSize) with quality/speed tradeoff
- Use J2K resolution levels when available

#### Frame Analysis
- Full analysis: motion vectors, change detection, similarity metrics

### Claude's Discretion
- Exact SIMD instruction selection per platform
- Internal buffer sizing and pooling strategies
- Specific codec implementation algorithms (within spec compliance)
- Performance optimization trade-offs
- Debug visualization implementation details
- GPU acceleration implementation (optional feature)

</decisions>

<specifics>
## Specific Ideas

- Error handling should follow established patterns from DicomStreamReader (callbacks, strictness levels)
- Codec interface already exists from Phase 9 (IPixelDataCodec, CodecRegistry) - must integrate seamlessly
- Pure C# is non-negotiable - no native dependencies (that's Phase 13)
- AOT/Trim compatibility required - no reflection, no dynamic code generation
- DICOM conformance suite from NEMA WG-04 is the validation target
- Photometric Interpretation tag must match actual pixel data after encode
- Color science implementation should be reference-quality for medical imaging use

</specifics>

<deferred>
## Deferred Ideas

- Native codec wrappers (libjpeg-turbo, OpenJPEG) — Phase 13
- JPEG-LS codec — future phase if needed
- JPEG XR codec — future phase if needed
- HTJ2K (High-Throughput JPEG 2000) — future enhancement
- GPU-accelerated codecs — optional feature, not core requirement

</deferred>

---

*Phase: 12-pure-csharp-codecs*
*Context gathered: 2026-01-28*
