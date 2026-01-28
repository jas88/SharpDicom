# Character Encoding

DICOM character set handling and the DicomEncoding system.

## DICOM Character Sets

| DICOM Term | Description | .NET Encoding | UTF-8 Compatible |
|------------|-------------|---------------|------------------|
| (default) | ISO-IR 6 | ASCII | ✓ (subset) |
| ISO_IR 100 | Latin1 | ISO-8859-1 | ✗ |
| ISO_IR 101 | Latin2 | ISO-8859-2 | ✗ |
| ISO_IR 144 | Cyrillic | ISO-8859-5 | ✗ |
| ISO_IR 127 | Arabic | ISO-8859-6 | ✗ |
| ISO_IR 126 | Greek | ISO-8859-7 | ✗ |
| ISO_IR 138 | Hebrew | ISO-8859-8 | ✗ |
| ISO_IR 148 | Turkish | ISO-8859-9 | ✗ |
| ISO_IR 13 | Japanese | JIS X 0201 | ✗ |
| ISO_IR 166 | Thai | TIS-620 | ✗ |
| ISO_IR 192 | UTF-8 | UTF-8 | ✓ |
| GB18030 | Chinese | GB18030 | ✗ |
| GBK | Chinese | GBK | ✗ |

## Design Principle

UTF-8 as far as possible, avoid string allocations.

- Default: ISO-IR 6 (ASCII subset)
- Specific Character Set (0008,0005) specifies encoding
- Modern DICOM: ISO-IR 192 (UTF-8) preferred
- Legacy support: ISO 8859-x, JIS, GB18030, etc.

## DicomEncoding

```csharp
public sealed class DicomEncoding
{
    public Encoding Primary { get; }
    public IReadOnlyList<Encoding>? Extensions { get; }  // ISO 2022 code extensions

    // ASCII is strict subset of UTF-8 - zero-copy for both
    public bool IsUtf8Compatible => Primary.CodePage is 65001 or 20127;

    public bool HasExtensions => Extensions is { Count: > 0 };

    // Well-known instances
    public static readonly DicomEncoding Default = new(Encoding.ASCII);  // UTF-8 compatible
    public static readonly DicomEncoding Utf8 = new(Encoding.UTF8);      // UTF-8 compatible
    public static readonly DicomEncoding Latin1 = new(Encoding.Latin1);  // Needs transcode

    // Parse from Specific Character Set (0008,0005)
    public static DicomEncoding FromSpecificCharacterSet(
        string? value,
        InvalidCharacterSetHandling handling = InvalidCharacterSetHandling.AssumeUtf8);

    public static DicomEncoding FromSpecificCharacterSet(
        string[]? values,
        InvalidCharacterSetHandling handling = InvalidCharacterSetHandling.AssumeUtf8);
}
```

## Zero-Copy String Access

```csharp
public sealed class DicomEncoding
{
    // Zero-copy if UTF-8 compatible (ASCII or UTF-8)
    public bool TryGetUtf8(ReadOnlySpan<byte> bytes, out ReadOnlySpan<byte> utf8)
    {
        if (IsUtf8Compatible)
        {
            utf8 = bytes;
            return true;
        }
        utf8 = default;
        return false;
    }

    // Decode with invalid character handling
    public string GetString(ReadOnlySpan<byte> bytes, InvalidCharacterHandling handling)
    {
        var decoder = Primary.GetDecoder();
        decoder.Fallback = handling == InvalidCharacterHandling.Replace
            ? DecoderFallback.ReplacementFallback   // U+FFFD
            : DecoderFallback.ExceptionFallback;    // Throw

        // Handle ISO 2022 escape sequences if extensions present
        if (HasExtensions)
            return DecodeWithExtensions(bytes, decoder);

        return Primary.GetString(bytes);
    }

    public byte[] GetBytes(string value);
}
```

## DicomStringValue

Stateless string value - parses on each access, caller caches if needed:

```csharp
public readonly struct DicomStringValue
{
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly DicomEncoding _encoding;

    public ReadOnlyMemory<byte> RawBytes { get; }
    public bool IsUtf8 { get; }  // True = zero-copy possible

    // Zero-copy if UTF-8, otherwise transcodes to caller's buffer
    public ReadOnlySpan<byte> AsUtf8(Span<byte> buffer);

    // Try zero-copy, returns false if transcoding needed
    public bool TryGetUtf8(out ReadOnlySpan<byte> utf8);

    // Allocates string
    public string AsString();
}
```

## Character Set Registry

```csharp
public static class DicomCharacterSets
{
    public static Encoding? GetEncoding(string dicomTerm);
    public static string? GetDicomTerm(Encoding encoding);
    public static void Register(string dicomTerm, Encoding encoding);  // Vendor-specific
}
```

## Validation Handling

```csharp
public enum InvalidCharacterSetHandling
{
    Throw,          // Strict: reject unknown charset
    AssumeUtf8      // Lenient: fall back to UTF-8
}

public enum InvalidCharacterHandling
{
    Throw,          // Strict: reject invalid sequences
    Replace         // Lenient: replace with U+FFFD
}
```

## Integration with DicomDataset

```csharp
public sealed class DicomDataset
{
    // Determined from Specific Character Set (0008,0005)
    public DicomEncoding Encoding { get; private set; } = DicomEncoding.Default;

    public void Add(DicomElement element)
    {
        _elements[element.Tag] = element;
        _isDirty = true;

        // Update encoding when SpecificCharacterSet changes
        if (element.Tag == DicomTag.SpecificCharacterSet)
            Encoding = DicomEncoding.FromSpecificCharacterSet(element.GetStrings());
    }

    // String access uses dataset encoding
    public string? GetString(DicomTag tag)
        => this[tag]?.GetString(Encoding);
}
```

## Reader Options with Character Set Handling

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

public static readonly DicomReaderOptions Lenient = new()
{
    InvalidVR = InvalidVRHandling.MapToUN,
    UnknownTransferSyntax = UnknownTransferSyntaxHandling.AssumeExplicitLE,
    Preamble = FilePreambleHandling.Optional,
    FileMetaInfo = FileMetaInfoHandling.Optional,
    UnknownCharacterSet = InvalidCharacterSetHandling.AssumeUtf8,
    InvalidCharacters = InvalidCharacterHandling.Replace
};

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
