# Phase 7: File Writing - Research

**Researched:** 2026-01-27
**Domain:** DICOM Part 10 file writing, streaming binary output, transfer syntax encoding
**Confidence:** HIGH

## Summary

Phase 7 implements DICOM Part 10 file writing with streaming support. The implementation centers on two writer classes: `DicomFileWriter` for complete file output and `DicomStreamWriter` for incremental element-by-element streaming. Both support `IBufferWriter<byte>` for high-performance zero-copy scenarios (network streaming, Pipelines integration).

Key implementation concerns:
- File Meta Information generation with correct required tags and group length calculation
- Defined vs undefined length encoding for sequences (with consistent item encoding)
- Transfer syntax conversion requiring codec support
- Deflated transfer syntax (1.2.840.10008.1.2.1.99) compressing entire dataset

**Primary recommendation:** Use undefined length encoding by default (simpler streaming, DCMTK default behavior), with option for defined length when interoperability requires it. Support both `Stream` and `IBufferWriter<byte>` targets for flexibility.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Buffers | built-in | `IBufferWriter<T>`, `ArrayPool<T>` | .NET standard for high-perf I/O |
| System.Memory | built-in/.NET Standard 2.0 shim | `Span<T>`, `Memory<T>`, `ReadOnlySequence<T>` | Zero-copy buffer handling |
| System.IO.Compression | built-in | `DeflateStream` for deflated TS | .NET standard compression |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.IO.Pipelines | 9.0.x | `PipeWriter` implements `IBufferWriter<byte>` | Network streaming, ASP.NET Core |
| ArrayBufferWriter<T> | built-in | Simple `IBufferWriter<T>` impl | In-memory buffering |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| DeflateStream | ZLibStream | ZLibStream adds zlib wrapper - DICOM uses raw deflate per RFC 1951 |
| ArrayBufferWriter | PooledArrayBufferWriter | Lower allocations but more complexity |

## Architecture Patterns

### Recommended Project Structure
```
src/SharpDicom/IO/
    DicomFileWriter.cs       # High-level file writing
    DicomStreamWriter.cs     # Low-level streaming writer
    FileMetaInfoGenerator.cs # FMI auto-generation
    SequenceLengthCalculator.cs # Defined length calculation
    DicomWriterOptions.cs    # Configuration
```

### Pattern 1: IBufferWriter<byte> Writing

**What:** Write DICOM data directly to any buffer writer target without intermediate allocations.

**When to use:** High-performance scenarios, network streaming, Pipelines integration.

**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/standard/io/buffers
public void WriteElement(IBufferWriter<byte> writer, IDicomElement element, bool explicitVR, bool littleEndian)
{
    // Request buffer for header (max 12 bytes: tag(4) + VR(2) + reserved(2) + length(4))
    var span = writer.GetSpan(12);
    int written = 0;

    // Write tag (4 bytes)
    if (littleEndian)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span, element.Tag.Group);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], element.Tag.Element);
    }
    else
    {
        BinaryPrimitives.WriteUInt16BigEndian(span, element.Tag.Group);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], element.Tag.Element);
    }
    written = 4;

    if (explicitVR)
    {
        // Write VR (2 bytes)
        span[4] = element.VR.Char1;
        span[5] = element.VR.Char2;
        written = 6;

        if (element.VR.Is32BitLength)
        {
            // Reserved 2 bytes + 4-byte length
            span[6] = 0;
            span[7] = 0;
            if (littleEndian)
                BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)element.Length);
            else
                BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)element.Length);
            written = 12;
        }
        else
        {
            // 2-byte length
            if (littleEndian)
                BinaryPrimitives.WriteUInt16LittleEndian(span[6..], (ushort)element.Length);
            else
                BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)element.Length);
            written = 8;
        }
    }
    else
    {
        // Implicit VR: 4-byte length only
        if (littleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)element.Length);
        else
            BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)element.Length);
        written = 8;
    }

    writer.Advance(written);

    // Write value
    if (element.Length > 0)
        writer.Write(element.RawValue.Span);
}
```

### Pattern 2: Two-Pass Defined Length Calculation

**What:** Calculate sequence/item lengths before writing when defined length encoding is required.

**When to use:** When `SequenceLengthEncoding.Defined` is specified, or for interoperability with legacy systems.

**Example:**
```csharp
// Calculate total bytes for sequence including all items
public uint CalculateSequenceLength(DicomSequence sequence, bool explicitVR)
{
    uint total = 0;
    foreach (var item in sequence.Items)
    {
        // Item tag (4) + length (4)
        total += 8;
        total += CalculateDatasetLength(item, explicitVR);
        // No item delimitation item when using defined length
    }
    // No sequence delimitation item when using defined length
    return total;
}

public uint CalculateDatasetLength(DicomDataset dataset, bool explicitVR)
{
    uint total = 0;
    foreach (var element in dataset)
    {
        total += CalculateElementLength(element, explicitVR);
    }
    return total;
}

private uint CalculateElementLength(IDicomElement element, bool explicitVR)
{
    if (element is DicomSequence seq)
    {
        // Header + nested length
        uint headerLen = explicitVR ? 12u : 8u; // SQ always 32-bit length
        return headerLen + CalculateSequenceLength(seq, explicitVR);
    }

    // Regular element: header + value
    uint headerLen = explicitVR
        ? (element.VR.Is32BitLength ? 12u : 8u)
        : 8u;

    return headerLen + (uint)element.Length;
}
```

### Pattern 3: Streaming With Undefined Length

**What:** Write sequences/items with delimiter-based termination for streaming scenarios.

**When to use:** Default mode, network streaming, when total size is unknown.

**Example:**
```csharp
public void WriteSequenceUndefined(IBufferWriter<byte> writer, DicomSequence sequence, bool littleEndian)
{
    // Sequence header with undefined length (0xFFFFFFFF)
    WriteTagVRLength(writer, sequence.Tag, DicomVR.SQ, 0xFFFFFFFF, true, littleEndian);

    foreach (var item in sequence.Items)
    {
        // Item tag with undefined length
        WriteDelimiterTag(writer, DicomTag.Item, 0xFFFFFFFF, littleEndian);

        // Write item dataset
        WriteDataset(writer, item, true, littleEndian);

        // Item Delimitation Item: (FFFE,E00D) + length 0
        WriteDelimiterTag(writer, DicomTag.ItemDelimitationItem, 0, littleEndian);
    }

    // Sequence Delimitation Item: (FFFE,E0DD) + length 0
    WriteDelimiterTag(writer, DicomTag.SequenceDelimitationItem, 0, littleEndian);
}

private void WriteDelimiterTag(IBufferWriter<byte> writer, DicomTag tag, uint length, bool littleEndian)
{
    var span = writer.GetSpan(8);
    if (littleEndian)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span, tag.Group);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], tag.Element);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], length);
    }
    else
    {
        BinaryPrimitives.WriteUInt16BigEndian(span, tag.Group);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], tag.Element);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], length);
    }
    writer.Advance(8);
}
```

### Anti-Patterns to Avoid
- **Mixed length encoding:** Don't mix defined and undefined length within same file - be consistent
- **Calculating FMI group length after writing:** Calculate before writing to avoid seeking/rewriting
- **Using Stream.Seek with network streams:** IBufferWriter pattern avoids this naturally
- **Allocating per-element buffers:** Use IBufferWriter.GetSpan/Advance pattern instead

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Deflate compression | Custom deflate impl | System.IO.Compression.DeflateStream | RFC 1951 compliant, hardware accelerated |
| Buffer pooling | Custom pool | ArrayPool<byte>.Shared | Thread-safe, configurable, built-in |
| Binary encoding | Bit manipulation | BinaryPrimitives | Endianness-safe, optimized |
| Stream to IBufferWriter | Custom adapter | Community Toolkit HighPerformance | Tested, performant |

**Key insight:** DICOM file writing is mostly byte shuffling - use .NET's optimized primitives rather than custom implementations.

## Common Pitfalls

### Pitfall 1: File Meta Information Group Length Calculation
**What goes wrong:** FMI Group Length (0002,0000) must contain the byte count of ALL following Group 0002 elements, but developers calculate it incorrectly or forget to include all elements.
**Why it happens:** The length must be known before writing, requiring buffering or two-pass writing.
**How to avoid:**
1. Build FMI dataset first, calculate total serialized length of elements (0002,0001) through (0002,00xx)
2. Write (0002,0000) with calculated length
3. Write remaining FMI elements
**Warning signs:** dcmtk validation errors: "File Meta Information Group Length doesn't match actual length"

### Pitfall 2: Transfer Syntax UID vs Actual Encoding Mismatch
**What goes wrong:** Writing (0002,0010) Transfer Syntax UID that doesn't match how the dataset is actually encoded.
**Why it happens:** Forgetting to convert data when changing transfer syntax, or not updating FMI after conversion.
**How to avoid:**
1. Determine target transfer syntax first
2. If source differs, perform actual conversion (VR encoding, byte order, pixel compression)
3. Write FMI with correct Transfer Syntax UID
**Warning signs:** Files unreadable by viewers, byte order corruption, "wrong VR" errors

### Pitfall 3: Odd Value Length Padding
**What goes wrong:** Writing elements with odd-length values, violating DICOM requirement for even lengths.
**Why it happens:** Raw string values have odd lengths; forgetting to add padding byte.
**How to avoid:**
1. Check `(length & 1) == 1` for each value
2. Add appropriate padding: 0x20 (space) for string VRs, 0x00 (null) for binary VRs
3. Use DicomVRInfo.PaddingByte to get correct padding character
**Warning signs:** "Odd value length" validation errors

### Pitfall 4: Incorrect Delimiter Tags for Sequences
**What goes wrong:** Using wrong delimiter tags or wrong length values.
**Why it happens:** Confusion between Item (FFFE,E000), ItemDelimitationItem (FFFE,E00D), and SequenceDelimitationItem (FFFE,E0DD).
**How to avoid:**
- Item: (FFFE,E000) - starts each item, length = item length OR 0xFFFFFFFF
- ItemDelimitationItem: (FFFE,E00D) - ends undefined-length items, length = 0x00000000
- SequenceDelimitationItem: (FFFE,E0DD) - ends undefined-length sequences, length = 0x00000000
**Warning signs:** Corrupt sequence parsing, "unexpected end of sequence" errors

### Pitfall 5: Deflated Transfer Syntax Scope
**What goes wrong:** Deflating only pixel data instead of entire dataset.
**Why it happens:** Confusion with encapsulated pixel data compression (JPEG etc.).
**How to avoid:**
- Deflated TS (1.2.840.10008.1.2.1.99) compresses the ENTIRE dataset byte stream
- File structure: Preamble (128) + DICM (4) + FMI (Explicit VR LE, uncompressed) + DEFLATED(Dataset)
- Dataset is first encoded as Explicit VR LE, then deflate-compressed
**Warning signs:** Files claiming deflated TS but having readable dataset

## Code Examples

### File Meta Information Generation

```csharp
// Source: https://dicom.nema.org/medical/dicom/current/output/chtml/part10/chapter_7.html
public static class FileMetaInfoGenerator
{
    public static DicomDataset Generate(
        DicomDataset dataset,
        TransferSyntax transferSyntax,
        DicomWriterOptions options)
    {
        var fmi = new DicomDataset();

        // (0002,0001) File Meta Information Version - Type 1
        // Value: 0x00 0x01 (version 1)
        fmi.Add(new DicomBinaryElement(
            DicomTag.FileMetaInformationVersion,
            DicomVR.OB,
            new byte[] { 0x00, 0x01 }));

        // (0002,0002) Media Storage SOP Class UID - Type 1
        var sopClassUid = dataset.GetString(DicomTag.SOPClassUID)
            ?? throw new DicomFileException("Dataset missing SOP Class UID (0008,0016)");
        fmi.Add(new DicomStringElement(
            DicomTag.MediaStorageSOPClassUID,
            DicomVR.UI,
            PadUid(sopClassUid)));

        // (0002,0003) Media Storage SOP Instance UID - Type 1
        var sopInstanceUid = dataset.GetString(DicomTag.SOPInstanceUID)
            ?? throw new DicomFileException("Dataset missing SOP Instance UID (0008,0018)");
        fmi.Add(new DicomStringElement(
            DicomTag.MediaStorageSOPInstanceUID,
            DicomVR.UI,
            PadUid(sopInstanceUid)));

        // (0002,0010) Transfer Syntax UID - Type 1
        fmi.Add(new DicomStringElement(
            DicomTag.TransferSyntaxUID,
            DicomVR.UI,
            PadUid(transferSyntax.UID.ToString())));

        // (0002,0012) Implementation Class UID - Type 1
        var implUid = options.ImplementationClassUID?.ToString()
            ?? SharpDicomInfo.ImplementationClassUID.ToString();
        fmi.Add(new DicomStringElement(
            DicomTag.ImplementationClassUID,
            DicomVR.UI,
            PadUid(implUid)));

        // (0002,0013) Implementation Version Name - Type 3 (optional)
        if (!string.IsNullOrEmpty(options.ImplementationVersionName))
        {
            fmi.Add(new DicomStringElement(
                DicomTag.ImplementationVersionName,
                DicomVR.SH,
                PadString(options.ImplementationVersionName)));
        }

        // Calculate and add (0002,0000) File Meta Information Group Length
        // Length = sum of all following Group 0002 elements (excluding 0002,0000 itself)
        uint groupLength = CalculateFmiGroupLength(fmi);
        var groupLengthBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(groupLengthBytes, groupLength);
        fmi.Add(new DicomNumericElement(
            DicomTag.FileMetaInformationGroupLength,
            DicomVR.UL,
            groupLengthBytes));

        return fmi;
    }

    private static uint CalculateFmiGroupLength(DicomDataset fmi)
    {
        uint length = 0;
        foreach (var element in fmi)
        {
            if (element.Tag == DicomTag.FileMetaInformationGroupLength)
                continue; // Don't include self

            // FMI is always Explicit VR Little Endian
            // Header: tag(4) + VR(2) + length(2 or 4+2reserved)
            var vrInfo = DicomVRInfo.GetInfo(element.VR);
            length += 4u; // tag
            length += 2u; // VR
            if (vrInfo.Is16BitLength)
            {
                length += 2u; // 16-bit length
            }
            else
            {
                length += 2u; // reserved
                length += 4u; // 32-bit length
            }
            length += (uint)element.Length;
        }
        return length;
    }

    private static byte[] PadUid(string uid)
    {
        // UI VR padding is 0x00 (null), must be even length
        var trimmed = uid.TrimEnd('\0', ' ');
        if ((trimmed.Length & 1) == 1)
            trimmed += '\0';
        return Encoding.ASCII.GetBytes(trimmed);
    }

    private static byte[] PadString(string value)
    {
        // String VR padding is 0x20 (space), must be even length
        if ((value.Length & 1) == 1)
            value += ' ';
        return Encoding.ASCII.GetBytes(value);
    }
}
```

### Part 10 File Writing

```csharp
// Source: DICOM Part 10 Chapter 7
public sealed class DicomFileWriter : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly DicomWriterOptions _options;
    private readonly IBufferWriter<byte> _bufferWriter;
    private bool _disposed;

    public DicomFileWriter(Stream stream, DicomWriterOptions? options = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _options = options ?? DicomWriterOptions.Default;
        _bufferWriter = new StreamBufferWriter(stream, _options.BufferSize);
    }

    public async ValueTask WriteAsync(DicomFile file, CancellationToken ct = default)
    {
        // 1. Write 128-byte preamble
        await WritePreambleAsync(file.Preamble, ct);

        // 2. Write "DICM" prefix
        await WriteDicmPrefixAsync(ct);

        // 3. Generate or use provided FMI
        var fmi = _options.AutoGenerateFmi
            ? FileMetaInfoGenerator.Generate(file.Dataset, file.TransferSyntax, _options)
            : file.FileMetaInfo;

        // 4. Write FMI (always Explicit VR Little Endian)
        await WriteDatasetAsync(fmi, explicitVR: true, littleEndian: true, ct);

        // 5. Write dataset in target transfer syntax
        if (file.TransferSyntax == TransferSyntax.DeflatedExplicitVRLittleEndian)
        {
            await WriteDeflatedDatasetAsync(file.Dataset, ct);
        }
        else
        {
            await WriteDatasetAsync(
                file.Dataset,
                file.TransferSyntax.IsExplicitVR,
                file.TransferSyntax.IsLittleEndian,
                ct);
        }

        await _stream.FlushAsync(ct);
    }

    private async ValueTask WritePreambleAsync(ReadOnlyMemory<byte> preamble, CancellationToken ct)
    {
        if (preamble.IsEmpty)
        {
            // Write 128 zero bytes
            var zeros = new byte[128];
            await _stream.WriteAsync(zeros, ct);
        }
        else if (preamble.Length == 128)
        {
            await _stream.WriteAsync(preamble, ct);
        }
        else
        {
            throw new DicomFileException($"Preamble must be exactly 128 bytes, got {preamble.Length}");
        }
    }

    private async ValueTask WriteDicmPrefixAsync(CancellationToken ct)
    {
        await _stream.WriteAsync("DICM"u8.ToArray(), ct);
    }

    private async ValueTask WriteDeflatedDatasetAsync(DicomDataset dataset, CancellationToken ct)
    {
        // Encode dataset as Explicit VR Little Endian to memory
        using var uncompressed = new MemoryStream();
        var tempWriter = new DicomFileWriter(uncompressed, _options);
        await tempWriter.WriteDatasetAsync(dataset, explicitVR: true, littleEndian: true, ct);
        var encodedData = uncompressed.ToArray();

        // Deflate the encoded data
        using var deflateStream = new DeflateStream(_stream, CompressionLevel.Optimal, leaveOpen: true);
        await deflateStream.WriteAsync(encodedData, ct);
        await deflateStream.FlushAsync(ct);

        // Add padding if odd length
        var deflatedLength = _stream.Position;
        if ((deflatedLength & 1) == 1)
        {
            await _stream.WriteAsync(new byte[] { 0x00 }, ct);
        }
    }

    // ... WriteDatasetAsync implementation
}
```

### Sequence Writing With Both Length Modes

```csharp
// Source: https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.html
public void WriteSequence(
    IBufferWriter<byte> writer,
    DicomSequence sequence,
    bool explicitVR,
    bool littleEndian,
    SequenceLengthEncoding lengthMode)
{
    if (lengthMode == SequenceLengthEncoding.Defined)
    {
        // Calculate total sequence length first
        uint seqLength = CalculateSequenceLength(sequence, explicitVR);
        WriteTagVRLength(writer, sequence.Tag, DicomVR.SQ, seqLength, explicitVR, littleEndian);

        foreach (var item in sequence.Items)
        {
            uint itemLength = CalculateDatasetLength(item, explicitVR);
            WriteItemHeader(writer, itemLength, littleEndian);
            WriteDataset(writer, item, explicitVR, littleEndian, lengthMode);
            // No Item Delimitation Item for defined length
        }
        // No Sequence Delimitation Item for defined length
    }
    else // Undefined length
    {
        // Write sequence header with undefined length (0xFFFFFFFF)
        WriteTagVRLength(writer, sequence.Tag, DicomVR.SQ, 0xFFFFFFFF, explicitVR, littleEndian);

        foreach (var item in sequence.Items)
        {
            // Write item with undefined length
            WriteItemHeader(writer, 0xFFFFFFFF, littleEndian);
            WriteDataset(writer, item, explicitVR, littleEndian, lengthMode);

            // Item Delimitation Item: (FFFE,E00D) length=0
            WriteDelimiter(writer, DicomTag.ItemDelimitationItem, littleEndian);
        }

        // Sequence Delimitation Item: (FFFE,E0DD) length=0
        WriteDelimiter(writer, DicomTag.SequenceDelimitationItem, littleEndian);
    }
}

private void WriteItemHeader(IBufferWriter<byte> writer, uint length, bool littleEndian)
{
    var span = writer.GetSpan(8);
    // Item tag: (FFFE,E000)
    if (littleEndian)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span, 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 0xE000);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], length);
    }
    else
    {
        BinaryPrimitives.WriteUInt16BigEndian(span, 0xFFFE);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 0xE000);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], length);
    }
    writer.Advance(8);
}

private void WriteDelimiter(IBufferWriter<byte> writer, DicomTag delimiterTag, bool littleEndian)
{
    var span = writer.GetSpan(8);
    if (littleEndian)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(span, delimiterTag.Group);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], delimiterTag.Element);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0); // Always 0 for delimiters
    }
    else
    {
        BinaryPrimitives.WriteUInt16BigEndian(span, delimiterTag.Group);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], delimiterTag.Element);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], 0);
    }
    writer.Advance(8);
}
```

## DICOM Part 10 File Structure Reference

### Complete File Layout

```
Offset  | Size    | Content
--------|---------|--------------------------------------------------
0       | 128     | Preamble (typically zeros, application-defined)
128     | 4       | DICM prefix (ASCII "DICM")
132     | varies  | File Meta Information (Group 0002)
                  |   - Always Explicit VR Little Endian
                  |   - (0002,0000) Group Length required
                  |   - (0002,0001) Version: 0x00 0x01
                  |   - (0002,0002) Media Storage SOP Class UID
                  |   - (0002,0003) Media Storage SOP Instance UID
                  |   - (0002,0010) Transfer Syntax UID
                  |   - (0002,0012) Implementation Class UID
                  |   - (0002,0013) Implementation Version Name (optional)
varies  | varies  | Dataset (per Transfer Syntax from 0002,0010)
```

### Element Header Formats

**Explicit VR Little Endian (16-bit length VRs):**
```
Offset | Size | Content
-------|------|------------------
0      | 2    | Group (LE)
2      | 2    | Element (LE)
4      | 2    | VR (ASCII)
6      | 2    | Length (LE, 16-bit)
8      | n    | Value (n bytes)
```

**Explicit VR Little Endian (32-bit length VRs: OB, OD, OF, OL, OV, OW, SQ, SV, UC, UN, UR, UT, UV):**
```
Offset | Size | Content
-------|------|------------------
0      | 2    | Group (LE)
2      | 2    | Element (LE)
4      | 2    | VR (ASCII)
6      | 2    | Reserved (0x0000)
8      | 4    | Length (LE, 32-bit)
12     | n    | Value (n bytes)
```

**Implicit VR Little Endian:**
```
Offset | Size | Content
-------|------|------------------
0      | 2    | Group (LE)
2      | 2    | Element (LE)
4      | 4    | Length (LE, 32-bit)
8      | n    | Value (n bytes)
```

### Sequence/Item Delimiter Tags

| Tag | Name | Length Field | Usage |
|-----|------|--------------|-------|
| (FFFE,E000) | Item | Item length or 0xFFFFFFFF | Starts each item |
| (FFFE,E00D) | Item Delimitation Item | Always 0x00000000 | Ends undefined-length items |
| (FFFE,E0DD) | Sequence Delimitation Item | Always 0x00000000 | Ends undefined-length sequences |

**Important:** These tags use implicit VR format even in Explicit VR transfer syntaxes (no VR field, just tag + 4-byte length).

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| DeflateStream only | DeflateStream + ZLibStream | .NET 6+ | ZLibStream for zlib format, DeflateStream for raw deflate (DICOM uses raw) |
| Stream.Write + seeking | IBufferWriter pattern | .NET Core 2.1+ | Zero-copy network streaming, Pipelines integration |
| Custom binary writers | BinaryPrimitives | .NET Core 2.1+ | Hardware-optimized, endianness-safe |

**Deprecated/outdated:**
- BinaryWriter class: Still works but allocates; prefer BinaryPrimitives for hot paths
- Sync-only writing: All new APIs should be async-first with sync wrappers
- FileStream without FileOptions.Asynchronous: Loses async benefits on Windows

## Open Questions

1. **Private Tag Writing Order**
   - What we know: Private creator must precede private data elements
   - What's unclear: Should writer auto-sort or require pre-sorted input?
   - Recommendation: DicomDataset enumerator already sorts by tag; rely on this

2. **Transfer Syntax Conversion Codec Errors**
   - What we know: Some conversions require codecs (e.g., uncompressed to JPEG)
   - What's unclear: Best error handling for missing codecs
   - Recommendation: Throw `DicomCodecException` with clear message; don't silently fall back

3. **Encapsulated Pixel Data Basic Offset Table**
   - What we know: BOT is optional but improves random frame access
   - What's unclear: Should writer auto-generate BOT?
   - Recommendation: Phase 5 decides; Phase 7 writes whatever BOT is provided

## Sources

### Primary (HIGH confidence)
- [DICOM Part 10 Chapter 7 - File Meta Information](https://dicom.nema.org/medical/dicom/current/output/chtml/part10/chapter_7.html)
- [DICOM Part 5 Section 7.5 - Nesting of Data Sets](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.html)
- [DICOM Part 5 Section 7.5.2 - Delimitation of Sequence of Items](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_7.5.2.html)
- [DICOM Part 5 Section A.5 - Deflated Explicit VR Little Endian](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_A.5.html)
- [Microsoft Learn - System.Buffers](https://learn.microsoft.com/en-us/dotnet/standard/io/buffers)
- [Microsoft Learn - DeflateStream Class](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.deflatestream?view=net-8.0)

### Secondary (MEDIUM confidence)
- [MemoryPack serializer design](https://neuecc.medium.com/how-to-make-the-fastest-net-serializer-with-net-7-c-11-case-of-memorypack-ad28c0366516) - IBufferWriter patterns
- [Pooling IBufferWriter](https://blog.ladeak.net/posts/pooling-bufferwriter) - Performance patterns
- [Medical Connections - Transfer Syntax](https://www.medicalconnections.co.uk/kb/Transfer-Syntax) - TS overview

### Tertiary (LOW confidence)
- [DCMTK Forum - Sequence Length](https://forum.dcmtk.org/viewtopic.php?t=4662) - Community patterns

## Metadata

**Confidence breakdown:**
- File structure: HIGH - Official DICOM standard
- IBufferWriter patterns: HIGH - Microsoft documentation
- Deflated TS: HIGH - Official DICOM standard
- Length encoding trade-offs: MEDIUM - Community consensus

**Research date:** 2026-01-27
**Valid until:** 60 days (stable DICOM standard, stable .NET APIs)
