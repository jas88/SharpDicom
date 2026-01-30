# Native Vendor Dependencies

This directory contains vendored source code for native codec libraries.

**IMPORTANT:** Source files are NOT committed to this repository. They are downloaded during CI builds to ensure reproducibility while keeping the repository lightweight.

## Libraries

### libjpeg-turbo

**Version:** 3.0.4
**Source:** https://github.com/libjpeg-turbo/libjpeg-turbo
**License:** IJG (Independent JPEG Group) / BSD-3-Clause / zlib

High-performance JPEG codec library providing:
- JPEG baseline (8-bit lossy DCT-based)
- JPEG extended (12-bit lossy DCT-based)
- JPEG lossless (predictive, not DCT)
- SIMD-accelerated encoding/decoding (SSE2, AVX2, NEON)
- TurboJPEG API for simplified high-performance access

**License Details:**
libjpeg-turbo is covered by three compatible licenses:
1. IJG license (original libjpeg) - permissive, attribution required
2. BSD-3-Clause - modified 3-clause BSD
3. zlib license - permissive, similar to MIT

All three licenses are BSD-compatible with SharpDicom's license.

**Build Notes:**
- Uses TurboJPEG API (turbojpeg.h) for simplified high-performance access
- Static library linked into sharpdicom_codecs
- For 12-bit extended JPEG support: library must be built with `-DWITH_12BIT=1`
- SIMD is auto-detected and enabled by libjpeg-turbo build system

### OpenJPEG

**Version:** 2.5.x (2.5.0 or later recommended)
**Source:** https://github.com/uclouvain/openjpeg
**License:** BSD-2-Clause

JPEG 2000 codec library providing:
- Lossless and lossy JPEG 2000 compression
- Support for J2K (codestream) and JP2 (file format)
- Resolution level decoding for thumbnails
- ROI (Region of Interest) decoding
- HTJ2K (High-Throughput JPEG 2000) support in 2.5+

**Build Notes:**
- Static library linked into sharpdicom_codecs
- Configure with `-DOPJ_USE_THREAD=ON` for multi-threaded operations
- Configure with `-DBUILD_SHARED_LIBS=OFF` for static build

### CharLS (Future)

**Version:** 2.4.x
**Source:** https://github.com/team-charls/charls
**License:** BSD-3-Clause

JPEG-LS codec library providing:
- Lossless JPEG-LS compression
- Near-lossless JPEG-LS compression
- Header-only C++ library option

## Local Development

For local development, you can either:

1. **Use system libraries:** Install via your package manager:
   - macOS: `brew install jpeg-turbo openjpeg`
   - Ubuntu/Debian: `apt install libturbojpeg0-dev libopenjp2-7-dev`
   - Windows: Download from https://libjpeg-turbo.org/ and https://github.com/uclouvain/openjpeg/releases

2. **Download sources manually:**
   ```bash
   cd native/vendor

   # libjpeg-turbo
   curl -L https://github.com/libjpeg-turbo/libjpeg-turbo/archive/refs/tags/3.0.4.tar.gz | tar xz
   mv libjpeg-turbo-3.0.4 libjpeg-turbo-src

   # OpenJPEG
   curl -L https://github.com/uclouvain/openjpeg/archive/refs/tags/v2.5.3.tar.gz | tar xz
   mv openjpeg-2.5.3 openjpeg-src
   ```

The build system will detect system headers if vendored sources are not present.

## CI Download Process

The GitHub Actions workflow downloads these sources automatically before build:

```yaml
- name: Download vendored libraries
  run: |
    # libjpeg-turbo
    curl -L https://github.com/libjpeg-turbo/libjpeg-turbo/archive/refs/tags/3.0.4.tar.gz | tar xz
    mv libjpeg-turbo-3.0.4 native/vendor/libjpeg-turbo-src

    # OpenJPEG
    curl -L https://github.com/uclouvain/openjpeg/archive/refs/tags/v2.5.3.tar.gz | tar xz
    mv openjpeg-2.5.3 native/vendor/openjpeg-src
```

This ensures:
- Reproducible builds with known versions
- No binary blobs in the repository
- Automatic updates via dependency version bumps

## Directory Structure

```
vendor/
  README.md                 # This file
  libjpeg-turbo/            # Placeholder for CI downloads
    .gitkeep
  libjpeg-turbo-src/        # Downloaded source (not committed)
    turbojpeg.h             # TurboJPEG API header
    ...
  openjpeg/                 # Placeholder for CI downloads
    .gitkeep
  openjpeg-src/             # Downloaded source (not committed)
    src/lib/openjp2/openjpeg.h
    ...
```

## Updating Dependencies

1. Update the version in this README
2. Update the download URL in `.github/workflows/native-build.yml`
3. Run local build to verify compatibility
4. Update any wrapper code if API changed
5. Test on all platforms

## License Compliance

All vendored libraries must be BSD-compatible with SharpDicom's license.
License texts are aggregated into `THIRD_PARTY_LICENSES.txt` during build.

| Library        | License                              | BSD Compatible |
|----------------|--------------------------------------|----------------|
| libjpeg-turbo  | IJG / BSD-3-Clause / zlib            | Yes            |
| OpenJPEG       | BSD-2-Clause                         | Yes            |
| CharLS         | BSD-3-Clause                         | Yes            |
