# Architecture Research: Streaming Parser Design for SharpDicom

## Executive Summary

This document provides architectural research for building a high-performance streaming DICOM parser in .NET. The key insight is that SharpDicom's target workflow (metadata to MongoDB, pixels to disk) aligns perfectly with streaming architecture where elements are processed as they arrive without materializing the entire dataset in memory.

The recommended approach combines:
- **System.IO.Pipelines** for buffer management and backpressure
- **ref struct readers** for zero-allocation parsing (like Utf8JsonReader)
- **IAsyncEnumerable** for streaming element iteration
- **Configurable lazy loading** for large elements (pixel data)

---

## 1. Streaming Parser Design

### 1.1 Core Challenge: DICOM's Variable-Length Elements

DICOM presents unique parsing challenges:
- Elements have variable lengths (4 bytes or undefined)
- Sequences contain nested datasets of arbitrary depth
- Pixel data can span gigabytes
- Implicit VR requires dictionary lookup to determine element length format

### 1.2 Recommended Pattern: PipeReader + ref struct Reader

**Inspiration**: System.Text.Json's `Utf8JsonReader` pattern.

```
                    ┌─────────────────────────────────────────┐
                    │            DicomFileReader              │
                    │  (IAsyncDisposable, manages lifecycle)  │
                    └──────────────────┬──────────────────────┘
                                       │
                    ┌──────────────────▼──────────────────────┐
                    │             PipeReader                   │
                    │  (manages buffer, backpressure, async)   │
                    └──────────────────┬──────────────────────┘
                                       │
                    ┌──────────────────▼──────────────────────┐
                    │        DicomStreamReader (ref struct)    │
                    │  (stateless parsing, works on spans)     │
                    └──────────────────┬──────────────────────┘
                                       │
                    ┌──────────────────▼──────────────────────┐
                    │            DicomElement                  │
                    │  (Tag, VR, value as ReadOnlyMemory<byte>)│
                    └─────────────────────────────────────────┘
```

### 1.3 The Two-Layer Architecture

**Layer 1: DicomStreamReader (ref struct)**
- Zero-allocation parsing logic
- Works directly on `ReadOnlySpan<byte>` or `ReadOnlySequence<byte>`
- Forward-only, no backtracking
- Must be passed by reference (like Utf8JsonReader)
- Cannot be async (ref structs cannot cross await boundaries)

**Layer 2: DicomFileReader (class)**
- Manages the stream/pipe lifecycle
- Handles async I/O and buffer management
- Provides `IAsyncEnumerable<DicomElement>` iteration
- Coordinates lazy loading callbacks

```csharp
// Layer 1: Low-level ref struct (no async, works on spans)
public ref struct DicomStreamReader
{
    private ReadOnlySequence<byte> _buffer;
    private SequencePosition _position;
    private DicomReaderState _state;

    // Current element data (valid until next Read)
    public DicomTag CurrentTag { get; }
    public DicomVR CurrentVR { get; }
    public ReadOnlySequence<byte> CurrentValue { get; }

    // Advance to next element, returns false if more data needed
    public bool TryRead(out DicomElement element);

    // How many bytes were consumed (for AdvanceTo)
    public long BytesConsumed { get; }
}

// Layer 2: High-level class (async, manages lifecycle)
public sealed class DicomFileReader : IAsyncDisposable
{
    private readonly PipeReader _pipeReader;
    private DicomReaderState _state;

    public async IAsyncEnumerable<DicomElement> ReadElementsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (true)
        {
            ReadResult result = await _pipeReader.ReadAsync(ct);
            ReadOnlySequence<byte> buffer = result.Buffer;

            var reader = new DicomStreamReader(buffer, _state);

            while (reader.TryRead(out var element))
            {
                yield return element;
            }

            // Save state for next iteration
            _state = reader.GetState();

            // Tell pipe how much we consumed
            _pipeReader.AdvanceTo(
                reader.Position,           // consumed
                buffer.End);               // examined

            if (result.IsCompleted)
                break;
        }
    }
}
```

### 1.4 Trade-offs: ref struct vs class Reader

| Aspect | ref struct Reader | class Reader |
|--------|------------------|--------------|
| Allocations | Zero | One per reader instance |
| Async support | Cannot cross await | Full async support |
| Storage | Cannot be field in class | Can be stored anywhere |
| Performance | Maximum | Slightly lower |
| Complexity | Higher (state management) | Lower |

**Recommendation**: Use the two-layer approach. The ref struct handles the hot path (parsing), while the class wrapper handles async I/O and provides a friendly API.

---

## 2. Lazy Loading Patterns

### 2.1 The Pixel Data Problem

DICOM pixel data can be:
- **Uncompressed**: Rows x Columns x BitsAllocated/8 x SamplesPerPixel x Frames
- **Compressed**: Variable size, stored as fragment sequence
- **Examples**: CT scan = ~512KB/frame, Whole slide image = gigabytes

Loading all pixel data into memory defeats the streaming purpose.

### 2.2 Configurable Handling Strategies

```csharp
public enum LargeElementHandling
{
    // Load immediately into Memory<byte> - small files, full access needed
    LoadInMemory,

    // Store stream position, load on access - metadata-first workflows
    LazyLoad,

    // Skip entirely - metadata-only extraction
    Skip,

    // Callback decides per-element - complex filtering
    Callback
}

public class DicomReaderOptions
{
    // Threshold for "large" elements (default 1MB)
    public int LargeElementThreshold { get; init; } = 1024 * 1024;

    // How to handle large elements
    public LargeElementHandling LargeElements { get; init; } = LargeElementHandling.LazyLoad;

    // Callback for Callback mode
    public Func<LargeElementContext, LargeElementHandling>? LargeElementCallback { get; init; }
}

public readonly struct LargeElementContext
{
    public DicomTag Tag { get; init; }
    public DicomVR VR { get; init; }
    public uint Length { get; init; }
    public long StreamPosition { get; init; }

    // Context from earlier elements (nullable - may not have been seen yet)
    public ushort? Rows { get; init; }
    public ushort? Columns { get; init; }
    public ushort? BitsAllocated { get; init; }
    public int? NumberOfFrames { get; init; }
    public TransferSyntax TransferSyntax { get; init; }

    public long? EstimatedSize => Rows.HasValue && Columns.HasValue && BitsAllocated.HasValue
        ? (long)Rows.Value * Columns.Value * (BitsAllocated.Value / 8) * (NumberOfFrames ?? 1)
        : null;
}
```

### 2.3 Lazy Element Implementation

```csharp
public readonly struct LazyElement
{
    private readonly Stream _stream;
    private readonly long _position;
    private readonly uint _length;
    private readonly bool _isCompressed;

    // Position tracking for seekable streams
    public long StreamPosition => _position;
    public uint DeclaredLength => _length;

    // Load on demand
    public async ValueTask<ReadOnlyMemory<byte>> LoadAsync(CancellationToken ct = default)
    {
        if (!_stream.CanSeek)
            throw new InvalidOperationException("Stream is not seekable - cannot lazy load");

        _stream.Position = _position;
        var buffer = new byte[_length];
        await _stream.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    // Stream directly to destination (for pixel data -> disk workflows)
    public async ValueTask CopyToAsync(Stream destination, CancellationToken ct = default)
    {
        if (!_stream.CanSeek)
            throw new InvalidOperationException("Stream is not seekable - cannot lazy load");

        _stream.Position = _position;
        await _stream.CopyToAsync(destination, bufferSize: 81920, ct);
    }
}
```

### 2.4 Memory-Mapped Files for Very Large Elements

For extremely large files (>2GB pixel data), memory-mapped files provide:
- OS-managed paging (only needed pages in RAM)
- Zero-copy access via spans
- Random access without seeking

```csharp
public sealed class MemoryMappedLazyElement : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;

    public static MemoryMappedLazyElement Create(string filePath, long offset, long length)
    {
        var mmf = MemoryMappedFile.CreateFromFile(
            filePath,
            FileMode.Open,
            mapName: null,
            capacity: 0,  // Use file size
            MemoryMappedFileAccess.Read);

        var accessor = mmf.CreateViewAccessor(offset, length, MemoryMappedFileAccess.Read);
        return new MemoryMappedLazyElement(mmf, accessor);
    }

    // Get frame by index without loading entire pixel data
    public unsafe ReadOnlySpan<byte> GetFrame(int frameIndex, int frameSize)
    {
        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        try
        {
            return new ReadOnlySpan<byte>(ptr + frameIndex * frameSize, frameSize);
        }
        finally
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }
}
```

**Platform note**: Memory-mapped file naming is Windows-only. Cross-platform code should use `mapName: null`.

---

## 3. State Machine Parsing

### 3.1 Why State Machines?

DICOM parsing must handle:
- **Incomplete reads**: Network data arrives in chunks
- **Nested structures**: Sequences contain datasets
- **Undefined lengths**: Some elements don't declare size upfront
- **Context-dependent parsing**: Implicit VR, multi-VR tags

State machines enable **resumable parsing** - save state when data runs out, resume when more arrives.

### 3.2 Parser State Structure

```csharp
public enum DicomReaderStateType
{
    Start,

    // File structure
    ReadingPreamble,
    ReadingDicmPrefix,
    ReadingFileMetaInfo,

    // Element parsing
    ReadingTag,
    ReadingVR,           // Explicit VR only
    ReadingLength16,     // 16-bit length
    ReadingLength32,     // 32-bit length
    ReadingValue,

    // Sequence parsing
    ReadingSequenceItem,
    ReadingItemValue,
    ReadingItemDelimiter,
    ReadingSequenceDelimiter,

    // Fragment sequence (encapsulated pixel data)
    ReadingOffsetTable,
    ReadingFragment,
    ReadingFragmentDelimiter,

    Completed,
    Error
}

public struct DicomReaderState
{
    // Current parser state
    public DicomReaderStateType State { get; internal set; }

    // Position tracking
    public long BytesRead { get; internal set; }
    public long ElementStartPosition { get; internal set; }

    // Partial element data (between reads)
    public DicomTag PartialTag { get; internal set; }
    public DicomVR PartialVR { get; internal set; }
    public uint PartialLength { get; internal set; }
    public int ValueBytesRead { get; internal set; }

    // Nesting tracking (for sequences)
    public int NestingDepth { get; internal set; }
    public StackArray<SequenceState> SequenceStack { get; internal set; }

    // Transfer syntax (affects parsing)
    public TransferSyntax TransferSyntax { get; internal set; }

    // Context for multi-VR resolution
    public ushort BitsAllocated { get; internal set; }
    public ushort PixelRepresentation { get; internal set; }
}

// Fixed-size stack to avoid allocations (max depth ~20 is reasonable)
[InlineArray(20)]
public struct StackArray<T>
{
    private T _element0;
}

public struct SequenceState
{
    public DicomTag SequenceTag { get; init; }
    public long RemainingBytes { get; set; }  // -1 for undefined length
    public int ItemIndex { get; set; }
}
```

### 3.3 State Transitions

```
                         ┌──────────────────────────────────────┐
                         │              Start                    │
                         └─────────────────┬────────────────────┘
                                           │
                    ┌──────────────────────▼───────────────────────┐
                    │           ReadingPreamble (128 bytes)        │
                    └──────────────────────┬───────────────────────┘
                                           │
                    ┌──────────────────────▼───────────────────────┐
                    │           ReadingDicmPrefix (4 bytes)        │
                    └──────────────────────┬───────────────────────┘
                                           │
                    ┌──────────────────────▼───────────────────────┐
                    │    ReadingFileMetaInfo (Explicit VR LE)      │
                    │    (loops until Group 0002 ends)              │
                    └──────────────────────┬───────────────────────┘
                                           │
           ┌───────────────────────────────▼───────────────────────────────┐
           │                      ReadingTag (4 bytes)                      │
           └───────────────┬────────────────────────────────┬──────────────┘
                           │                                │
              (Explicit VR)│                                │(Implicit VR)
                           ▼                                ▼
           ┌───────────────────────────┐    ┌───────────────────────────────┐
           │    ReadingVR (2 bytes)    │    │  [VR from dictionary]         │
           └───────────────┬───────────┘    │  ReadingLength32 (4 bytes)    │
                           │                └───────────────┬───────────────┘
           ┌───────────────▼───────────┐                    │
           │  Is 16-bit or 32-bit VR?  │                    │
           └─────┬─────────────┬───────┘                    │
                 │             │                            │
    (16-bit)     │             │ (32-bit: OB,OW,OF,SQ,UC,UN,UR,UT,SV,UV)
                 ▼             ▼                            │
    ┌────────────────┐  ┌──────────────────────┐           │
    │ReadingLength16 │  │ Skip 2 reserved bytes │           │
    │   (2 bytes)    │  │ ReadingLength32       │           │
    └───────┬────────┘  │     (4 bytes)         │           │
            │           └──────────┬────────────┘           │
            │                      │                        │
            └──────────┬───────────┴────────────────────────┘
                       │
                       ▼
           ┌───────────────────────────┐
           │  Length == 0xFFFFFFFF?    │
           └─────────┬─────────┬───────┘
                     │         │
        (undefined)  │         │ (defined)
                     ▼         ▼
    ┌────────────────────┐  ┌───────────────────────────┐
    │ Is Sequence (SQ)?  │  │     ReadingValue          │
    │ or Fragment (OB)?  │  │  (length bytes)           │
    └────────┬───────────┘  └───────────────┬───────────┘
             │                              │
             ▼                              │
    ┌────────────────────┐                  │
    │ Push sequence state│                  │
    │ ReadingSequenceItem│                  │
    └────────────────────┘                  │
                                            │
            ┌───────────────────────────────┘
            ▼
    ┌───────────────────────────┐
    │      Yield Element        │
    │   Loop back to ReadingTag │
    └───────────────────────────┘
```

### 3.4 Handling Buffer Boundaries

The key insight from Utf8JsonReader: when buffer runs out mid-element, return `false` and let the caller provide more data.

```csharp
public ref struct DicomStreamReader
{
    public bool TryRead(out DicomElement element)
    {
        element = default;

        switch (_state.State)
        {
            case DicomReaderStateType.ReadingTag:
                // Need 4 bytes for tag
                if (_buffer.Length - _consumed < 4)
                    return false;  // Need more data

                var tagSpan = GetSpan(4);
                _state.PartialTag = new DicomTag(
                    BinaryPrimitives.ReadUInt16LittleEndian(tagSpan),
                    BinaryPrimitives.ReadUInt16LittleEndian(tagSpan[2..]));
                _consumed += 4;

                _state.State = _state.TransferSyntax.IsExplicitVR
                    ? DicomReaderStateType.ReadingVR
                    : DicomReaderStateType.ReadingLength32;

                goto case DicomReaderStateType.ReadingVR;

            case DicomReaderStateType.ReadingVR:
                if (_buffer.Length - _consumed < 2)
                    return false;  // Need more data

                var vrSpan = GetSpan(2);
                _state.PartialVR = DicomVR.FromBytes(vrSpan);
                _consumed += 2;

                // Determine if 16-bit or 32-bit length
                if (_state.PartialVR.Is16BitLength())
                {
                    _state.State = DicomReaderStateType.ReadingLength16;
                    goto case DicomReaderStateType.ReadingLength16;
                }
                else
                {
                    // Skip 2 reserved bytes
                    if (_buffer.Length - _consumed < 2)
                        return false;
                    _consumed += 2;
                    _state.State = DicomReaderStateType.ReadingLength32;
                    goto case DicomReaderStateType.ReadingLength32;
                }

            // ... more cases
        }

        return false;
    }
}
```

---

## 4. Memory Management

### 4.1 Buffer Pooling with ArrayPool

The core principle: **rent buffers, don't allocate**.

```csharp
public sealed class PooledBuffer : IDisposable
{
    private byte[]? _array;
    private int _length;
    private readonly ArrayPool<byte> _pool;

    public PooledBuffer(int minimumLength, ArrayPool<byte>? pool = null)
    {
        _pool = pool ?? ArrayPool<byte>.Shared;
        _array = _pool.Rent(minimumLength);
        _length = minimumLength;
    }

    public Memory<byte> Memory => _array.AsMemory(0, _length);
    public Span<byte> Span => _array.AsSpan(0, _length);

    public void Dispose()
    {
        if (_array != null)
        {
            _pool.Return(_array);
            _array = null;
        }
    }
}
```

### 4.2 System.IO.Pipelines Integration

Pipelines handle buffer management automatically:

```csharp
public static class DicomPipeReaderExtensions
{
    public static async ValueTask<DicomDataset> ReadDatasetAsync(
        this PipeReader reader,
        DicomReaderOptions? options = null,
        CancellationToken ct = default)
    {
        var dataset = new DicomDataset();
        var state = new DicomReaderState();

        while (true)
        {
            // Ask pipe for data (async, may yield)
            ReadResult result = await reader.ReadAsync(ct);
            ReadOnlySequence<byte> buffer = result.Buffer;

            // Parse synchronously on current buffer
            var streamReader = new DicomStreamReader(buffer, state);

            while (streamReader.TryRead(out var element))
            {
                dataset.Add(element);
            }

            // Save state for next iteration
            state = streamReader.GetState();

            // Tell pipe what we consumed (releases buffers)
            reader.AdvanceTo(
                buffer.GetPosition(streamReader.BytesConsumed),  // consumed
                buffer.End);                                     // examined

            if (result.IsCompleted)
            {
                if (state.State != DicomReaderStateType.Completed)
                    throw new DicomFileException("Unexpected end of stream");
                break;
            }
        }

        return dataset;
    }
}
```

### 4.3 Ownership Semantics: Borrowed vs Owned

Critical insight: parsed elements reference pooled buffers. Once `AdvanceTo` is called, those buffers may be recycled.

**Pattern**: Explicit `ToOwned()` for data that must outlive the buffer.

```csharp
public readonly struct DicomElement
{
    // References pooled buffer - valid only until next pipe read
    public ReadOnlyMemory<byte> RawValue { get; }

    // Copy to owned buffer (allocates)
    public DicomElement ToOwned()
    {
        return new DicomElement(
            Tag,
            VR,
            RawValue.ToArray()  // Allocates copy
        );
    }
}

public sealed class DicomDataset
{
    // Deep-copy all elements to owned buffers
    public DicomDataset ToOwned()
    {
        var copy = new DicomDataset();
        foreach (var element in this)
        {
            copy.Add(element.ToOwned());
        }
        return copy;
    }
}
```

**Usage pattern**:
```csharp
// Streaming: process immediately, don't keep references
await foreach (var element in reader.ReadElementsAsync())
{
    // Element is valid here
    await ProcessAndForgetAsync(element);
}   // Buffer recycled, element no longer valid

// Keeping data: explicit copy
DicomElement? savedElement = null;
await foreach (var element in reader.ReadElementsAsync())
{
    if (element.Tag == DicomTag.PatientName)
    {
        savedElement = element.ToOwned();  // Explicit allocation
    }
}
// savedElement still valid
```

### 4.4 SequenceReader for Multi-Segment Buffers

When PipeReader returns `ReadOnlySequence<byte>` spanning multiple segments:

```csharp
public ref struct DicomStreamReader
{
    private SequenceReader<byte> _reader;

    public DicomStreamReader(ReadOnlySequence<byte> buffer, DicomReaderState state)
    {
        _reader = new SequenceReader<byte>(buffer);
        _state = state;
    }

    private bool TryReadTag(out DicomTag tag)
    {
        tag = default;

        // SequenceReader handles segment boundaries automatically
        if (!_reader.TryReadLittleEndian(out ushort group))
            return false;
        if (!_reader.TryReadLittleEndian(out ushort element))
            return false;

        tag = new DicomTag(group, element);
        return true;
    }

    private bool TryReadValue(uint length, out ReadOnlySequence<byte> value)
    {
        value = default;

        if (_reader.Remaining < length)
            return false;

        // Slice out the value (may span segments)
        if (!_reader.TryReadExact((int)length, out value))
            return false;

        return true;
    }
}
```

---

## 5. Async Patterns

### 5.1 IAsyncEnumerable for Element Streaming

The primary API for streaming DICOM parsing:

```csharp
public sealed class DicomFileReader : IAsyncDisposable
{
    public async IAsyncEnumerable<DicomElement> ReadElementsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!_completed)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _pipeReader.ReadAsync(ct).ConfigureAwait(false);

            // ... parse buffer ...

            foreach (var element in ParsedElements)
            {
                yield return element;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        return _pipeReader.CompleteAsync();
    }
}
```

### 5.2 Cancellation Support

Full cancellation support at multiple levels:

```csharp
// 1. EnumeratorCancellation on the IAsyncEnumerable
public async IAsyncEnumerable<DicomElement> ReadElementsAsync(
    [EnumeratorCancellation] CancellationToken ct = default)

// 2. WithCancellation() extension for consumers
await foreach (var element in reader.ReadElementsAsync()
    .WithCancellation(cts.Token))
{
    // Process element
}

// 3. Periodic checks in long operations
public async ValueTask<DicomDataset> ReadDatasetAsync(CancellationToken ct)
{
    int elementCount = 0;
    await foreach (var element in ReadElementsAsync(ct))
    {
        dataset.Add(element);

        // Check cancellation periodically (every 1000 elements)
        if (++elementCount % 1000 == 0)
            ct.ThrowIfCancellationRequested();
    }
    return dataset;
}
```

### 5.3 ValueTask for Hot Paths

Use `ValueTask` where operations often complete synchronously:

```csharp
// Often completes synchronously (data already in buffer)
public ValueTask<DicomElement?> ReadNextElementAsync(CancellationToken ct = default)
{
    // Try synchronous path first
    if (_reader.TryRead(out var element))
    {
        return new ValueTask<DicomElement?>(element);
    }

    // Fall back to async
    return ReadNextElementAsyncCore(ct);
}

private async ValueTask<DicomElement?> ReadNextElementAsyncCore(CancellationToken ct)
{
    var result = await _pipeReader.ReadAsync(ct).ConfigureAwait(false);
    // ... parse ...
}
```

### 5.4 ConfigureAwait Considerations

Library code should use `ConfigureAwait(false)` to avoid context capture:

```csharp
// Library internal code
var result = await _pipeReader.ReadAsync(ct).ConfigureAwait(false);

// Consumer code can use default context capture
await foreach (var element in reader.ReadElementsAsync())
{
    // Runs on captured context (UI thread, etc.)
    UpdateUI(element);
}

// Or explicitly disable context
await foreach (var element in reader.ReadElementsAsync().ConfigureAwait(false))
{
    // Runs on thread pool
}
```

---

## 6. Writer Architecture

### 6.1 Streaming Writes with IBufferWriter

```csharp
public sealed class DicomStreamWriter
{
    private readonly IBufferWriter<byte> _writer;

    public DicomStreamWriter(IBufferWriter<byte> writer)
    {
        _writer = writer;
    }

    public void WriteElement(in DicomElement element)
    {
        // Get buffer from writer
        var span = _writer.GetSpan(8);  // Tag + VR + length

        // Write tag (4 bytes)
        BinaryPrimitives.WriteUInt16LittleEndian(span, element.Tag.Group);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], element.Tag.Element);

        // Write VR (2 bytes, explicit VR only)
        if (_transferSyntax.IsExplicitVR)
        {
            span[4] = element.VR.Char1;
            span[5] = element.VR.Char2;

            // Write length (2 or 4 bytes depending on VR)
            if (element.VR.Is16BitLength())
            {
                BinaryPrimitives.WriteUInt16LittleEndian(span[6..], (ushort)element.RawValue.Length);
                _writer.Advance(8);
            }
            else
            {
                // 2 reserved bytes + 4-byte length
                span[6] = 0;
                span[7] = 0;
                _writer.Advance(8);

                span = _writer.GetSpan(4);
                BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)element.RawValue.Length);
                _writer.Advance(4);
            }
        }
        else
        {
            // Implicit VR: 4-byte length only
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)element.RawValue.Length);
            _writer.Advance(8);
        }

        // Write value
        WriteValue(element.RawValue);
    }

    private void WriteValue(ReadOnlyMemory<byte> value)
    {
        var source = value.Span;
        while (source.Length > 0)
        {
            var span = _writer.GetSpan(Math.Min(source.Length, 65536));
            var toCopy = Math.Min(source.Length, span.Length);
            source[..toCopy].CopyTo(span);
            _writer.Advance(toCopy);
            source = source[toCopy..];
        }
    }
}
```

### 6.2 Handling Undefined Length Sequences

Sequences with undefined length require delimiter items:

```csharp
public void WriteSequence(DicomTag tag, IReadOnlyList<DicomDataset> items, bool undefinedLength)
{
    // Write sequence tag and VR
    WriteTagAndVR(tag, DicomVR.SQ);

    if (undefinedLength)
    {
        // Write undefined length marker
        WriteLength(0xFFFFFFFF);

        foreach (var item in items)
        {
            // Item tag (FFFE,E000) with undefined length
            WriteTag(DicomTag.Item);
            WriteLength(0xFFFFFFFF);

            // Write item contents
            foreach (var element in item)
            {
                WriteElement(element);
            }

            // Item delimitation tag (FFFE,E00D)
            WriteTag(DicomTag.ItemDelimitationItem);
            WriteLength(0);
        }

        // Sequence delimitation tag (FFFE,E0DD)
        WriteTag(DicomTag.SequenceDelimitationItem);
        WriteLength(0);
    }
    else
    {
        // Calculate total length (complex for nested sequences)
        var length = CalculateSequenceLength(items);
        WriteLength(length);

        foreach (var item in items)
        {
            WriteTag(DicomTag.Item);
            WriteLength(CalculateItemLength(item));

            foreach (var element in item)
            {
                WriteElement(element);
            }
            // No delimiter needed for defined length
        }
        // No delimiter needed for defined length
    }
}
```

### 6.3 Network PDU Buffering

For network writes, buffer complete PDUs before sending:

```csharp
public sealed class PduWriter
{
    private readonly ArrayPoolBufferWriter<byte> _buffer;
    private readonly PipeWriter _output;

    public PduWriter(PipeWriter output)
    {
        _buffer = new ArrayPoolBufferWriter<byte>();
        _output = output;
    }

    public async ValueTask WritePDataAsync(
        byte presentationContextId,
        ReadOnlyMemory<byte> data,
        bool isCommand,
        bool isLast,
        CancellationToken ct = default)
    {
        // Build PDU in buffer
        _buffer.Clear();

        // PDU type (1 byte) + reserved (1 byte) + length (4 bytes)
        var header = _buffer.GetSpan(6);
        header[0] = 0x04;  // P-DATA-TF
        header[1] = 0x00;  // Reserved
        // Length filled later
        _buffer.Advance(6);

        // PDV item header
        var pdvHeader = _buffer.GetSpan(6);
        // Item length (4 bytes)
        BinaryPrimitives.WriteUInt32BigEndian(pdvHeader, (uint)(data.Length + 2));
        // Presentation context ID
        pdvHeader[4] = presentationContextId;
        // Message control header
        pdvHeader[5] = (byte)((isCommand ? 0x01 : 0x00) | (isLast ? 0x02 : 0x00));
        _buffer.Advance(6);

        // Write data
        data.Span.CopyTo(_buffer.GetSpan(data.Length));
        _buffer.Advance(data.Length);

        // Fill in PDU length
        var written = _buffer.WrittenSpan;
        BinaryPrimitives.WriteUInt32BigEndian(written[2..], (uint)(written.Length - 6));

        // Send complete PDU
        await _output.WriteAsync(_buffer.WrittenMemory, ct).ConfigureAwait(false);
    }
}
```

---

## 7. Recommendations for SharpDicom

### 7.1 Core Architecture

1. **Two-layer reader design**:
   - `DicomStreamReader` (ref struct): Zero-allocation parsing on spans
   - `DicomFileReader` (class): Async I/O, lifecycle management, IAsyncEnumerable

2. **System.IO.Pipelines for I/O**:
   - Automatic buffer management and backpressure
   - Easy integration with streams and sockets
   - Consistent pattern for file and network I/O

3. **State machine parsing**:
   - Full state externalized in `DicomReaderState` struct
   - Handles buffer boundaries gracefully
   - Enables pause/resume for streaming scenarios

### 7.2 Memory Strategy

1. **Default to pooled buffers** (ArrayPool<byte>.Shared)
2. **Explicit ownership semantics** (`ToOwned()` for keeping data)
3. **Lazy loading for large elements** (configurable threshold)
4. **Memory-mapped files for huge pixel data** (>2GB)

### 7.3 API Design

```csharp
// Primary streaming API
public sealed class DicomFile
{
    // Load entire file (small files)
    public static DicomFile Open(string path, DicomReaderOptions? options = null);
    public static ValueTask<DicomFile> OpenAsync(string path, DicomReaderOptions? options = null, CancellationToken ct = default);

    // Streaming (large files, metadata extraction)
    public static DicomFileReader OpenStreaming(string path, DicomReaderOptions? options = null);
    public static DicomFileReader OpenStreaming(Stream stream, DicomReaderOptions? options = null);
    public static DicomFileReader OpenStreaming(PipeReader reader, DicomReaderOptions? options = null);
}

public sealed class DicomFileReader : IAsyncDisposable
{
    // Metadata only (fastest)
    public ValueTask<DicomDataset> ReadFileMetaInfoAsync(CancellationToken ct = default);

    // Element-by-element streaming
    public IAsyncEnumerable<DicomElement> ReadElementsAsync(CancellationToken ct = default);

    // Full dataset (with configurable lazy loading)
    public ValueTask<DicomDataset> ReadDatasetAsync(CancellationToken ct = default);

    // Skip pixel data entirely
    public ValueTask<DicomDataset> ReadMetadataOnlyAsync(CancellationToken ct = default);
}
```

### 7.4 Configuration Presets

```csharp
public static class DicomReaderOptions
{
    // Maximum performance, strict validation
    public static readonly DicomReaderOptions Strict = new()
    {
        InvalidVRHandling = InvalidVRHandling.Throw,
        LargeElements = LargeElementHandling.LazyLoad,
        LargeElementThreshold = 1024 * 1024,  // 1MB
        ValidateOnRead = true
    };

    // Balanced performance and tolerance
    public static readonly DicomReaderOptions Lenient = new()
    {
        InvalidVRHandling = InvalidVRHandling.MapToUN,
        LargeElements = LargeElementHandling.LazyLoad,
        LargeElementThreshold = 1024 * 1024,
        ValidateOnRead = false
    };

    // Metadata extraction (fastest)
    public static readonly DicomReaderOptions MetadataOnly = new()
    {
        InvalidVRHandling = InvalidVRHandling.Preserve,
        LargeElements = LargeElementHandling.Skip,
        StopBeforePixelData = true,
        ValidateOnRead = false
    };

    // Full load for small files
    public static readonly DicomReaderOptions FullLoad = new()
    {
        InvalidVRHandling = InvalidVRHandling.MapToUN,
        LargeElements = LargeElementHandling.LoadInMemory,
        ValidateOnRead = false
    };
}
```

---

## 8. References

### Microsoft Documentation
- [System.IO.Pipelines](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines)
- [Utf8JsonReader](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonreader)
- [SequenceReader<T>](https://learn.microsoft.com/en-us/dotnet/standard/io/buffers#sequencereader%3Ct%3E)
- [ArrayPool<T>](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1)
- [Memory<T> and Span<T>](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/)
- [IAsyncEnumerable<T>](https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream)
- [ValueTask](https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/)
- [Memory-mapped files](https://learn.microsoft.com/en-us/dotnet/standard/io/memory-mapped-files)

### Similar Projects (Architecture Inspiration)
- **System.Text.Json**: Utf8JsonReader (ref struct), JsonDocument, streaming deserialization
- **protobuf-net**: State machine parsing, buffer pooling
- **Kestrel HTTP parser**: PipeReader integration, zero-allocation parsing
- **MessagePack-CSharp**: High-performance binary serialization

### DICOM Standard
- PS3.5: Data Structures and Encoding
- PS3.10: Media Storage and File Format
- PS3.8: Network Communication Support

---

*Research completed: 2026-01-26*
*Target: SharpDicom streaming DICOM parser*
