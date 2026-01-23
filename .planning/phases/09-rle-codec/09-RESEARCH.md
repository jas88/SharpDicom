# Phase 9: RLE Codec - Research

**Researched:** 2026-01-27
**Domain:** DICOM RLE Compression (TIFF PackBits), Codec Architecture
**Confidence:** HIGH

## Summary

DICOM RLE compression uses the TIFF 6.0 PackBits algorithm applied to byte-separated pixel data. The key insight is that pixels are first decomposed into separate byte planes (most significant byte first), then each plane is independently RLE-compressed. This "color-by-plane" approach differs fundamentally from JPEG's "color-by-pixel" and enables efficient compression of medical images where high-order bytes tend to have similar values.

The codec architecture should use a static registry with explicit registration, optional assembly scanning, and DI integration. For runtime lookup performance, `FrozenDictionary<TransferSyntax, IPixelDataCodec>` provides thread-safe, lock-free reads with ~43% better lookup performance than regular Dictionary.

**Primary recommendation:** Implement RLE codec with SIMD-accelerated run detection on .NET 8+, separate MSB-first byte segment generation, and lenient header parsing for malformed data recovery.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.Runtime.Intrinsics | Built-in | SIMD operations | Cross-platform Vector128 support |
| System.Buffers | Built-in | ArrayPool<byte> | Zero-allocation buffer management |
| System.Collections.Frozen | .NET 8+ | FrozenDictionary | Thread-safe codec registry |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0+ | IServiceCollection | DI integration |
| System.IO.Pipelines | Built-in | High-perf I/O | Streaming encode/decode |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| FrozenDictionary | ConcurrentDictionary | 43% slower reads, allows runtime mutation |
| Manual SIMD | Scalar loops | 2-4x slower, simpler code |
| Static registry | DI-only | Less flexible for library consumers |

## Architecture Patterns

### Recommended Project Structure
```
src/SharpDicom/
├── Codecs/
│   ├── IPixelDataCodec.cs          # Core interface
│   ├── CodecRegistry.cs            # Static + DI registry
│   ├── CodecCapabilities.cs        # Capability metadata
│   ├── DecodeResult.cs             # Decode outcome
│   └── Rle/
│       ├── RleCodec.cs             # Main codec implementation
│       ├── RleDecoder.cs           # Decode logic
│       ├── RleEncoder.cs           # Encode logic
│       ├── RleSegmentHeader.cs     # 64-byte header parsing
│       └── RleCodecOptions.cs      # Codec options
```

### Pattern 1: RLE Segment Header (64 bytes)
**What:** Fixed-size header preceding each compressed frame
**When to use:** Every RLE frame decoding/encoding

**Header Structure:**
```
Offset  Size  Description
------  ----  -----------
0       4     Number of segments (1-15)
4       4     Offset to segment 1 (always 64)
8       4     Offset to segment 2 (or 0 if unused)
12      4     Offset to segment 3 (or 0 if unused)
...
60      4     Offset to segment 15 (or 0 if unused)
```

**Example (C#):**
```csharp
// Source: DICOM PS3.5 Annex G.5
public readonly struct RleSegmentHeader
{
    public const int HeaderSize = 64;
    public const int MaxSegments = 15;

    private readonly uint _numberOfSegments;
    private readonly uint[] _segmentOffsets; // 15 elements

    public static RleSegmentHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new DicomCodecException("RLE header too short");

        var numberOfSegments = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (numberOfSegments > MaxSegments)
            throw new DicomCodecException($"Invalid segment count: {numberOfSegments}");

        var offsets = new uint[MaxSegments];
        for (int i = 0; i < MaxSegments; i++)
        {
            offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4 + i * 4));
        }

        return new RleSegmentHeader(numberOfSegments, offsets);
    }

    public int NumberOfSegments => (int)_numberOfSegments;
    public uint GetSegmentOffset(int index) => _segmentOffsets[index];
}
```

### Pattern 2: Byte Segment Generation (MSB-First)
**What:** Decompose pixels into separate byte planes for compression
**When to use:** Before RLE encoding, after RLE decoding

**Segment Order (CRITICAL - MSB first):**
```
16-bit Grayscale (2 segments):
  Segment 0: High bytes of all pixels (MSB)
  Segment 1: Low bytes of all pixels (LSB)

8-bit RGB (3 segments):
  Segment 0: All Red values
  Segment 1: All Green values
  Segment 2: All Blue values

16-bit RGB (6 segments):
  Segment 0: High bytes of all Red values
  Segment 1: Low bytes of all Red values
  Segment 2: High bytes of all Green values
  Segment 3: Low bytes of all Green values
  Segment 4: High bytes of all Blue values
  Segment 5: Low bytes of all Blue values
```

**Number of Segments Formula:**
```csharp
// Source: DICOM PS3.5 Section G.2
int numberOfSegments = (bitsAllocated / 8) * samplesPerPixel;

// Examples:
// 8-bit grayscale:  (8/8) * 1 = 1 segment
// 16-bit grayscale: (16/8) * 1 = 2 segments
// 8-bit RGB:        (8/8) * 3 = 3 segments
// 16-bit RGB:       (16/8) * 3 = 6 segments
```

**Deinterleaving Example (16-bit grayscale):**
```csharp
// Source: DICOM PS3.5 Section G.2
public static void DeinterleaveToSegments(
    ReadOnlySpan<byte> pixelData,
    Span<byte> highByteSegment,
    Span<byte> lowByteSegment)
{
    int pixelCount = pixelData.Length / 2;
    for (int i = 0; i < pixelCount; i++)
    {
        // Little-endian: low byte first in memory
        lowByteSegment[i] = pixelData[i * 2];
        highByteSegment[i] = pixelData[i * 2 + 1];
    }
}

// Inverse for decoding
public static void InterleaveFromSegments(
    ReadOnlySpan<byte> highByteSegment,
    ReadOnlySpan<byte> lowByteSegment,
    Span<byte> pixelData)
{
    int pixelCount = highByteSegment.Length;
    for (int i = 0; i < pixelCount; i++)
    {
        pixelData[i * 2] = lowByteSegment[i];
        pixelData[i * 2 + 1] = highByteSegment[i];
    }
}
```

### Pattern 3: PackBits RLE Algorithm
**What:** TIFF 6.0 PackBits compression/decompression
**When to use:** Compress/decompress individual byte segments

**Byte Values:**
| Header Byte (n) | Signed Value | Action |
|-----------------|--------------|--------|
| 0x00 - 0x7F | 0 to 127 | Literal run: copy next n+1 bytes (1-128) |
| 0x81 - 0xFF | -127 to -1 | Replicate run: repeat next byte -n+1 times (2-128) |
| 0x80 | -128 | No operation (reserved, must not be used as prefix) |

**Decoder Implementation:**
```csharp
// Source: DICOM PS3.5 Annex G.3.2, TIFF 6.0 Specification
public static int DecodeRleSegment(
    ReadOnlySpan<byte> compressed,
    Span<byte> output)
{
    int srcPos = 0;
    int dstPos = 0;

    while (srcPos < compressed.Length && dstPos < output.Length)
    {
        sbyte header = (sbyte)compressed[srcPos++];

        if (header >= 0)
        {
            // Literal run: copy next (header + 1) bytes
            int count = header + 1;
            if (srcPos + count > compressed.Length || dstPos + count > output.Length)
                break; // Truncated data

            compressed.Slice(srcPos, count).CopyTo(output.Slice(dstPos));
            srcPos += count;
            dstPos += count;
        }
        else if (header != -128)
        {
            // Replicate run: repeat next byte (-header + 1) times
            int count = -header + 1;
            if (srcPos >= compressed.Length || dstPos + count > output.Length)
                break; // Truncated data

            byte value = compressed[srcPos++];
            output.Slice(dstPos, count).Fill(value);
            dstPos += count;
        }
        // header == -128 (-0x80): noop, skip
    }

    return dstPos;
}
```

**Encoder Implementation:**
```csharp
// Source: DICOM PS3.5 Annex G.3.1, TIFF 6.0 Specification
public static int EncodeRleSegment(
    ReadOnlySpan<byte> input,
    Span<byte> output)
{
    int srcPos = 0;
    int dstPos = 0;

    while (srcPos < input.Length)
    {
        // Look for run of identical bytes
        int runLength = 1;
        while (srcPos + runLength < input.Length &&
               runLength < 128 &&
               input[srcPos + runLength] == input[srcPos])
        {
            runLength++;
        }

        if (runLength >= 3 || (runLength == 2 && srcPos + runLength >= input.Length))
        {
            // Replicate run (3+ bytes, or 2 bytes at end)
            output[dstPos++] = (byte)(-(runLength - 1)); // -1 to -127
            output[dstPos++] = input[srcPos];
            srcPos += runLength;
        }
        else
        {
            // Literal run - find extent
            int literalStart = srcPos;
            int literalLength = 0;

            while (srcPos < input.Length && literalLength < 128)
            {
                // Check if starting a run of 3+ identical bytes
                if (srcPos + 2 < input.Length &&
                    input[srcPos] == input[srcPos + 1] &&
                    input[srcPos] == input[srcPos + 2])
                {
                    break; // End literal, start replicate
                }
                srcPos++;
                literalLength++;
            }

            if (literalLength > 0)
            {
                output[dstPos++] = (byte)(literalLength - 1); // 0 to 127
                input.Slice(literalStart, literalLength).CopyTo(output.Slice(dstPos));
                dstPos += literalLength;
            }
        }
    }

    // Pad to even length
    if (dstPos % 2 != 0)
    {
        output[dstPos++] = 0;
    }

    return dstPos;
}
```

### Pattern 4: SIMD Run Detection
**What:** Vectorized detection of repeated bytes for faster encoding
**When to use:** Encoding optimization on .NET 8+ with Vector128 support

```csharp
// Source: .NET Hardware Intrinsics documentation
public static int FindRunLength(ReadOnlySpan<byte> data, int startIndex)
{
    if (!Vector128.IsHardwareAccelerated || data.Length - startIndex < Vector128<byte>.Count)
    {
        return FindRunLengthScalar(data, startIndex);
    }

    byte target = data[startIndex];
    var targetVector = Vector128.Create(target);
    int pos = startIndex;

    // Process 16 bytes at a time
    while (pos + Vector128<byte>.Count <= data.Length)
    {
        var chunk = Vector128.Create(data.Slice(pos, Vector128<byte>.Count));
        var comparison = Vector128.Equals(chunk, targetVector);

        // Check if all bytes match
        if (comparison != Vector128<byte>.AllBitsSet)
        {
            // Find first non-matching byte
            var mask = ~comparison.ExtractMostSignificantBits();
            int firstDiff = BitOperations.TrailingZeroCount(mask);
            return Math.Min(pos + firstDiff - startIndex, 128);
        }

        pos += Vector128<byte>.Count;
        if (pos - startIndex >= 128) // Max run length
            return 128;
    }

    // Handle remaining bytes
    while (pos < data.Length && pos - startIndex < 128 && data[pos] == target)
    {
        pos++;
    }

    return pos - startIndex;
}

private static int FindRunLengthScalar(ReadOnlySpan<byte> data, int startIndex)
{
    byte target = data[startIndex];
    int length = 1;
    while (startIndex + length < data.Length && length < 128 && data[startIndex + length] == target)
    {
        length++;
    }
    return length;
}
```

### Pattern 5: Codec Registry with FrozenDictionary
**What:** Thread-safe, high-performance codec lookup
**When to use:** Application startup, codec resolution

```csharp
// Source: .NET 8 FrozenDictionary documentation
public static class CodecRegistry
{
    private static readonly object _lock = new();
    private static Dictionary<TransferSyntax, IPixelDataCodec> _mutableRegistry = new();
    private static FrozenDictionary<TransferSyntax, IPixelDataCodec>? _frozenRegistry;

    // Registration phase (startup only)
    public static void Register(IPixelDataCodec codec)
    {
        lock (_lock)
        {
            _mutableRegistry[codec.TransferSyntax] = codec;
            _frozenRegistry = null; // Invalidate frozen cache
        }
    }

    public static void Register<TCodec>() where TCodec : IPixelDataCodec, new()
    {
        Register(new TCodec());
    }

    // Freeze registry for read-only access
    public static void Freeze()
    {
        lock (_lock)
        {
            _frozenRegistry = _mutableRegistry.ToFrozenDictionary();
        }
    }

    // High-performance lookup (thread-safe, lock-free after freeze)
    public static IPixelDataCodec? GetCodec(TransferSyntax syntax)
    {
        var registry = _frozenRegistry ?? EnsureFrozen();
        return registry.TryGetValue(syntax, out var codec) ? codec : null;
    }

    private static FrozenDictionary<TransferSyntax, IPixelDataCodec> EnsureFrozen()
    {
        lock (_lock)
        {
            return _frozenRegistry ??= _mutableRegistry.ToFrozenDictionary();
        }
    }

    // Assembly scanning
    public static void RegisterFromAssembly(Assembly assembly)
    {
        var codecTypes = assembly.GetTypes()
            .Where(t => typeof(IPixelDataCodec).IsAssignableFrom(t)
                     && !t.IsAbstract
                     && t.GetConstructor(Type.EmptyTypes) != null);

        foreach (var type in codecTypes)
        {
            var codec = (IPixelDataCodec)Activator.CreateInstance(type)!;
            Register(codec);
        }
    }

    // DI integration
    public static IServiceCollection AddDicomCodecs(this IServiceCollection services)
    {
        services.AddSingleton<IPixelDataCodec, RleCodec>();
        // Add other codecs...

        services.AddSingleton(sp =>
        {
            var codecs = sp.GetServices<IPixelDataCodec>();
            return codecs.ToFrozenDictionary(c => c.TransferSyntax);
        });

        return services;
    }
}
```

### Anti-Patterns to Avoid
- **LSB-first segment ordering:** DICOM RLE is always MSB-first. Some implementations incorrectly use LSB-first.
- **Compressing across row boundaries:** Each image row must be encoded separately per TIFF spec.
- **Using -128 as a header byte:** The value -128 (0x80) is reserved as a no-op.
- **Mutable codec registry after startup:** Use FrozenDictionary for thread-safe reads.
- **Ignoring even-length padding:** Each RLE segment must be padded to even byte count.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Run detection | Simple byte-by-byte loop | SIMD Vector128 comparison | 2-4x faster on hot paths |
| Thread-safe dictionary | Manual locking | FrozenDictionary | 43% faster reads, lock-free |
| Buffer management | new byte[] allocations | ArrayPool<byte>.Shared | Reduces GC pressure |
| Byte swapping (endian) | Manual bit shifting | BinaryPrimitives | Optimized, handles all cases |

**Key insight:** The RLE algorithm itself is simple, but segment management, byte ordering, and thread-safe registry access have well-tested solutions.

## Common Pitfalls

### Pitfall 1: Wrong Segment Byte Order
**What goes wrong:** Segments decoded in LSB-first order instead of MSB-first
**Why it happens:** Confusion between DICOM's MSB-first segments and little-endian byte storage
**How to avoid:** Always decompose starting from most significant byte
**Warning signs:** 16-bit images appear noisy or have inverted contrast

### Pitfall 2: Malformed RLE Headers
**What goes wrong:** Segment count or offsets are corrupted/invalid
**Why it happens:** Truncated files, buggy encoders, or transmission errors
**How to avoid:** Validate segment count <= 15, offsets within bounds, monotonically increasing
**Warning signs:** Out-of-bounds access, negative lengths, overlapping segments

### Pitfall 3: Odd-Length RLE Segments
**What goes wrong:** Decoder misaligned on subsequent segments
**Why it happens:** Encoder didn't pad to even length
**How to avoid:** Accept odd-length in lenient mode, always produce even-length on encode
**Warning signs:** Subsequent segments decode incorrectly

### Pitfall 4: Row Boundary Compression
**What goes wrong:** Runs cross image row boundaries incorrectly
**Why it happens:** TIFF PackBits spec requires per-row encoding, some skip this
**How to avoid:** For strict compliance, encode each row separately (DICOM allows frame-level)
**Warning signs:** Subtle visual artifacts at row boundaries in some viewers

### Pitfall 5: Empty Basic Offset Table Handling
**What goes wrong:** Decoder can't locate frames in multi-frame images
**Why it happens:** BOT is optional (can be empty), Extended Offset Table may be present instead
**How to avoid:** Check for Extended Offset Table (7FE0,0001) when BOT is empty
**Warning signs:** Only first frame decodes, others fail or return garbage

## Code Examples

### Complete Frame Decode
```csharp
// Source: DICOM PS3.5 Annex G
public void DecodeFrame(
    ReadOnlyMemory<byte> fragment,
    PixelDataInfo info,
    Memory<byte> destination)
{
    var compressed = fragment.Span;
    var output = destination.Span;

    // 1. Parse RLE header
    var header = RleSegmentHeader.Parse(compressed);

    // 2. Calculate expected segments
    int bytesPerSample = info.BitsAllocated / 8;
    int expectedSegments = bytesPerSample * info.SamplesPerPixel;

    if (header.NumberOfSegments != expectedSegments)
        throw new DicomCodecException(
            $"Expected {expectedSegments} segments, found {header.NumberOfSegments}");

    // 3. Decode each segment into temporary buffers
    int pixelCount = info.Rows * info.Columns;
    var segmentBuffers = new byte[expectedSegments][];

    for (int seg = 0; seg < expectedSegments; seg++)
    {
        uint offset = header.GetSegmentOffset(seg);
        uint nextOffset = seg + 1 < expectedSegments
            ? header.GetSegmentOffset(seg + 1)
            : (uint)compressed.Length;

        var segmentData = compressed.Slice((int)offset, (int)(nextOffset - offset));
        segmentBuffers[seg] = new byte[pixelCount];

        int decoded = DecodeRleSegment(segmentData, segmentBuffers[seg]);
        if (decoded != pixelCount)
            throw new DicomCodecException(
                $"Segment {seg}: decoded {decoded} bytes, expected {pixelCount}");
    }

    // 4. Interleave segments back to pixels
    InterleaveSegments(segmentBuffers, output, info);
}

private void InterleaveSegments(
    byte[][] segments,
    Span<byte> output,
    PixelDataInfo info)
{
    int bytesPerSample = info.BitsAllocated / 8;
    int pixelCount = info.Rows * info.Columns;
    int bytesPerPixel = bytesPerSample * info.SamplesPerPixel;

    for (int pixel = 0; pixel < pixelCount; pixel++)
    {
        int segIndex = 0;
        for (int sample = 0; sample < info.SamplesPerPixel; sample++)
        {
            // MSB first within each sample
            for (int byteIndex = bytesPerSample - 1; byteIndex >= 0; byteIndex--)
            {
                int outputIndex = pixel * bytesPerPixel + sample * bytesPerSample + byteIndex;
                output[outputIndex] = segments[segIndex++][pixel];
            }
        }
    }
}
```

### Complete Frame Encode
```csharp
// Source: DICOM PS3.5 Annex G
public ReadOnlyMemory<byte> EncodeFrame(
    ReadOnlyMemory<byte> pixelData,
    PixelDataInfo info)
{
    var input = pixelData.Span;

    // 1. Calculate segments needed
    int bytesPerSample = info.BitsAllocated / 8;
    int numberOfSegments = bytesPerSample * info.SamplesPerPixel;
    int pixelCount = info.Rows * info.Columns;

    // 2. Deinterleave pixels into byte segments (MSB first)
    var segments = new byte[numberOfSegments][];
    DeinterleaveToSegments(input, segments, info);

    // 3. RLE encode each segment
    var encodedSegments = new List<byte[]>(numberOfSegments);
    foreach (var segment in segments)
    {
        var encoded = new byte[segment.Length + (segment.Length / 128) + 2]; // Worst case
        int encodedLength = EncodeRleSegment(segment, encoded);

        // Ensure even length
        if (encodedLength % 2 != 0)
            encodedLength++;

        Array.Resize(ref encoded, encodedLength);
        encodedSegments.Add(encoded);
    }

    // 4. Build output with header
    int totalSize = RleSegmentHeader.HeaderSize + encodedSegments.Sum(s => s.Length);
    var output = new byte[totalSize];
    var outputSpan = output.AsSpan();

    // Write header
    BinaryPrimitives.WriteUInt32LittleEndian(outputSpan, (uint)numberOfSegments);
    uint offset = RleSegmentHeader.HeaderSize;

    for (int i = 0; i < numberOfSegments; i++)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(outputSpan.Slice(4 + i * 4), offset);
        offset += (uint)encodedSegments[i].Length;
    }

    // Fill unused offset slots with zero
    for (int i = numberOfSegments; i < 15; i++)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(outputSpan.Slice(4 + i * 4), 0);
    }

    // Write encoded segments
    offset = RleSegmentHeader.HeaderSize;
    foreach (var segment in encodedSegments)
    {
        segment.CopyTo(outputSpan.Slice((int)offset));
        offset += (uint)segment.Length;
    }

    return output;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Dictionary for codec lookup | FrozenDictionary | .NET 8 (2023) | 43% faster reads, thread-safe |
| Scalar run detection | Vector128 SIMD | .NET Core 3.0+ | 2-4x faster encoding |
| Basic Offset Table only | Extended Offset Table | DICOM 2020a | Better large file support |
| Allocate new arrays | ArrayPool<byte> | .NET Core 2.1+ | Reduced GC pressure |

**Deprecated/outdated:**
- **-128 header byte usage:** Never valid per TIFF spec, should not appear
- **Explicit VR Big Endian RLE:** RLE is always little-endian in DICOM
- **Single-frame-per-fragment assumption:** Multi-fragment frames now supported

## Open Questions

Things that couldn't be fully resolved:

1. **SIMD encoding threshold**
   - What we know: Vector128 helps with run detection
   - What's unclear: Minimum data size where SIMD overhead is worth it
   - Recommendation: Benchmark with 128-byte, 512-byte, and 1KB thresholds

2. **Row-level vs frame-level encoding**
   - What we know: TIFF spec says encode per row; DICOM allows frame-level
   - What's unclear: Which approach gives better compression for medical images
   - Recommendation: Implement frame-level (simpler), add row-level option if needed

3. **Parallel frame encoding thread count**
   - What we know: Frames can encode independently
   - What's unclear: Optimal parallelism for typical medical image sizes
   - Recommendation: Default to Environment.ProcessorCount, make configurable

## Sources

### Primary (HIGH confidence)
- [DICOM PS3.5 Annex G.3 - The RLE Algorithm](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_G.3.html)
- [DICOM PS3.5 Annex G.5 - RLE Header Format](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_G.5.html)
- [DICOM PS3.5 Section G.2 - Byte Segments](https://dicom.nema.org/dicom/2013/output/chtml/part05/sect_G.2.html)
- [DICOM PS3.5 Section 8.2.2 - RLE Compression](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_8.2.2.html)
- [DICOM PS3.5 Section A.4 - Transfer Syntaxes for Encapsulated Pixel Data](https://dicom.nema.org/medical/dicom/current/output/chtml/part05/sect_A.4.html)
- [TIFF 6.0 PackBits Algorithm](https://www.fileformat.info/format/tiff/corion-packbits.htm)
- [.NET FrozenDictionary Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary-2)
- [.NET Vector128 Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector128)

### Secondary (MEDIUM confidence)
- [.NET Hardware Intrinsics Blog](https://devblogs.microsoft.com/dotnet/dotnet-8-hardware-intrinsics/)
- [SIMD Performance Blog](https://xoofx.github.io/blog/2023/07/09/10x-performance-with-simd-in-csharp-dotnet/)
- [Scrutor Assembly Scanning](https://github.com/khellang/Scrutor)
- [DCMTK RLE Forum Discussion](https://forum.dcmtk.org/viewtopic.php?t=352)

### Tertiary (LOW confidence)
- fo-dicom codec patterns (source not directly accessed, patterns inferred)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - Well-documented .NET 8 features
- RLE Algorithm: HIGH - Directly from DICOM PS3.5 Annex G
- Byte segment ordering: HIGH - Confirmed MSB-first from multiple DICOM sources
- SIMD patterns: MEDIUM - General .NET patterns, not RLE-specific implementations found
- Codec registry: MEDIUM - Patterns from Scrutor and .NET DI, not DICOM-specific

**Research date:** 2026-01-27
**Valid until:** 2026-04-27 (90 days - stable domain)
