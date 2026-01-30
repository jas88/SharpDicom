# Phase 13: Native Codecs Package - Research

**Researched:** 2026-01-29
**Domain:** Native library interop, cross-platform codec bindings, GPU acceleration
**Confidence:** HIGH

## Summary

This phase creates SharpDicom.Codecs, an optional high-performance codec package wrapping native libraries (libjpeg-turbo, OpenJPEG, CharLS, FFmpeg) with GPU acceleration support (nvJPEG2000, OpenCL). The package uses Zig for cross-platform builds from a single machine and follows NuGet native package conventions with runtime-specific packages.

The research confirms the existing 13-CONTEXT.md decisions are sound and aligns with current best practices for native interop in .NET 8/9. Key findings include:
- Zig 0.13+ provides reliable cross-compilation to all six target RIDs
- LibraryImport (NET7+) with DllImport fallback is the correct AOT-compatible P/Invoke strategy
- NuGet's `runtimes/{RID}/native/` pattern is well-supported for self-contained and single-file publish
- ModuleInitializer + NativeLibrary.SetDllImportResolver enables robust native loading
- SafeHandle is the modern pattern for native resource management

**Primary recommendation:** Build a single combined native library per platform using Zig, expose via LibraryImport/DllImport dual-path P/Invoke, and auto-register codecs via ModuleInitializer at assembly load time.

## Standard Stack

The established libraries/tools for this domain:

### Core Native Libraries

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| libjpeg-turbo | 3.0+ | JPEG baseline/extended/lossless | 2-6x faster than libjpeg, SIMD-optimized, most widely used |
| OpenJPEG | 2.5+ | JPEG 2000 lossless/lossy + HTJ2K | BSD license, ISO reference implementation, HTJ2K support |
| CharLS | 2.4+ | JPEG-LS lossless/near-lossless | Only maintained JPEG-LS library, pure C++14, ~2x faster than HP reference |
| FFmpeg (libavcodec) | 6.0+ | MPEG2/MPEG4/HEVC video | Industry standard, hardware codec support, GPL (compatible) |
| zlib-ng | 2.1+ | Deflate compression | Drop-in zlib replacement with SIMD, 2-3x faster |

### Build Toolchain

| Tool | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| Zig | 0.13+ | Cross-platform C compilation | Single toolchain for all targets, bundled libc, no separate cross-compilers needed |
| NVCC | CUDA 12.4+ | CUDA compilation | Required for nvJPEG2000 GPU acceleration |

### .NET Interop

| Package/API | Version | Purpose | Why Standard |
|-------------|---------|---------|--------------|
| LibraryImport | .NET 7+ | P/Invoke source generation | AOT-compatible, compile-time marshalling |
| NativeLibrary | .NET Core 3.1+ | Native library loading | DllImportResolver for custom loading logic |
| SafeHandle | .NET Framework 2.0+ | Native resource management | GC-aware, finalization-safe handle wrapper |

### GPU Acceleration

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| nvJPEG2000 | CUDA 12.x | GPU JPEG 2000 decode | NVIDIA's optimized library, 7-10x faster than CPU |
| OpenCL ICD Loader | 2023+ | GPU compute fallback | Platform-agnostic GPU support (AMD, Intel, NVIDIA) |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| OpenJPEG | OpenJPH | Better HTJ2K performance, but less mature for standard J2K |
| Zig | CMake + multiple toolchains | More complex CI, separate toolchain per target |
| libjpeg-turbo | Jpegli | Better quality at same size, but newer/less tested |
| zlib-ng | ISA-L | Faster on Intel, but less portable |

## Architecture Patterns

### Recommended Project Structure

```
native/
├── build.zig                    # Main Zig build script
├── src/
│   ├── sharpdicom_codecs.h      # Public C API header
│   ├── sharpdicom_codecs.c      # Main entry points
│   ├── jpeg_wrapper.c           # libjpeg-turbo wrapper
│   ├── j2k_wrapper.c            # OpenJPEG wrapper
│   ├── jls_wrapper.c            # CharLS wrapper
│   ├── video_wrapper.c          # FFmpeg wrapper
│   └── gpu_wrapper.c            # nvJPEG2000/OpenCL
├── vendor/
│   ├── libjpeg-turbo/           # Vendored source
│   ├── openjpeg/
│   ├── charls/
│   ├── ffmpeg/                  # Minimal subset (libavcodec only)
│   └── zlib-ng/
├── cuda/
│   ├── nvjpeg2k_wrapper.cu      # CUDA-specific code
│   └── CMakeLists.txt           # Built separately with nvcc
└── test/
    └── test_codecs.c            # Native test executable

src/SharpDicom.Codecs/
├── NativeCodecs.cs              # Static initialization, feature detection
├── NativeCodecException.cs      # Exception type with NativeErrorCode
├── CodecErrorCategory.cs        # Error category enum
├── CodecError.cs                # Error struct for TryDecode pattern
├── Interop/
│   ├── NativeMethods.cs         # P/Invoke declarations (DllImport)
│   ├── NativeMethods.g.cs       # LibraryImport (NET7+ only, source generated)
│   └── SafeHandles.cs           # VideoDecoderHandle, StreamingDecodeHandle
├── Codecs/
│   ├── NativeJpegCodec.cs       # IPixelDataCodec for JPEG baseline/extended/lossless
│   ├── NativeJpeg2000Codec.cs   # IPixelDataCodec for J2K lossless/lossy
│   ├── NativeJpegLsCodec.cs     # IPixelDataCodec for JPEG-LS
│   ├── NativeRleCodec.cs        # Native RLE (faster than pure C#)
│   └── NativeVideoCodec.cs      # MPEG2/MPEG4/HEVC decode
└── AssemblyInfo.cs              # ModuleInitializer
```

### Pattern 1: Dual P/Invoke Declaration

**What:** Use LibraryImport on .NET 7+ for AOT compatibility, DllImport fallback for older TFMs.
**When to use:** All native function declarations.
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation
internal static partial class NativeMethods
{
    private const string LibName = "sharpdicom_codecs";

#if NET7_0_OR_GREATER
    [LibraryImport(LibName, EntryPoint = "jpeg_decode")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int JpegDecode(
        byte* input, int inputLen,
        byte* output, int outputLen,
        out int width, out int height, out int components,
        int colorspace);
#else
    [DllImport(LibName, EntryPoint = "jpeg_decode", CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int JpegDecode(
        byte* input, int inputLen,
        byte* output, int outputLen,
        out int width, out int height, out int components,
        int colorspace);
#endif
}
```

### Pattern 2: ModuleInitializer with DllImportResolver

**What:** Auto-initialize native library on assembly load with custom resolution logic.
**When to use:** For the NativeCodecs static class initialization.
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/native-interop/native-library-loading
public static class NativeCodecs
{
    private static int _initialized;
    private static Exception? _initException;

    public static bool IsAvailable { get; private set; }

    [ModuleInitializer]
    internal static void AutoInitialize()
    {
        if (AppContext.TryGetSwitch("SharpDicom.Codecs.DisableAutoInit", out var disabled) && disabled)
            return;

        try
        {
            Initialize();
        }
        catch
        {
            // Swallow on auto-init, user can call Initialize() explicitly for exception
        }
    }

    public static void Initialize(NativeCodecOptions? options = null)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            if (_initException != null) throw _initException;
            return;
        }

        try
        {
#if NET5_0_OR_GREATER
            NativeLibrary.SetDllImportResolver(typeof(NativeCodecs).Assembly, ResolveNativeLibrary);
#endif
            // Validate version
            int version = NativeMethods.sharpdicom_version();
            if (version != ExpectedVersion && !(options?.SkipVersionCheck ?? false))
                throw new NativeCodecException($"Version mismatch: expected {ExpectedVersion}, got {version}");

            // Register codecs with priority 100
            RegisterCodecs();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            _initException = new NativeCodecException("Failed to initialize native codecs", ex);
            throw _initException;
        }
    }

#if NET5_0_OR_GREATER
    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "sharpdicom_codecs")
            return IntPtr.Zero;

        // Try custom path first
        if (CustomLibraryPath != null && NativeLibrary.TryLoad(CustomLibraryPath, out var handle))
            return handle;

        // Fall through to default resolution
        return IntPtr.Zero;
    }
#endif
}
```

### Pattern 3: SafeHandle for Native Resources

**What:** Use SafeHandle subclass to wrap native handles with automatic cleanup.
**When to use:** For video decoder handles, streaming decode state.
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle
internal sealed class VideoDecoderHandle : SafeHandle
{
    public VideoDecoderHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.video_decoder_destroy(handle);
            handle = IntPtr.Zero;
        }
        return true;
    }
}
```

### Pattern 4: TryDecode Pattern with Error Struct

**What:** Provide bool-returning TryDecode alongside exception-throwing Decode.
**When to use:** Performance-critical paths where exceptions are expensive.
**Example:**
```csharp
public readonly struct CodecError
{
    public int NativeErrorCode { get; init; }
    public CodecErrorCategory Category { get; init; }
    public string Message { get; init; }
}

public bool TryDecode(ReadOnlySpan<byte> input, Span<byte> output, PixelDataInfo info,
    out DecodeResult result, out CodecError error)
{
    unsafe
    {
        fixed (byte* pIn = input, pOut = output)
        {
            int code = NativeMethods.jpeg_decode(pIn, input.Length, pOut, output.Length, ...);
            if (code == 0)
            {
                result = new DecodeResult(true);
                error = default;
                return true;
            }
            else
            {
                result = default;
                error = MapError(code);
                return false;
            }
        }
    }
}
```

### Anti-Patterns to Avoid

- **Mixing DllImport and LibraryImport on same assembly:** Use `#if` directives, not both active.
- **Returning managed objects from native code:** Native returns error codes; managed side creates exceptions.
- **Not pinning managed buffers:** Always use `fixed` or GCHandle.Alloc for buffers passed to native.
- **Forgetting AOT constraints:** No string marshalling in hot paths; use blittable types only.
- **Ignoring thread safety:** Native library handles are typically not thread-safe; document clearly.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JPEG decoding | Custom decoder | libjpeg-turbo | SIMD optimization, edge cases, DICOM quirks |
| JPEG 2000 decoding | Custom decoder | OpenJPEG | EBCOT complexity, tile handling, HTJ2K |
| Cross-compilation | Shell scripts + toolchains | Zig build system | Handles libc differences, target tuning |
| Native library loading | Manual LoadLibrary/dlopen | NativeLibrary API | Platform-specific paths, search logic built-in |
| Handle cleanup | Manual try/finally | SafeHandle | GC integration, CriticalFinalizerObject safety |
| P/Invoke marshalling | Manual Marshal calls | LibraryImport source gen | AOT-compatible, debuggable generated code |
| Version checking | Manual file reading | Compile-time constant + API | Reliable, no file system dependency |

**Key insight:** The native codec ecosystem is mature; the challenge is correct integration, not reimplementation. Focus effort on the managed-to-native boundary, not duplicating codec algorithms.

## Common Pitfalls

### Pitfall 1: Architecture Mismatch

**What goes wrong:** Loading x64 native library in arm64 process or vice versa causes DllNotFoundException or crashes.
**Why it happens:** Wrong RID selection, missing platform-specific package.
**How to avoid:** Detect ProcessArchitecture at initialization, provide clear error message with expected vs actual.
**Warning signs:** DllNotFoundException with correct file present, "bad image" OS errors.

### Pitfall 2: ModuleInitializer in Libraries Warning (CA2255)

**What goes wrong:** Roslyn analyzer warns that ModuleInitializer should not be used in libraries.
**Why it happens:** Can interfere with app initialization, limits trimming.
**How to avoid:** Suppress with justification (codec registration is the legitimate use case); document AppContext switch for disabling.
**Warning signs:** CA2255 warning during build.

### Pitfall 3: FrozenDictionary Invalidation

**What goes wrong:** Native codec registration after CodecRegistry is frozen fails silently or requires costly re-freeze.
**Why it happens:** CodecRegistry freezes on first lookup for performance.
**How to avoid:** Register in ModuleInitializer before any user code runs; priority system handles ordering.
**Warning signs:** Native codec available but pure C# codec used.

### Pitfall 4: Native Crash Masking

**What goes wrong:** SIGSEGV/AccessViolation in native code terminates process without exception.
**Why it happens:** Native code dereferences bad pointer, no managed exception boundary.
**How to avoid:** Validate all inputs in managed code before P/Invoke; use structured exception handling on Windows.
**Warning signs:** Sudden process termination without exception log.

### Pitfall 5: OpenCL ICD Missing

**What goes wrong:** GPU fallback path crashes or hangs on system without proper OpenCL drivers.
**Why it happens:** OpenCL ICD loader finds no platform, or platform reports GPU but driver broken.
**How to avoid:** Wrap OpenCL initialization in try/catch, timeout probe, set GpuAvailable only on success.
**Warning signs:** Hang on first GPU decode, clGetPlatformIDs returning zero platforms.

### Pitfall 6: TurboJPEG Handle Thread Safety

**What goes wrong:** Concurrent decode calls corrupt state, produce garbled output.
**Why it happens:** libjpeg-turbo tjhandle is not thread-safe.
**How to avoid:** Thread-local handles or explicit locking per handle. Document thread-safety model.
**Warning signs:** Intermittent decode failures, memory corruption under load.

## Code Examples

Verified patterns from official sources:

### libjpeg-turbo Decode (TurboJPEG 3.x API)

```c
// Source: https://github.com/libjpeg-turbo/libjpeg-turbo
// Note: tj3* API is TurboJPEG 3.0+
tjhandle handle = tj3Init(TJINIT_DECOMPRESS);
if (!handle) return SHARPDICOM_ERR_MEMORY;

// Set source
if (tj3SetSourceBuffer(handle, jpegBuf, jpegSize) < 0) {
    set_error(tj3GetErrorStr(handle));
    tj3Destroy(handle);
    return SHARPDICOM_ERR_DECODE_FAILED;
}

// Get header info
if (tj3ReadHeader(handle, NULL) < 0) {
    set_error(tj3GetErrorStr(handle));
    tj3Destroy(handle);
    return SHARPDICOM_ERR_DECODE_FAILED;
}

int width = tj3Get(handle, TJPARAM_JPEGWIDTH);
int height = tj3Get(handle, TJPARAM_JPEGHEIGHT);

// Decompress
if (tj3Decompress8(handle, dstBuf, pitch, pixelFormat) < 0) {
    set_error(tj3GetErrorStr(handle));
    tj3Destroy(handle);
    return SHARPDICOM_ERR_DECODE_FAILED;
}

tj3Destroy(handle);
return SHARPDICOM_OK;
```

### CharLS Decode

```c
// Source: https://github.com/team-charls/charls (charls_jpegls_decoder.h)
charls_jpegls_decoder* decoder = charls_jpegls_decoder_create();
if (!decoder) return SHARPDICOM_ERR_MEMORY;

charls_jpegls_errc err;
err = charls_jpegls_decoder_set_source_buffer(decoder, source, sourceSize);
if (err != CHARLS_JPEGLS_ERRC_SUCCESS) {
    charls_jpegls_decoder_destroy(decoder);
    return MapCharlsError(err);
}

err = charls_jpegls_decoder_read_header(decoder);
if (err != CHARLS_JPEGLS_ERRC_SUCCESS) {
    charls_jpegls_decoder_destroy(decoder);
    return MapCharlsError(err);
}

charls_frame_info frameInfo;
charls_jpegls_decoder_get_frame_info(decoder, &frameInfo);

*width = frameInfo.width;
*height = frameInfo.height;
*components = frameInfo.component_count;
*bitsPerSample = frameInfo.bits_per_sample;

err = charls_jpegls_decoder_decode_to_buffer(decoder, destination, destSize, 0);
charls_jpegls_decoder_destroy(decoder);

return (err == CHARLS_JPEGLS_ERRC_SUCCESS) ? SHARPDICOM_OK : MapCharlsError(err);
```

### OpenJPEG Decode

```c
// Source: https://github.com/uclouvain/openjpeg (wiki/DocJ2KCodec)
opj_codec_t* codec = opj_create_decompress(OPJ_CODEC_J2K);
if (!codec) return SHARPDICOM_ERR_MEMORY;

// Memory stream
opj_stream_t* stream = opj_stream_create(OPJ_J2K_STREAM_CHUNK_SIZE, OPJ_TRUE);
// Set custom read/seek functions for buffer input

opj_image_t* image = NULL;
if (!opj_read_header(stream, codec, &image)) {
    opj_destroy_codec(codec);
    opj_stream_destroy(stream);
    return SHARPDICOM_ERR_DECODE_FAILED;
}

// Optional: ROI decode
if (roi_x >= 0) {
    opj_set_decode_area(codec, image, roi_x, roi_y, roi_x + roi_width, roi_y + roi_height);
}

// Optional: Resolution level (0 = full, 1 = half, etc.)
opj_set_decoded_resolution_factor(codec, resolutionLevel);

if (!opj_decode(codec, stream, image)) {
    opj_image_destroy(image);
    opj_destroy_codec(codec);
    opj_stream_destroy(stream);
    return SHARPDICOM_ERR_DECODE_FAILED;
}

// Copy image data to output buffer
// image->comps[i].data contains component data

opj_image_destroy(image);
opj_destroy_codec(codec);
opj_stream_destroy(stream);
return SHARPDICOM_OK;
```

### NuGet Package Structure

```xml
<!-- SharpDicom.Codecs.nuspec -->
<package>
  <metadata>
    <id>SharpDicom.Codecs</id>
    <version>2.0.0</version>
    <dependencies>
      <group targetFramework=".NETStandard2.0">
        <dependency id="SharpDicom" version="[2.0.0]" />
      </group>
      <group targetFramework="net8.0">
        <dependency id="SharpDicom" version="[2.0.0]" />
      </group>
      <group targetFramework="net9.0">
        <dependency id="SharpDicom" version="[2.0.0]" />
      </group>
    </dependencies>
  </metadata>
  <files>
    <file src="lib/netstandard2.0/SharpDicom.Codecs.dll" target="lib/netstandard2.0/" />
    <file src="lib/net8.0/SharpDicom.Codecs.dll" target="lib/net8.0/" />
    <file src="lib/net9.0/SharpDicom.Codecs.dll" target="lib/net9.0/" />
    <file src="analyzers/**/*" target="analyzers/" />
    <file src="build/SharpDicom.Codecs.targets" target="build/" />
  </files>
</package>

<!-- SharpDicom.Codecs.runtime.linux-x64.nuspec -->
<package>
  <metadata>
    <id>SharpDicom.Codecs.runtime.linux-x64</id>
    <version>2.0.0</version>
  </metadata>
  <files>
    <file src="runtimes/linux-x64/native/libsharpdicom_codecs.so"
          target="runtimes/linux-x64/native/" />
  </files>
</package>
```

```xml
<!-- SharpDicom.Codecs.targets -->
<Project>
  <ItemGroup Condition="'$(RuntimeIdentifier)' == 'win-x64'">
    <PackageReference Include="SharpDicom.Codecs.runtime.win-x64" Version="$(PackageVersion)" PrivateAssets="all" />
  </ItemGroup>
  <!-- Repeat for other RIDs -->

  <!-- Ensure natives copied to output -->
  <Target Name="CopyNativeCodecs" AfterTargets="Build">
    <ItemGroup>
      <NativeCodecFiles Include="$(MSBuildThisFileDirectory)../runtimes/$(RuntimeIdentifier)/native/*" />
    </ItemGroup>
    <Copy SourceFiles="@(NativeCodecFiles)" DestinationFolder="$(OutputPath)" SkipUnchangedFiles="true" />
  </Target>
</Project>
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| DllImport with runtime marshalling | LibraryImport source generation | .NET 7 (2022) | AOT compatible, better performance |
| Marshal.PtrToStructure | ref/Span<T> with blittable types | .NET Core 2.1 (2018) | Zero allocation |
| HandleRef | SafeHandle/CriticalHandle | .NET Framework 2.0 (2005) | GC-aware, finalization-safe |
| NuGet contentFiles | runtimes/RID/native | NuGet 3.x (2015) | Proper RID selection |
| OpenJPEG without HTJ2K | OpenJPEG 2.5+ with HTJ2K | May 2022 | 10-30x decode throughput for HTJ2K |
| tj2* API | tj3* API | libjpeg-turbo 3.0 (2023) | Better precision, cleaner API |

**Deprecated/outdated:**
- `avcodec_decode_video2()` - Use send/receive pattern (`avcodec_send_packet`/`avcodec_receive_frame`)
- HandleRef - Use SafeHandle for all native resource handles
- CharSet.Auto in DllImport - Use explicit StringMarshalling in LibraryImport

## Open Questions

Things that couldn't be fully resolved:

1. **HTJ2K Hardware Decode**
   - What we know: nvJPEG2000 added HTJ2K support in recent releases
   - What's unclear: Whether nvJPEG2000 HTJ2K decode matches software quality
   - Recommendation: Test with reference images, fall back to CPU if quality issues

2. **Windows ARM64 FFmpeg**
   - What we know: Zig can cross-compile to aarch64-windows
   - What's unclear: FFmpeg hardware codec availability on Windows ARM64
   - Recommendation: Initially CPU-only for win-arm64, add hardware later if demand

3. **musl vs glibc for Linux packages**
   - What we know: musl static linking gives zero runtime dependencies
   - What's unclear: Performance impact of musl malloc vs glibc malloc
   - Recommendation: Use musl as decided; benchmark if performance concerns arise

## Sources

### Primary (HIGH confidence)
- [Microsoft Learn: Source generation for P/Invokes](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation) - LibraryImport patterns
- [Microsoft Learn: Native library loading](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/native-library-loading) - NativeLibrary API
- [Microsoft Learn: SafeHandle Class](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle) - Handle management
- [GitHub: libjpeg-turbo](https://github.com/libjpeg-turbo/libjpeg-turbo) - TurboJPEG API
- [GitHub: team-charls/charls](https://github.com/team-charls/charls) - CharLS C API headers
- [Zig Language Reference](https://ziglang.org/documentation/0.15.2/) - Cross-compilation
- [NVIDIA nvJPEG2000 Documentation](https://docs.nvidia.com/cuda/nvjpeg2000/) - GPU decode API
- [FFmpeg libavcodec](https://ffmpeg.org/doxygen/trunk/group__lavc__encdec.html) - Send/receive API

### Secondary (MEDIUM confidence)
- [NuGet: Including native libraries in .NET packages](https://learn.microsoft.com/en-us/nuget/create-packages/native-files-in-net-packages) - Package structure
- [OpenJPEG 2.5 Release Notes](https://www.openjpeg.org/2022/05/13/OpenJPEG-2.5.0-released) - HTJ2K support
- [Roslyn Analyzer NuGet packaging](https://roslyn-analyzers.readthedocs.io/en/latest/create-nuget-package.html) - Analyzer distribution

### Tertiary (LOW confidence)
- Web search results for ModuleInitializer patterns - validate with official docs
- Web search results for OpenCL ICD loader - verify platform-specific behavior in testing

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Well-established libraries with clear documentation
- Architecture: HIGH - Microsoft's official guidance for native interop
- Build system: MEDIUM - Zig cross-compilation is newer but well-documented
- GPU acceleration: MEDIUM - nvJPEG2000 documented, OpenCL fallback needs testing
- Pitfalls: HIGH - Common issues well-documented in Microsoft Learn and community

**Research date:** 2026-01-29
**Valid until:** 60 days (stable domain, libraries update infrequently)

---

*Phase: 13-native-codecs-package*
*Research completed: 2026-01-29*
