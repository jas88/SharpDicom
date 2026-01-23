---
phase: 09-rle-codec
plan: 01
subsystem: codecs
tags: [codec, interface, registry, FrozenDictionary]
dependency-graph:
  requires: [01-data-model, 02-file-reading]
  provides: [codec-interface, codec-registry, codec-types]
  affects: [09-02-rle-implementation]
tech-stack:
  added: []
  patterns: [FrozenDictionary, static-registry, thread-safe-lazy-init]
key-files:
  created:
    - src/SharpDicom/Codecs/CodecRegistry.cs
  modified: []
  note: Other codec types were already created in Phase 8
decisions:
  - id: frozen-dict
    choice: "FrozenDictionary on .NET 8+"
    reason: "40-50% faster lookups after freeze"
  - id: lazy-freeze
    choice: "Auto-freeze on first lookup"
    reason: "Transparent optimization without explicit call"
  - id: cache-invalidation
    choice: "Registration after freeze invalidates cache"
    reason: "Allows dynamic codec registration in test scenarios"
metrics:
  duration: "6 minutes"
  completed: "2026-01-27"
---

# Phase 9 Plan 01: Codec Interface and Registry Summary

IPixelDataCodec interface with FrozenDictionary registry for extensible codec plugins.

## What Was Built

### Core Codec Types (Previously Created in Phase 8)

| Type | Description |
|------|-------------|
| `IPixelDataCodec` | Main codec interface with Decode/Encode/Validate |
| `CodecCapabilities` | Describes codec abilities (encode, decode, lossy, bit depths) |
| `DecodeResult` | Success/failure with bytes written and diagnostics |
| `ValidationResult` | Compressed data validation result with issues |
| `CodecDiagnostic` | Detailed diagnostic (frame, position, message, expected/actual) |
| `PixelDataInfo` (Codecs) | Non-nullable pixel metadata for codec operations |
| `DicomCodecException` | Exception with codec context (transfer syntax, frame, position) |

### CodecRegistry (New)

Static registry with FrozenDictionary optimization:

```csharp
// Registration
CodecRegistry.Register(new RleCodec());
CodecRegistry.Register<JpegCodec>();
CodecRegistry.RegisterFromAssembly(typeof(Codecs).Assembly);

// Lookup
var codec = CodecRegistry.GetCodec(TransferSyntax.RLELossless);
bool canDecode = CodecRegistry.CanDecode(TransferSyntax.JPEGBaseline);
bool canEncode = CodecRegistry.CanEncode(TransferSyntax.JPEG2000Lossless);
```

**Thread-safety:**
- Registration protected by lock
- First lookup freezes registry (auto or explicit via `Freeze()`)
- FrozenDictionary on .NET 8+ provides lock-free reads
- Registration after freeze invalidates cache (re-freeze on next lookup)

## Test Results

| Category | Tests | Status |
|----------|-------|--------|
| CodecRegistry | 13 | PASS |
| Total (all phases) | 616 | PASS |

## Files

### Created

- `src/SharpDicom/Codecs/CodecRegistry.cs` - Static registry with FrozenDictionary
- `tests/SharpDicom.Tests/Codecs/CodecRegistryTests.cs` - 13 tests

### Already Existed (from Phase 8)

- `src/SharpDicom/Codecs/IPixelDataCodec.cs`
- `src/SharpDicom/Codecs/CodecCapabilities.cs`
- `src/SharpDicom/Codecs/DecodeResult.cs`
- `src/SharpDicom/Codecs/ValidationResult.cs`
- `src/SharpDicom/Codecs/CodecDiagnostic.cs`
- `src/SharpDicom/Codecs/PixelDataInfo.cs`
- `src/SharpDicom/Data/Exceptions/DicomCodecException.cs`

## Deviations from Plan

### Pre-existing Code

**Task 1 artifacts already existed** from Phase 8 (08-01-PLAN). The codec types were created earlier as part of validation infrastructure. Plan 09-01 proceeded with Task 2 (CodecRegistry) and Task 3 (tests) which were not yet implemented.

### Bug Fixes (Rule 1)

**1. [Rule 1 - Bug] Fixed missing System import in PixelDataInfoTests**
- Found during: Task 3 build
- Issue: `BitConverter` not in scope
- Fix: Added `using System;`
- Files: `tests/SharpDicom.Tests/Data/PixelDataInfoTests.cs`
- Commit: 8cdb1ec

**2. [Rule 1 - Bug] Fixed nullable reference warnings in FragmentParserTests**
- Found during: Task 3 build
- Issue: `Assert.Throws<T>()` returns `T?`, subsequent access triggered CS8602
- Fix: Added null-forgiving operator (`ex!.Message`)
- Files: `tests/SharpDicom.Tests/IO/FragmentParserTests.cs`
- Commit: 8cdb1ec

## Commits

| Hash | Type | Description |
|------|------|-------------|
| d1ef62d | feat | CodecRegistry with FrozenDictionary |
| 8cdb1ec | test | CodecRegistry tests and pre-existing fixes |

## Next Phase Readiness

Phase 9 Plan 02 (RLE Codec Implementation) can proceed:

- IPixelDataCodec interface defined
- CodecRegistry available for registration
- Test patterns established with MockCodec
- DicomFragmentSequence available for compressed data storage

## Success Criteria Verification

- [x] IPixelDataCodec interface defined with Decode/Encode/Validate contracts
- [x] CodecCapabilities readonly record struct describes codec abilities
- [x] DecodeResult provides success/failure with optional diagnostic
- [x] PixelDataInfo captures pixel data metadata (Codecs namespace)
- [x] DicomCodecException extends DicomException with codec context
- [x] CodecRegistry supports Register/GetCodec/CanDecode/CanEncode
- [x] CodecRegistry uses FrozenDictionary on .NET 8+
- [x] 13 tests verify CodecRegistry behavior (exceeded 10+ requirement)
- [x] Solution builds warning-free on all TFMs (1 external warning from System.Text.Encoding.CodePages)
