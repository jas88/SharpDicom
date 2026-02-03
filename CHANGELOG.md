# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0] - 2026-02-02

### Added

#### DICOM Networking (Phase 10-11)
- Full DICOM networking stack with `DicomClient` and `DicomServer` classes
- PDU parsing with TCP fragmentation handling
- 13-state association machine matching PS3.8 Section 9.2
- C-ECHO SCU/SCP for connectivity verification
- C-STORE SCU/SCP for image transfer with streaming support
- C-FIND SCU with `IAsyncEnumerable` for query results
- C-MOVE SCU for third-party retrieval
- C-GET SCU with C-STORE sub-operation handling
- Fluent `DicomQuery` builder for Patient/Study/Series/Instance level queries
- Modality Worklist (MWL) SCU support

#### Pure C# Image Codecs (Phase 12)
- JPEG Baseline codec (8-bit lossy, Process 1)
- JPEG Lossless codec (Process 14, Selection Value 1)
- JPEG 2000 Lossless codec (5/3 reversible wavelet)
- JPEG 2000 Lossy codec (9/7 irreversible wavelet)
- Loeffler algorithm DCT with AVX2 SIMD optimization
- AOT-compatible `CodecInitializer.RegisterAll()` registration

#### Native Codecs Package (Phase 13)
- `SharpDicom.Codecs` NuGet package with native library wrappers
- libjpeg-turbo wrapper for high-performance JPEG (TurboJPEG API)
- OpenJPEG wrapper for JPEG 2000 with resolution level and ROI decode
- CharLS wrapper for JPEG-LS
- FFmpeg wrapper for MPEG2/H.264/HEVC video codecs
- nvJPEG2000 GPU acceleration with CPU fallback
- Zig 0.13.0 cross-compilation for 6 platforms (win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64, linux-musl-x64)
- Priority-based codec registration (native overrides managed)

#### De-identification (Phase 14)
- PS3.15 Basic Application Level Confidentiality Profile
- Source-generated action table from NEMA part15.xml (~600 tags)
- UUID-derived UID remapping (2.25.xxx format)
- Date/time shifting with fixed and random-per-patient strategies
- Fluent `DicomDeidentifierBuilder` API
- JSON configuration with `$extends` inheritance
- Built-in presets: basic-profile, research, clinical-trial, teaching
- Pixel data redaction for burned-in annotations
- Batch processing with `BatchDeidentifier`

#### Infrastructure
- Zero-copy PDU infrastructure with `SlabMemoryPool` and `SocketPipe`
- JPEG-LS codec infrastructure (encode/decode skeletons)
- HTJ2K codec infrastructure

### Changed
- Test count increased from 1030 to 3660

## [1.0.1] - 2026-01-28

### Fixed
- NuGet package now includes README.md
- Embedded PDBs in DLLs for debugging (replaces broken snupkg)
- CI pack step uses `-warnaserror` to catch packaging issues

## [1.0.0] - 2026-01-28

### Added
- Complete DICOM Part 10 file reading and writing
- Streaming parser with async support (IAsyncEnumerable)
- Explicit and Implicit VR support
- Little and Big Endian support
- Nested sequence parsing to configurable depth
- RLE Lossless codec with SIMD optimization
- Lazy pixel data loading with configurable strategies
- Private tag support with vendor dictionaries (Siemens, GE, Philips, etc.)
- Character encoding support (ISO-IR 6 through UTF-8)
- Validation framework with Strict/Lenient/Permissive profiles
- Source-generated DICOM dictionary from NEMA XML
- Source-generated vendor private tag dictionaries
- GitHub Actions CI with code coverage
- Dependabot configuration for NuGet, Actions, and SDK updates

### Target Frameworks
- netstandard2.0 (broad compatibility)
- net8.0 (LTS)
- net9.0
- net10.0

[Unreleased]: https://github.com/jas88/SharpDicom/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/jas88/SharpDicom/compare/v1.0.1...v2.0.0
[1.0.1]: https://github.com/jas88/SharpDicom/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/jas88/SharpDicom/releases/tag/v1.0.0
