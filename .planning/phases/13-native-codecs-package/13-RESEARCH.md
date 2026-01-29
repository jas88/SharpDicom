# Phase 13: Native Codecs Package - Research

**Completed:** 2026-01-28
**Status:** Ready for planning

## Executive Summary

This phase creates SharpDicom.Codecs, an optional high-performance codec package wrapping native libraries (libjpeg-turbo, OpenJPEG, CharLS, FFmpeg) with GPU acceleration support (nvJPEG2000, OpenCL). The package uses Zig for cross-platform builds and follows NuGet native package conventions.

---

## 1. Zig Cross-Compilation

### Why Zig

Zig provides a single toolchain that cross-compiles C code for all target platforms from any host platform. Unlike traditional cross-compilation (requiring separate toolchains per target), Zig bundles libc implementations and handles platform differences automatically.

### Cross-Compilation Targets

| RID | Zig Target | Notes |
|-----|------------|-------|
| win-x64 | x86_64-windows-gnu | Uses mingw-w64 libc |
| win-arm64 | aarch64-windows-gnu | Uses mingw-w64 libc |
| linux-x64 | x86_64-linux-musl | Static musl = zero runtime deps |
| linux-arm64 | aarch64-linux-musl | Static musl = zero runtime deps |
| osx-x64 | x86_64-macos | Apple Clang compatible |
| osx-arm64 | aarch64-macos | Apple Silicon native |

### Zig as C Compiler

```bash
# Build C library with Zig
zig cc -target x86_64-linux-musl -O3 -c wrapper.c -o wrapper.o

# Link static library
zig ar rcs libsharpdicom_codecs.a wrapper.o libjpeg-turbo.a openjpeg.a charls.a

# Build shared library
zig cc -target x86_64-linux-musl -shared -O3 wrapper.o -o libsharpdicom_codecs.so \
    libjpeg-turbo.a openjpeg.a charls.a -lz
```

### Static Linking Strategy

All dependencies are statically linked into a single shared library per platform:
- libjpeg-turbo (JPEG baseline, lossless, 8/12/16-bit)
- OpenJPEG (JPEG 2000, HTJ2K)
- CharLS (JPEG-LS)
- FFmpeg (MPEG2/4, HEVC, audio)
- zlib-ng (deflate)

Result: Single `sharpdicom_codecs.dll/so/dylib` with no external runtime dependencies.

### SIMD Considerations

| Platform | SIMD Features |
|----------|---------------|
| x86_64 | SSE2 baseline, AVX2 optional (runtime dispatch) |
| aarch64 | NEON baseline (always available on 64-bit ARM) |

Zig handles SIMD feature detection and compiles with appropriate flags. Runtime dispatch is handled within the wrapper code.

---

## 2. Native Library APIs

### 2.1 libjpeg-turbo

**Coverage:** JPEG Baseline (8-bit), Extended (12-bit), and with patches 16-bit support

**Key API Functions:**
```c
// Decompression
tjhandle tjInitDecompress(void);
int tjDecompressHeader3(tjhandle handle, const unsigned char *jpegBuf,
    unsigned long jpegSize, int *width, int *height, int *jpegSubsamp, int *jpegColorspace);
int tjDecompress2(tjhandle handle, const unsigned char *jpegBuf, unsigned long jpegSize,
    unsigned char *dstBuf, int width, int pitch, int height, int pixelFormat, int flags);
void tjDestroy(tjhandle handle);

// Compression
tjhandle tjInitCompress(void);
int tjCompress2(tjhandle handle, const unsigned char *srcBuf, int width, int pitch,
    int height, int pixelFormat, unsigned char **jpegBuf, unsigned long *jpegSize,
    int jpegSubsamp, int jpegQual, int flags);
void tjFree(unsigned char *buffer);
```

**Error Handling:** `tjGetErrorStr2(handle)` returns error message. Functions return -1 on error.

**Thread Safety:** Each thread needs its own `tjhandle`. Handles are not thread-safe.

**DICOM Quirks:** libjpeg-turbo handles embedded SOI markers and common JPEG non-conformance.

### 2.2 OpenJPEG

**Coverage:** JPEG 2000 Lossless/Lossy, HTJ2K (High-Throughput)

**Key API Functions:**
```c
// Codec creation
opj_codec_t* opj_create_decompress(OPJ_CODEC_FORMAT format);
opj_codec_t* opj_create_compress(OPJ_CODEC_FORMAT format);

// Stream handling
opj_stream_t* opj_stream_create(OPJ_SIZE_T buffer_size, OPJ_BOOL is_input);
void opj_stream_set_read_function(opj_stream_t*, opj_stream_read_fn);
void opj_stream_set_write_function(opj_stream_t*, opj_stream_write_fn);

// Decode
OPJ_BOOL opj_read_header(opj_stream_t*, opj_codec_t*, opj_image_t**);
OPJ_BOOL opj_decode(opj_codec_t*, opj_stream_t*, opj_image_t*);
OPJ_BOOL opj_set_decode_area(opj_codec_t*, opj_image_t*, int, int, int, int);  // ROI decode

// Encode
OPJ_BOOL opj_encode(opj_codec_t*, opj_stream_t*);
```

**Memory Management:** `opj_image_destroy()`, `opj_destroy_codec()`, `opj_stream_destroy()`

**HTJ2K:** Enabled via `OPJ_CODEC_FORMAT` = `OPJ_CODEC_HTJ2K` (OpenJPEG 2.5+)

**Thread Safety:** Codec instances are not thread-safe. Create per-thread instances.

### 2.3 CharLS

**Coverage:** JPEG-LS lossless/near-lossless (ISO 14495-1)

**Key API Functions:**
```c
// Modern C API (CharLS 2.x)
charls_jpegls_decoder* charls_jpegls_decoder_create(void);
charls_jpegls_error charls_jpegls_decoder_set_source_buffer(
    charls_jpegls_decoder*, const void* source, size_t source_size);
charls_jpegls_error charls_jpegls_decoder_read_header(charls_jpegls_decoder*);
charls_jpegls_error charls_jpegls_decoder_decode_to_buffer(
    charls_jpegls_decoder*, void* destination, size_t destination_size, uint32_t stride);
void charls_jpegls_decoder_destroy(charls_jpegls_decoder*);

// Encoder
charls_jpegls_encoder* charls_jpegls_encoder_create(void);
charls_jpegls_error charls_jpegls_encoder_set_frame_info(charls_jpegls_encoder*, const charls_frame_info*);
charls_jpegls_error charls_jpegls_encoder_encode_from_buffer(
    charls_jpegls_encoder*, const void* source, size_t source_size, uint32_t stride);
```

**Error Handling:** Returns `charls_jpegls_error` enum. Use `charls_get_error_message()` for strings.

**Performance:** ~2x faster than original HP reference implementation.

### 2.4 FFmpeg (libavcodec)

**Coverage:** MPEG2, MPEG4/H.264, HEVC/H.265, audio codecs

**Key API Sequence:**
```c
// Initialize
avcodec_register_all();  // Pre-FFmpeg 4.0 only
const AVCodec *codec = avcodec_find_decoder(AV_CODEC_ID_H265);
AVCodecContext *ctx = avcodec_alloc_context3(codec);
avcodec_open2(ctx, codec, NULL);

// Decode frame
AVPacket *pkt = av_packet_alloc();
av_packet_from_data(pkt, data, size);
avcodec_send_packet(ctx, pkt);

AVFrame *frame = av_frame_alloc();
avcodec_receive_frame(ctx, frame);

// Access pixel data
frame->data[0];  // Y plane
frame->data[1];  // U plane
frame->data[2];  // V plane
frame->linesize[0];  // Y stride

// Cleanup
av_frame_free(&frame);
av_packet_free(&pkt);
avcodec_free_context(&ctx);
```

**Hardware Acceleration:** NVDEC, QuickSync (VA-API on Linux), VideoToolbox (macOS)

**Thread Safety:** AVCodecContext is not thread-safe. One context per thread.

---

## 3. .NET Native Library Loading

### NativeLibrary API (.NET Core 3.1+)

```csharp
// Manual loading
IntPtr handle = NativeLibrary.Load("sharpdicom_codecs", assembly, searchPath);
IntPtr symbol = NativeLibrary.GetExport(handle, "jpeg_decode");

// Custom resolver
NativeLibrary.SetDllImportResolver(assembly, (name, asm, path) => {
    if (name == "sharpdicom_codecs") {
        // Custom loading logic (e.g., architecture-specific)
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return NativeLibrary.Load("sharpdicom_codecs_neon", asm, path);
    }
    return IntPtr.Zero;  // Fall through to default
});
```

### LibraryImport vs DllImport

| Feature | DllImport | LibraryImport |
|---------|-----------|---------------|
| Availability | .NET Framework+ | .NET 7+ |
| Source generator | No | Yes |
| AOT compatible | Limited | Full |
| Marshalling | Runtime | Compile-time |
| Blittable types | Required for AOT | Required |

**Strategy:** Use `#if NET7_0_OR_GREATER` for LibraryImport, DllImport fallback for older TFMs.

```csharp
#if NET7_0_OR_GREATER
[LibraryImport("sharpdicom_codecs")]
internal static partial int jpeg_decode(
    [In] byte* input, int inputLen,
    byte* output, int outputLen,
    out int width, out int height);
#else
[DllImport("sharpdicom_codecs", CallingConvention = CallingConvention.Cdecl)]
internal static extern int jpeg_decode(
    byte* input, int inputLen,
    byte* output, int outputLen,
    out int width, out int height);
#endif
```

### Library Discovery Path

Runtime searches for native libraries in this order:
1. App directory
2. `runtimes/{RID}/native/` (NuGet convention)
3. System library paths (`LD_LIBRARY_PATH`, `PATH`, etc.)

### AOT Considerations

- Use blittable types only (no string marshalling in hot paths)
- Avoid `Marshal` class where possible
- Use `fixed` statements for pinned arrays
- Prefer `Span<byte>` with `MemoryMarshal.GetReference`

---

## 4. NuGet Native Package Patterns

### Package Structure

```
SharpDicom.Codecs/                          # Meta package (managed code)
├── lib/
│   ├── netstandard2.0/
│   │   └── SharpDicom.Codecs.dll
│   ├── net8.0/
│   │   └── SharpDicom.Codecs.dll
│   └── net9.0/
│       └── SharpDicom.Codecs.dll
├── build/
│   └── SharpDicom.Codecs.targets           # Ensures native copy
├── README.md
└── icon.png

SharpDicom.Codecs.runtime.win-x64/          # Runtime package (native only)
├── runtimes/
│   └── win-x64/
│       └── native/
│           └── sharpdicom_codecs.dll
└── (no lib/ folder)
```

### Package Dependencies

```xml
<!-- SharpDicom.Codecs.nuspec -->
<dependencies>
  <group targetFramework=".NETStandard2.0">
    <dependency id="SharpDicom" version="[2.0.0]" />
  </group>
  <group targetFramework="net8.0">
    <dependency id="SharpDicom" version="[2.0.0]" />
  </group>
</dependencies>

<!-- Runtime package selection via .targets -->
<ItemGroup Condition="'$(RuntimeIdentifier)' == 'win-x64'">
  <PackageReference Include="SharpDicom.Codecs.runtime.win-x64" Version="$(PackageVersion)" />
</ItemGroup>
```

### Build Targets File

```xml
<!-- SharpDicom.Codecs.targets -->
<Project>
  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)../runtimes/$(RuntimeIdentifier)/native/*"
          CopyToOutputDirectory="PreserveNewest"
          Visible="false" />
  </ItemGroup>
</Project>
```

### Self-Contained and Single-File Support

Native binaries in `runtimes/RID/native/` are automatically included in:
- `dotnet publish --self-contained`
- `dotnet publish -p:PublishSingleFile=true`

---

## 5. GPU Acceleration

### 5.1 NVIDIA nvJPEG2000

**API Overview:**
```c
// Library initialization
nvjpeg2kHandle_t handle;
nvjpeg2kCreate(NVJPEG2K_BACKEND_DEFAULT, NULL, &handle);

// Decoder state
nvjpeg2kDecodeState_t state;
nvjpeg2kDecodeStateCreate(handle, &state);

// Stream parsing
nvjpeg2kStream_t stream;
nvjpeg2kStreamCreate(&stream);
nvjpeg2kStreamParse(handle, data, dataLen, 0, 0, stream);

// Decode
nvjpeg2kImage_t output;
nvjpeg2kDecode(handle, state, stream, &output, cudaStream);

// Cleanup
nvjpeg2kStreamDestroy(stream);
nvjpeg2kDecodeStateDestroy(state);
nvjpeg2kDestroy(handle);
```

**Requirements:**
- CUDA Toolkit 11.0+
- Compute Capability 5.0+ (Maxwell, GTX 750 Ti and newer)
- NVIDIA driver 450+

**Features:**
- Lossless and lossy JPEG 2000 decode
- Batch decode API for multi-frame
- GPU memory output (requires copy to host for managed use)
- Single-threaded per stream, multiple streams possible

### 5.2 OpenCL Fallback

**Strategy:** Use OpenCL for GPU acceleration on non-NVIDIA platforms (AMD, Intel).

**Key APIs:**
```c
// Platform/device discovery
clGetPlatformIDs(1, &platform, NULL);
clGetDeviceIDs(platform, CL_DEVICE_TYPE_GPU, 1, &device, NULL);

// Context and queue
cl_context ctx = clCreateContext(NULL, 1, &device, NULL, NULL, NULL);
cl_command_queue queue = clCreateCommandQueueWithProperties(ctx, device, NULL, NULL);

// Memory and kernel
cl_mem input = clCreateBuffer(ctx, CL_MEM_READ_ONLY | CL_MEM_COPY_HOST_PTR, size, data, NULL);
cl_mem output = clCreateBuffer(ctx, CL_MEM_WRITE_ONLY, outSize, NULL, NULL);
clEnqueueNDRangeKernel(queue, kernel, 1, NULL, &globalSize, &localSize, 0, NULL, NULL);
clEnqueueReadBuffer(queue, output, CL_TRUE, 0, outSize, result, 0, NULL, NULL);
```

**OpenCL ICD Loader:** Bundle `OpenCL.dll/libOpenCL.so` (Khronos ICD loader) with package. Runtime loads vendor-specific ICDs.

### 5.3 GPU Fallback Chain

```
nvJPEG2000 (NVIDIA GPU)
    |
    v [if unavailable or error]
OpenCL (any GPU)
    |
    v [if unavailable or error]
CPU (libjpeg-turbo/OpenJPEG)
```

Implementation:
```csharp
public static DecodeResult Decode(ReadOnlySpan<byte> input, Span<byte> output, PixelDataInfo info)
{
    if (NativeCodecs.GpuAvailable && !NativeCodecs.PreferCpu)
    {
        if (NativeCodecs.NvJpeg2kAvailable)
            return DecodeNvJpeg2k(input, output, info);

        if (NativeCodecs.OpenClAvailable)
            return DecodeOpenCl(input, output, info);
    }

    return DecodeCpu(input, output, info);
}
```

---

## 6. C Wrapper Design

### API Contract

Single combined library with cdecl calling convention:

```c
// sharpdicom_codecs.h

// Version and capability query
EXPORT int sharpdicom_version(void);
EXPORT int sharpdicom_features(void);  // Bitmap: JPEG|J2K|JLS|MPEG|GPU

// SIMD feature detection
EXPORT int sharpdicom_simd_features(void);  // Bitmap: SSE2|AVX2|NEON

// JPEG (libjpeg-turbo)
EXPORT int jpeg_decode(
    const uint8_t* input, int inputLen,
    uint8_t* output, int outputLen,
    int* width, int* height, int* components,
    int colorspace);  // 0=RGB, 1=YBR, 2=GRAY

EXPORT int jpeg_encode(
    const uint8_t* input, int width, int height, int components,
    uint8_t** output, int* outputLen,
    int quality, int subsamp);  // 0=444, 1=422, 2=420

EXPORT void jpeg_free(uint8_t* buffer);

// JPEG 2000 (OpenJPEG)
EXPORT int j2k_decode(
    const uint8_t* input, int inputLen,
    uint8_t* output, int outputLen,
    int* width, int* height, int* components, int* bitsPerSample,
    int resolutionLevel);  // 0=full, 1=half, 2=quarter...

EXPORT int j2k_encode(
    const uint8_t* input, int width, int height, int components, int bitsPerSample,
    uint8_t** output, int* outputLen,
    int lossless, float compressionRatio, int tileSize);

EXPORT void j2k_free(uint8_t* buffer);

// JPEG-LS (CharLS)
EXPORT int jls_decode(
    const uint8_t* input, int inputLen,
    uint8_t* output, int outputLen,
    int* width, int* height, int* components, int* bitsPerSample);

EXPORT int jls_encode(
    const uint8_t* input, int width, int height, int components, int bitsPerSample,
    uint8_t** output, int* outputLen,
    int nearLossless);  // 0=lossless, >0=near-lossless threshold

EXPORT void jls_free(uint8_t* buffer);

// Video (FFmpeg) - handle-based for multi-frame
EXPORT void* video_decoder_create(int codecId);  // AV_CODEC_ID_*
EXPORT int video_decode_frame(void* decoder, const uint8_t* input, int inputLen,
    uint8_t* output, int outputLen, int* width, int* height);
EXPORT void video_decoder_destroy(void* decoder);

// GPU decode (nvJPEG2000 / OpenCL)
EXPORT int gpu_available(void);
EXPORT int gpu_j2k_decode(
    const uint8_t* input, int inputLen,
    uint8_t* output, int outputLen,
    int* width, int* height, int* components, int* bitsPerSample);

// Error handling
EXPORT const char* sharpdicom_last_error(void);
```

### Error Codes

```c
#define SHARPDICOM_OK                    0
#define SHARPDICOM_ERR_INVALID_INPUT    -1
#define SHARPDICOM_ERR_BUFFER_TOO_SMALL -2
#define SHARPDICOM_ERR_DECODE_FAILED    -3
#define SHARPDICOM_ERR_ENCODE_FAILED    -4
#define SHARPDICOM_ERR_UNSUPPORTED      -5
#define SHARPDICOM_ERR_MEMORY           -6
#define SHARPDICOM_ERR_TIMEOUT          -7
#define SHARPDICOM_ERR_GPU_UNAVAILABLE  -8
```

### Thread-Local Error Messages

```c
static _Thread_local char g_error_message[256];

EXPORT const char* sharpdicom_last_error(void) {
    return g_error_message;
}

static void set_error(const char* msg) {
    strncpy(g_error_message, msg, sizeof(g_error_message) - 1);
    g_error_message[sizeof(g_error_message) - 1] = '\0';
}
```

---

## 7. Build System

### Directory Structure

```
native/
├── build.zig                    # Zig build script
├── vendor/
│   ├── libjpeg-turbo/          # Vendored source
│   ├── openjpeg/
│   ├── charls/
│   ├── ffmpeg/                 # Minimal subset
│   └── zlib-ng/
├── src/
│   ├── sharpdicom_codecs.h
│   ├── sharpdicom_codecs.c     # Main wrapper
│   ├── jpeg_wrapper.c
│   ├── j2k_wrapper.c
│   ├── jls_wrapper.c
│   ├── video_wrapper.c
│   └── gpu_wrapper.c           # nvJPEG2000/OpenCL
├── cuda/
│   ├── nvjpeg2k_wrapper.cu     # CUDA-specific (built with nvcc)
│   └── CMakeLists.txt
└── test/
    └── test_codecs.c           # Native test executable
```

### Zig Build Script

```zig
// build.zig
const std = @import("std");

pub fn build(b: *std.Build) void {
    const optimize = b.standardOptimizeOption(.{});

    // Target matrix
    const targets = [_]std.Target.Query{
        .{ .cpu_arch = .x86_64, .os_tag = .windows, .abi = .gnu },
        .{ .cpu_arch = .aarch64, .os_tag = .windows, .abi = .gnu },
        .{ .cpu_arch = .x86_64, .os_tag = .linux, .abi = .musl },
        .{ .cpu_arch = .aarch64, .os_tag = .linux, .abi = .musl },
        .{ .cpu_arch = .x86_64, .os_tag = .macos },
        .{ .cpu_arch = .aarch64, .os_tag = .macos },
    };

    for (targets) |t| {
        const lib = b.addSharedLibrary(.{
            .name = "sharpdicom_codecs",
            .target = b.resolveTargetQuery(t),
            .optimize = optimize,
        });

        lib.addCSourceFile(.{ .file = "src/sharpdicom_codecs.c" });
        lib.addCSourceFile(.{ .file = "src/jpeg_wrapper.c" });
        lib.addCSourceFile(.{ .file = "src/j2k_wrapper.c" });
        lib.addCSourceFile(.{ .file = "src/jls_wrapper.c" });
        lib.addCSourceFile(.{ .file = "src/video_wrapper.c" });

        // Vendor libraries
        lib.linkLibrary(libjpeg_turbo);
        lib.linkLibrary(openjpeg);
        lib.linkLibrary(charls);
        lib.linkLibrary(zlib_ng);

        lib.installLibraryHeaders();
        b.installArtifact(lib);
    }
}
```

### GitHub Actions CI

```yaml
name: Native Build

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install Zig
        uses: goto-bus-stop/setup-zig@v2
        with:
          version: 0.13.0

      - name: Build all targets
        run: |
          cd native
          zig build -Doptimize=ReleaseFast

      - name: Upload artifacts
        uses: actions/upload-artifact@v4
        with:
          name: native-libs
          path: native/zig-out/lib/

  build-cuda:
    runs-on: ubuntu-latest
    container:
      image: nvidia/cuda:12.4-devel-ubuntu22.04
    steps:
      - uses: actions/checkout@v4

      - name: Build CUDA wrapper
        run: |
          cd native/cuda
          nvcc -shared -O3 -o nvjpeg2k_wrapper.so nvjpeg2k_wrapper.cu -lnvjpeg2k
```

---

## 8. Managed P/Invoke Layer

### Architecture

```
SharpDicom.Codecs/
├── NativeCodecs.cs              # Static initialization, feature detection
├── NativeCodecException.cs      # Exception type
├── Interop/
│   ├── NativeMethods.cs         # P/Invoke declarations
│   ├── NativeMethods.g.cs       # Generated LibraryImport (NET7+)
│   └── SafeHandles.cs           # Video decoder handle wrapper
├── Codecs/
│   ├── NativeJpegCodec.cs       # IPixelDataCodec for JPEG
│   ├── NativeJpeg2000Codec.cs   # IPixelDataCodec for J2K
│   ├── NativeJpegLsCodec.cs     # IPixelDataCodec for JPEG-LS
│   └── NativeVideoCodec.cs      # Video decode (MPEG2/4, HEVC)
└── AssemblyInfo.cs              # ModuleInitializer
```

### NativeCodecs Static Class

```csharp
public static class NativeCodecs
{
    private static int _initialized;
    private static Exception? _initException;

    public static bool IsAvailable { get; private set; }
    public static bool GpuAvailable { get; private set; }
    public static string? LibjpegTurboVersion { get; private set; }
    public static string? OpenJpegVersion { get; private set; }
    public static SimdFeatures ActiveSimdFeatures { get; private set; }

    public static bool EnableJpeg { get; set; } = true;
    public static bool EnableJpeg2000 { get; set; } = true;
    public static bool EnableJpegLs { get; set; } = true;
    public static bool PreferCpu { get; set; } = false;

    [ModuleInitializer]
    internal static void AutoInitialize()
    {
        if (!AppContext.TryGetSwitch("SharpDicom.Codecs.DisableAutoInit", out var disabled) || !disabled)
        {
            Initialize();
        }
    }

    public static void Initialize(NativeCodecOptions? options = null)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            if (_initException != null)
                throw _initException;
            return;
        }

        try
        {
            // Load native library
            var handle = NativeLibrary.Load("sharpdicom_codecs", typeof(NativeCodecs).Assembly, null);

            // Version check
            int version = NativeMethods.sharpdicom_version();
            if (version != ExpectedVersion && !(options?.SkipVersionCheck ?? false))
                throw new NativeCodecException($"Version mismatch: expected {ExpectedVersion}, got {version}");

            // Feature detection
            int features = NativeMethods.sharpdicom_features();
            int simd = NativeMethods.sharpdicom_simd_features();
            ActiveSimdFeatures = (SimdFeatures)simd;

            GpuAvailable = NativeMethods.gpu_available() != 0;
            IsAvailable = true;

            // Register codecs with priority 100 (above pure C# at 50)
            if (EnableJpeg)
                CodecRegistry.Register(new NativeJpegCodec(), priority: 100);
            if (EnableJpeg2000)
                CodecRegistry.Register(new NativeJpeg2000Codec(), priority: 100);
            if (EnableJpegLs)
                CodecRegistry.Register(new NativeJpegLsCodec(), priority: 100);
        }
        catch (Exception ex)
        {
            _initException = new NativeCodecException("Failed to initialize native codecs", ex);
            throw _initException;
        }
    }
}
```

### P/Invoke Declarations

```csharp
internal static partial class NativeMethods
{
    private const string LibName = "sharpdicom_codecs";

#if NET7_0_OR_GREATER
    [LibraryImport(LibName)]
    internal static partial int sharpdicom_version();

    [LibraryImport(LibName)]
    internal static partial int jpeg_decode(
        byte* input, int inputLen,
        byte* output, int outputLen,
        out int width, out int height, out int components,
        int colorspace);
#else
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int sharpdicom_version();

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern unsafe int jpeg_decode(
        byte* input, int inputLen,
        byte* output, int outputLen,
        out int width, out int height, out int components,
        int colorspace);
#endif
}
```

---

## 9. CodecRegistry Integration

### Priority System

```csharp
// Modify CodecRegistry to support priority
public static void Register(IPixelDataCodec codec, int priority = 50)
{
    lock (_lock)
    {
        var key = codec.TransferSyntax;
        if (_priorities.TryGetValue(key, out var existing) && existing >= priority)
            return;  // Higher priority already registered

        _mutableRegistry[key] = codec;
        _priorities[key] = priority;

        if (_isFrozen)
        {
            _frozenRegistry = null;
            _isFrozen = false;
        }
    }
}
```

### Priority Levels

| Level | Use Case |
|-------|----------|
| 0 | Fallback (placeholder) |
| 50 | Pure C# (Phase 12) |
| 100 | Native (Phase 13) |
| 200 | User override |

### Eager Registration

Native codecs register in `ModuleInitializer`, so referencing SharpDicom.Codecs automatically overrides pure C# implementations.

---

## Key Decisions Summary

| Topic | Decision | Rationale |
|-------|----------|-----------|
| Build tool | Zig | Single toolchain for all platforms |
| Linking | Static | Zero runtime dependencies |
| Library count | 1 per platform | Simpler distribution |
| P/Invoke style | LibraryImport + DllImport | AOT on .NET 7+, compat on older |
| GPU fallback | nvJPEG2000 -> OpenCL -> CPU | NVIDIA priority, universal fallback |
| Priority system | Numeric (0-200) | Clear override semantics |
| Error handling | Error codes + thread-local message | No exceptions across native boundary |

---

*Phase: 13-native-codecs-package*
*Research completed: 2026-01-28*
