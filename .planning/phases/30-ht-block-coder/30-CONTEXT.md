# Phase 30: HT Block Coder - Context

**Gathered:** 2026-02-07
**Status:** Ready for planning

<domain>
## Phase Boundary

Implement the true High-Throughput JPEG 2000 block coding algorithm per ISO/IEC 15444-15 (ITU-T T.814). This includes rebuilding the J2K pipeline to fix multi-resolution subband architecture gaps from Phase 21, implementing the HT block coder with all three coding passes (Cleanup, SigProp, MagRef), integrating into the existing codec infrastructure, adding a `sharpdcm convert` CLI command, and achieving 10x performance over the current managed EBCOT implementation.

</domain>

<decisions>
## Implementation Decisions

### Spec Conformance Scope
- Full ITU-T T.814 (Part 15) implementation — all coding passes (Cleanup, SigProp, MagRef)
- Both lossless (5/3 DWT) and lossy (9/7 DWT) transforms, implemented with equal priority
- 8-bit, 12-bit, and 16-bit support — full medical imaging range
- MIXED mode supported — HT + legacy EBCOT blocks in same codestream
- PLT (Packet Length, Tile-part) markers always emitted for random access
- Multi-tile support from the start — enables whole-slide pathology and parallel decode
- All code-block styles supported (bypass, reset, restart, causal, erterm, segmark)
- Grayscale + multi-component color (1-component and 3+ with reversible/irreversible color transform)
- Configurable resolution levels with no artificial limit
- Full precinct partitioning support — configurable precinct sizes per resolution level
- Rate control: both bits-per-pixel and PSNR targets, plus named presets (Diagnostic, Archive, Review, Fast)
- CAP markers always emitted for interoperability
- All 5 progression orders: LRCP, RLCP, RPCL, PCRL, CPRL
- Full error resilience markers (SOP/EPH) configurable

### Integration Strategy
- Fix J2K pipeline first — full pipeline rebuild with proper multi-resolution subband architecture; HT builds on solid foundation
- HT default for HTJ2K transfer syntax; EBCOT remains for standard J2K transfer syntaxes
- Design for future native OpenJPH wrapper — registration points follow NativeJpegCodec pattern
- Extended API surface: IProgressiveCodec for resolution-level decode, ROI access, streaming decode, and encode-time quality layers
- Resolution-level decode returns native resolution by default; option to request upsampled to full size
- Span-based output for zero-allocation decode — caller provides output buffer
- Stateless/thread-safe block coder — no mutable state, all per-call state in structs
- Standard CodecRegistry priority rules — native > managed convention; managed HT at normal priority
- FoDicom compat layers (5.x and 4.x) expose HTJ2K codec options
- `sharpdcm convert` CLI command added — batch directory conversion with TTY-aware progress
- Direct J2K-to-HTJ2K transcoding via auto-detection — standard encode pipeline detects J2K input and skips DWT/tier-2 when possible
- Full decompress for OCR in de-identification pipeline — no low-res shortcuts
- IPixelDataCodec returns full buffer; IProgressiveCodec provides tile-by-tile streaming decode
- Partial codestream decode supported for DICOMweb byte-range requests — decode available resolution levels from incomplete data
- Both encode and decode support progressive quality layers (SNR scalability)

### Performance Targets
- Benchmark baseline: 10x faster than current managed SharpDicom EBCOT implementation
- Decode throughput target: 50+ megapixels/second on typical developer machine
- SIMD from day one — design data structures for SIMD from the start
- SIMD tiers: Vector128 (SSE2/NEON) + Vector256 (AVX2) + Vector512 (AVX-512) + SVE (Arm64)
- BMI2 hardware intrinsics (PDEP/PEXT) for VLC bit manipulation
- Configurable tile parallelism — MaxDegreeOfParallelism, default Environment.ProcessorCount
- Full parallel pipeline — parallel block decode + parallel DWT inverse per tile
- Stackalloc for small blocks (< 4KB), ArrayPool for large — minimize GC pressure
- 4x4 quad-aligned coefficient layout — cache-optimal for HT algorithm
- Branchless hot paths — conditional moves and lookup tables for VLC decode loops
- Lazy-init VLC lookup tables — compute on first use, cache for reuse
- BenchmarkDotNet suite: 256, 512, 2048, 4096, 8192, 16384 image sizes
- Compare vs native OpenJPH in benchmarks — shows managed vs native gap
- Benchmarks in CI for tracking but no build-failure gate (machine variability)
- Memory budget: 4x raw image size peak (128MB for 4096x4096 16-bit)
- Optimize DWT too — bring existing DWT up to Vector256/512/SVE alongside HT work
- Encode presets with speed tradeoff: 'fast' (cleanup-only) for streaming, 'quality' (all passes, full RD) for archival
- PGO-friendly attributes and benchmark profiles for .NET 8+ dynamic PGO
- Full AOT support — no reflection, no Reflection.Emit, all tables compile-time or static init

### Conformance Testing
- Verify against both OpenJPH and OpenJPEG reference implementations
- Official ITU-T test vectors for conformance proof + synthetic test data for edge cases
- Lossy PSNR thresholds by preset: Diagnostic 40+ dB, Archive 35+ dB, Review 30+ dB, Fast 25+ dB
- Lossless roundtrip: pixel values identical, allow trailing byte padding differences
- Cross-decoder interop: OpenJPH and OpenJPEG (not DCMTK/GDCM)
- MIXED mode cross-decode: SharpDicom-encoded MIXED decodable by OpenJPH
- Test images: geometric patterns, medical-like phantoms, plus edge cases (all-black, all-white, single-pixel, max-size, random noise)
- FsCheck property-based tests for broad coverage + explicit NUnit cases for known edge cases
- Standalone codestream validator — checks marker syntax independent of decode
- Full EBCOT regression suite — all existing J2K tests must pass after pipeline rebuild
- Configurable strict/lenient error handling — default strict, lenient via option (matches DicomReaderOptions pattern)
- SharpFuzz integration for security fuzzing of managed decoder
- Test data generated on-the-fly using reference encoders — no stored test data in repo
- Reference implementations (OpenJPH, OpenJPEG) built from source in CI
- 90%+ line coverage target
- SSIM perceptual diff comparison against reference decoder output

### Claude's Discretion
- Exact internal data structure layouts beyond the 4x4 quad-aligned requirement
- VLC lookup table sizes and organization
- Specific SIMD instruction selection within each tier
- Rate-distortion optimization algorithm details
- Thread pool strategy for parallel tile decode
- Error recovery strategies in lenient mode
- Pipeline rebuild sequencing and intermediate milestones

</decisions>

<specifics>
## Specific Ideas

- Direct J2K-to-HTJ2K transcoding by swapping only the block coder layer — leverage shared DWT/tier-2 infrastructure
- IProgressiveCodec extends IPixelDataCodec with resolution-level access, tile streaming, and partial codestream decode
- "Cleanup-only" fast encode mode that skips SigProp and MagRef passes entirely for maximum throughput
- Native OpenJPH wrapper slots in via standard CodecRegistry priority (native > managed) when available
- `sharpdcm convert --transfer-syntax htj2k-lossless` for immediate CLI access to the new codec

</specifics>

<deferred>
## Deferred Ideas

- HTJ2K resolution-level metadata in BSON/MongoDB serialization layer — valuable for server-side partial decode, but belongs in a BSON enhancement phase rather than codec implementation
- Native OpenJPH wrapper implementation — Phase 30 designs the integration points, actual wrapper is a separate effort
- DICOMweb server-side HTJ2K streaming — partial decode support is in the codec, but the HTTP/WADO-RS serving layer is future work

</deferred>

---

*Phase: 30-ht-block-coder*
*Context gathered: 2026-02-07*
