---
phase: 07-file-writing
created: 2026-01-27
status: ready-for-research
---

# Phase 7: File Writing — Context

## Phase Goal

Write valid DICOM Part 10 files with streaming support.

## Core Decisions

### Transfer Syntax Conversion

| Decision | Choice | Rationale |
|----------|--------|-----------|
| TS conversion | Full conversion | Support any TS to any TS with codecs |
| Missing codec | Fail with clear error | Exception if codec not registered |
| Big endian | Yes, for compatibility | Support reading and writing big endian |
| Deflated TS | Yes | Support Deflated Explicit VR LE |

### Length Encoding

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Default sequence length | Undefined length | Use delimiters, simpler streaming |
| Defined length option | Full support | Option to write all sequences with defined length |
| Item length | Match sequence style | Undefined seq = undefined items |
| Nested sequences | Inherit from root | Consistent encoding throughout file |

### File Meta Generation

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Auto-generate FMI | Yes, with overrides | Auto-generate but allow explicit values |
| Implementation UID | SharpDicom UID | Unique UID for SharpDicom library |
| UID validation | Match dataset | FMI UIDs must match dataset SOPClassUID/SOPInstanceUID |
| Preamble | Zero-filled, configurable | 128 zeros by default, option to set custom |

### Stream API Design

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Write API | Both Stream and IBufferWriter | Support both for flexibility |
| Streaming write | DicomStreamWriter class | Write elements incrementally |
| Buffering | Configurable buffer size | Option to control write buffer size |
| Cancellation | CancellationToken throughout | All async write methods accept cancellation |

## Success Criteria

From ROADMAP.md:
- [ ] Written files validate with dcmtk
- [ ] Roundtrip read→write→read identical
- [ ] Streaming write to network
- [ ] Both length modes work

## Dependencies

**Requires:**
- Phase 5: Pixel data writing
- Phase 6: Private tag writing

**Provides:**
- Complete read/write cycle for Phase 8 validation

## Implementation Notes

### DicomFileWriter

```csharp
public sealed class DicomFileWriter : IAsyncDisposable
{
    public DicomFileWriter(Stream stream, DicomWriterOptions? options = null);

    public ValueTask WriteAsync(DicomFile file, CancellationToken ct = default);
    public void Write(DicomFile file);
}
```

### DicomStreamWriter (Streaming)

```csharp
public sealed class DicomStreamWriter : IAsyncDisposable
{
    public DicomStreamWriter(Stream stream, DicomWriterOptions? options = null);
    public DicomStreamWriter(IBufferWriter<byte> writer, DicomWriterOptions? options = null);

    // Write preamble + DICM
    public ValueTask WriteHeaderAsync(CancellationToken ct = default);

    // Write File Meta Information
    public ValueTask WriteFmiAsync(DicomDataset fmi, CancellationToken ct = default);

    // Write individual elements
    public ValueTask WriteElementAsync(IDicomElement element, CancellationToken ct = default);

    // Write sequence start/end
    public ValueTask BeginSequenceAsync(DicomTag tag, CancellationToken ct = default);
    public ValueTask EndSequenceAsync(CancellationToken ct = default);

    // Flush and close
    public ValueTask FlushAsync(CancellationToken ct = default);
}
```

### DicomWriterOptions

```csharp
public class DicomWriterOptions
{
    // Transfer syntax
    public TransferSyntax TransferSyntax { get; init; } = TransferSyntax.ExplicitVRLittleEndian;

    // Length encoding
    public SequenceLengthEncoding SequenceLength { get; init; } = SequenceLengthEncoding.Undefined;

    // File Meta
    public bool AutoGenerateFmi { get; init; } = true;
    public DicomUID? ImplementationClassUID { get; init; }  // null = SharpDicom default
    public string? ImplementationVersionName { get; init; } = "SHARPDICOM_1_0";

    // Preamble
    public ReadOnlyMemory<byte>? Preamble { get; init; }  // null = 128 zeros

    // Buffering
    public int BufferSize { get; init; } = 81920;  // 80KB default

    // Validation
    public bool ValidateFmiUids { get; init; } = true;
}

public enum SequenceLengthEncoding
{
    Undefined,
    Defined
}
```

### SharpDicom Implementation UID

```csharp
public static class SharpDicomInfo
{
    // Root: 1.2.826.0.1.3680043.10.1234 (example - need to register)
    public static readonly DicomUID ImplementationClassUID =
        new("1.2.826.0.1.3680043.10.1234.1");

    public const string ImplementationVersionName = "SHARPDICOM_1_0";
}
```

---

*Created: 2026-01-27*
*Status: Ready for research and planning*
