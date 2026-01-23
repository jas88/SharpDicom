# Phase 2: Basic File Reading — Context

## Phase Goal

Parse DICOM Part 10 files with Explicit VR Little Endian transfer syntax.

## Requirements Covered

- **FR-03.1**: Read DICOM Part 10 file structure (preamble, DICM prefix, File Meta Information)
- **FR-03.2**: Parse Explicit VR Little Endian elements
- **FR-03.3**: Extract File Meta Information (Group 0002)
- **FR-03.6**: Streaming element access via IAsyncEnumerable

## Key Decisions

### 1. Two-Layer Reader Architecture

**Decision**: Implement two layers as specified in CLAUDE.md.

| Layer | Class | Purpose |
|-------|-------|---------|
| Low-level | `DicomStreamReader` | Span<T>-based parsing, zero-copy |
| High-level | `DicomFileReader` | Async streaming, IAsyncEnumerable |

**Rationale**:
- Low-level for maximum performance (sync, Span-based)
- High-level for convenience (async, streaming)
- Both share parsing logic via internal methods

### 2. State Machine Parser

**Decision**: Use explicit state machine for element parsing.

```
States: Preamble → Prefix → FileMetaInfo → Dataset → Complete
```

**Rationale**:
- Clear state transitions
- Easy to suspend/resume for async
- Handles partial reads gracefully
- Testable state by state

### 3. DicomFile Class Design

**Decision**: Immutable DicomFile with separate FileMetaInfo and Dataset.

```csharp
public sealed class DicomFile
{
    public ReadOnlyMemory<byte> Preamble { get; }      // 128 bytes or empty
    public DicomDataset FileMetaInfo { get; }          // Group 0002
    public DicomDataset Dataset { get; }               // Main content
    public TransferSyntax TransferSyntax { get; }      // From FileMetaInfo
}
```

**Rationale**:
- Clear separation of concerns
- FileMetaInfo always Explicit VR LE (per spec)
- Dataset uses TransferSyntax from (0002,0010)

### 4. Memory Management

**Decision**: Use ArrayPool<byte> for read buffers, return Memory<byte> slices.

**Rationale**:
- Reduces GC pressure
- Elements reference pooled buffers during parsing
- ToOwned() copies to independent array when needed

### 5. Error Handling Strategy

**Decision**: Exception-based with DicomFileException hierarchy.

| Exception | When |
|-----------|------|
| DicomPreambleException | Invalid/missing preamble |
| DicomMetaInfoException | Invalid File Meta Information |
| DicomFileException | General file format errors |

**Rationale**:
- Consistent with Phase 1 exception design
- Rich context (file position, tag, etc.)
- Lenient mode catches and continues

### 6. Reader Options

**Decision**: Use DicomReaderOptions as defined in CLAUDE.md.

```csharp
public class DicomReaderOptions
{
    public FilePreambleHandling Preamble { get; init; }
    public FileMetaInfoHandling FileMetaInfo { get; init; }
    public PixelDataHandling PixelData { get; init; }
    // ... other options
}
```

**Rationale**:
- Strict/Lenient/Permissive presets
- Per-operation customization
- Extensible for future phases

### 7. Explicit VR Element Parsing

**Decision**: Parse element header based on VR type.

| VR Category | Header Format |
|-------------|---------------|
| Short VRs (AE, AS, AT, CS, DA, DS, DT, FL, FD, IS, LO, LT, PN, SH, SL, SS, ST, TM, UI, UL, US) | Tag(4) + VR(2) + Length(2) |
| Long VRs (OB, OD, OF, OL, OW, SQ, UC, UN, UR, UT) | Tag(4) + VR(2) + Reserved(2) + Length(4) |

**Rationale**: Per DICOM PS3.5 Section 7.1.2

### 8. Streaming API

**Decision**: IAsyncEnumerable<IDicomElement> for streaming access.

```csharp
public IAsyncEnumerable<IDicomElement> ReadElementsAsync(CancellationToken ct)
```

**Rationale**:
- Memory-efficient for large files
- Natural async pattern
- Caller controls when to stop

## Out of Scope (Future Phases)

- Implicit VR parsing (Phase 3)
- Sequence parsing (Phase 3)
- Character encoding (Phase 4)
- Pixel data handling options (Phase 5)

## Test Strategy

1. **Unit tests**: Each parsing state, element types
2. **Integration tests**: Real DICOM files (Explicit VR LE)
3. **Edge cases**: Truncated files, missing preamble, invalid data
4. **Performance**: Benchmark against fo-dicom baseline

## Dependencies

- Phase 1 complete: DicomTag, DicomVR, DicomElement, DicomDataset, TransferSyntax

## Success Criteria

From ROADMAP.md:
- [ ] Parse standard test files
- [ ] Stream elements via IAsyncEnumerable
- [ ] File Meta Information extracted correctly
- [ ] Benchmarks show target performance
