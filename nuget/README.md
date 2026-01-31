# SharpDicom.Codecs Runtime Package

This package contains native codec libraries for SharpDicom.Codecs.

**Do not reference directly.** This package is automatically referenced
by SharpDicom.Codecs based on your target platform (RuntimeIdentifier).

## Supported Platforms

| Package | Platform | Architecture |
|---------|----------|--------------|
| SharpDicom.Codecs.runtime.win-x64 | Windows | x64 |
| SharpDicom.Codecs.runtime.win-arm64 | Windows | ARM64 |
| SharpDicom.Codecs.runtime.linux-x64 | Linux | x64 (glibc/musl) |
| SharpDicom.Codecs.runtime.linux-arm64 | Linux | ARM64 (glibc/musl) |
| SharpDicom.Codecs.runtime.osx-x64 | macOS | x64 (Intel) |
| SharpDicom.Codecs.runtime.osx-arm64 | macOS | ARM64 (Apple Silicon) |

## Bundled Codecs

The native library provides hardware-accelerated implementations of:

- **JPEG Baseline/Extended** - via libjpeg-turbo (SIMD-accelerated)
- **JPEG 2000** - via OpenJPEG
- **JPEG-LS** - via CharLS
- **MPEG2/MPEG4/HEVC** - via FFmpeg (subset)

## Build Information

Native libraries are built using Zig for cross-compilation, ensuring:

- Consistent builds across all platforms
- Statically linked dependencies (no external DLL requirements)
- Security hardening (stack protector, ASLR, FORTIFY_SOURCE)

## License

This package is licensed under **GPL-3.0-or-later**.

The bundled third-party libraries have their own licenses:

- libjpeg-turbo: IJG License / BSD-3-Clause / zlib License
- OpenJPEG: BSD-2-Clause
- CharLS: BSD-3-Clause
- FFmpeg: LGPL-2.1+ / GPL-2.0+ (depending on configuration)

See `THIRD_PARTY_LICENSES.txt` included in this package for full license texts.

## More Information

- [SharpDicom Documentation](https://github.com/jas88/SharpDicom)
- [Issue Tracker](https://github.com/jas88/SharpDicom/issues)
