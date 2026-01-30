# Phase 13: Native Codecs Package - Context

**Gathered:** 2026-01-28
**Status:** Ready for planning

<domain>
## Phase Boundary

Optional high-performance codec package (`SharpDicom.Codecs`) wrapping native libraries that override pure C# codecs when loaded. Provides libjpeg-turbo, OpenJPEG, CharLS, FFmpeg bindings with GPU acceleration support. Cross-platform NuGet package with binaries for win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64.

</domain>

<decisions>
## Implementation Decisions

### Native Library Loading

- **Bundled in NuGet**: Native binaries included in package, extracted by NuGet to runtimes/RID/native/
- **Eager load on module init**: ModuleInitializer loads natives at assembly load time (fail fast)
- **Throw on load failure**: No silent fallback to pure C# — exception with detailed diagnostics
- **IsAvailable property**: Static `NativeCodecs.IsAvailable` for pre-checks before use
- **Configurable path**: `NativeCodecs.SetLibraryPath()` allows custom native location
- **Version validation**: Verify native version matches compiled-against version, throw on mismatch
- **SkipVersionCheck option**: `NativeCodecs.Initialize(skipVersionCheck: true)` escape hatch
- **Thread-safe init**: Use locking/Lazy<T> for single initialization
- **Once-only init**: `Initialize()` can only be called once, throws if called again
- **Both auto and explicit init**: Auto-init by default, explicit `Initialize()` with options supported
- **AppContext switch to disable auto-init**: `AppContext.SetSwitch("SharpDicom.Codecs.DisableAutoInit", true)`
- **InitializeAsync**: Provide async initialization for hosted scenarios
- **Detailed exceptions**: Include file path tried, OS error code, architecture info
- **No ILogger**: Diagnostics via exception messages only, no logging dependency
- **Framework-specific loading**: SetDllDirectory on .NET Framework, NativeLibrary.SetDllImportResolver on modern
- **Let OS handle unload**: No explicit native library unload
- **Standard error for VC++ runtime**: Let DllNotFound propagate naturally
- **Version info exposed**: `NativeCodecs.LibjpegTurboVersion`, `NativeCodecs.OpenJpegVersion`, etc.
- **SIMD feature info exposed**: `NativeCodecs.ActiveSimdFeatures` shows SSE2/AVX2/NEON etc.
- **Architecture mismatch detection**: Detect and throw clear message on x64/arm64 mismatch
- **CPU capability check**: Verify CPU supports required SIMD at load time
- **AOT-aware loading**: Detect AOT mode and use appropriate loading strategy

### Build System

- **Zig for cross-platform builds**: Use Zig compiler for cross-compilation from single platform
- **GitHub Actions with Zig**: CI uses Zig to cross-compile for all targets
- **NVCC for CUDA**: Use NVIDIA's nvcc compiler for CUDA portions separately
- **Static linking**: Link libjpeg-turbo, OpenJPEG, CharLS, FFmpeg, zlib-ng statically
- **Static musl for Linux**: Statically link musl for zero runtime dependencies
- **Same repo as SharpDicom**: Native code in SharpDicom/native/ subdirectory
- **C language wrapper**: Pure C wrapper around library APIs
- **Vendored source**: Copy libjpeg-turbo/OpenJPEG/CharLS/FFmpeg source into native/vendor/
- **CI artifacts only**: Binaries built in CI, not committed to repo
- **Same version as NuGet**: Native version = package version
- **Native test executable**: Build test binary for verifying native builds work

### Native Library Features

- **Single combined library**: One sharpdicom_codecs.dll/so containing all codec bindings
- **Name convention**: sharpdicom_codecs.dll / libsharpdicom_codecs.so / libsharpdicom_codecs.dylib
- **Release builds only**: No debug builds in NuGet package
- **Full SIMD enabled**: SSE4.2/AVX2/NEON optimizations in builds
- **Runtime SIMD dispatch**: Single binary detects CPU features at runtime
- **ForceScalar option**: `NativeCodecs.Initialize(forceScalar: true)` to disable SIMD
- **PIC/PIE**: Position-independent code for ASLR
- **Full hardening**: -fstack-protector-strong, -D_FORTIFY_SOURCE=2
- **Unsigned binaries**: No code signing for natives
- **Source link included**: Enable source link for native debugging
- **No integrity hash check**: Trust NuGet package signing
- **SBOM generated**: Software Bill of Materials for dependencies
- **License texts included**: THIRD_PARTY_LICENSES.txt with all licenses

### Codec Coverage

- **JPEG Baseline/Extended**: libjpeg-turbo for 8-bit, 12-bit, 16-bit JPEG
- **JPEG Lossless**: libjpeg-turbo for Process 14 SV1
- **JPEG-LS**: CharLS for lossless JPEG (ISO 14495-1)
- **JPEG 2000**: OpenJPEG for lossless and lossy J2K
- **HTJ2K**: OpenJPEG's HTJ2K support for High-Throughput JPEG 2000
- **MPEG2/MPEG4/HEVC**: FFmpeg for video codecs including H.265
- **RLE**: Native RLE faster than pure C# implementation
- **Deflate**: zlib-ng for deflated transfer syntax
- **No JPEG XL**: Wait until DICOM adopts it
- **No zstd**: Stick to DICOM-standard zlib/deflate only
- **No documents**: PDF/CDA out of scope (image/video only)

### GPU Acceleration

- **GPU in main package**: GPU paths included in SharpDicom.Codecs
- **NVIDIA nvJPEG2000 + OpenCL fallback**: Try nvJPEG2000 first, fall back to OpenCL, then CPU
- **CUDA bundled**: Include CUDA runtime stubs in package
- **Compute 5.0 minimum**: GTX 750 Ti and newer (Maxwell+)
- **OpenCL bundled**: Include khronos-opencl-icd-loader
- **GpuAvailable property**: Expose GPU availability for caller checks
- **ForceCpu option**: `NativeCodecs.Initialize(preferCpu: true)` to skip GPU
- **Both encode and decode**: GPU acceleration for both directions
- **Async GPU decode**: `DecodeAsync()` returns Task for background GPU decode
- **Batch API**: `DecodeMultiple()` for multi-frame throughput on GPU
- **Hardware video decode**: NVDEC, QuickSync, VA-API for video
- **Hardware video encode**: NVENC, QuickSync for video encoding
- **CPU memory output**: GPU decode always copies to managed buffer
- **No GPU metrics**: No built-in performance tracking

### Codec API

- **Thread-safe concurrent decode**: Multiple threads can decode simultaneously
- **System malloc**: Standard malloc/free, no custom allocator
- **Direct to managed buffer**: Pin managed array, pass pointer to native
- **No callbacks**: Full decode only, no progressive/partial callbacks
- **Streaming decode**: Support incremental decode as data arrives
- **Opaque handle for streaming**: Native returns handle for streaming state
- **SafeHandle wrapper**: Wrap streaming handle for GC-aware cleanup
- **No cancel API**: Once started, decode runs to completion or error
- **Configurable quality/compression**: Pass parameters via struct
- **Resolution levels**: Decode at 1/2, 1/4, etc. resolution for thumbnails
- **Quality layers**: Decode up to specified quality layer for progressive J2K
- **ROI decode**: Decode only a region for large images
- **Tiled encode**: Configurable tile size for J2K
- **Multi-frame aware**: Support multi-frame J2K codestreams natively
- **YBR/RGB conversion in native**: Handle photometric interpretation conversion
- **Planar configuration in native**: Color-by-plane to color-by-pixel conversion
- **Signed/unsigned in native**: Handle pixel representation based on metadata
- **Raw pixels (no rescale)**: Return stored values, rescale in managed
- **Managed handles overlays**: Overlay extraction is managed responsibility
- **Managed handles endianness**: Byte swapping in managed code
- **Trust managed for fragments**: Native receives raw fragments, managed validates structure
- **Return raw compressed**: Native returns compressed bytes, managed wraps in fragments
- **DICOM-aware JPEG quirks**: Handle embedded SOI markers and other edge cases
- **Lenient JPEG parsing**: Tolerate minor non-conformance in JPEG structure
- **Validate decoded dimensions**: Verify size matches expected from DICOM metadata
- **Graceful corruption detection**: Detect corrupted/truncated data, return clear error
- **1 GB default memory limit**: Configurable max memory per decode
- **30 second default timeout**: Configurable decode timeout with checkpoints in native
- **Audio support**: Extract audio tracks from DICOM multi-media
- **Compressed audio output**: Keep audio in compressed format
- **Timestamp sync**: Expose PTS/DTS for A/V synchronization
- **Frame seeking**: Support seeking to specific frame number in video
- **cdecl calling convention**: C conventions for maximum compatibility
- **Out parameters**: Pass pointers for multiple return values
- **Error codes**: Functions return error code, success = 0
- **ABI breaking allowed**: ABI can change between versions, version check enforces match
- **Individual function exports**: jpeg_decode(), j2k_decode(), etc. as separate exports
- **Raw bytes API only**: No file I/O in native, buffer + length only

### Registration & Override

- **Replace CodecRegistry registrations**: Native codecs call `CodecRegistry.Register()` with higher priority
- **Numeric priority**: Register with priority value, highest wins
- **Priority levels**: 0=fallback, 50=pure C#, 100=native, 200=user
- **Per-codec enable/disable**: `NativeCodecs.EnableJpeg = false` to skip specific codec override
- **GetCodecInfo()**: Return codec name, priority, native/managed for debugging
- **CodecRegistered event**: Fire event when new codec registered
- **Eager registration**: Register all codecs in ModuleInitializer at load time

### Platform Packaging

- **Meta + runtime packages**: SharpDicom.Codecs (meta) + SharpDicom.Codecs.runtime.* per platform
- **Auto-select runtime**: NuGet targets auto-select correct runtime package
- **6 RIDs**: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64
- **Managed in meta package**: Managed wrapper code in SharpDicom.Codecs
- **Standard folder structure**: runtimes/RID/native/ convention
- **Include .targets**: Ensure natives copied to output directory
- **Self-contained support**: Natives bundled for dotnet publish --self-contained
- **Single-file support**: Support dotnet publish -p:PublishSingleFile=true
- **AOT compatible**: No reflection, trim-compatible, AOT friendly
- **Full trim support**: Include [DynamicallyAccessedMembers], no trim warnings
- **Both P/Invoke styles**: LibraryImport on .NET 7+, DllImport on older via #if
- **Same version all packages**: All packages share version number
- **Embedded PDB**: Embed PDB in DLL for debugging
- **Embedded readme**: Include README.md for NuGet package page
- **Deterministic builds**: Enable full reproducibility
- **Unsigned packages**: No NuGet package signing
- **Same TFMs as core**: netstandard2.0, net8.0, net9.0
- **Include analyzer**: Roslyn analyzer for initialization and disposal warnings
- **Full XML docs**: Complete documentation comments for IntelliSense
- **Natives-only runtime packages**: Runtime packages just have native libs
- **Exact version dependency**: Require same version of SharpDicom core
- **No benchmark data in package**: Benchmarks in repo only
- **Package icon**: Include icon.png for NuGet page
- **Release notes in package**: CHANGELOG.md excerpt in metadata
- **Reproducible builds**: Same source = same package hash
- **NuGet.org publish**: Publish to public NuGet.org
- **Stable releases only**: No pre-release packages
- **Auto-release on tag**: Push v1.0.0 tag triggers release
- **GitHub release created**: Create GitHub release with binaries
- **Auto + manual release notes**: Auto-generate from commits, allow edits
- **Test on 3 platforms**: linux-x64, win-x64, osx-arm64 as primary test targets
- **Native ARM runners**: Use ARM-native GitHub runners for ARM tests
- **Cache native builds**: Cache with hash-based invalidation
- **Same workflow**: One ci.yml for native and managed

### Error Handling

- **DicomCodecException**: Use existing exception type from Phase 9
- **NativeErrorCode property**: Expose raw error code from native library
- **Full context in exception**: Include TS, frame, image dimensions
- **Catch native crashes**: Wrap SEH/SIGSEGV as managed exceptions
- **Native stack in exception**: Include native stack trace if available
- **Error categories**: CodecErrorCategory enum (Decode, Encode, Memory, Timeout, Unsupported, IO, Configuration, Initialization)
- **TryDecode pattern**: bool TryDecode(out result, out error) available
- **CodecError struct**: Struct with code, category, message for TryDecode
- **Fail fast**: No auto-retry for transient errors
- **Suggestions in errors**: Error includes helpUrl or suggestion string
- **Validate input first**: Check dimensions, bit depth before native call
- **ArgumentException for validation**: Validation errors use ArgumentException
- **Localized error messages**: Support resource-based localization
- **Major language support**: English, German, French, Japanese, Chinese
- **Stable error codes**: Error codes are part of API contract
- **DiagnosticSource tracing**: Use System.Diagnostics.DiagnosticSource for operations
- **Basic events only**: Start/stop/error events, no timing/memory metrics
- **DiagnosticSource only**: No direct OpenTelemetry integration

### Claude's Discretion

- Native library loading mechanism details on each platform
- Exact memory limit configuration API
- Timeout checkpoint frequency in native code
- Analyzer diagnostic IDs and messages
- DiagnosticSource event naming
- Exact error code assignments
- Resource file organization for localization
- Cache key structure for CI

</decisions>

<specifics>
## Specific Ideas

- "Use Zig for cross-platform builds" — single toolchain for all targets from one platform
- "Static musl so library has no dependencies" — eliminate glibc/musl choice entirely
- "SharpDicom is already GPL" — no licensing conflict with GPL'd FFmpeg/x264/x265
- GPU decode with nvJPEG2000 → OpenCL → CPU fallback chain

</specifics>

<deferred>
## Deferred Ideas

- ICC color profile support — future phase (not in v1.0.0/v2.0.0 requirements)
- JPEG XL codec — wait until DICOM adopts it
- Encapsulated document codecs (PDF/CDA) — out of scope for this package

</deferred>

---

*Phase: 13-native-codecs-package*
*Context gathered: 2026-01-28*
