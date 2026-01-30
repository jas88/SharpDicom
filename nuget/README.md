# SharpDicom.Codecs

High-performance native codec wrappers for SharpDicom supporting JPEG, JPEG 2000, and JPEG-LS compression.

## Installation

Install the meta package, which automatically pulls in the correct runtime package for your platform:

```bash
dotnet add package SharpDicom.Codecs
```

## Supported Platforms

| Platform | Runtime Identifier | Package |
|----------|-------------------|---------|
| Windows x64 | win-x64 | SharpDicom.Codecs.runtime.win-x64 |
| Windows ARM64 | win-arm64 | SharpDicom.Codecs.runtime.win-arm64 |
| Linux x64 | linux-x64 | SharpDicom.Codecs.runtime.linux-x64 |
| Linux ARM64 | linux-arm64 | SharpDicom.Codecs.runtime.linux-arm64 |
| macOS x64 (Intel) | osx-x64 | SharpDicom.Codecs.runtime.osx-x64 |
| macOS ARM64 (Apple Silicon) | osx-arm64 | SharpDicom.Codecs.runtime.osx-arm64 |

## Bundled Libraries

This package contains pre-built binaries from the following open-source projects:

### libjpeg-turbo 3.0.4

- **Description**: JPEG image codec with SIMD optimizations (2-6x faster than libjpeg)
- **License**: BSD-3-Clause, IJG, Zlib (see THIRD_PARTY_LICENSES.txt)
- **Website**: https://libjpeg-turbo.org/
- **Used for**: JPEG baseline and extended transfer syntaxes

### OpenJPEG 2.5.3

- **Description**: Open-source JPEG 2000 codec
- **License**: BSD-2-Clause (see THIRD_PARTY_LICENSES.txt)
- **Website**: https://www.openjpeg.org/
- **Used for**: JPEG 2000 lossless and lossy transfer syntaxes

### CharLS 2.4.2

- **Description**: JPEG-LS codec optimized for medical imaging
- **License**: BSD-3-Clause (see THIRD_PARTY_LICENSES.txt)
- **Website**: https://github.com/team-charls/charls
- **Used for**: JPEG-LS lossless and near-lossless transfer syntaxes

## Usage

The codecs are automatically registered when you reference SharpDicom.Codecs:

```csharp
using SharpDicom.Codecs.Native;

// Register native codecs with the SharpDicom codec registry
NativeCodecs.RegisterAll();

// Now decompress/compress DICOM files with supported transfer syntaxes
var file = DicomFile.Open("compressed.dcm");
var pixelData = file.Dataset.GetPixelData();
byte[] decompressed = pixelData.Decompress();
```

## Performance

Native codecs provide significant performance improvements over pure C# implementations:

| Codec | Speedup vs Pure C# | Notes |
|-------|-------------------|-------|
| JPEG | 3-5x | SIMD optimized (AVX2, NEON) |
| JPEG 2000 | 2-4x | Multi-threaded decode |
| JPEG-LS | 2-3x | Optimized for medical images |

## Requirements

- .NET Standard 2.0+ / .NET 6.0+ / .NET 8.0+
- No system dependencies (statically linked)
- AOT/Trimming compatible

## License

The SharpDicom.Codecs package is licensed under GPL-3.0-or-later.

The bundled native libraries have their own licenses (all permissive BSD-style).
See THIRD_PARTY_LICENSES.txt for complete license texts.
