# Native Codec Build Requirements

## Overview

The native build system (`build.zig`) compiles `sharpdicom_codecs` as a shared library for 6 target platforms (win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64). It supports optional vendor libraries. When a vendor library is absent, a stub implementation is compiled that returns `UNSUPPORTED` error codes at runtime. The managed C# codecs (in `src/SharpDicom/Codecs/`) serve as fallback for any unsupported native features.

## Required Tools

- **Zig 0.13.0+** -- cross-compilation toolchain. Zig compiles C/C++ sources for all target platforms without requiring platform-specific SDKs.

## Optional Vendor Libraries

Each vendor library is controlled by a boolean constant at the top of `build.zig`. Set to `true` and place vendor sources at the expected path to enable.

| Feature | Build Flag | Vendor Path | Source |
|---------|-----------|-------------|--------|
| 8-bit JPEG | `have_libjpeg` | `vendor/libjpeg-turbo/src` | https://github.com/libjpeg-turbo/libjpeg-turbo |
| 12-bit JPEG | `have_libjpeg12` | `vendor/libjpeg-turbo/src` (same source, compiled with `-DWITH_12BIT=1` and symbol prefix flags) | https://github.com/libjpeg-turbo/libjpeg-turbo |
| JPEG 2000 | `have_openjpeg` | `vendor/openjpeg/src` | https://github.com/uclouvain/openjpeg |
| JPEG-LS | `have_charls` | `vendor/charls/src` | https://github.com/team-charls/charls |
| FFmpeg (decode) | `have_ffmpeg` | `vendor/ffmpeg/` | https://github.com/FFmpeg/FFmpeg |
| FFmpeg (encode) | `have_ffmpeg_enc` | `vendor/ffmpeg/` + `vendor/x264/` + `vendor/x265/` | https://github.com/FFmpeg/FFmpeg, https://code.videolan.org/videolan/x264, https://bitbucket.org/multicoreware/x265_git |
| Tesseract OCR | `have_tesseract` | `vendor/tesseract/src` + `vendor/leptonica/src` | https://github.com/tesseract-ocr/tesseract |
| stb_image | `have_stb_image` | `vendor/stb/` | https://github.com/nothings/stb |

## Enabling 12-bit JPEG

The 8-bit and 12-bit JPEG codecs use the same libjpeg-turbo source but are compiled separately with different configurations. The 12-bit build uses symbol prefix flags so both can coexist in a single shared library.

### Steps

1. **Clone libjpeg-turbo source:**

   ```bash
   git clone https://github.com/libjpeg-turbo/libjpeg-turbo.git native/vendor/libjpeg-turbo/src
   ```

2. **Enable the build flag in `build.zig`:**

   Set `have_libjpeg12 = true` on line 19.

3. **Build:**

   ```bash
   cd native
   zig build
   ```

### How the dual build works

The build system compiles the 12-bit libjpeg-turbo sources with:
- `-DWITH_12BIT=1` to select 12-bit sample precision
- Symbol prefix defines that rename every public libjpeg function (e.g., `jpeg_CreateCompress` becomes `jpeg12_jpeg_CreateCompress`, `jpeg_read_scanlines` becomes `jpeg12_jpeg_read_scanlines`, etc.)

This prevents symbol collisions between the 8-bit and 12-bit builds. The `jpeg12_wrapper.c` wrapper calls the prefixed symbols.

### Limitations of the 12-bit build

- The 12-bit build does **not** use TurboJPEG or SIMD. The TurboJPEG API and SIMD optimisations are incompatible with `WITH_12BIT`. It uses the raw libjpeg API only.
- The 8-bit path (`have_libjpeg`) retains full TurboJPEG and SIMD performance.

## Fallback Behaviour

When `have_libjpeg12 = false` (the default), the build compiles a stub `jpeg12_wrapper.c` that returns error codes for all functions. At runtime:

- `NativeCodecs.HasFeature(NativeCodecFeature.Jpeg12Bit)` returns `false`.
- The managed `JpegExtendedCodec` handles 12-bit JPEG decoding and encoding instead.

This pattern applies to all optional vendor libraries: the native feature probe returns false, and the managed codec serves as fallback.

## CI Notes

The CI workflow builds without vendor sources by default, producing stub-only native libraries. The managed codecs handle all compression in this configuration.

To test native 12-bit JPEG in CI, add a build matrix variant that:

1. Fetches libjpeg-turbo source before the Zig build step.
2. Sets `have_libjpeg12 = true` in `build.zig` (or uses a sed/patch step).
3. Runs the native codec test suite to verify 12-bit encode/decode roundtrips.
