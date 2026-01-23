# Stack Research: .NET Performance Patterns for SharpDicom

## Executive Summary

This document covers the core .NET stack technologies required for building a high-performance DICOM library: Roslyn incremental source generators, buffer pooling, Span/Memory patterns, System.IO.Pipelines, and FrozenDictionary. Each section provides actionable patterns specific to SharpDicom's requirements.

---

## 1. Source Generator Patterns (Roslyn Incremental Generators)

### Key Concepts

Incremental generators (`IIncrementalGenerator`) replace the deprecated `ISourceGenerator`. They build immutable execution pipelines during initialization that execute on-demand as input data changes.

**Core Architecture:**
- Pipeline-based: Transformations create a directed graph executed when inputs change
- Deferred execution: Similar to LINQ, cached results reused for unchanged items
- Item-wise caching: Changes to one item don't invalidate cached results for others

### Implementation Pattern for DICOM Dictionary

```csharp
[Generator]
public class DicomDictionaryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Filter for DICOM XML files in AdditionalFiles
        var xmlFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith("part06.xml", StringComparison.Ordinal));

        // Transform XML to intermediate model (cache-friendly)
        var tagModels = xmlFiles.Select(static (file, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var text = file.GetText(ct);
            if (text is null) return default;

            // Parse XML to value-equatable model (NOT syntax nodes)
            return ParseDicomTags(text.ToString(), ct);
        })
        .Where(static model => model.HasValue)
        .Select(static (model, _) => model!.Value);

        // Register output
        context.RegisterSourceOutput(tagModels, GenerateDicomTags);
    }

    // Use value-equatable record for caching
    private readonly record struct DicomTagModel(
        uint Tag,
        string Keyword,
        string Name,
        string[] VRs,
        string VM,
        bool IsRetired);
}
```

### Best Practices

1. **Use `ForAttributeWithMetadataName`**: 99x faster than `CreateSyntaxProvider` for attribute-based generation

2. **Pipeline model design**: Use value-equatable models (records) rather than syntax nodes or symbols
   - Enables incremental caching when outputs remain unchanged
   - Symbols/nodes aren't comparable between compilations

3. **Text generation over syntax nodes**: Use `StringBuilder` with indented writers
   - Avoid `SyntaxNode` construction and `NormalizeWhitespace()`
   - Much better performance for large generated files

4. **Filter early**: Use `Where` to reduce dataset size before expensive operations

5. **Respect cancellation tokens**: Always call `ThrowIfCancellationRequested()` in transformations

### Project Configuration

```xml
<!-- In SharpDicom.Generators.csproj -->
<PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
</PropertyGroup>

<ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
</ItemGroup>

<!-- In consuming project -->
<ItemGroup>
    <ProjectReference Include="..\SharpDicom.Generators\SharpDicom.Generators.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <AdditionalFiles Include="data\dicom-standard\part06.xml" />
</ItemGroup>
```

### Multi-TFM Output Pattern

Generate different code based on target framework:

```csharp
context.RegisterSourceOutput(provider.Combine(context.CompilationProvider),
    (spc, pair) =>
{
    var (model, compilation) = pair;

    // Check target framework from assembly attributes
    var targetFramework = compilation.Assembly.GetAttributes()
        .FirstOrDefault(a => a.AttributeClass?.Name == "TargetFrameworkAttribute");

    bool usesFrozenDictionary = targetFramework?.ConstructorArguments
        .FirstOrDefault().Value?.ToString()?.Contains("net8") ?? false;

    var code = usesFrozenDictionary
        ? GenerateWithFrozenDictionary(model)
        : GenerateWithDictionary(model);

    spc.AddSource("DicomDictionary.g.cs", code);
});
```

---

## 2. Buffer Pooling Patterns

### ArrayPool<T> Usage

```csharp
// Rent and return pattern
public void ProcessDicomData(ReadOnlySpan<byte> input)
{
    byte[] buffer = ArrayPool<byte>.Shared.Rent(input.Length);
    try
    {
        input.CopyTo(buffer);
        ProcessBuffer(buffer.AsSpan(0, input.Length));
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
    }
}

// Important: ArrayPool may return larger buffer than requested
// Always track actual used length
```

### IBufferWriter<T> Pattern

`IBufferWriter<T>` is the contract for synchronous buffered writing:

```csharp
void WriteDicomElement(IBufferWriter<byte> writer, DicomTag tag, DicomVR vr, ReadOnlySpan<byte> value)
{
    // Request buffer for tag (4 bytes) + VR (2 bytes) + length (2-4 bytes) + value
    int headerSize = 4 + 2 + (vr.Is16BitLength ? 2 : 6);
    Span<byte> span = writer.GetSpan(headerSize + value.Length);

    // Write tag
    BinaryPrimitives.WriteUInt16LittleEndian(span, tag.Group);
    BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2), tag.Element);

    // Write VR
    span[4] = vr.Char1;
    span[5] = vr.Char2;

    // Write length and value
    int offset = WriteLength(span.Slice(6), vr, value.Length);
    value.CopyTo(span.Slice(6 + offset));

    // Notify writer of bytes written
    writer.Advance(headerSize + value.Length);
}
```

### ArrayBufferWriter<T> (Growable Buffer)

```csharp
// Use ArrayBufferWriter for building output of unknown size
var writer = new ArrayBufferWriter<byte>(initialCapacity: 4096);

foreach (var element in dataset)
{
    WriteDicomElement(writer, element.Tag, element.VR, element.RawValue.Span);
}

ReadOnlySpan<byte> result = writer.WrittenSpan;
```

### IMemoryOwner<T> Pattern

```csharp
// For buffers that need to survive async boundaries
public async ValueTask<IMemoryOwner<byte>> ReadPixelDataAsync(Stream stream, int length)
{
    var owner = MemoryPool<byte>.Shared.Rent(length);
    try
    {
        await stream.ReadExactlyAsync(owner.Memory.Slice(0, length));
        return owner;
    }
    catch
    {
        owner.Dispose();
        throw;
    }
}

// Caller is responsible for disposal
await using var pixelData = await ReadPixelDataAsync(stream, size);
ProcessPixels(pixelData.Memory.Span);
```

### When to Pool vs Stackalloc

| Scenario | Recommendation |
|----------|----------------|
| < 256 bytes, synchronous | `stackalloc` |
| < 1024 bytes, synchronous, known size | `stackalloc` with fallback |
| Unknown size, may grow | `ArrayBufferWriter<T>` |
| Fixed size > 1KB | `ArrayPool<T>.Rent()` |
| Async operations | `MemoryPool<T>` or `ArrayPool<T>` |
| High frequency, same size | Custom pool or `IMemoryOwner<T>` |

---

## 3. Span<T> / Memory<T> Best Practices

### Type Selection Guide

| Type | Stack-only | Async-safe | Use Case |
|------|------------|------------|----------|
| `Span<T>` | Yes | No | Synchronous hot paths, parsing |
| `ReadOnlySpan<T>` | Yes | No | Read-only synchronous operations |
| `Memory<T>` | No | Yes | Async, storage in classes |
| `ReadOnlyMemory<T>` | No | Yes | Async read-only, storage |

### Zero-Copy Parsing Pattern

```csharp
public readonly struct DicomStreamReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public DicomStreamReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    // Zero-allocation tag reading
    public DicomTag ReadTag()
    {
        var span = _buffer.Slice(_position, 4);
        _position += 4;

        ushort group = BinaryPrimitives.ReadUInt16LittleEndian(span);
        ushort element = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2));

        return new DicomTag(group, element);
    }

    // Zero-copy value access
    public ReadOnlySpan<byte> ReadValue(int length)
    {
        var span = _buffer.Slice(_position, length);
        _position += length;
        return span;
    }
}
```

### Span with Stackalloc Fallback

```csharp
public string DecodeString(ReadOnlySpan<byte> bytes, Encoding encoding)
{
    const int StackAllocThreshold = 256;

    int maxCharCount = encoding.GetMaxCharCount(bytes.Length);

    // Stack allocate for small strings
    if (maxCharCount <= StackAllocThreshold)
    {
        Span<char> chars = stackalloc char[maxCharCount];
        int written = encoding.GetChars(bytes, chars);
        return new string(chars.Slice(0, written));
    }

    // Pool for larger strings
    char[] pooled = ArrayPool<char>.Shared.Rent(maxCharCount);
    try
    {
        int written = encoding.GetChars(bytes, pooled);
        return new string(pooled, 0, written);
    }
    finally
    {
        ArrayPool<char>.Shared.Return(pooled);
    }
}
```

### Ref Struct Limitations

`Span<T>` and `ReadOnlySpan<T>` are ref structs with restrictions:
- Cannot be boxed or assigned to `object`/`dynamic`
- Cannot be fields in reference types
- Cannot be used across `await`/`yield`
- Cannot implement interfaces

**Workaround for async boundaries:**
```csharp
// BAD: Span cannot cross await
async Task ProcessAsync(ReadOnlySpan<byte> data) // COMPILER ERROR

// GOOD: Use Memory<T>
async Task ProcessAsync(ReadOnlyMemory<byte> data)
{
    await Task.Yield();
    ProcessSync(data.Span); // Access span after await completes
}
```

### NetStandard 2.0 Polyfills

For netstandard2.0 compatibility, reference:
```xml
<PackageReference Include="System.Memory" Version="4.5.5" />
```

This provides:
- `Span<T>`, `ReadOnlySpan<T>`
- `Memory<T>`, `ReadOnlyMemory<T>`
- `ArrayPool<T>`
- `MemoryExtensions` (AsSpan, etc.)

---

## 4. System.IO.Pipelines

### When to Use Pipelines

Use Pipelines when:
- Processing streaming network data
- Handling backpressure is important
- Reading data in chunks with unknown boundaries
- Building protocol parsers (like DICOM network protocol)

### Basic Pipeline Pattern

```csharp
public async Task ProcessDicomStreamAsync(Stream stream, CancellationToken ct)
{
    var pipe = new Pipe(new PipeOptions(
        minimumSegmentSize: 4096,
        pauseWriterThreshold: 64 * 1024,
        resumeWriterThreshold: 32 * 1024
    ));

    // Writer task - fills pipe from stream
    var writerTask = FillPipeAsync(stream, pipe.Writer, ct);

    // Reader task - processes DICOM data
    var readerTask = ReadDicomDataAsync(pipe.Reader, ct);

    await Task.WhenAll(writerTask, readerTask);
}

private async Task FillPipeAsync(Stream stream, PipeWriter writer, CancellationToken ct)
{
    const int MinBufferSize = 4096;

    try
    {
        while (true)
        {
            Memory<byte> memory = writer.GetMemory(MinBufferSize);
            int bytesRead = await stream.ReadAsync(memory, ct);

            if (bytesRead == 0)
                break;

            writer.Advance(bytesRead);

            FlushResult result = await writer.FlushAsync(ct);
            if (result.IsCompleted)
                break;
        }
    }
    finally
    {
        await writer.CompleteAsync();
    }
}

private async Task ReadDicomDataAsync(PipeReader reader, CancellationToken ct)
{
    try
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(ct);
            ReadOnlySequence<byte> buffer = result.Buffer;

            while (TryReadDicomElement(ref buffer, out var element))
            {
                ProcessElement(element);
            }

            // Tell pipe how much was consumed
            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
                break;
        }
    }
    finally
    {
        await reader.CompleteAsync();
    }
}
```

### ReadOnlySequence<T> Handling

Pipelines return `ReadOnlySequence<T>` which may be non-contiguous:

```csharp
private bool TryReadDicomElement(ref ReadOnlySequence<byte> buffer, out DicomElement element)
{
    element = default;

    // Need at least 8 bytes for tag + VR + length
    if (buffer.Length < 8)
        return false;

    // Handle potentially fragmented buffer
    Span<byte> header = stackalloc byte[8];

    if (buffer.IsSingleSegment)
    {
        // Fast path: contiguous
        buffer.FirstSpan.Slice(0, 8).CopyTo(header);
    }
    else
    {
        // Slow path: copy from segments
        buffer.Slice(0, 8).CopyTo(header);
    }

    // Parse header...
    var tag = new DicomTag(
        BinaryPrimitives.ReadUInt16LittleEndian(header),
        BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(2)));

    // Read value length and check if complete
    int valueLength = ParseValueLength(header.Slice(4));
    int totalLength = 8 + valueLength;

    if (buffer.Length < totalLength)
        return false;

    // Extract value
    var valueSequence = buffer.Slice(8, valueLength);
    byte[] value = valueSequence.ToArray(); // Copy for storage

    element = new DicomElement(tag, default, value);
    buffer = buffer.Slice(totalLength);
    return true;
}
```

### Stream Integration

Convert between Stream and Pipelines:

```csharp
// Stream to PipeReader
var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(
    bufferSize: 4096,
    minimumReadSize: 1024,
    leaveOpen: false
));

// PipeReader to Stream
Stream readStream = reader.AsStream();

// Stream to PipeWriter
var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(
    minimumBufferSize: 4096,
    leaveOpen: false
));

// PipeWriter to Stream
Stream writeStream = writer.AsStream();
```

---

## 5. FrozenDictionary (.NET 8+)

### Purpose

`FrozenDictionary<TKey,TValue>` and `FrozenSet<T>` are optimized for:
- Collections created infrequently (startup)
- Collections used frequently (runtime lookups)
- Immutable after creation

They have higher construction cost but faster lookup than `Dictionary<TKey,TValue>`.

### Usage Pattern

```csharp
// At startup - high construction cost is acceptable
private static readonly FrozenDictionary<uint, DicomDictionaryEntry> s_tagLookup =
    LoadDicomDictionary().ToFrozenDictionary(e => e.Tag.Value);

private static readonly FrozenDictionary<ushort, DicomVRInfo> s_vrLookup =
    DicomVRInfo.All.ToFrozenDictionary(v => v.VR.Code);

// At runtime - fast lookups
public static DicomDictionaryEntry? GetEntry(DicomTag tag)
{
    return s_tagLookup.TryGetValue(tag.Value, out var entry) ? entry : null;
}
```

### Multi-TFM Fallback Pattern

```csharp
// In generated code, conditional compilation
#if NET8_0_OR_GREATER
using System.Collections.Frozen;

internal static partial class DicomDictionary
{
    private static readonly FrozenDictionary<uint, DicomDictionaryEntry> s_entries =
        CreateEntries().ToFrozenDictionary(e => e.Tag.Value);

    public static DicomDictionaryEntry? GetEntry(DicomTag tag)
        => s_entries.TryGetValue(tag.Value, out var e) ? e : null;
}
#else
internal static partial class DicomDictionary
{
    private static readonly Dictionary<uint, DicomDictionaryEntry> s_entries =
        CreateEntries().ToDictionary(e => e.Tag.Value);

    public static DicomDictionaryEntry? GetEntry(DicomTag tag)
        => s_entries.TryGetValue(tag.Value, out var e) ? e : null;
}
#endif
```

### Generator-Based Conditional Output

```csharp
// In source generator
private static string GenerateLookup(bool useFrozenDictionary)
{
    var sb = new StringBuilder();

    if (useFrozenDictionary)
    {
        sb.AppendLine("#if NET8_0_OR_GREATER");
        sb.AppendLine("using System.Collections.Frozen;");
        sb.AppendLine("private static readonly FrozenDictionary<uint, Entry> s_lookup =");
        sb.AppendLine("    CreateEntries().ToFrozenDictionary(e => e.Key);");
        sb.AppendLine("#else");
    }

    sb.AppendLine("private static readonly Dictionary<uint, Entry> s_lookup =");
    sb.AppendLine("    CreateEntries().ToDictionary(e => e.Key);");

    if (useFrozenDictionary)
    {
        sb.AppendLine("#endif");
    }

    return sb.ToString();
}
```

### Performance Characteristics

| Operation | Dictionary | FrozenDictionary | Notes |
|-----------|------------|------------------|-------|
| Construction | O(n) | O(n) with higher constant | One-time cost |
| TryGetValue | ~O(1) | ~O(1), faster constant | Optimized for strings |
| Enumeration | O(n) | O(n), cache-friendly | Contiguous memory |
| Memory | Per-entry overhead | Lower per-entry | Packed storage |

**String Key Optimization**: FrozenDictionary performs special optimizations for string keys, analyzing key characteristics (length, common prefixes) to select optimal comparison strategies.

---

## Recommendations for SharpDicom

### Source Generator Architecture

1. **Create `SharpDicom.Generators` project** targeting netstandard2.0
2. **Cache DICOM XML files** in `data/dicom-standard/` directory
3. **Use incremental generator** with `AdditionalTextsProvider` for XML parsing
4. **Generate separate files**: `DicomTag.Generated.cs`, `DicomUID.Generated.cs`, `DicomVRInfo.Generated.cs`
5. **Include conditional compilation** for FrozenDictionary vs Dictionary

### Buffer Strategy

1. **DicomFileReader**: Use `System.IO.Pipelines` for streaming reads
2. **Element values**: Store as `ReadOnlyMemory<byte>` (async-safe)
3. **Parsing hot paths**: Use `ReadOnlySpan<byte>` with stackalloc fallbacks
4. **Writing**: Implement `IBufferWriter<byte>` support throughout

### Memory Management

1. **Small buffers (<256 bytes)**: `stackalloc` to `Span<T>`
2. **Medium buffers**: `ArrayPool<byte>.Shared` with tracked length
3. **Large/async buffers**: `MemoryPool<byte>.Shared` with `IMemoryOwner<byte>`
4. **Pixel data**: Lazy loading with `MemoryPool` or file-backed memory

### Lookup Tables

1. **DICOM dictionary (4000+ entries)**: FrozenDictionary on .NET 8+
2. **VR lookup (31 entries)**: Array with ushort index (fastest)
3. **UID lookup**: FrozenDictionary keyed by string
4. **Transfer syntax lookup**: FrozenDictionary keyed by UID

---

## References

- [Roslyn Incremental Generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)
- [Incremental Generators Cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md)
- [Memory and Span Usage Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines)
- [System.IO.Pipelines](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines)
- [ArrayPool<T>](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1)
- [IBufferWriter<T>](https://learn.microsoft.com/en-us/dotnet/standard/io/buffers)
- [FrozenDictionary](https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary-2)
- [stackalloc Best Practices](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/stackalloc)
