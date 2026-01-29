---
phase: 12
plan: 01
subsystem: codecs
tags: [jpeg, huffman, quantization, color-conversion, transfer-syntax]
dependencies:
  requires: [phase-9-codecs]
  provides: [jpeg-codec-infrastructure, color-conversion]
  affects: [12-02, 12-03, 12-04, 12-05]
tech-stack:
  added: []
  patterns: [ref-struct-bit-reader, standard-tables, span-api]
key-files:
  created:
    - src/SharpDicom/Codecs/Jpeg/JpegMarkers.cs
    - src/SharpDicom/Codecs/Jpeg/JpegFrameInfo.cs
    - src/SharpDicom/Codecs/Jpeg/HuffmanTable.cs
    - src/SharpDicom/Codecs/Jpeg/QuantizationTable.cs
    - src/SharpDicom/Codecs/ColorConversion.cs
  modified:
    - src/SharpDicom/Data/TransferSyntax.cs
decisions:
  - id: huffman-bit-reader-ref-struct
    choice: ref struct HuffmanBitReader
    rationale: Zero-allocation bit reading with JPEG byte-stuffing handling
  - id: itu-standard-tables
    choice: ITU-T T.81 Annex K tables as static fields
    rationale: Standard JPEG tables for encoding when no DHT segment present
  - id: bt601-coefficients
    choice: ITU-R BT.601 coefficients for YCbCr
    rationale: DICOM PS3.3 C.7.6.3.1.2 specifies BT.601 for photometric interpretation
metrics:
  duration: 5m
  completed: 2026-01-29
---

# Phase 12 Plan 01: JPEG Codec Infrastructure Summary

JPEG codec foundation with TransferSyntax definitions, marker parsing, ITU-T T.81 Huffman/quantization tables, and ITU-R BT.601 color conversion.

## Commits

| Hash | Type | Description |
|------|------|-------------|
| 21a5157 | feat | Add JPEGLossless and JPEG2000Lossy transfer syntaxes |
| fcbaaa7 | feat | Add JPEG marker parsing infrastructure |
| 9081491 | feat | Add Huffman tables, quantization tables, and color conversion |

## Changes Made

### Task 1: TransferSyntax Definitions

Added two new transfer syntax definitions:

- **JPEGLossless** (1.2.840.10008.1.2.4.70): JPEG Lossless Process 14 Selection Value 1
- **JPEG2000Lossy** (1.2.840.10008.1.2.4.91): JPEG 2000 lossy compression

Updated `FromUID()` to recognize both new transfer syntaxes. CompressionType enum already had the required values.

### Task 2: JPEG Marker Parsing

Created `src/SharpDicom/Codecs/Jpeg/` directory with:

**JpegMarkers.cs**:
- All ITU-T T.81 marker constants (SOI, EOI, SOF0-SOF15, DHT, DQT, SOS, RST, APP, etc.)
- Helper methods: `IsSOF()`, `IsRST()`, `IsAPP()`, `IsLossless()`, `IsProgressive()`, `IsArithmetic()`

**JpegFrameInfo.cs**:
- `JpegFrameInfo` readonly record struct with precision, dimensions, component count
- `JpegComponentInfo` for per-component sampling factors and quantization table ID
- `TryParse()` and `TryParseWithLength()` for SOF segment parsing
- Uses `BinaryPrimitives.ReadUInt16BigEndian` for JPEG's big-endian format

### Task 3: Huffman Tables, Quantization Tables, Color Conversion

**HuffmanTable.cs**:
- ITU-T T.81 Annex K standard tables: `LuminanceDC`, `LuminanceAC`, `ChrominanceDC`, `ChrominanceAC`
- Decode structures: minCode/maxCode/valPtr for fast symbol lookup
- Encode structures: code/size tables indexed by symbol
- `DecodeSymbol()` for Huffman decoding
- `GetCode()` for Huffman encoding
- `TryParseDHT()` for parsing DHT segments

**HuffmanBitReader** (ref struct):
- Bit-by-bit reading with JPEG byte-stuffing (0xFF 0x00) handling
- RST marker skipping
- `TryReadBit()`, `TryReadBits()`, `TryReadCoefficient()` for sign extension

**QuantizationTable.cs**:
- ITU-T T.81 Annex K standard tables: `LuminanceDefault`, `ChrominanceDefault`
- `ZigZagOrder` and `InverseZigZagOrder` lookup tables
- `Quantize()` and `Dequantize()` methods
- `TryParseDQT()` for parsing DQT segments
- `CreateScaled()` for quality-based table scaling (IJG formula)

**ColorConversion.cs**:
- `YCbCrToRgb()` and `RgbToYCbCr()` using ITU-R BT.601 coefficients (0.299, 0.587, 0.114)
- 8-bit and 16-bit sample support
- JPEG 2000 `ForwardRct()` / `InverseRct()` (lossless, integer arithmetic)
- JPEG 2000 `ForwardIct()` / `InverseIct()` (lossy, floating-point)

## Verification Results

| Check | Result |
|-------|--------|
| `dotnet build` | 0 warnings, 0 errors |
| `dotnet test` | 3064 passed, 0 failed, 54 skipped |
| TransferSyntax.JPEGLossless.UID | "1.2.840.10008.1.2.4.70" |
| TransferSyntax.JPEG2000Lossy.UID | "1.2.840.10008.1.2.4.91" |
| JpegMarkers.SOF0 | 0xC0 |
| HuffmanTable.LuminanceDC | not null |

## Deviations from Plan

None - plan executed exactly as written.

## Decisions Made

### 1. HuffmanBitReader as ref struct

Using ref struct for the bit reader ensures zero-allocation bit reading on the stack. The byte-stuffing logic is encapsulated within `TryReadBit()` so callers don't need to handle it.

### 2. Standard Tables as Static Fields

ITU-T T.81 Annex K tables are pre-computed static fields rather than lazy initialization. These are small (162 bytes for DC, ~256 bytes for AC) and always needed for JPEG.

### 3. ITU-R BT.601 Coefficients

Color conversion uses the exact BT.601 coefficients per DICOM PS3.3 C.7.6.3.1.2:
- Y = 0.299R + 0.587G + 0.114B
- Clamping to [0, 255] for 8-bit or [0, maxValue] for high bit-depth

## Files Modified

| File | Change |
|------|--------|
| src/SharpDicom/Data/TransferSyntax.cs | Added JPEGLossless, JPEG2000Lossy, updated FromUID |
| src/SharpDicom/Codecs/Jpeg/JpegMarkers.cs | Created - marker constants and helpers |
| src/SharpDicom/Codecs/Jpeg/JpegFrameInfo.cs | Created - SOF segment parsing |
| src/SharpDicom/Codecs/Jpeg/HuffmanTable.cs | Created - Huffman coding with standard tables |
| src/SharpDicom/Codecs/Jpeg/QuantizationTable.cs | Created - quantization with zigzag scan |
| src/SharpDicom/Codecs/ColorConversion.cs | Created - YCbCr/RGB and RCT/ICT transforms |

## Next Phase Readiness

Plan 12-01 provides the foundation for:
- **12-02**: JPEG Baseline codec can use HuffmanTable, QuantizationTable, ColorConversion
- **12-03**: JPEG Lossless codec can use JpegMarkers, JpegFrameInfo, HuffmanTable
- **12-04/12-05**: JPEG 2000 codecs can use ColorConversion RCT/ICT transforms
