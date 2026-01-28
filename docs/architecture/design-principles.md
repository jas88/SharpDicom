# Design Principles

Core design philosophy for SharpDicom.

## Span<T> First

Use `Span<T>` and `ReadOnlySpan<T>` for parsing and buffer manipulation:

```csharp
// Prefer
public static DicomTag Parse(ReadOnlySpan<byte> buffer)

// Over
public static DicomTag Parse(byte[] buffer)
```

**Benefits**:
- Stack-allocated slices, no heap allocation
- Works with arrays, stackalloc, and Memory<T>
- Enables zero-copy parsing

## Streaming

Support streaming for large files:

```csharp
// Allow processing without loading entire file
await foreach (var element in reader.ReadElementsAsync())
{
    // Process element
}
```

**Benefits**:
- Handle files larger than available memory
- Start processing before download completes
- Efficient pipeline integration

## Async I/O

All I/O operations should have async variants:

```csharp
// Async file reading
var file = await DicomFile.OpenAsync(path, cancellationToken);

// Async network operations
await client.SendAsync(request, cancellationToken);
```

**Benefits**:
- Non-blocking I/O for server applications
- Proper cancellation support
- Efficient thread pool usage

## Minimal Allocations

Avoid allocations in hot paths:

- **Use pooled buffers** (`ArrayPool<T>`)
- **Prefer stackalloc** for small buffers
- **Return Span<T>** instead of new arrays where appropriate
- **Use ref structs** for transient parsing state

**Examples**:

```csharp
// Pooled buffer usage
var buffer = ArrayPool<byte>.Shared.Rent(4096);
try
{
    // Use buffer
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}

// Stackalloc for small buffers
Span<byte> header = stackalloc byte[132];  // Preamble + DICM
await stream.ReadExactlyAsync(header);

// Ref struct for parsing state
ref struct ElementParser
{
    private ReadOnlySpan<byte> _buffer;
    private int _position;
    // No heap allocation
}
```

## Stateless Value Parsing

Parse values on each access rather than caching:

```csharp
// Each call parses, no cached state
public DateOnly? GetDate() => ParseDA(RawValue.Span);

// Caller caches if needed
var dateElement = dataset[DicomTag.StudyDate];
var date = dateElement?.GetDate();  // Parse once, use result
```

**Benefits**:
- Simpler memory model
- No synchronization concerns
- Caller controls caching strategy

## Nullable + Throwing Accessors

Provide both nullable returns and throwing accessors:

```csharp
// Returns null if empty/invalid
public DateOnly? GetDate();

// Throws DicomDataException if empty/invalid
public DateOnly GetDateOrThrow();
```

**Benefits**:
- Caller chooses error handling style
- Throwing version for fail-fast scenarios
- Nullable version for optional data

## Raw Access

Always expose raw bytes for diagnostics:

```csharp
public readonly struct DicomElement
{
    public ReadOnlyMemory<byte> RawValue { get; }  // Always available

    public string? GetString();     // Parsed value
    public string RawString { get; } // Raw bytes as string for diagnostics
}
```

**Benefits**:
- Debug invalid/malformed data
- Round-trip preservation
- No data loss in error scenarios

## Zero-Copy When Possible

Design APIs to avoid unnecessary copying:

```csharp
// PDU references pooled buffer
public abstract class Pdu
{
    internal ReadOnlyMemory<byte> RawBuffer { get; }  // Slice, not copy

    // Explicit copy only when needed
    public abstract Pdu ToOwned();
}
```

**Pattern**:
1. Reference original buffer by default
2. Provide `ToOwned()` for explicit copying
3. Document buffer lifetime clearly

## Struct for Data, Class for Hierarchy

- **Structs** for fixed-size data types (DicomTag, DicomVR, DicomUID)
- **Classes** for variable-size or hierarchical data (DicomSequence, DicomDataset)

```csharp
// 4 bytes, trivial equality
public readonly struct DicomTag { }

// 2 bytes, trivial equality
public readonly struct DicomVR { }

// 65 bytes inline, no heap allocation
public readonly struct DicomUID { }

// Variable size, reference semantics
public class DicomSequence
{
    public IReadOnlyList<DicomDataset> Items { get; }
}
```
