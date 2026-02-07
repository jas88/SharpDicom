# Phase 27: Extended Codec Support - Research

**Researched:** 2026-02-06
**Domain:** 12-bit JPEG codecs (managed + native), video encoding (MPEG2/H.264/HEVC), stb_image integration
**Confidence:** MEDIUM (native build complexity is the primary risk; managed codec patterns well-understood)

## Summary

Phase 27 extends the SharpDicom codec infrastructure with two major capabilities: 12-bit/16-bit JPEG support (both managed pure-C# and native via libjpeg-turbo), and video encoding for MPEG2, H.264, and HEVC (adding encoding to the existing decode-only FFmpeg wrappers from Phase 13). Additionally, stb_image is integrated into the native library for image file sequence input to the video encoder.

The 12-bit JPEG work involves building libjpeg-turbo twice from source via Zig (once for 8-bit with full TurboJPEG/SIMD, once for 12-bit with the slower libjpeg API), then merging them into a single native library with symbol prefixes (`jpeg8_*`/`jpeg12_*`). The managed side adds a new `JpegExtendedCodec` class for 12-bit lossy DCT and extends the existing `JpegLosslessCodec` to handle up to 16-bit precision. The video encoding work mirrors the existing decode-only `video_wrapper.c` pattern, adding `video_encoder_create`/`video_encode_frame`/`video_encoder_destroy` functions, with a managed `VideoEncoder` class providing both streaming and batch APIs.

**Primary recommendation:** Build 12-bit JPEG support first (smaller scope, verifiable independently), then video encoding. The symbol-prefix approach for the fat native library is the right call -- it avoids the fragility of loading two separate native libraries.

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| libjpeg-turbo | 3.1.x (latest stable) | 8-bit JPEG (TurboJPEG API) + 12-bit JPEG (libjpeg API) | Only JPEG library with 12-bit support; already used in Phase 13 |
| FFmpeg (libavcodec/libavutil/libswscale/libavformat/libswresample) | 7.x (latest stable) | MPEG2/H.264/HEVC encoding + audio muxing | Only viable cross-platform video codec library; already used for decoding |
| x264 | latest stable | H.264 encoding backend for FFmpeg | De facto standard H.264 encoder; GPL-compatible |
| x265 | latest stable | HEVC encoding backend for FFmpeg | De facto standard HEVC encoder; GPL-compatible |
| stb_image | 2.30 (latest) | Image loading (PNG/BMP/TGA/JPEG/PSD/GIF/HDR/PIC) | Single-header, zero-dependency, battle-tested in game dev |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Zig | 0.14.x | Cross-compilation toolchain for native library | Building all 6 platform targets (already established in Phase 13) |
| objcopy/llvm-objcopy | system | Symbol renaming for 8-bit/12-bit library merge | Post-build step to prefix symbols before linking into fat library |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| libjpeg-turbo 12-bit | Pure C# 12-bit DCT | Context: managed 12-bit codec IS being built too; native just adds performance |
| FFmpeg encoding | GStreamer | FFmpeg already integrated for decode; no reason to introduce second framework |
| stb_image | libpng+libbmp separately | stb_image is single header, zero-dep, covers all needed formats in ~50KB |
| x264/x265 | FFmpeg native encoders | x264/x265 produce significantly better quality at same bitrate |

## Architecture Patterns

### Recommended Project Structure

```
src/SharpDicom/Codecs/
├── Jpeg/
│   ├── JpegBaselineCodec.cs       # Existing 8-bit (Phase 12)
│   ├── JpegExtendedCodec.cs       # NEW: 12-bit lossy DCT (Process 2,4)
│   └── JpegCodecOptions.cs        # Existing
├── JpegLossless/
│   ├── JpegLosslessCodec.cs       # EXTEND: support 12-16 bit precision
│   └── JpegLosslessDecoder.cs     # EXTEND: wider sample handling
├── Video/
│   ├── VideoEncoder.cs            # NEW: High-level encoding API
│   ├── VideoEncoderOptions.cs     # NEW: Quality presets, codec selection
│   ├── VideoDicomBuilder.cs       # NEW: Builder for video DICOM files
│   ├── VideoQualityPreset.cs      # NEW: Diagnostic/Review/Archive
│   └── FrameSource.cs            # NEW: IAsyncEnumerable<Frame> abstraction

src/SharpDicom.Codecs/
├── Codecs/
│   ├── NativeJpeg8Codec.cs        # NEW: Explicit 8-bit native codec
│   ├── NativeJpeg12Codec.cs       # NEW: Explicit 12-bit native codec
│   ├── NativeVideoEncoder.cs      # NEW: Native video encoding wrapper
│   └── NativeJpegCodec.cs         # Existing (becomes NativeJpeg8Codec)
├── Interop/
│   ├── NativeMethods.cs           # EXTEND: 12-bit JPEG + video encoder P/Invoke
│   └── StbImageMethods.cs         # NEW: stb_image P/Invoke

native/src/
├── jpeg_wrapper.c                 # MAJOR CHANGE: dual 8-bit/12-bit with prefixed symbols
├── jpeg8_wrapper.c                # NEW: 8-bit specific (calls jpeg8_* prefixed functions)
├── jpeg12_wrapper.c               # NEW: 12-bit specific (calls jpeg12_* prefixed functions)
├── video_wrapper.c                # EXTEND: add encoder functions alongside existing decoder
├── video_encoder.c                # NEW: video_encoder_create/encode_frame/destroy
├── stb_image_wrapper.c            # NEW: thin wrapper around stb_image.h
├── build.zig                      # EXTEND: dual libjpeg-turbo builds, stb_image, full FFmpeg
```

### Pattern 1: Symbol-Prefixed Fat Native Library

**What:** Build libjpeg-turbo twice (8-bit, 12-bit), rename all exported symbols with `jpeg8_`/`jpeg12_` prefixes, link both into single `sharpdicom_codecs` library.

**When to use:** Whenever two builds of the same library must coexist in one process.

**Implementation approach:**

1. Build libjpeg-turbo 8-bit normally via Zig (gets TurboJPEG API + SIMD)
2. Build libjpeg-turbo 12-bit with `-DWITH_12BIT=1` (no TurboJPEG, no SIMD -- this is a libjpeg limitation)
3. Use `objcopy --prefix-symbols=jpeg8_` on 8-bit .o files
4. Use `objcopy --prefix-symbols=jpeg12_` on 12-bit .o files
5. Write thin C wrappers: `jpeg8_wrapper.c` calls `jpeg8_tjCompress2(...)`, `jpeg12_wrapper.c` calls `jpeg12_jpeg_write_scanlines(...)`
6. Link all into single shared library

**Critical detail:** The 12-bit build disables TurboJPEG API entirely. The 12-bit wrapper must use the raw libjpeg API (`jpeg_create_compress`/`jpeg_write_scanlines`) instead of TurboJPEG's `tjCompress2`. The prefixed versions become `jpeg12_jpeg_create_compress`, etc.

**Alternative approach (simpler):** Instead of objcopy, compile libjpeg-turbo 12-bit source files with `#define jpeg_create_compress jpeg12_jpeg_create_compress` etc. via `-D` flags. This is more portable (works on macOS where objcopy may not be available natively) and avoids post-build object manipulation. The Zig build system can pass these defines as compiler flags.

### Pattern 2: Video Encoder Handle API (mirrors existing decoder)

**What:** Handle-based C API for video encoding, matching the existing `video_decoder_t` pattern.

**When to use:** For all video encoding operations.

**Example API (C side):**
```c
typedef struct video_encoder video_encoder_t;

typedef struct {
    int codec_id;           // VIDEO_CODEC_MPEG2/H264/HEVC
    int width;
    int height;
    double frame_rate;
    int bit_depth;          // 8 or 10
    int gop_size;           // keyframe interval
    int quality_preset;     // VIDEO_QUALITY_DIAGNOSTIC/REVIEW/ARCHIVE
    int crf;                // constant rate factor (-1 for preset default)
    int bitrate;            // target bitrate in bps (0 for CRF mode)
    int hw_accel;           // 0=auto, 1=force CPU, 2=prefer GPU
    int audio_codec;        // AUDIO_CODEC_NONE/AAC/PCM
    int audio_sample_rate;
    int audio_channels;
} video_encoder_config_t;

SHARPDICOM_API int video_encoder_create(
    const video_encoder_config_t* config,
    video_encoder_t** encoder_out);

SHARPDICOM_API int video_encode_frame(
    video_encoder_t* encoder,
    const uint8_t* pixels,
    size_t pixel_len,
    int pixel_format,           // VIDEO_FORMAT_RGB24/GRAY8/YUV420P
    uint8_t** output,
    size_t* output_len,
    int* packet_available);

SHARPDICOM_API int video_encode_audio(
    video_encoder_t* encoder,
    const uint8_t* samples,
    size_t samples_len,
    int sample_format);         // AUDIO_FMT_PCM16/FLOAT

SHARPDICOM_API int video_encoder_flush(
    video_encoder_t* encoder,
    uint8_t** output,
    size_t* output_len,
    int* packet_available);

SHARPDICOM_API void video_encoder_destroy(video_encoder_t* encoder);
SHARPDICOM_API void video_encoder_free(uint8_t* buffer);
```

### Pattern 3: Managed VideoEncoder with IAsyncEnumerable

**What:** High-level C# API that wraps the native encoder with streaming frame input.

**Example:**
```csharp
public sealed class VideoEncoder : IAsyncDisposable
{
    public static async Task<DicomFile> EncodeFromFramesAsync(
        IAsyncEnumerable<VideoFrame> frames,
        VideoEncoderOptions options,
        DicomDataset? templateDataset = null,
        IProgress<VideoEncodeProgress>? progress = null,
        CancellationToken ct = default);

    public static DicomFile EncodeFromDicom(
        DicomFile multiFrameDicom,
        VideoEncoderOptions options,
        IProgress<VideoEncodeProgress>? progress = null);
}

public record VideoEncoderOptions
{
    public VideoCodecType Codec { get; init; } = VideoCodecType.H264;
    public VideoQualityPreset Preset { get; init; } = VideoQualityPreset.Diagnostic;
    public double FrameRate { get; init; } = 30.0;
    public AudioCodecType AudioCodec { get; init; } = AudioCodecType.None;
    public HardwareAcceleration HwAccel { get; init; } = HardwareAcceleration.Auto;
    // Escape hatch
    public Dictionary<string, string>? RawParameters { get; init; }
}

public enum VideoQualityPreset { Diagnostic, Review, Archive }
public enum VideoCodecType { MPEG2, H264, HEVC }
public enum AudioCodecType { None, AAC, PCM }
public enum HardwareAcceleration { Auto, ForceCpu, PreferGpu }
```

### Pattern 4: VideoDicomBuilder (fluent pattern)

**What:** Builder for creating valid video DICOM files with correct SOP classes and metadata.

```csharp
var dicomFile = new VideoDicomBuilder()
    .WithSopClass(VideoSopClass.Endoscopic)
    .WithTransferSyntax(TransferSyntax.H264HighProfile41)
    .WithPatientFromTemplate(existingDataset)
    .WithFrameRate(30.0)
    .WithPixelData(encodedVideoBytes)
    .Build();
```

### Anti-Patterns to Avoid

- **Separate native libraries for 8-bit and 12-bit:** Symbol collisions at runtime, complex loading logic, packaging nightmare.
- **Loading FFmpeg as a system dependency:** Must build from source via Zig for consistent cross-platform behavior and GPL compliance.
- **Per-GOP fragmentation for video:** DICOM context decision says single encapsulated fragment. Multi-fragment is a newer supplement (225) and adds complexity.
- **Auto-detecting DICOM SOP class from video content:** The SOP class should be explicitly chosen by the caller, not guessed.
- **Blocking synchronous video encoding:** Video encoding is inherently slow; always provide async with progress reporting.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| 12-bit DCT JPEG encode/decode | Custom 12-bit DCT implementation | libjpeg-turbo with WITH_12BIT | DCT coefficient handling at 12-bit precision has subtle edge cases |
| H.264/HEVC encoding | Raw NAL unit construction | FFmpeg libavcodec + x264/x265 | Encoding is enormously complex (motion estimation, rate control, etc.) |
| MPEG2 encoding | Custom MPEG2 encoder | FFmpeg's native mpeg2video encoder | MPEG2 is well-handled by FFmpeg's built-in encoder |
| Audio muxing into video | Manual AAC/PCM interleaving | FFmpeg libavformat | Audio/video synchronization is deceptively complex |
| Image loading (PNG/BMP) | Custom PNG/BMP decoders | stb_image | Battle-tested, handles edge cases in real-world files |
| GPU encoder detection | Custom GPU probing | FFmpeg's hwaccel framework | Already handles NVENC/VideoToolbox/VAAPI detection |
| Color space conversion for video | Manual YCbCr/RGB conversion | FFmpeg's libswscale | Handles all pixel format combinations correctly |

**Key insight:** Video encoding is one of the most complex computing problems. Every component (rate control, motion estimation, entropy coding, audio sync) has decades of refinement in FFmpeg. Hand-rolling any of it would be a multi-year effort.

## Common Pitfalls

### Pitfall 1: libjpeg-turbo 12-bit Build Loses All Performance Features

**What goes wrong:** Building with `-DWITH_12BIT=1` disables SIMD, TurboJPEG API, and arithmetic coding. The resulting library is as slow as libjpeg 6b.
**Why it happens:** libjpeg-turbo's SIMD paths only handle 8-bit samples. The 12-bit code path is essentially vanilla libjpeg.
**How to avoid:** This is expected and acceptable. 12-bit JPEG is rare in DICOM (mostly CT/MR at specific facilities). The 8-bit path retains full SIMD performance. The fat library approach preserves both code paths.
**Warning signs:** If someone tries to use TurboJPEG API (tjCompress2/tjDecompress2) for 12-bit, it will fail at compile time. The 12-bit wrapper must use the raw libjpeg API.

### Pitfall 2: Symbol Collision Between 8-bit and 12-bit libjpeg

**What goes wrong:** Both builds export `jpeg_CreateCompress`, `jpeg_CreateDecompress`, etc. Linking both into one library causes duplicate symbol errors.
**Why it happens:** libjpeg-turbo does not natively support symbol namespacing for dual-precision builds.
**How to avoid:** Use the `-D` flag approach to rename symbols at compile time. For example: `-Djpeg_CreateCompress=jpeg12_jpeg_CreateCompress`. Build a comprehensive rename list from the libjpeg public API header. Alternatively, use `objcopy --prefix-symbols` on the object files post-compilation.
**Warning signs:** Linker errors with "duplicate symbol" during the native build.

### Pitfall 3: FFmpeg Encoding Requires libavformat for Proper Container Output

**What goes wrong:** Using libavcodec alone produces raw encoded packets, not a properly muxed bitstream. DICOM video requires a well-formed bitstream with correct NAL unit boundaries.
**Why it happens:** The existing decode-only wrapper uses libavcodec directly (raw packet input). Encoding needs libavformat to produce correct container output (at minimum for audio interleaving).
**How to avoid:** For MPEG2, raw packets may suffice. For H.264/HEVC, use libavformat's raw H264/HEVC muxer (or Annex-B format) to ensure proper NAL unit start codes. For audio, libavformat is mandatory.
**Warning signs:** Encoded video plays in some players but not others, or audio desync.

### Pitfall 4: GPU Encoder Availability Is Platform-Specific

**What goes wrong:** Code assumes NVENC is available on all systems, fails on CI servers or Mac.
**Why it happens:** NVENC requires NVIDIA GPU + driver. VideoToolbox requires macOS. VAAPI requires Linux + Intel/AMD GPU.
**How to avoid:** Always implement CPU fallback. Detection order: try GPU encoder first (via FFmpeg's `avcodec_find_encoder_by_name`), fall back to software encoder on failure. The `hw_accel` config option controls this behavior.
**Warning signs:** Tests pass locally (developer has GPU) but fail in CI (no GPU).

### Pitfall 5: 12-bit JPEG in Process 1 (1.2.840.10008.1.2.4.50) Transfer Syntax

**What goes wrong:** Some non-conformant DICOM systems encode 12-bit images using the Process 1 (Baseline) transfer syntax instead of Process 2,4 (Extended).
**Why it happens:** Vendor implementation errors. The context decision requires handling this leniently.
**How to avoid:** When decoding, check BitsAllocated/BitsStored from the DICOM dataset. If >8, attempt 12-bit decode even for Process 1 TS. When encoding, always use the correct Process 2,4 (1.2.840.10008.1.2.4.51) transfer syntax.
**Warning signs:** Decode fails for images from certain vendors that claim 8-bit TS but contain 12-bit data.

### Pitfall 6: Video DICOM File Size Limits

**What goes wrong:** Encoded video exceeds 2^32-2 bytes, breaking non-fragmentable transfer syntaxes.
**Why it happens:** Long procedures (endoscopy, surgery) at high resolution can produce multi-gigabyte videos.
**How to avoid:** For non-fragmentable transfer syntaxes (the default per context decision), warn if encoded size approaches 4GB. The context decision specifies single encapsulated fragment, so very large videos may need the fragmentable variants (Supplement 225). Log a warning but don't fail.
**Warning signs:** DICOM writer fails or produces corrupt file for long procedures.

### Pitfall 7: Zig Cross-Compilation of FFmpeg Dependencies (x264/x265)

**What goes wrong:** x264 and x265 have complex build systems (bash configure, CMake respectively) that don't trivially port to Zig build.
**Why it happens:** These are large, mature codebases with GNU autotools/CMake build systems.
**How to avoid:** Use the `allyourcodebase/ffmpeg` Zig package as a reference -- it replaces FFmpeg's configure with build.zig. For x264/x265, compile the C source files directly via Zig's C compilation support, bypassing their native build systems. This is the same approach used for OpenJPEG and CharLS in the existing build.zig.
**Warning signs:** Configure-generated headers missing; architecture-specific defines not set.

## Code Examples

### 12-bit JPEG Native P/Invoke (extending existing NativeMethods.cs)

```csharp
// New P/Invoke declarations for 12-bit JPEG
#if NET7_0_OR_GREATER
[LibraryImport(LibraryName, EntryPoint = "jpeg12_decode")]
[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
internal static partial int jpeg12_decode(
    byte* input, int inputLen,
    byte* output, int outputLen,
    out int width, out int height, out int components,
    int colorspace);

[LibraryImport(LibraryName, EntryPoint = "jpeg12_encode")]
[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
internal static partial int jpeg12_encode(
    byte* input, int width, int height, int components,
    out byte* output, out int outputLen,
    int quality, int subsamp);

[LibraryImport(LibraryName, EntryPoint = "jpeg12_free")]
[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
internal static partial void jpeg12_free(byte* buffer);
#endif
```

### Video Encoder P/Invoke (extending NativeMethods.cs)

```csharp
#if NET7_0_OR_GREATER
[LibraryImport(LibraryName, EntryPoint = "video_encoder_create")]
[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
internal static partial int video_encoder_create(
    VideoEncoderConfig* config,
    out IntPtr encoderOut);

[LibraryImport(LibraryName, EntryPoint = "video_encode_frame")]
[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
internal static partial int video_encode_frame(
    IntPtr encoder,
    byte* pixels, int pixelLen,
    int pixelFormat,
    out byte* output, out int outputLen,
    out int packetAvailable);

[LibraryImport(LibraryName, EntryPoint = "video_encoder_destroy")]
[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
internal static partial void video_encoder_destroy(IntPtr encoder);
#endif

[StructLayout(LayoutKind.Sequential)]
internal struct VideoEncoderConfig
{
    public int CodecId;
    public int Width;
    public int Height;
    public double FrameRate;
    public int BitDepth;
    public int GopSize;
    public int QualityPreset;
    public int Crf;
    public int Bitrate;
    public int HwAccel;
    public int AudioCodec;
    public int AudioSampleRate;
    public int AudioChannels;
}
```

### Managed JpegExtendedCodec (new 12-bit lossy)

```csharp
public sealed class JpegExtendedCodec : IPixelDataCodec
{
    public TransferSyntax TransferSyntax => TransferSyntax.JPEGExtended;
    public string Name => "JPEG Extended (Process 2,4) - 12-bit";

    public CodecCapabilities Capabilities { get; } = new(
        CanEncode: true,
        CanDecode: true,
        IsLossy: true,
        SupportsMultiFrame: true,
        SupportsParallelEncode: true,
        SupportedBitDepths: new[] { 8, 12 },  // Process 2,4 supports 8 and 12 bit
        SupportedSamplesPerPixel: new[] { 1, 3 });

    // Uses SOF1 marker instead of SOF0 (Baseline)
    // Sample precision field in SOF1 can be 8 or 12
    // DCT-based like baseline, but with extended coefficient precision
}
```

### DICOM Video Transfer Syntax Definitions (extending TransferSyntax.cs)

```csharp
// MPEG2
public static readonly TransferSyntax MPEG2MainML = new()
{
    UID = new DicomUID("1.2.840.10008.1.2.4.100"),
    IsExplicitVR = true, IsLittleEndian = true,
    IsEncapsulated = true, IsLossy = true,
    Compression = CompressionType.MPEG2, IsKnown = true
};

public static readonly TransferSyntax MPEG2MainHL = new()
{
    UID = new DicomUID("1.2.840.10008.1.2.4.101"),
    IsExplicitVR = true, IsLittleEndian = true,
    IsEncapsulated = true, IsLossy = true,
    Compression = CompressionType.MPEG2, IsKnown = true
};

// H.264
public static readonly TransferSyntax H264HighProfile41 = new()
{
    UID = new DicomUID("1.2.840.10008.1.2.4.102"),
    IsExplicitVR = true, IsLittleEndian = true,
    IsEncapsulated = true, IsLossy = true,
    Compression = CompressionType.H264, IsKnown = true
};

// HEVC
public static readonly TransferSyntax HEVCMainProfile51 = new()
{
    UID = new DicomUID("1.2.840.10008.1.2.4.107"),
    IsExplicitVR = true, IsLittleEndian = true,
    IsEncapsulated = true, IsLossy = true,
    Compression = CompressionType.HEVC, IsKnown = true
};
```

## DICOM Video Transfer Syntax Reference

Complete list of video transfer syntaxes to register:

| UID | Name | Codec | Notes |
|-----|------|-------|-------|
| 1.2.840.10008.1.2.4.100 | MPEG2 Main Profile / Main Level | MPEG2 | Non-fragmentable |
| 1.2.840.10008.1.2.4.101 | MPEG2 Main Profile / High Level | MPEG2 | Non-fragmentable, HDTV |
| 1.2.840.10008.1.2.4.102 | MPEG-4 AVC/H.264 High Profile / Level 4.1 | H.264 | Non-fragmentable |
| 1.2.840.10008.1.2.4.103 | MPEG-4 AVC/H.264 BD-compatible HP / Level 4.1 | H.264 | Blu-ray compatible |
| 1.2.840.10008.1.2.4.104 | MPEG-4 AVC/H.264 HP / Level 4.2 For 2D Video | H.264 | Non-fragmentable |
| 1.2.840.10008.1.2.4.105 | MPEG-4 AVC/H.264 HP / Level 4.2 For 3D Video | H.264 | Stereo |
| 1.2.840.10008.1.2.4.106 | MPEG-4 AVC/H.264 Stereo HP / Level 4.2 | H.264 | MVC |
| 1.2.840.10008.1.2.4.107 | HEVC/H.265 Main Profile / Level 5.1 | HEVC | 8-bit |
| 1.2.840.10008.1.2.4.108 | HEVC/H.265 Main 10 Profile / Level 5.1 | HEVC | 10-bit |

Additionally, fragmentable variants (.1 suffix) exist for 100-106 but are out of initial scope (context decision: single encapsulated fragment).

## DICOM Video SOP Classes

| UID | Name |
|-----|------|
| 1.2.840.10008.5.1.4.1.1.77.1.1.1 | Video Endoscopic Image Storage |
| 1.2.840.10008.5.1.4.1.1.77.1.2.1 | Video Microscopic Image Storage |
| 1.2.840.10008.5.1.4.1.1.77.1.4.1 | Video Photographic Image Storage |
| 1.2.840.10008.5.1.4.1.1.12.2.1 | Enhanced XA Image Storage (video frames) |
| 1.2.840.10008.5.1.4.1.1.12.1.1 | Enhanced XRF Image Storage (video frames) |
| 1.2.840.10008.5.1.4.1.1.6.2 | US Multi-frame Image Storage (video capable) |
| 1.2.840.10008.5.1.4.1.1.7.4 | SC Multi-frame True Color Image Storage |

## JPEG Extended Transfer Syntax

| UID | Name | Notes |
|-----|------|-------|
| 1.2.840.10008.1.2.4.51 | JPEG Extended (Process 2 & 4) | Standard 12-bit lossy, uses SOF1 marker |
| 1.2.840.10008.1.2.4.50 | JPEG Baseline (Process 1) | Nominally 8-bit only, but lenient decode for 12-bit needed |

## Video Quality Preset Values

Recommended values for medical imaging video (Claude's discretion area):

| Preset | H.264 CRF | HEVC CRF | MPEG2 Bitrate | Description |
|--------|-----------|----------|---------------|-------------|
| Diagnostic | 17 | 20 | 15 Mbps | Near-lossless visual quality for primary diagnosis |
| Review | 23 | 26 | 8 Mbps | Good quality for case review and comparison |
| Archive | 28 | 31 | 4 Mbps | Acceptable quality for long-term storage |

## FFmpeg Configure Flags (Minimal Encoding Build)

Recommended FFmpeg configure equivalent for the Zig build:

```
--enable-gpl              # Required for x264/x265
--enable-libx264          # H.264 encoding
--enable-libx265          # HEVC encoding
--disable-programs        # No ffmpeg/ffprobe CLI tools
--disable-doc             # No documentation
--disable-network         # No network support
--disable-everything      # Start with nothing enabled
--enable-encoder=mpeg2video,libx264,libx265,aac,pcm_s16le
--enable-decoder=mpeg2video,h264,hevc,aac,pcm_s16le
--enable-muxer=rawvideo,h264,hevc,mpegvideo,adts,wav
--enable-demuxer=rawvideo,h264,hevc,mpegvideo
--enable-protocol=pipe
--enable-filter=null,format,aformat
--enable-swscale          # Pixel format conversion
--enable-swresample       # Audio resampling
```

Note: In the Zig build, these are not configure flags but rather conditional compilation defines in generated config.h. The `allyourcodebase/ffmpeg` package shows how to generate this header programmatically.

## GPU Encoder Detection Strategy

Detection order for hardware acceleration:

1. **Auto mode (default):**
   - Try `avcodec_find_encoder_by_name("h264_nvenc")` (NVIDIA)
   - Try `avcodec_find_encoder_by_name("h264_videotoolbox")` (macOS)
   - Try `avcodec_find_encoder_by_name("h264_vaapi")` (Linux Intel/AMD)
   - Fall back to `avcodec_find_encoder(AV_CODEC_ID_H264)` (x264 software)

2. **Force CPU:** Skip GPU detection, use software encoder directly.

3. **Prefer GPU:** Try GPU first, but don't fail if unavailable -- still fall back.

Equivalent pattern for HEVC: `hevc_nvenc`, `hevc_videotoolbox`, `hevc_vaapi`, then `libx265`.

For MPEG2: No GPU encoder exists; always use FFmpeg's native `mpeg2video` software encoder.

## Frame Rate Derivation from DICOM Tags

When auto-detecting frame rate from existing multi-frame DICOM files:

1. Check `FrameTime` (0018,1063) -- time between frames in milliseconds. FPS = 1000 / FrameTime.
2. If absent, check `CineRate` (0018,0040) -- frames per second directly.
3. If absent, check `RecommendedDisplayFrameRate` (0008,2144).
4. If all absent, **require explicit frame rate** in encoder options (do not guess).

When multiple tags are present and conflict: `FrameTime` takes priority (most specific to actual acquisition).

## stb_image Integration

stb_image supports: JPEG, PNG, BMP, TGA, PSD, GIF, HDR, PIC, PNM. For the video encoder's image sequence input, this covers all the required formats (PNG/BMP/TIFF) plus extras. Note: stb_image does NOT support TIFF -- if TIFF input is required, it would need a separate solution. The context decision lists "PNG/BMP/TIFF" but stb_image covers PNG and BMP. For TIFF, consider either dropping it or adding a minimal TIFF decoder.

**stb_image C wrapper pattern:**
```c
#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"

SHARPDICOM_API int stbi_load_from_memory_wrapper(
    const uint8_t* buffer, int len,
    uint8_t** pixels, int* width, int* height, int* channels,
    int desired_channels);

SHARPDICOM_API void stbi_free_wrapper(uint8_t* pixels);
```

## Existing Codebase Integration Points

### Files to Modify

| File | Change |
|------|--------|
| `src/SharpDicom/Data/TransferSyntax.cs` | Add JPEGExtended, MPEG2, H264, HEVC transfer syntaxes + FromUID cases |
| `src/SharpDicom/Data/CompressionType.cs` | Add MPEG2, H264, HEVC enum values (JPEGExtended already exists) |
| `src/SharpDicom/Codecs/CodecInitializer.cs` | Register JpegExtendedCodec |
| `src/SharpDicom.Codecs/NativeCodecs.cs` | Register NativeJpeg8Codec, NativeJpeg12Codec; add NativeCodecFeature.Jpeg12Bit |
| `src/SharpDicom.Codecs/Interop/NativeMethods.cs` | Add jpeg12_* and video_encoder_* P/Invoke declarations |
| `src/SharpDicom.Codecs/Interop/NativeFeatures.cs` | Add Jpeg12Bit feature flag |
| `native/build.zig` | Dual libjpeg-turbo builds, FFmpeg encoding, stb_image, x264/x265 |
| `native/src/jpeg_wrapper.h` | Add jpeg12_* function declarations |
| `native/src/video_wrapper.h` | Add video_encoder_* declarations |
| `native/src/sharpdicom_codecs.h` | Add SHARPDICOM_HAS_JPEG12, SHARPDICOM_HAS_VIDEO_ENC feature flags |

### Files to Create

| File | Purpose |
|------|---------|
| `src/SharpDicom/Codecs/Jpeg/JpegExtendedCodec.cs` | Managed 12-bit lossy DCT codec |
| `src/SharpDicom/Codecs/Jpeg/JpegExtendedDecoder.cs` | Pure C# 12-bit JPEG decoder |
| `src/SharpDicom/Codecs/Jpeg/JpegExtendedEncoder.cs` | Pure C# 12-bit JPEG encoder |
| `src/SharpDicom/Codecs/Video/VideoEncoder.cs` | High-level video encoding API |
| `src/SharpDicom/Codecs/Video/VideoEncoderOptions.cs` | Options and presets |
| `src/SharpDicom/Codecs/Video/VideoDicomBuilder.cs` | Fluent builder for video DICOM files |
| `src/SharpDicom/Codecs/Video/VideoFrame.cs` | Frame data container |
| `src/SharpDicom/Codecs/Video/VideoEncodeProgress.cs` | Progress reporting |
| `src/SharpDicom.Codecs/Codecs/NativeJpeg8Codec.cs` | Explicit 8-bit native (replaces NativeJpegCodec) |
| `src/SharpDicom.Codecs/Codecs/NativeJpeg12Codec.cs` | 12-bit native codec |
| `src/SharpDicom.Codecs/Codecs/NativeVideoEncoder.cs` | Native video encoding wrapper |
| `native/src/jpeg12_wrapper.c` | 12-bit libjpeg wrapper (uses raw libjpeg API, not TurboJPEG) |
| `native/src/video_encoder.c` | Video encoder implementation |
| `native/src/stb_image_wrapper.c` | stb_image integration |
| `native/src/video_encoder.h` | Video encoder header |
| `native/vendor/stb/stb_image.h` | stb_image header (vendored) |

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Two libjpeg builds as separate .so/.dll | Single fat library with symbol prefixes | Established pattern in medical imaging SDKs | Eliminates symbol collision at load time |
| MPEG2-only DICOM video | H.264/HEVC encoding standard in DICOM | DICOM Supplements 149 (H.264) and 163 (HEVC) | Much better compression ratios |
| Manual FFmpeg configure + make | Zig-based build system | allyourcodebase/ffmpeg (2023+) | Cross-compilation from any host to any target |
| GPU encoding optional/rare | GPU encoding expected where available | NVENC/VideoToolbox maturation 2020+ | Dramatically faster encoding for GPU-equipped systems |

**Deprecated/outdated:**
- libjpeg-turbo's `WITH_12BIT` flag alone: Disables all performance features; the dual-build approach is the correct modern solution
- MPEG2 as primary video codec: Still supported but H.264 is preferred for new content
- Non-fragmentable video transfer syntaxes for very large files: Supplement 225 adds fragmentable variants

## Open Questions

1. **TIFF support in stb_image**
   - What we know: stb_image does NOT support TIFF. The context decision lists "PNG/BMP/TIFF" as image sequence inputs.
   - What's unclear: Whether TIFF is actually needed (it's listed but may be rare in practice).
   - Recommendation: Implement PNG/BMP via stb_image initially. Add TIFF via a small dedicated TIFF reader if needed (or vendor libtiff). Flag this for discussion.

2. **allyourcodebase/ffmpeg build.zig reuse**
   - What we know: The package exists and builds FFmpeg with Zig. It enables "everything supported by the target."
   - What's unclear: Whether it can be easily configured to build only the needed encoders/decoders, or if we need a custom build.zig for FFmpeg.
   - Recommendation: Start by adapting the existing Zig build approach already in `native/build.zig`. The allyourcodebase package is a reference but may be too heavyweight. Compile FFmpeg C sources directly via Zig (same as OpenJPEG/CharLS pattern in existing build.zig).

3. **x264/x265 source compilation via Zig**
   - What we know: Both have complex build systems. Zig can compile C source directly.
   - What's unclear: Exact list of source files needed and configure-generated headers required.
   - Recommendation: This will require investigation during implementation. The approach of compiling C source files directly (bypassing configure/CMake) is proven for OpenJPEG in the existing build.zig but x264/x265 may have more complex requirements.

4. **Fragmentable video transfer syntaxes**
   - What we know: DICOM Supplement 225 adds fragmentable variants (.1 suffix). Context decision says single encapsulated fragment.
   - What's unclear: Whether to register the fragmentable TS UIDs for future use.
   - Recommendation: Register the non-fragmentable UIDs for now. Add fragmentable support as a future extension.

5. **Audio in DICOM video**
   - What we know: DICOM specifies audio can be interleaved in H.264/HEVC bitstreams (AAC, LPCM, AC-3, MP3, MPEG-1 Layer II).
   - What's unclear: How commonly audio is used in practice and what the exact interleaving format looks like.
   - Recommendation: Implement AAC and PCM per context decision. Use FFmpeg's muxer for interleaving. Test with real DICOM video viewers.

## Sources

### Primary (HIGH confidence)
- Codebase analysis of existing codec infrastructure (NativeJpegCodec.cs, NativeMethods.cs, build.zig, video_wrapper.c, jpeg_wrapper.c, CodecRegistry.cs, TransferSyntax.cs)
- [DICOM Transfer Syntax Registry (Part 6, Annex A)](https://dicom.nema.org/medical/dicom/current/output/chtml/part06/chapter_a.html) - Complete video transfer syntax UIDs
- [DICOM Library Transfer Syntax Reference](https://www.dicomlibrary.com/dicom/transfer-syntax/) - UID verification

### Secondary (MEDIUM confidence)
- [libjpeg-turbo BUILDING.md](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/main/BUILDING.md) - 12-bit build limitations confirmed
- [libjpeg-turbo Issue #199](https://github.com/libjpeg-turbo/libjpeg-turbo/issues/199) - No SIMD for 12-bit confirmed
- [libjpeg-turbo Issue #590](https://github.com/libjpeg-turbo/libjpeg-turbo/issues/590) - 12-bit usability status
- [FFmpeg encoding API documentation](https://ffmpeg.org/doxygen/4.0/group__lavc__encdec.html) - send_frame/receive_packet pattern
- [allyourcodebase/ffmpeg](https://github.com/allyourcodebase/ffmpeg) - Zig FFmpeg build reference
- [stb_image.h](https://github.com/nothings/stb/blob/master/stb_image.h) - Format support list (v2.30)
- [NVIDIA FFmpeg encoding guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.0/ffmpeg-with-nvidia-gpu/index.html) - NVENC integration
- [FFmpeg Hardware Context System](https://deepwiki.com/FFmpeg/FFmpeg/7.1-hardware-context-system) - GPU abstraction
- [DICOM PS3.5 Section 8.2.6-8.2.10](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_8.2.6.html) - Video compression specifications
- [DICOM PS3.5 Section 10.3](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_10.3.html) - JPEG Extended (12-bit) transfer syntax
- [objcopy documentation](https://www.sourceware.org/binutils/docs/binutils/objcopy.html) - Symbol renaming

### Tertiary (LOW confidence)
- [TurboJPEG Guide](https://copyprogramming.com/howto/examples-or-tutorials-of-using-libjpeg-turbo-s-turbojpeg) - General TurboJPEG usage patterns
- [Hardware-Accelerated FFmpeg article](https://www.ffmpeg.media/articles/hardware-accelerated-ffmpeg-nvenc-vaapi-videotoolbox) - GPU encoder comparison
- [dmorn/ffmpeg.zig](https://github.com/dmorn/ffmpeg.zig) - Alternative Zig FFmpeg build

## Metadata

**Confidence breakdown:**
- Standard stack: MEDIUM - libjpeg-turbo and FFmpeg are well-known, but the dual-build symbol prefix approach and Zig cross-compilation of x264/x265 have implementation risk
- Architecture: HIGH - Patterns directly extend existing codebase patterns (handle-based C API, IPixelDataCodec, CodecRegistry)
- Pitfalls: HIGH - libjpeg-turbo 12-bit limitations well-documented; FFmpeg encoding patterns well-established
- Video quality presets: MEDIUM - CRF values are reasonable defaults but may need tuning with real medical video content

**Research date:** 2026-02-06
**Valid until:** 2026-03-06 (stable domain; libjpeg-turbo and FFmpeg APIs change slowly)
