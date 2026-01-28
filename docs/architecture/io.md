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
public class DicomReaderOptions
{
    public InvalidVRHandling InvalidVR { get; init; } = InvalidVRHandling.MapToUN;
    public UnknownTransferSyntaxHandling UnknownTransferSyntax { get; init; }
    public FilePreambleHandling Preamble { get; init; } = FilePreambleHandling.Optional;
    public FileMetaInfoHandling FileMetaInfo { get; init; } = FileMetaInfoHandling.Optional;
    public CallbackFilter CallbackFilter { get; init; }
    public ElementCallback? ElementCallback { get; init; }
    public PixelDataHandling PixelData { get; init; }

    // Convenience presets
    public static readonly DicomReaderOptions Strict;
    public static readonly DicomReaderOptions Lenient;
    public static readonly DicomReaderOptions Permissive;
}
```

### Presets

**Strict**:
```csharp
public static readonly DicomReaderOptions Strict = new()
{
    InvalidVR = InvalidVRHandling.Throw,
    UnknownTransferSyntax = UnknownTransferSyntaxHandling.Throw,
    Preamble = FilePreambleHandling.Require,
    FileMetaInfo = FileMetaInfoHandling.Require,
    UnknownCharacterSet = InvalidCharacterSetHandling.Throw,
    InvalidCharacters = InvalidCharacterHandling.Throw
};
```

**Lenient**:
```csharp
public static readonly DicomReaderOptions Lenient = new()
{
    InvalidVR = InvalidVRHandling.MapToUN,
    UnknownTransferSyntax = UnknownTransferSyntaxHandling.AssumeExplicitLE,
    Preamble = FilePreambleHandling.Optional,
    FileMetaInfo = FileMetaInfoHandling.Optional,
    UnknownCharacterSet = InvalidCharacterSetHandling.AssumeUtf8,
    InvalidCharacters = InvalidCharacterHandling.Replace
};
```

**Permissive**:
```csharp
public static readonly DicomReaderOptions Permissive = new()
{
    InvalidVR = InvalidVRHandling.Preserve,
    UnknownTransferSyntax = UnknownTransferSyntaxHandling.TryParse,
    Preamble = FilePreambleHandling.Ignore,
    FileMetaInfo = FileMetaInfoHandling.Ignore,
    UnknownCharacterSet = InvalidCharacterSetHandling.AssumeUtf8,
    InvalidCharacters = InvalidCharacterHandling.Replace
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

public enum UnknownTransferSyntaxHandling
{
    Throw,            // Strict: reject file
    AssumeExplicitLE, // Lenient: assume Explicit VR Little Endian
    TryParse          // Permissive: attempt to detect from data
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

## Element Callback System

Unified callback mechanism for validation, de-identification, and custom processing.

**Callback filter** controls which elements are passed:

```csharp
public enum CallbackFilter
{
    None,        // No callback - fastest path
    InvalidOnly, // Only elements with validation issues
    All          // Every element
}
```

**Callback result** (static instances for common cases, no allocation):

```csharp
public readonly struct ElementCallbackResult
{
    public ElementAction Action { get; }
    public ReadOnlyMemory<byte> ModifiedValue { get; }
    public DicomVR? ModifiedVR { get; }
    public string? RejectReason { get; }

    // Static instances - no allocation, no method call
    public static readonly ElementCallbackResult Keep;
    public static readonly ElementCallbackResult KeepWithWarning;
    public static readonly ElementCallbackResult Remove;

    // Methods only when data needed
    public static ElementCallbackResult Reject(string reason);
    public static ElementCallbackResult Modify(ReadOnlyMemory<byte> value, DicomVR? vr = null);
}

public enum ElementAction { Keep, KeepWithWarning, Modify, Remove, Reject }
```

**Standard callbacks provided**:
- `StandardCallbacks.StrictReject` - Reject any validation issue
- `StandardCallbacks.Lenient` - Accept with warnings
- `StandardCallbacks.StripPrivate` - Remove all private tags
- `StandardCallbacks.BasicDeidentify` - PS3.15 basic profile

**Composable** via `CallbackComposers.Chain(...)` for combining behaviors.

## Validation

Handled via callback system:
- `CallbackFilter.InvalidOnly` + `StandardCallbacks.StrictReject` = strict mode
- `CallbackFilter.InvalidOnly` + `StandardCallbacks.Lenient` = lenient mode
- `CallbackFilter.None` = permissive (no validation)

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
