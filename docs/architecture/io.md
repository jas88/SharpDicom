# File I/O

DICOM Part 10 file format reading and writing.

## File Format (Part 10)

**File structure**:
```
┌─────────────────────────────────┐
│ 128-byte preamble (optional)    │
├─────────────────────────────────┤
│ "DICM" prefix (4 bytes)         │
├─────────────────────────────────┤
│ File Meta Information           │
│ (Group 0002, Explicit VR LE)    │
├─────────────────────────────────┤
│ Dataset                         │
│ (Transfer Syntax from meta)     │
└─────────────────────────────────┘
```

## DicomFile

```csharp
public sealed class DicomFile
{
    public ReadOnlyMemory<byte> Preamble { get; }  // 128 bytes or empty
    public DicomDataset FileMetaInfo { get; }      // Group 0002
    public DicomDataset Dataset { get; }
    public TransferSyntax TransferSyntax { get; }

    // Reading
    public static DicomFile Open(string path, DicomReaderOptions? options = null);
    public static DicomFile Open(Stream stream, DicomReaderOptions? options = null);
    public static ValueTask<DicomFile> OpenAsync(string path, DicomReaderOptions? options = null, CancellationToken ct = default);
    public static ValueTask<DicomFile> OpenAsync(Stream stream, DicomReaderOptions? options = null, CancellationToken ct = default);

    // Writing
    public void Save(string path, DicomWriterOptions? options = null);
    public void Save(Stream stream, DicomWriterOptions? options = null);
    public ValueTask SaveAsync(string path, DicomWriterOptions? options = null, CancellationToken ct = default);
    public ValueTask SaveAsync(Stream stream, DicomWriterOptions? options = null, CancellationToken ct = default);

    // Creation
    public DicomFile(DicomDataset dataset, TransferSyntax? transferSyntax = null);
}
```

## Streaming Reader

For large files / network:

```csharp
public sealed class DicomFileReader : IAsyncDisposable
{
    public DicomFileReader(Stream stream, DicomReaderOptions? options = null);

    public ValueTask<DicomDataset> ReadFileMetaInfoAsync(CancellationToken ct = default);
    public IAsyncEnumerable<DicomElement> ReadElementsAsync(CancellationToken ct = default);
    public ValueTask<DicomDataset> ReadDatasetAsync(CancellationToken ct = default);
}
```

## Reader Options

```csharp
public sealed class DicomReaderOptions
{
    // File format handling
    public FilePreambleHandling Preamble { get; init; } = FilePreambleHandling.Optional;
    public FileMetaInfoHandling FileMetaInfo { get; init; } = FileMetaInfoHandling.Optional;
    public InvalidVRHandling InvalidVR { get; init; } = InvalidVRHandling.MapToUN;

    // Security limits
    public uint MaxElementLength { get; init; } = 256 * 1024 * 1024; // 256 MB
    public int MaxSequenceDepth { get; init; } = 128;
    public int MaxTotalItems { get; init; } = 100_000;

    // Pixel data handling
    public PixelDataHandling PixelDataHandling { get; init; } = PixelDataHandling.LoadInMemory;
    public Func<PixelDataContext, PixelDataHandling>? PixelDataCallback { get; init; }
    public string? TempDirectory { get; init; }

    // Private tag handling
    public bool RetainUnknownPrivateTags { get; init; } = true;
    public bool FailOnOrphanPrivateElements { get; init; }
    public bool FailOnDuplicatePrivateSlots { get; init; }

    // Validation
    public ValidationProfile? ValidationProfile { get; init; }
    public Func<ValidationIssue, bool>? ValidationCallback { get; init; }
    public bool CollectValidationIssues { get; init; } = true;

    // Convenience presets
    public static DicomReaderOptions Strict { get; }
    public static DicomReaderOptions Lenient { get; }
    public static DicomReaderOptions Permissive { get; }
    public static DicomReaderOptions Default { get; }
}
```

### Presets

**Strict** - requires valid preamble and FMI, strict validation:
```csharp
public static DicomReaderOptions Strict { get; } = new()
{
    Preamble = FilePreambleHandling.Require,
    FileMetaInfo = FileMetaInfoHandling.Require,
    InvalidVR = InvalidVRHandling.Throw,
    MaxSequenceDepth = 128,
    MaxTotalItems = 100_000,
    PixelDataHandling = PixelDataHandling.LoadInMemory,
    FailOnOrphanPrivateElements = true,
    FailOnDuplicatePrivateSlots = true,
    ValidationProfile = ValidationProfile.Strict,
    CollectValidationIssues = true
};
```

**Lenient** - accepts variations, validation with warnings:
```csharp
public static DicomReaderOptions Lenient { get; } = new()
{
    Preamble = FilePreambleHandling.Optional,
    FileMetaInfo = FileMetaInfoHandling.Optional,
    InvalidVR = InvalidVRHandling.MapToUN,
    MaxSequenceDepth = 128,
    MaxTotalItems = 100_000,
    PixelDataHandling = PixelDataHandling.LoadInMemory,
    ValidationProfile = ValidationProfile.Lenient,
    CollectValidationIssues = true
};
```

**Permissive** - maximum compatibility, no validation:
```csharp
public static DicomReaderOptions Permissive { get; } = new()
{
    Preamble = FilePreambleHandling.Ignore,
    FileMetaInfo = FileMetaInfoHandling.Ignore,
    InvalidVR = InvalidVRHandling.Preserve,
    MaxSequenceDepth = 256,
    MaxTotalItems = 500_000,
    PixelDataHandling = PixelDataHandling.LoadInMemory,
    ValidationProfile = ValidationProfile.Permissive,
    CollectValidationIssues = false  // Performance optimization
};
```

**Default** - lenient parsing without validation (backward compatible):
```csharp
public static DicomReaderOptions Default { get; } = new()
{
    Preamble = FilePreambleHandling.Optional,
    FileMetaInfo = FileMetaInfoHandling.Optional,
    InvalidVR = InvalidVRHandling.MapToUN,
    MaxSequenceDepth = 128,
    MaxTotalItems = 100_000,
    PixelDataHandling = PixelDataHandling.LoadInMemory,
    ValidationProfile = null  // No validation by default
};
```

## Format Handling Options

```csharp
public enum FilePreambleHandling
{
    Require,    // Strict: must have 128-byte preamble + DICM
    Optional,   // Lenient: accept with or without (detect)
    Ignore      // Permissive: skip detection, assume raw dataset
}

public enum FileMetaInfoHandling
{
    Require,    // Strict: must have valid Group 0002
    Optional,   // Lenient: use if present, infer if missing
    Ignore      // Permissive: skip to dataset, assume Implicit VR LE
}

public enum InvalidVRHandling
{
    Throw,      // Strict: throw DicomDataException
    MapToUN,    // Lenient: treat as UN, continue
    Preserve    // Permissive: keep original bytes, best-effort
}
```

## Transfer Syntax

```csharp
public readonly struct TransferSyntax : IEquatable<TransferSyntax>
{
    public DicomUID UID { get; }
    public bool IsExplicitVR { get; }
    public bool IsLittleEndian { get; }
    public bool IsEncapsulated { get; }      // Compressed pixel data
    public bool IsLossy { get; }
    public CompressionType Compression { get; }
    public bool IsKnown { get; }             // False for unrecognized UIDs

    // Well-known instances (generated from Part 6)
    public static readonly TransferSyntax ImplicitVRLittleEndian;
    public static readonly TransferSyntax ExplicitVRLittleEndian;
    public static readonly TransferSyntax ExplicitVRBigEndian;  // Retired
    public static readonly TransferSyntax JPEGBaseline;
    public static readonly TransferSyntax JPEG2000Lossless;
    // ... all ~30 standard transfer syntaxes

    public static TransferSyntax FromUID(DicomUID uid);
}

public enum CompressionType
{
    None,
    JPEGBaseline,
    JPEGExtended,
    JPEGLossless,
    JPEG2000Lossless,
    JPEG2000Lossy,
    JPEGLSLossless,
    JPEGLSNearLossless,
    RLE
}
```

## Pixel Data Handling

Configurable via `DicomReaderOptions`:

```csharp
public enum PixelDataHandling
{
    LoadInMemory,  // Load immediately
    LazyLoad,      // Keep stream reference, load on access
    Skip,          // Discard (metadata-only use cases)
    Callback       // Decide per-instance via callback
}
```

**Streaming-friendly callback** receives context seen so far:

```csharp
public readonly struct PixelDataContext
{
    public ushort? Rows { get; init; }
    public ushort? Columns { get; init; }
    public ushort? BitsAllocated { get; init; }
    public int? NumberOfFrames { get; init; }
    public TransferSyntax TransferSyntax { get; init; }

    public long? EstimatedSize { get; }      // Calculated if dimensions known
    public bool HasImageDimensions { get; }  // True if Rows/Columns present
}
```

## Validation System

Validation is handled via `ValidationProfile` and callbacks:

```csharp
// Enable validation during parsing
var options = new DicomReaderOptions
{
    ValidationProfile = ValidationProfile.Strict,
    ValidationCallback = issue =>
    {
        Console.WriteLine($"Issue: {issue}");
        return true;  // Continue parsing (return false to abort)
    },
    CollectValidationIssues = true
};

var file = DicomFile.Open(path, options);
// Access collected issues via file.ValidationResult
```

**Validation issues detected**:

| Issue | Description |
|-------|-------------|
| Wrong VR | Declared VR doesn't match dictionary |
| Value too long | Exceeds VR maximum length |
| Invalid characters | Characters not allowed for VR |
| Wrong padding | Incorrect padding byte |
| Odd length | Value length not even |
| Invalid format | Date/time/PN format errors |

## Codec System

**Pluggable codec interface**:

```csharp
public interface IPixelDataCodec
{
    TransferSyntax TransferSyntax { get; }

    void Decode(
        DicomFragmentSequence fragments,
        PixelDataInfo info,
        Span<byte> destination);

    DicomFragmentSequence Encode(
        ReadOnlySpan<byte> pixelData,
        PixelDataInfo info,
        CodecOptions options);

    // Async variants
    ValueTask DecodeAsync(...);
    ValueTask<DicomFragmentSequence> EncodeAsync(...);
}
```

**Codec registry**:

```csharp
public static class CodecRegistry
{
    public static void Register(IPixelDataCodec codec);
    public static IPixelDataCodec? GetCodec(TransferSyntax syntax);
    public static bool CanDecode(TransferSyntax syntax);
    public static bool CanEncode(TransferSyntax syntax);
}
```

**Baseline implementations** (in main library):
- `RawCodec` - Uncompressed (passthrough)
- `RleCodec` - RLE (simple, no external deps)

**Optional codec packages** (separate NuGet):
- `SharpDicom.Codecs.Jpeg` - JPEG baseline/lossless
- `SharpDicom.Codecs.Jpeg2000` - JP2K via OpenJPEG
- `SharpDicom.Codecs.JpegLS` - CharLS binding
