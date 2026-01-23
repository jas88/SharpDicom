# Phase 5: Pixel Data & Lazy Loading - Research

**Researched:** 2026-01-27
**Domain:** DICOM Pixel Data Encoding, Lazy Loading, Large Binary Handling
**Confidence:** HIGH

## Summary

This research covers DICOM pixel data encoding (native vs encapsulated), fragment structure for compressed data, offset tables (Basic and Extended), multi-frame organization, and lazy loading patterns in .NET. The findings are based on the official DICOM PS3.5 standard, existing implementations (fo-dicom, dcmtk, pydicom), and .NET best practices for large binary handling.

DICOM pixel data comes in two fundamental forms: Native (uncompressed) and Encapsulated (compressed). Native format stores pixels as a contiguous block where frame positions can be calculated from dimensions. Encapsulated format uses a fragment-based structure with Item tags, requiring offset tables for random frame access. The Extended Offset Table (64-bit) was introduced to handle files larger than 4GB.

**Primary recommendation:** Implement a unified pixel data element type that abstracts native vs encapsulated differences, with configurable loading strategies (immediate, lazy, skip) and support for both Basic and Extended Offset Tables.

## Standard Stack

### Core (No additional dependencies for Phase 5)

| Component | Purpose | Notes |
|-----------|---------|-------|
| `ReadOnlyMemory<byte>` | Raw pixel data storage | Already in use |
| `MemoryMappedFile` | Large file lazy loading | Built into .NET |
| `System.IO.Pipelines` | Streaming parsing | Optional, already available |
| `ArrayPool<byte>` | Buffer pooling | Already in use |

### Supporting (Already in project)

| Component | Purpose | When to Use |
|-----------|---------|-------------|
| `DicomFragmentSequence` | Fragment storage | Already exists, needs extension |
| `DicomReaderOptions` | Loading configuration | Already exists, needs extension |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `MemoryMappedFile` | Keep `FileStream` open | MMF better for random access; FileStream simpler |
| Custom offset parsing | Use existing libraries | Control vs convenience - custom needed for DICOM specifics |
| Span-based frame access | Array allocation | Span is zero-copy but can't escape stack |

## Architecture Patterns

### Recommended Project Structure

```
src/SharpDicom/
├── Data/
│   ├── DicomFragmentSequence.cs      # Extend existing
│   ├── DicomPixelDataElement.cs      # NEW: Unified pixel data wrapper
│   └── PixelDataInfo.cs              # NEW: Metadata struct
├── IO/
│   ├── PixelDataReader.cs            # NEW: Parse native/encapsulated
│   ├── FragmentParser.cs             # NEW: Fragment sequence parsing
│   ├── LazyPixelDataSource.cs        # NEW: Lazy loading abstraction
│   └── DicomReaderOptions.cs         # Extend existing
└── Internal/
    └── OffsetTableParser.cs          # NEW: BOT/EOT parsing
```

### Pattern 1: Pixel Data Element Hierarchy

**What:** A unified pixel data element type that handles both native and encapsulated formats.

**When to use:** Always for pixel data access - provides consistent API regardless of encoding.

**Example:**
```csharp
// Source: CONTEXT.md design decisions
public sealed class DicomPixelDataElement : IDicomElement
{
    public DicomTag Tag => DicomTag.PixelData;
    public DicomVR VR { get; }  // OB or OW based on BitsAllocated

    // Metadata (from dataset context)
    public ushort Rows { get; }
    public ushort Columns { get; }
    public ushort BitsAllocated { get; }
    public int NumberOfFrames { get; }
    public bool IsEncapsulated { get; }

    // State
    public PixelDataLoadState LoadState { get; }  // NotLoaded, Loading, Loaded, Failed

    // Access
    public ReadOnlyMemory<byte> RawBytes { get; }
    public ReadOnlySpan<byte> GetFrameSpan(int frameIndex);
    public T[] GetFrame<T>(int frameIndex) where T : unmanaged;

    // Streaming
    public ValueTask CopyToAsync(Stream destination, CancellationToken ct = default);
    public ValueTask<ReadOnlyMemory<byte>> LoadAsync(CancellationToken ct = default);
}

public enum PixelDataLoadState
{
    NotLoaded,    // Skipped during initial parse
    Loading,      // Async load in progress
    Loaded,       // Data available in memory
    Failed        // Load failed, error stored
}
```

### Pattern 2: Fragment Sequence Structure

**What:** Encapsulated pixel data uses a sequence of fragments with an optional offset table.

**When to use:** When parsing compressed transfer syntaxes (JPEG, RLE, etc.).

**Example:**
```csharp
// Source: DICOM PS3.5 Section A.4
public sealed class DicomFragmentSequence : IDicomElement
{
    public DicomTag Tag { get; }
    public DicomVR VR { get; }  // Always OB for encapsulated

    // Basic Offset Table (32-bit offsets, may be empty)
    public ReadOnlyMemory<byte> BasicOffsetTable { get; }
    public IReadOnlyList<uint> ParsedBasicOffsets { get; }

    // Extended Offset Table (7FE0,0001) - 64-bit offsets
    public ReadOnlyMemory<byte> ExtendedOffsetTable { get; }
    public IReadOnlyList<ulong> ParsedExtendedOffsets { get; }

    // Extended Offset Table Lengths (7FE0,0002)
    public ReadOnlyMemory<byte> ExtendedOffsetLengths { get; }
    public IReadOnlyList<ulong> ParsedExtendedLengths { get; }

    // Fragment data
    public IReadOnlyList<ReadOnlyMemory<byte>> Fragments { get; }

    public int FragmentCount { get; }
    public long TotalSize { get; }

    // Frame access (uses offset tables if available)
    public ReadOnlyMemory<byte> GetFrameFragments(int frameIndex);
}
```

### Pattern 3: Lazy Loading Source Abstraction

**What:** Abstract the data source to support immediate, lazy, and deferred loading.

**When to use:** When configuring how pixel data is loaded during file parsing.

**Example:**
```csharp
// Source: Phase 5 design decisions
public interface IPixelDataSource : IDisposable
{
    bool IsLoaded { get; }
    long Length { get; }

    ReadOnlyMemory<byte> GetData();
    ValueTask<ReadOnlyMemory<byte>> GetDataAsync(CancellationToken ct = default);

    // For streaming without full memory load
    ValueTask CopyToAsync(Stream destination, CancellationToken ct = default);
}

// Immediate loading - data already in memory
public sealed class ImmediatePixelDataSource : IPixelDataSource
{
    private readonly ReadOnlyMemory<byte> _data;
    public bool IsLoaded => true;
    // ...
}

// Lazy loading from seekable stream
public sealed class LazyPixelDataSource : IPixelDataSource
{
    private readonly Stream _stream;
    private readonly long _offset;
    private readonly long _length;
    private ReadOnlyMemory<byte>? _cached;
    // ...
}

// Memory-mapped file source for very large data
public sealed class MemoryMappedPixelDataSource : IPixelDataSource
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    // ...
}
```

### Anti-Patterns to Avoid

- **Loading all frames into separate arrays:** Use slicing into a single contiguous buffer instead
- **Assuming offset table is always present:** Must handle empty Basic Offset Table gracefully
- **Ignoring stream position after skipping:** Track exact byte positions for lazy loading
- **Blocking on async in sync context:** Use ConfigureAwait(false) and avoid .Result/.Wait()
- **Not validating frame index bounds:** Always check against NumberOfFrames
- **Ignoring endianness for native pixel data:** Big endian transfer syntaxes require byte swapping

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Large file partial access | Custom seekable cache | `MemoryMappedFile` | OS handles paging, virtual memory |
| Buffer pooling | Fixed buffers | `ArrayPool<byte>.Shared` | Built-in, thread-safe |
| Async stream copying | Manual byte loops | `Stream.CopyToAsync` | Optimized, handles cancellation |
| Binary reading | Manual pointer math | `BinaryPrimitives` | Safe, handles endianness |

**Key insight:** The complexity in pixel data handling is in the DICOM-specific structure (fragments, offset tables, VR determination), not in basic I/O operations. Use .NET primitives for I/O, focus custom code on DICOM semantics.

## Common Pitfalls

### Pitfall 1: Empty Basic Offset Table Handling

**What goes wrong:** Assuming empty BOT means single-frame or error, when it's actually valid for multi-frame.

**Why it happens:** Specification allows empty BOT; decoders must accept it.

**How to avoid:** When BOT is empty and parsing multi-frame:
1. If Extended Offset Table present, use that
2. If single frame, offset is 0
3. If multi-frame with no offsets, must decode sequentially to find frame boundaries

**Warning signs:** `IndexOutOfRangeException` when accessing frames > 0.

### Pitfall 2: Fragment vs Frame Confusion

**What goes wrong:** Treating fragments as frames (1:1 mapping).

**Why it happens:** Often they do match, but spec allows one frame to span multiple fragments.

**How to avoid:** Use offset tables to identify frame boundaries. Fragment index != frame index.

**Warning signs:** Corrupted decompressed images, "invalid JPEG header" errors mid-frame.

### Pitfall 3: VR Determination for Pixel Data (7FE0,0010)

**What goes wrong:** Using wrong VR leads to incorrect length parsing.

**Why it happens:** Pixel Data has multi-VR (OB or OW), context-dependent.

**How to avoid:**
- **Encapsulated:** Always OB
- **Native with BitsAllocated <= 8:** OB
- **Native with BitsAllocated > 8:** OW

**Warning signs:** Length mismatches, parser out of sync.

### Pitfall 4: Stream Disposed During Lazy Load

**What goes wrong:** `ObjectDisposedException` when accessing pixel data after file close.

**Why it happens:** Lazy loading keeps stream reference, but stream is disposed.

**How to avoid:**
- Track source stream lifetime in `DicomPixelDataElement`
- Throw descriptive error on access after dispose
- Provide `ToOwned()` method that forces immediate load

**Warning signs:** Intermittent exceptions on pixel data access.

### Pitfall 5: 32-bit Offset Table Overflow

**What goes wrong:** Offset table values wrap around or truncate for files > 4GB.

**Why it happens:** Basic Offset Table uses 32-bit offsets (max ~4GB).

**How to avoid:** Check for Extended Offset Table (7FE0,0001) first. If file size > 4GB and no EOT, warn user.

**Warning signs:** Negative-looking offsets, frames appearing at wrong positions.

### Pitfall 6: Big Endian Pixel Data

**What goes wrong:** Pixel values appear inverted or corrupt.

**Why it happens:** Explicit VR Big Endian (1.2.840.10008.1.2.2) uses big endian for pixel values.

**How to avoid:** Check `TransferSyntax.IsLittleEndian` and byte-swap if needed during access.

**Warning signs:** Image appears as negative or has wrong intensity values.

## Code Examples

### Example 1: Parsing Encapsulated Pixel Data Structure

```csharp
// Source: DICOM PS3.5 Section A.4 + PS3.3 C.7.6.3

/// <summary>
/// Encapsulated pixel data byte layout:
///
/// [Item Tag (FFFE,E000)] [Length = n] [Basic Offset Table: n bytes]
/// [Item Tag (FFFE,E000)] [Length = m1] [Fragment 1: m1 bytes]
/// [Item Tag (FFFE,E000)] [Length = m2] [Fragment 2: m2 bytes]
/// ...
/// [Seq Delim Tag (FFFE,E0DD)] [Length = 0]
/// </summary>
public static DicomFragmentSequence ParseEncapsulated(
    ReadOnlySpan<byte> data,
    DicomTag pixelDataTag,
    DicomVR vr)
{
    const uint ItemTag = 0xFFFEE000;        // (FFFE,E000)
    const uint SeqDelimTag = 0xFFFEE0DD;    // (FFFE,E0DD)

    int pos = 0;

    // First item is always Basic Offset Table
    uint botTag = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos));
    if (botTag != ItemTag)
        throw new DicomDataException("Expected Item tag for Basic Offset Table");
    pos += 4;

    uint botLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos));
    pos += 4;

    var basicOffsetTable = data.Slice(pos, (int)botLength).ToArray();
    pos += (int)botLength;

    // Parse remaining items as fragments
    var fragments = new List<ReadOnlyMemory<byte>>();

    while (pos + 8 <= data.Length)
    {
        uint tag = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos));
        pos += 4;

        if (tag == SeqDelimTag)
        {
            // Sequence Delimitation Item - skip length (should be 0)
            pos += 4;
            break;
        }

        if (tag != ItemTag)
            throw new DicomDataException($"Unexpected tag in fragment sequence: {tag:X8}");

        uint fragLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos));
        pos += 4;

        fragments.Add(data.Slice(pos, (int)fragLength).ToArray());
        pos += (int)fragLength;
    }

    return new DicomFragmentSequence(pixelDataTag, vr, basicOffsetTable, fragments);
}
```

### Example 2: Parsing Basic Offset Table

```csharp
// Source: DICOM PS3.5 Section A.4

/// <summary>
/// Basic Offset Table format:
/// - Empty: Length = 0 (valid, no offsets provided)
/// - Single frame: 00 00 00 00 (offset 0)
/// - Multi-frame: offset1 (32-bit LE) | offset2 | ... | offsetN
/// </summary>
public static uint[] ParseBasicOffsetTable(ReadOnlySpan<byte> botData)
{
    if (botData.IsEmpty)
        return Array.Empty<uint>();

    int count = botData.Length / 4;
    var offsets = new uint[count];

    for (int i = 0; i < count; i++)
    {
        offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(
            botData.Slice(i * 4, 4));
    }

    return offsets;
}

/// <summary>
/// Extended Offset Table format (7FE0,0001):
/// - 64-bit unsigned integers (little endian)
/// - Cannot be empty if present
/// - First offset is always 0
/// </summary>
public static ulong[] ParseExtendedOffsetTable(ReadOnlySpan<byte> eotData)
{
    if (eotData.IsEmpty)
        throw new DicomDataException("Extended Offset Table cannot be empty");

    int count = eotData.Length / 8;
    var offsets = new ulong[count];

    for (int i = 0; i < count; i++)
    {
        offsets[i] = BinaryPrimitives.ReadUInt64LittleEndian(
            eotData.Slice(i * 8, 8));
    }

    return offsets;
}
```

### Example 3: Calculating Native Frame Size

```csharp
// Source: DICOM PS3.5 Section 8.2

/// <summary>
/// For native (uncompressed) pixel data, frame positions are calculated:
/// Frame Size = Rows x Columns x SamplesPerPixel x BytesPerSample
/// BytesPerSample = ceil(BitsAllocated / 8)
/// </summary>
public readonly struct PixelDataInfo
{
    public ushort Rows { get; init; }
    public ushort Columns { get; init; }
    public ushort BitsAllocated { get; init; }
    public ushort BitsStored { get; init; }
    public ushort HighBit { get; init; }
    public ushort SamplesPerPixel { get; init; }
    public int NumberOfFrames { get; init; }
    public ushort PlanarConfiguration { get; init; }
    public ushort PixelRepresentation { get; init; }  // 0=unsigned, 1=signed

    public int BytesPerSample => (BitsAllocated + 7) / 8;

    public int FrameSize => Rows * Columns * SamplesPerPixel * BytesPerSample;

    public long TotalSize => (long)FrameSize * NumberOfFrames;

    public long GetFrameOffset(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= NumberOfFrames)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        return (long)frameIndex * FrameSize;
    }

    public static PixelDataInfo FromDataset(DicomDataset dataset)
    {
        return new PixelDataInfo
        {
            Rows = dataset.GetUInt16(DicomTag.Rows) ?? 0,
            Columns = dataset.GetUInt16(DicomTag.Columns) ?? 0,
            BitsAllocated = dataset.GetUInt16(DicomTag.BitsAllocated) ?? 16,
            BitsStored = dataset.GetUInt16(DicomTag.BitsStored) ?? 16,
            HighBit = dataset.GetUInt16(DicomTag.HighBit) ?? 15,
            SamplesPerPixel = dataset.GetUInt16(DicomTag.SamplesPerPixel) ?? 1,
            NumberOfFrames = dataset.GetInt32(DicomTag.NumberOfFrames) ?? 1,
            PlanarConfiguration = dataset.GetUInt16(DicomTag.PlanarConfiguration) ?? 0,
            PixelRepresentation = dataset.GetUInt16(DicomTag.PixelRepresentation) ?? 0
        };
    }
}
```

### Example 4: Lazy Loading with Memory-Mapped File

```csharp
// Source: .NET documentation + Phase 5 design decisions

public sealed class MemoryMappedPixelDataSource : IPixelDataSource, IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _offset;
    private readonly long _length;
    private bool _disposed;

    public bool IsLoaded => true;  // Memory-mapped is always "available"
    public long Length => _length;

    public MemoryMappedPixelDataSource(string filePath, long offset, long length)
    {
        _offset = offset;
        _length = length;

        // Create memory-mapped file from existing file
        _mmf = MemoryMappedFile.CreateFromFile(
            filePath,
            FileMode.Open,
            mapName: null,
            capacity: 0,  // Use file size
            access: MemoryMappedFileAccess.Read);

        // Create view for the pixel data region only
        _accessor = _mmf.CreateViewAccessor(
            offset,
            length,
            MemoryMappedFileAccess.Read);
    }

    public ReadOnlyMemory<byte> GetData()
    {
        ThrowIfDisposed();

        // For Memory<byte> from MMF, we need to copy
        // (MMF doesn't directly expose Memory<byte>)
        var buffer = new byte[_length];
        _accessor.ReadArray(0, buffer, 0, (int)_length);
        return buffer;
    }

    public unsafe ReadOnlySpan<byte> GetDataSpan()
    {
        ThrowIfDisposed();

        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        try
        {
            return new ReadOnlySpan<byte>(ptr + _accessor.PointerOffset, (int)_length);
        }
        finally
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    public ValueTask<ReadOnlyMemory<byte>> GetDataAsync(CancellationToken ct = default)
    {
        // MMF is synchronous - could use Task.Run for true async if needed
        return new ValueTask<ReadOnlyMemory<byte>>(GetData());
    }

    public async ValueTask CopyToAsync(Stream destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        const int BufferSize = 81920;  // 80KB chunks
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long remaining = _length;
            long position = 0;

            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();

                int toRead = (int)Math.Min(remaining, BufferSize);
                _accessor.ReadArray(position, buffer, 0, toRead);

                await destination.WriteAsync(buffer.AsMemory(0, toRead), ct)
                    .ConfigureAwait(false);

                position += toRead;
                remaining -= toRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemoryMappedPixelDataSource));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _accessor.Dispose();
            _mmf.Dispose();
            _disposed = true;
        }
    }
}
```

### Example 5: PixelDataHandling Options Integration

```csharp
// Source: Phase 5 CONTEXT.md decisions

public enum PixelDataHandling
{
    /// <summary>Load pixel data into memory immediately during file parse.</summary>
    LoadInMemory,

    /// <summary>Keep stream reference, load on first access (requires seekable stream).</summary>
    LazyLoad,

    /// <summary>Skip pixel data entirely (metadata-only use case).</summary>
    Skip,

    /// <summary>Let callback decide per-instance based on context.</summary>
    Callback
}

public sealed class DicomReaderOptions
{
    // ... existing properties ...

    /// <summary>How to handle pixel data during parsing.</summary>
    public PixelDataHandling PixelDataHandling { get; init; } = PixelDataHandling.LoadInMemory;

    /// <summary>
    /// Callback to determine pixel data handling per instance.
    /// Only called when PixelDataHandling is Callback.
    /// </summary>
    public Func<PixelDataContext, PixelDataHandling>? PixelDataCallback { get; init; }

    /// <summary>
    /// Directory for temporary files when buffering non-seekable streams.
    /// Defaults to system temp directory.
    /// </summary>
    public string? TempDirectory { get; init; }

    /// <summary>
    /// Use memory-mapped files for lazy loading large pixel data.
    /// Only applies when PixelDataHandling is LazyLoad.
    /// </summary>
    public bool UseMemoryMappedFiles { get; init; } = true;
}

public readonly struct PixelDataContext
{
    public ushort? Rows { get; init; }
    public ushort? Columns { get; init; }
    public ushort? BitsAllocated { get; init; }
    public int? NumberOfFrames { get; init; }
    public TransferSyntax TransferSyntax { get; init; }
    public bool IsEncapsulated { get; init; }

    /// <summary>Estimated uncompressed size (null if cannot determine).</summary>
    public long? EstimatedSize => (Rows.HasValue && Columns.HasValue &&
        BitsAllocated.HasValue && NumberOfFrames.HasValue)
        ? (long)Rows.Value * Columns.Value * ((BitsAllocated.Value + 7) / 8) * NumberOfFrames.Value
        : null;

    /// <summary>True if sufficient metadata exists to calculate frame positions.</summary>
    public bool HasImageDimensions => Rows.HasValue && Columns.HasValue;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| 32-bit Basic Offset Table only | Extended Offset Table (64-bit) | DICOM 2017 | Files > 4GB now supported |
| Implicit VR for compressed | Explicit VR required | Always | Encapsulated always uses Explicit VR LE |
| Single fragment per frame | Multiple fragments allowed | Always | Must use offset tables, not fragment index |
| OW for all pixel data | Context-dependent OB/OW | Always | BitsAllocated <= 8 uses OB |

**Deprecated/outdated:**
- Explicit VR Big Endian (1.2.840.10008.1.2.2): Retired, rarely seen in practice
- Assuming BOT is always populated: Many modern encoders omit it

## Open Questions

1. **Thread safety for lazy loading**
   - What we know: Context.md specifies "Yes, synchronization on load"
   - What's unclear: Exact locking strategy (per-element vs per-dataset)
   - Recommendation: Use `SemaphoreSlim` for async-compatible locking per element

2. **Non-seekable stream temp file cleanup**
   - What we know: Cleanup on Dispose per Context.md
   - What's unclear: Behavior on abnormal termination
   - Recommendation: Use `FileOptions.DeleteOnClose` where possible

3. **Multiple fragments per frame**
   - What we know: Spec allows it, must reassemble
   - What's unclear: How common this is in practice
   - Recommendation: Support it, but optimize for common case (1:1 mapping)

## Sources

### Primary (HIGH confidence)
- [DICOM PS3.5 Native/Encapsulated Encoding](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_8.2.html) - Format specifications
- [DICOM PS3.5 Transfer Syntaxes for Encapsulation](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_A.4.html) - Fragment structure
- [DICOM PS3.3 C.7.6.3 Image Pixel Module](https://dicom.nema.org/medical/dicom/current/output/chtml/part03/sect_C.7.6.3.html) - Extended Offset Table
- [Innolitics Extended Offset Table (7FE0,0001)](https://dicom.innolitics.com/ciods/rt-dose/image-pixel/7fe00001) - Attribute details
- [Innolitics Extended Offset Table Lengths (7FE0,0002)](https://dicom.innolitics.com/ciods/rt-image/image-pixel/7fe00002) - Lengths attribute
- [Microsoft Memory-Mapped Files](https://learn.microsoft.com/en-us/dotnet/standard/io/memory-mapped-files) - .NET MMF documentation
- [System.IO.Pipelines](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines) - High-performance I/O

### Secondary (MEDIUM confidence)
- [fo-dicom DicomPixelData Wiki](https://github.com/fo-dicom/fo-dicom/wiki/Raw-pixel-data) - Implementation patterns
- [fo-dicom DicomPixelData.cs](https://github.com/technocratz/fo-dicom-master/blob/master/DICOM/Imaging/DicomPixelData.cs) - Source reference
- [DCMTK Accessing Compressed Data](https://support.dcmtk.org/redmine/projects/dcmtk/wiki/Howto_AccessingCompressedData) - Fragment parsing
- [pydicom encaps module](https://pydicom.github.io/pydicom/stable/reference/generated/pydicom.encaps.generate_pixel_data_fragment.html) - Fragment generation

### Tertiary (LOW confidence)
- [fo-dicom Stream Issues](https://github.com/fo-dicom/fo-dicom/issues/617) - Lazy loading challenges
- [MemoryMappedFile Examples](https://www.dotnetperls.com/memorymappedfile) - Usage patterns

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Using built-in .NET types with official documentation
- Architecture: HIGH - Based on DICOM specification and Context.md decisions
- Pitfalls: HIGH - Verified against official spec and existing implementations

**Research date:** 2026-01-27
**Valid until:** 90 days (DICOM spec is stable; .NET APIs are stable)
