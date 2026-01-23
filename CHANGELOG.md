# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/jas88/SharpDicom/commits/main
