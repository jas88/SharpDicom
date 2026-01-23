---
phase: 02-basic-file-reading
plan: 01
subsystem: io
tags: [parsing, streaming, dicom-elements, span]

dependency-graph:
  requires: [01-core-data-model]
  provides: [low-level-parsing, element-headers, vr-detection]
  affects: [02-02-file-meta, 02-03-file-reader, 03-implicit-vr]

tech-stack:
  added: []
  patterns: [ref-struct, span-based-parsing, binary-primitives]

key-files:
  created:
    - src/SharpDicom/IO/DicomStreamReader.cs
    - src/SharpDicom/IO/DicomReaderOptions.cs
    - src/SharpDicom/IO/DicomReaderState.cs
    - tests/SharpDicom.Tests/IO/DicomStreamReaderTests.cs
  modified:
    - src/SharpDicom/Data/DicomVR.cs

decisions:
  - id: "02-01-001"
    title: "DicomStreamReader as ref struct"
    choice: "ref struct for zero-copy Span<T> parsing"
    rationale: "Cannot escape stack frame, ensures no heap allocation during parsing"

metrics:
  duration: "5 minutes"
  completed: "2026-01-27"
---

# Phase 2 Plan 01: DicomStreamReader Summary

Low-level Span-based DICOM element parser for zero-allocation parsing.

## What Was Built

### DicomStreamReader (ref struct)

A `ref struct` parser that operates on `ReadOnlySpan<byte>` for zero-copy element parsing.

**Key capabilities:**
- Parse element headers (tag, VR, length) from byte spans
- Support both Explicit VR (VR in stream) and Implicit VR (VR from dictionary lookup)
- Handle short VR headers (8 bytes) and long VR headers (12 bytes)
- Support little-endian and big-endian byte orders
- Track position for error reporting
- Read raw values by length

**API:**
```csharp
public ref struct DicomStreamReader
{
    public DicomStreamReader(ReadOnlySpan<byte> buffer, bool explicitVR, bool littleEndian, DicomReaderOptions? options);

    public bool TryReadElementHeader(out DicomTag tag, out DicomVR vr, out uint length);
    public bool TryReadValue(uint length, out ReadOnlySpan<byte> value);
    public bool CheckDicmPrefix();
    public void Skip(int count);
    public ushort ReadUInt16();
    public uint ReadUInt32();
    public ReadOnlySpan<byte> Peek(int count);
    public ReadOnlySpan<byte> ReadBytes(int count);

    public int Position { get; }
    public int Remaining { get; }
    public bool IsAtEnd { get; }
}
```

### DicomReaderOptions

Configuration for reading behavior with preset profiles.

| Preset | Preamble | FileMetaInfo | InvalidVR |
|--------|----------|--------------|-----------|
| Strict | Require | Require | Throw |
| Lenient | Optional | Optional | MapToUN |
| Permissive | Ignore | Ignore | Preserve |

### DicomReaderState

State machine enum for file parsing (used by higher-level readers):
- Initial, Preamble, Prefix, FileMetaInfo, Dataset, Complete, Error

### Is32BitLength Property (DicomVR)

Added `Is32BitLength` property to determine header format:
- Short VRs (16-bit length): AE, AS, AT, CS, DA, DS, DT, FL, FD, IS, LO, LT, PN, SH, SL, SS, ST, TM, UI, UL, US
- Long VRs (32-bit length): OB, OD, OF, OL, OW, SQ, UC, UN, UR, UT

## Deviations from Plan

None - plan executed exactly as written.

## Test Coverage

35 new tests covering:
- Short VR element headers (8-byte format)
- Long VR element headers (12-byte format)
- Implicit VR mode with dictionary lookup
- Unknown tags defaulting to UN VR
- Value reading with defined length
- Undefined length handling (returns false)
- Maximum length enforcement
- DICM prefix detection
- Little-endian and big-endian byte orders
- Position tracking and skip operations
- Reader options presets

## Technical Notes

### ref struct Implications

`DicomStreamReader` is a `ref struct`, which means:
- Cannot escape the stack frame (cannot be stored in fields, async methods, or lambdas)
- Cannot box to object
- Suitable for synchronous, stack-bound parsing only

Higher-level readers (e.g., `DicomFileReader`) will manage buffers and provide async support.

### Header Format Detection

Element header format depends on VR:

| VR Type | Format |
|---------|--------|
| Short VR | Tag(4) + VR(2) + Length(2) = 8 bytes |
| Long VR | Tag(4) + VR(2) + Reserved(2) + Length(4) = 12 bytes |

The `Is32BitLength` property on `DicomVR` enables this decision inline during parsing.

### Implicit VR Support

When `explicitVR=false`, the reader:
1. Reads tag (4 bytes)
2. Looks up VR from `DicomDictionary.Default`
3. Falls back to UN for unknown tags
4. Reads 4-byte length

## Commits

| Hash | Message |
|------|---------|
| 8ee0e6a | feat(02-01): implement DicomStreamReader for low-level parsing |

## Next Phase Readiness

Ready for:
- **02-02**: File Meta Information parsing (uses DicomStreamReader to parse Group 0002)
- **02-03**: DicomFileReader (wraps DicomStreamReader with async support and buffer management)
