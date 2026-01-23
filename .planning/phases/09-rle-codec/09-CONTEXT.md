---
phase: 09-rle-codec
created: 2026-01-27
status: ready-for-research
---

# Phase 9: RLE Codec — Context

## Phase Goal

Validate codec interface with built-in RLE codec (no external dependencies).

## Core Decisions

### Codec Interface Design

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Discovery | All methods | Explicit registration, assembly scanning, and DI |
| Options | Per-codec options | Each codec defines its own options type |
| Partial decoding | Frame ranges | Decode frames 5-10 without decoding 0-4 |
| Capabilities | Full metadata | Supported TS, encode/decode, lossy/lossless, bit depths |

### RLE Implementation

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Malformed headers | Lenient | Try to recover from common errors |
| SIMD optimization | Yes | Use SIMD intrinsics for run detection |
| Multi-sample (RGB) | Both | Support planar and interleaved encoding |
| Odd dimensions | Pad | Pad to even bytes per row |

### Multi-Frame Handling

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Decode approach | Per-frame on demand | Decode individual frames when accessed |
| Caching | Caller caches | Decode returns new memory each call |
| Parallel encode | Yes | Encode multiple frames in parallel |
| Offset tables | Both tables | BOT + Extended for compatibility |

### Error Handling

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Frame errors | Placeholder + flag | Return blank frame, set error flag |
| Diagnostics | Full detail | Frame index, byte position, expected vs actual |
| Validation mode | Yes | Check if data can be decoded without full decode |
| Exception base | DicomCodecException | Common base for all codec errors |

## Success Criteria

From ROADMAP.md:
- [ ] Decode RLE-compressed files
- [ ] Encode to RLE format
- [ ] Codec interface extensible
- [ ] No external dependencies

## Dependencies

**Requires:**
- Phase 5: Pixel data infrastructure
- Phase 7: File writing for encode output

**Provides:**
- Reference codec implementation
- Extensible codec interface for future packages

## Implementation Notes

### IPixelDataCodec Interface

```csharp
public interface IPixelDataCodec
{
    // Identification
    TransferSyntax TransferSyntax { get; }
    string Name { get; }
    CodecCapabilities Capabilities { get; }

    // Decode
    DecodeResult Decode(
        DicomFragmentSequence fragments,
        PixelDataInfo info,
        int frameIndex,
        Memory<byte> destination);

    ValueTask<DecodeResult> DecodeAsync(
        DicomFragmentSequence fragments,
        PixelDataInfo info,
        int frameIndex,
        Memory<byte> destination,
        CancellationToken ct = default);

    // Encode
    DicomFragmentSequence Encode(
        ReadOnlySpan<byte> pixelData,
        PixelDataInfo info,
        object? options = null);

    ValueTask<DicomFragmentSequence> EncodeAsync(
        ReadOnlyMemory<byte> pixelData,
        PixelDataInfo info,
        object? options = null,
        CancellationToken ct = default);

    // Validation
    ValidationResult ValidateCompressedData(DicomFragmentSequence fragments);
}
```

### CodecCapabilities

```csharp
public readonly record struct CodecCapabilities(
    bool CanEncode,
    bool CanDecode,
    bool IsLossy,
    bool SupportsMultiFrame,
    bool SupportsParallelEncode,
    int[] SupportedBitDepths,
    int[] SupportedSamplesPerPixel
);
```

### DecodeResult

```csharp
public readonly record struct DecodeResult(
    bool Success,
    int BytesWritten,
    CodecDiagnostic? Diagnostic
);

public readonly record struct CodecDiagnostic(
    int FrameIndex,
    long BytePosition,
    string Message,
    string? Expected,
    string? Actual
);
```

### CodecRegistry

```csharp
public static class CodecRegistry
{
    // Explicit registration
    public static void Register(IPixelDataCodec codec);
    public static void Register<TCodec>() where TCodec : IPixelDataCodec, new();

    // Lookup
    public static IPixelDataCodec? GetCodec(TransferSyntax syntax);
    public static bool CanDecode(TransferSyntax syntax);
    public static bool CanEncode(TransferSyntax syntax);

    // Assembly scanning
    public static void RegisterFromAssembly(Assembly assembly);

    // DI integration
    public static IServiceCollection AddDicomCodecs(this IServiceCollection services);
}
```

### RleCodec

```csharp
public sealed class RleCodec : IPixelDataCodec
{
    public TransferSyntax TransferSyntax => TransferSyntax.RLELossless;
    public string Name => "RLE Lossless";

    public CodecCapabilities Capabilities => new(
        CanEncode: true,
        CanDecode: true,
        IsLossy: false,
        SupportsMultiFrame: true,
        SupportsParallelEncode: true,
        SupportedBitDepths: [8, 16],
        SupportedSamplesPerPixel: [1, 3]
    );

    // Implementation...
}
```

### RleCodecOptions

```csharp
public sealed class RleCodecOptions
{
    public bool GenerateBasicOffsetTable { get; init; } = true;
    public bool GenerateExtendedOffsetTable { get; init; } = true;
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
    public bool UsePlanarConfiguration { get; init; } = true;  // For RGB
}
```

### DicomCodecException

```csharp
public class DicomCodecException : DicomException
{
    public TransferSyntax TransferSyntax { get; init; }
    public int? FrameIndex { get; init; }
    public long? BytePosition { get; init; }

    public DicomCodecException(string message) : base(message) { }
    public DicomCodecException(string message, Exception inner) : base(message, inner) { }
}
```

---

*Created: 2026-01-27*
*Status: Ready for research and planning*
